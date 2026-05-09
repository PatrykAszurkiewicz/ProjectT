using System.Collections.Generic;
using UnityEngine;

public class TowerDefenseMap : MonoBehaviour
{
    [Header("Map Configuration")]
    public float mapRadius = 10f;
    public GameObject backgroundGameObject; // Manual background GameObject reference
    //public string backgroundImagePath = "Backgrounds/Background3"; // Fallback for generated terrain
    public string backgroundImagePath = "Backgrounds/Background8"; // Fallback for generated terrain    
    public bool useBackgroundImage = true;
    public Material terrainMaterial;
    public Color terrainColor = Color.green;

    [Header("Tower Slot Configuration")]
    public GameObject towerSlotPrefab;
    public List<RingConfiguration> rings = new List<RingConfiguration>();
    public int maxTotalRings = 8; // Limits total rings including augment-added ones

    [Header("Central Core Configuration")]
    public bool enableCentralCore = true;
    public float coreSize = 2f;
    public float coreMaxEnergy = 100f;
    public float coreStartingEnergy = 100f;

    [Header("Layout Override")]
    [Tooltip("Set by the orchestrator each stage. When non-null, this layout's slot\n" +
             "positions override the rings list above. Leave null to use the rings.")]
    public MapLayoutDefinition activeLayout;

    [Tooltip("Runtime multiplier applied to all layout positions, ring radii, " +
             "obstacle positions/sizes, and connection-line points when a layout " +
             "is applied.\n" +
             "1.0 = layouts are used as-authored (recommended — built-in layouts " +
             "are already pre-spaced).\n" +
             "Values >1 push slots further apart and scale obstacles proportionally.\n" +
             "Slot SIZES are NOT scaled — towers stay the same size.\n" +
             "mapRadius is auto-scaled to match so outer slots don't hit the border.")]
    [Min(0.1f)]
    public float layoutSpreadScale = 1.0f;

    [Tooltip("Physics layer name for layout obstacles (walls, buildings).\n" +
             "Must match the LayerMask 'obstacleLayer' on enemy prefabs so they avoid them.")]
    public string obstacleLayerName = "Obstacle";

    [Tooltip("Maximum size of any single collider segment. Long obstacles are\n" +
             "broken into multiple colliders of this size so enemies can avoid them.\n" +
             "Smaller = better navigation, more colliders. 1.2 is a good default.")]
    public float maxColliderSegmentSize = 1.2f;

    [Header("Visual Settings")]
    public bool showDebugCircles = true;
    public Color debugCircleColor = Color.white;
    public float debugCircleWidth = 0.02f;

    [System.NonSerialized]
    private List<TowerSlot> allTowerSlots = new List<TowerSlot>();
    private int bonusSlotsAdded = 0; // tracks how many bonus slots have been revealed
    private GameObject terrainObject;
    private GameObject slotsContainer;
    private GameObject obstaclesContainer; // layout-specific obstacles (walls, buildings)
    private CentralCore centralCore;

    // Captured on first GenerateMap so we can rescale mapRadius from the
    // original (un-scaled) value whenever layoutSpreadScale changes.
    [System.NonSerialized]
    private float baseMapRadius = -1f;
    [System.NonSerialized]
    private bool baseMapRadiusCaptured = false;

    // Track the SOURCE layout asset (not the scaled clone) so we can detect
    // "same layout asked for again" and skip the rebuild — preserving towers
    // and slots between stages. Without this, ApplyLayout's reference check
    // against `activeLayout` always misses when scale != 1.0 because we
    // create a fresh clone each call.
    [System.NonSerialized]
    private MapLayoutDefinition sourceLayout;
    [System.NonSerialized]
    private float lastAppliedSpreadScale = 1f;
    [System.NonSerialized]
    private bool sourceLayoutCaptured = false;

    [System.Serializable]
    public class RingConfiguration
    {
        public float radius = 5f;
        public int slotCount = 8;
        public float slotSize = 1f;
        public float rotationOffset = 0f; // Degrees
        public bool enabled = true;
    }

    void Start()
    {
        // Add default rings if none are configured
        if (rings.Count == 0)
        {
            rings.Add(new RingConfiguration { radius = 2.3f, slotCount = 6, slotSize = 1.9f });
            rings.Add(new RingConfiguration { radius = 3.8f, slotCount = 6, slotSize = 1.9f });
        }
        GenerateMap();
    }

    [ContextMenu("Generate Map")]
    public void GenerateMap()
    {
        ClearExistingMap();
        CreateTerrain();
        CreateCentralCore();
        CreateLayoutObstacles();
        CreateTowerSlots();
        if (showDebugCircles)
        {
            DrawDebugCircles();
        }
    }

    void ClearExistingMap()
    {
        // Clear existing slots
        allTowerSlots.Clear();

        // Destroy existing terrain, but preserve manually assigned background
        if (terrainObject != null && terrainObject != backgroundGameObject)
        {
            DestroyImmediate(terrainObject);
        }
        terrainObject = null;

        // Destroy existing slots container
        if (slotsContainer != null)
        {
            DestroyImmediate(slotsContainer);
        }

        // Destroy existing obstacles container
        if (obstaclesContainer != null)
        {
            DestroyImmediate(obstaclesContainer);
        }

        // Destroy old debug rings and layout connection lines
        var toDelete = new List<GameObject>();
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Debug_Ring_") || child.name.StartsWith("LayoutLine_"))
                toDelete.Add(child.gameObject);
        }
        foreach (var go in toDelete) DestroyImmediate(go);

        // Destroy existing central core
        if (centralCore != null)
        {
            DestroyImmediate(centralCore.gameObject);
            centralCore = null;
        }
    }

    void CreateTerrain()
    {
        if (backgroundGameObject != null)
        {
            // Use manually assigned background GameObject
            terrainObject = backgroundGameObject;

            // Ensure proper parenting
            if (terrainObject.transform.parent != transform)
            {
                terrainObject.transform.SetParent(transform);
            }

            // Ensure SpriteRenderer exists
            var renderer = terrainObject.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = terrainObject.AddComponent<SpriteRenderer>();
            }
            renderer.sortingOrder = -1;
        }
        else
        {
            // Generate terrain procedurally
            terrainObject = new GameObject("Terrain");
            terrainObject.transform.parent = transform;
            terrainObject.transform.localPosition = Vector3.zero;

            var renderer = terrainObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = -1;

            if (useBackgroundImage && !string.IsNullOrEmpty(backgroundImagePath))
            {
                // Load background image from Resources
                Texture2D backgroundTexture = Resources.Load<Texture2D>(backgroundImagePath);
                if (backgroundTexture != null)
                {
                    Sprite backgroundSprite = Sprite.Create(
                        backgroundTexture,
                        new Rect(0, 0, backgroundTexture.width, backgroundTexture.height),
                        Vector2.one * 0.5f,
                        100f
                    );

                    renderer.sprite = backgroundSprite;
                    renderer.color = Color.white;

                    // Keep native pixel size (no scaling) — use tiling for full coverage
                    terrainObject.transform.localScale = Vector3.one;

                    // Add BackgroundTiler to cover the map area via repeating tiles
                    BackgroundTiler tiler = terrainObject.GetComponent<BackgroundTiler>();
                    if (tiler == null)
                        tiler = terrainObject.AddComponent<BackgroundTiler>();
                    tiler.autoCalculateGrid = true;
                    tiler.coverageRadius = mapRadius + 5f;
                    tiler.GenerateTiles();
                }
                else
                {
                    Debug.LogWarning($"Background image not found: {backgroundImagePath}. Using fallback.");
                    CreateFallbackTerrain(renderer);
                }
            }
            else
            {
                CreateFallbackTerrain(renderer);
            }
        }

        // Add boundary collider
        var collider = terrainObject.GetComponent<CircleCollider2D>();
        if (collider == null)
        {
            collider = terrainObject.AddComponent<CircleCollider2D>();
        }
        collider.radius = mapRadius;
        collider.isTrigger = true;
    }

    void CreateFallbackTerrain(SpriteRenderer renderer)
    {
        renderer.sprite = CreateSimpleCircleSprite();
        renderer.color = terrainColor;

        // Scale to desired map size
        float desiredDiameter = mapRadius * 2f;
        float currentSize = 0.64f; // Default sprite size
        float scale = desiredDiameter / currentSize;
        terrainObject.transform.localScale = Vector3.one * scale;
    }

    void CreateCentralCore()
    {
        if (!enableCentralCore) return;

        GameObject coreObject = new GameObject("CentralCore");
        coreObject.transform.parent = transform;
        coreObject.transform.position = Vector3.zero;

        centralCore = coreObject.AddComponent<CentralCore>();
        centralCore.maxEnergy = coreMaxEnergy;
        centralCore.currentEnergy = coreStartingEnergy;
        centralCore.coreSize = coreSize;

        // Subscribe to core events
        centralCore.OnEnergyChanged += OnCoreEnergyChanged;
        centralCore.OnEnergyDepleted += OnCoreEnergyDepleted;
    }

    void CreateTowerSlots()
    {
        slotsContainer = new GameObject("Tower Slots");
        slotsContainer.transform.parent = transform;
        slotsContainer.transform.localPosition = Vector3.zero;

        bonusSlotsAdded = 0;

        // If a layout is active, use it; otherwise fall back to rings (original behaviour).
        if (activeLayout != null && activeLayout.layoutType == MapLayoutDefinition.LayoutType.Custom)
        {
            CreateCustomSlots(activeLayout.customSlotPositions, activeLayout.customSlotSize);
        }
        else
        {
            // Concentric rings — original logic, untouched.
            List<TowerDefenseMap.RingConfiguration> sourceRings =
                (activeLayout != null && activeLayout.rings != null && activeLayout.rings.Count > 0)
                ? activeLayout.rings
                : rings;

            for (int ringIndex = 0; ringIndex < sourceRings.Count; ringIndex++)
            {
                var ring = sourceRings[ringIndex];
                if (!ring.enabled) continue;

                if (ringIndex % 2 == 1)
                {
                    float angleStep = 360f / ring.slotCount;
                    ring.rotationOffset = angleStep / 2f;
                }
                else
                {
                    ring.rotationOffset = 0f;
                }

                CreateRingSlots(ring);
            }
        }
    }

    // Spawns free-form slots from a list of world-space positions.
    void CreateCustomSlots(List<Vector2> positions, float slotSize)
    {
        if (positions == null) return;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 pos = new Vector3(positions[i].x, positions[i].y, 0f);
            GameObject slotObj = CreateTowerSlot(pos, slotSize, i);
            slotObj.transform.parent = slotsContainer.transform;
            slotObj.name = $"Slot_{i}";

            TowerSlot slot = slotObj.GetComponent<TowerSlot>();
            slot.ringIndex = 0;
            slot.slotIndex = i;
            allTowerSlots.Add(slot);
        }
    }

    // Spawns the rectangular obstacles defined by the active layout.
    // Skipped silently when there's no active layout or no obstacles.
    void CreateLayoutObstacles()
    {
        if (activeLayout == null || activeLayout.obstacles == null || activeLayout.obstacles.Count == 0)
            return;

        obstaclesContainer = new GameObject("Layout Obstacles");
        obstaclesContainer.transform.parent = transform;
        obstaclesContainer.transform.localPosition = Vector3.zero;

        Sprite squareSprite = CreateSimpleSquareSprite();
        int obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        if (obstacleLayer < 0) obstacleLayer = 0; // fallback to default if layer doesn't exist

        // Core safety radius: any obstacle that intersects this disc around (0,0)
        // is skipped so a misplaced wall can never block the central core.
        float coreSafeRadius = (enableCentralCore ? coreSize : 0f) + 1.0f;

        for (int i = 0; i < activeLayout.obstacles.Count; i++)
        {
            var obs = activeLayout.obstacles[i];

            if (obs.blocksMovement && IntersectsCore(obs, coreSafeRadius))
            {
                Debug.LogWarning($"[TowerDefenseMap] Skipping obstacle '{obs.label}' " +
                                 $"at {obs.position} size {obs.size} — would overlap central core.");
                continue;
            }

            CreateOneObstacle(obs, i, squareSprite, obstacleLayer);
        }
    }

    // Returns true if the obstacle's axis-aligned bounding rectangle overlaps
    // a circle of `safeRadius` around the world origin (where the core lives).
    // Conservative: ignores rotation, treats rotated rects as their AABB.
    bool IntersectsCore(MapLayoutDefinition.LayoutObstacle obs, float safeRadius)
    {
        // Closest point on the obstacle rect to origin
        float halfW = obs.size.x * 0.5f;
        float halfH = obs.size.y * 0.5f;
        float closestX = Mathf.Clamp(0f, obs.position.x - halfW, obs.position.x + halfW);
        float closestY = Mathf.Clamp(0f, obs.position.y - halfH, obs.position.y + halfH);
        float dx = closestX;
        float dy = closestY;
        return (dx * dx + dy * dy) < (safeRadius * safeRadius);
    }

    // Creates one obstacle: a single sprite + segmented colliders.
    // Uses YSortEntity so the obstacle sorts correctly against grass/players/enemies.
    //
    // - blockMovement obstacles: solid grey "wall/building" look
    // - decorative obstacles (moat, bridges): keep their layout-specified color
    void CreateOneObstacle(MapLayoutDefinition.LayoutObstacle obs, int i, Sprite squareSprite, int obstacleLayer)
    {
        string name = string.IsNullOrEmpty(obs.label) ? $"Obstacle_{i}" : obs.label;
        GameObject root = new GameObject(name);
        root.transform.parent = obstaclesContainer.transform;
        root.transform.position = new Vector3(obs.position.x, obs.position.y, 0f);
        root.transform.rotation = Quaternion.Euler(0f, 0f, obs.rotationDegrees);

        GameObject visualGO = new GameObject("Visual");
        visualGO.transform.parent = root.transform;
        visualGO.transform.localPosition = Vector3.zero;
        visualGO.transform.localRotation = Quaternion.identity;
        visualGO.transform.localScale = new Vector3(obs.size.x, obs.size.y, 1f);

        var sr = visualGO.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Default";

        if (obs.blocksMovement)
        {
            // Wall/building — stone-textured sprite, scaled to obstacle size.
            // The procedural stone uses isotropic Perlin noise so stretching
            // doesn't produce obviously horizontal/vertical artifacts.
            sr.sprite = CreateRoundedSquareSprite();
            sr.color = Color.white;
            visualGO.transform.localScale = new Vector3(obs.size.x, obs.size.y, 1f);

            // Set sortingOrder directly using the same formula as GrassCartoonOverlay
            // and YSortEntity. Obstacles never move, so we don't need a per-frame
            // updater — set it once and forget. Sort point = bottom edge so the
            // obstacle behaves like a sprite "standing" at its base.
            const int sortOrderBase = 1000;
            const float sortPrecision = 10f;
            float sortY = obs.position.y - obs.size.y * 0.5f;
            sr.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);

            CreateSegmentedColliders(root, obs.size, obstacleLayer);
        }
        else
        {
            // Decorative — flat sprite below all gameplay (moat, bridges, etc.)
            sr.sprite = squareSprite;
            sr.color = obs.color.a > 0f ? obs.color : new Color(0.35f, 0.40f, 0.50f, 0.85f);
            sr.sortingOrder = 600; // above terrain (-1), below grass+sprites
        }
    }

    // Cache the stone texture so all obstacles share one (saves memory).
    private static Sprite cachedStoneSprite;

    // Creates a stone-textured sprite with rough irregular edges and
    // darker noise patterns suggesting cracks and mineral variation.
    Sprite CreateRoundedSquareSprite()
    {
        if (cachedStoneSprite != null) return cachedStoneSprite;

        const int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        // Edge irregularity — perturb the rectangle's outline using low-freq noise.
        // The result reads as rough stone instead of a clean rectangle.
        const float edgeNoiseScale = 0.08f;
        const float edgeNoiseAmp = size * 0.06f;     // up to ~6% jitter on edges
        const float baseInset = size * 0.04f;        // 4% inset baseline

        // Surface noise — multiple octaves of value noise.
        const float surfaceScale1 = 0.06f;
        const float surfaceScale2 = 0.18f;
        const float surfaceScale3 = 0.45f;

        // Stone palette: mid-grey with slight cool tint, darker cracks.
        Color stoneLight = new Color(0.62f, 0.63f, 0.65f, 1f);
        Color stoneMid = new Color(0.48f, 0.49f, 0.52f, 1f);
        Color stoneDark = new Color(0.30f, 0.31f, 0.34f, 1f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                //  Edge with noise-perturbed outline 
                float dx = x - halfSize;
                float dy = y - halfSize;

                // Direction-dependent edge noise — different perturbation per side
                float edgeNoise = (Mathf.PerlinNoise(x * edgeNoiseScale, y * edgeNoiseScale) - 0.5f) * 2f * edgeNoiseAmp;

                float halfX = halfSize - baseInset + edgeNoise;
                float halfY = halfSize - baseInset + edgeNoise;

                // Corner roundness: combine x/y distances with a soft corner radius
                float cornerRadius = size * 0.08f;
                float adx = Mathf.Abs(dx);
                float ady = Mathf.Abs(dy);
                float cornerDx = Mathf.Max(0f, adx - (halfX - cornerRadius));
                float cornerDy = Mathf.Max(0f, ady - (halfY - cornerRadius));
                float cornerDist = Mathf.Sqrt(cornerDx * cornerDx + cornerDy * cornerDy);

                float inside;
                if (adx <= halfX - cornerRadius && ady <= halfY - cornerRadius)
                    inside = 1f;
                else if (cornerDist <= cornerRadius - 1f)
                    inside = 1f;
                else if (cornerDist <= cornerRadius)
                    inside = 1f - (cornerDist - (cornerRadius - 1f));
                else
                    inside = 0f;

                if (inside <= 0f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                //  Stone surface with multi-octave noise 
                float n1 = Mathf.PerlinNoise(x * surfaceScale1, y * surfaceScale1);
                float n2 = Mathf.PerlinNoise(x * surfaceScale2 + 7.3f, y * surfaceScale2 + 4.1f) * 0.5f;
                float n3 = Mathf.PerlinNoise(x * surfaceScale3 + 13.7f, y * surfaceScale3 + 9.2f) * 0.25f;
                float surface = (n1 + n2 + n3) / 1.75f; // normalise to ~0..1

                // Add subtle "cracks" — darken where noise is very low
                float crack = surface < 0.32f ? Mathf.InverseLerp(0.32f, 0.18f, surface) : 0f;

                // Blend stone palette: dark→mid→light by surface value
                Color stone;
                if (surface < 0.5f)
                    stone = Color.Lerp(stoneMid, stoneDark, (0.5f - surface) * 1.4f);
                else
                    stone = Color.Lerp(stoneMid, stoneLight, (surface - 0.5f) * 1.4f);

                // Apply crack darkening
                stone = Color.Lerp(stone, stoneDark * 0.7f, crack);

                // Slight darkening near edges for depth
                float edgeDist = Mathf.Min(halfX - adx, halfY - ady);
                float edgeDarkening = Mathf.InverseLerp(0f, size * 0.04f, edgeDist);
                stone = Color.Lerp(stoneDark, stone, edgeDarkening);

                stone.a = inside;
                pixels[y * size + x] = stone;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cachedStoneSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, size);
        return cachedStoneSprite;
    }

    // Plain 64×64 white square (used by decorative obstacles).
    Sprite CreateSimpleSquareSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 64f);
    }

    // Creates a grid of small BoxCollider2Ds covering the obstacle area, so
    // enemies treat each one as an individual obstacle and can navigate around
    // segment edges instead of getting stuck on a giant wall's center.
    void CreateSegmentedColliders(GameObject root, Vector2 size, int obstacleLayer)
    {
        float seg = Mathf.Max(0.5f, maxColliderSegmentSize);

        int cols = Mathf.Max(1, Mathf.CeilToInt(size.x / seg));
        int rows = Mathf.Max(1, Mathf.CeilToInt(size.y / seg));

        float cellW = size.x / cols;
        float cellH = size.y / rows;
        float startX = -size.x * 0.5f + cellW * 0.5f;
        float startY = -size.y * 0.5f + cellH * 0.5f;

        for (int cx = 0; cx < cols; cx++)
        {
            for (int cy = 0; cy < rows; cy++)
            {
                GameObject seg2 = new GameObject($"Col_{cx}_{cy}");
                seg2.transform.parent = root.transform;
                seg2.transform.localRotation = Quaternion.identity;
                seg2.transform.localScale = Vector3.one;
                seg2.transform.localPosition = new Vector3(
                    startX + cx * cellW,
                    startY + cy * cellH,
                    0f);
                seg2.layer = obstacleLayer;

                var col = seg2.AddComponent<BoxCollider2D>();
                col.size = new Vector2(cellW * 0.95f, cellH * 0.95f); // small gap so segments are individually findable
                col.isTrigger = false;
            }
        }
    }

    void CreateRingSlots(RingConfiguration ring)
    {
        GameObject ringContainer = new GameObject($"Ring_R{ring.radius}_S{ring.slotCount}");
        ringContainer.transform.parent = slotsContainer.transform;
        ringContainer.transform.localPosition = Vector3.zero;

        float angleStep = 360f / ring.slotCount;

        for (int i = 0; i < ring.slotCount; i++)
        {
            float angle = (i * angleStep + ring.rotationOffset) * Mathf.Deg2Rad;
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * ring.radius,
                Mathf.Sin(angle) * ring.radius,
                0f
            );

            GameObject slotObj = CreateTowerSlot(position, ring.slotSize, i);
            slotObj.transform.parent = ringContainer.transform;
            slotObj.name = $"Slot_{i}";

            TowerSlot slot = slotObj.GetComponent<TowerSlot>();
            slot.ringIndex = rings.IndexOf(ring);
            slot.slotIndex = i;

            allTowerSlots.Add(slot);
        }
    }

    GameObject CreateTowerSlot(Vector3 position, float size, int index)
    {
        GameObject slot;

        if (towerSlotPrefab != null)
        {
            slot = Instantiate(towerSlotPrefab, position, Quaternion.identity);

            // Scale prefab to match desired size
            var sr = slot.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                float currentDiameter = sr.bounds.size.x;
                float desiredDiameter = size * 0.3f;
                float scaleFactor = desiredDiameter / currentDiameter;
                slot.transform.localScale = Vector3.one * scaleFactor;
            }

            var col = slot.GetComponent<CircleCollider2D>();
            if (col != null)
            {
                col.radius = size * 0.5f;
            }
        }
        else
        {
            // Create default slot
            slot = new GameObject("TowerSlot");
            slot.transform.position = position;

            var renderer = slot.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSimpleCircleSprite();
            renderer.color = new Color(1f, 1f, 1f, 0.5f);
            renderer.sortingOrder = 1;

            // Scale to desired size
            float desiredDiameter = size;
            float currentSize = 0.64f;
            float scale = desiredDiameter / currentSize;
            slot.transform.localScale = Vector3.one * scale;

            var collider = slot.AddComponent<CircleCollider2D>();
            collider.radius = size * 0.5f;
            collider.isTrigger = true;
        }

        // Ensure TowerSlot component exists
        if (slot.GetComponent<TowerSlot>() == null)
        {
            slot.AddComponent<TowerSlot>();
        }

        // Force the slot to be Untagged so EnemyController.UpdateTarget() never
        // mistakes an empty slot for a real tower. Only actual placed towers
        // (instantiated by TowerSlot.PlaceTower) carry the "Tower" tag.
        if (slot.CompareTag("Tower"))
            slot.tag = "Untagged";

        return slot;
    }

    Sprite CreateSimpleCircleSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                colors[y * size + x] = distance <= radius ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    void DrawDebugCircles()
    {
        // ── Custom layouts: draw their own connection lines, skip rings ─────
        if (activeLayout != null && activeLayout.layoutType == MapLayoutDefinition.LayoutType.Custom)
        {
            DrawLayoutConnectionLines();
            return;
        }

        // ── Concentric layouts (or no layout): draw the rings as before ─────
        List<RingConfiguration> sourceRings =
            (activeLayout != null && activeLayout.rings != null && activeLayout.rings.Count > 0)
            ? activeLayout.rings
            : rings;

        foreach (var ring in sourceRings)
        {
            if (!ring.enabled) continue;

            GameObject debugCircle = new GameObject($"Debug_Ring_{ring.radius}");
            debugCircle.transform.parent = transform;
            debugCircle.transform.localPosition = Vector3.zero;

            LineRenderer lr = debugCircle.AddComponent<LineRenderer>();
            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.color = debugCircleColor;
            lr.material = lineMaterial;
            lr.startColor = debugCircleColor;
            lr.endColor = debugCircleColor;
            lr.startWidth = debugCircleWidth;
            lr.endWidth = debugCircleWidth;
            lr.useWorldSpace = false;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 500;  // above terrain (-1), below all gameplay sprites

            int segments = 64;
            lr.positionCount = segments + 1;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * 2f * Mathf.PI;
                Vector3 pos = new Vector3(
                    Mathf.Cos(angle) * ring.radius,
                    Mathf.Sin(angle) * ring.radius,
                    0f
                );
                lr.SetPosition(i, pos);
            }
        }
    }

    // Draws the polylines defined by the active custom layout. Each line
    // becomes its own LineRenderer GameObject under the map.
    void DrawLayoutConnectionLines()
    {
        if (activeLayout == null || activeLayout.connectionLines == null) return;

        for (int idx = 0; idx < activeLayout.connectionLines.Count; idx++)
        {
            var line = activeLayout.connectionLines[idx];
            if (line == null || line.points == null || line.points.Count < 2) continue;

            GameObject lineObj = new GameObject($"LayoutLine_{idx}");
            lineObj.transform.parent = transform;
            lineObj.transform.localPosition = Vector3.zero;

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = line.color;
            lr.material = mat;
            lr.startColor = line.color;
            lr.endColor = line.color;
            lr.startWidth = line.width;
            lr.endWidth = line.width;
            lr.useWorldSpace = false;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = 500;  // above terrain (-1), below all gameplay sprites
            lr.loop = line.closed;

            lr.positionCount = line.points.Count;
            for (int i = 0; i < line.points.Count; i++)
                lr.SetPosition(i, new Vector3(line.points[i].x, line.points[i].y, 0f));
        }
    }

    // Central Core event handlers
    private void OnCoreEnergyChanged(float newEnergy)
    {
        // Handle core energy changes if needed
    }

    private void OnCoreEnergyDepleted()
    {
        Debug.Log("Core energy depleted! Game Over");
        if (GameOrchestrator.Instance != null)
            GameOrchestrator.Instance.TriggerGameOver();
    }

    //  Layout API (called by orchestrator) 
    public void ApplyLayout(MapLayoutDefinition layout)
    {
        // FIX: Compare against the SOURCE layout (the asset reference passed in
        // by the orchestrator), not the scaled-clone activeLayout. When scale
        // != 1.0 we replace activeLayout with a fresh clone every call, so the
        // old `layout == activeLayout` check always missed and rebuilt the map
        // every stage — destroying all placed towers in the process.
        if (sourceLayoutCaptured &&
            layout == sourceLayout &&
            Mathf.Approximately(layoutSpreadScale, lastAppliedSpreadScale))
        {
            // Same source layout AND same scale — preserve everything.
            Debug.Log($"[TowerDefenseMap] ApplyLayout: same layout '{(layout != null ? layout.layoutName : "null")}'" +
                      $" — preserving towers/slots, no rebuild.");
            return;
        }

        // Capture the authored mapRadius once so we can rescale from the
        // original value every time layoutSpreadScale changes.
        if (!baseMapRadiusCaptured)
        {
            baseMapRadius = mapRadius;
            baseMapRadiusCaptured = true;
        }

        // Build a scaled working copy so we never mutate the source asset.
        // When scale == 1.0 we skip the clone for zero overhead (and keep the
        // exact reference so other systems comparing to the asset still match).
        if (layout != null && !Mathf.Approximately(layoutSpreadScale, 1f))
        {
            activeLayout = CreateScaledLayout(layout, layoutSpreadScale);
            // Auto-scale mapRadius proportionally so outer slots / obstacles
            // don't run past the border ring.
            mapRadius = baseMapRadius * layoutSpreadScale;
        }
        else
        {
            activeLayout = layout;
            mapRadius = baseMapRadius;
        }

        // Remember source asset + scale we just applied so the next call can
        // short-circuit if neither has changed.
        sourceLayout = layout;
        lastAppliedSpreadScale = layoutSpreadScale;
        sourceLayoutCaptured = true;

        GenerateMap();

        if (layout != null)
        {
            string scaleNote = Mathf.Approximately(layoutSpreadScale, 1f)
                ? ""
                : $" (spread ×{layoutSpreadScale:F2})";
            Debug.Log($"[TowerDefenseMap] Layout applied: {layout.layoutName}{scaleNote}");
        }
        else
        {
            Debug.Log("[TowerDefenseMap] Reverted to default rings.");
        }
    }

    // Creates a scaled clone of the supplied layout. All position-like fields
    // are multiplied by `scale`; slot SIZES are intentionally left untouched
    // so towers stay the same physical size while spreading further apart.
    private MapLayoutDefinition CreateScaledLayout(MapLayoutDefinition src, float scale)
    {
        var copy = ScriptableObject.CreateInstance<MapLayoutDefinition>();
        copy.layoutName = src.layoutName;
        copy.description = src.description;
        copy.layoutType = src.layoutType;

        // Concentric rings — scale radius only, keep slotCount/slotSize/rotation/enabled.
        copy.rings = new List<RingConfiguration>();
        if (src.rings != null)
        {
            foreach (var r in src.rings)
            {
                if (r == null) continue;
                copy.rings.Add(new RingConfiguration
                {
                    radius = r.radius * scale,
                    slotCount = r.slotCount,
                    slotSize = r.slotSize,        // unchanged
                    rotationOffset = r.rotationOffset,
                    enabled = r.enabled,
                });
            }
        }

        // Custom slot positions.
        copy.customSlotPositions = new List<Vector2>();
        if (src.customSlotPositions != null)
        {
            foreach (var p in src.customSlotPositions)
                copy.customSlotPositions.Add(p * scale);
        }
        copy.customSlotSize = src.customSlotSize;       // unchanged

        // Bonus slot positions.
        copy.bonusSlotPositions = new List<Vector2>();
        if (src.bonusSlotPositions != null)
        {
            foreach (var p in src.bonusSlotPositions)
                copy.bonusSlotPositions.Add(p * scale);
        }
        copy.bonusSlotSize = src.bonusSlotSize;         // unchanged

        // Obstacles — scale position AND size so walls/buildings keep
        // proportional length relative to the layout. Rotation/color/flags stay.
        copy.obstacles = new List<MapLayoutDefinition.LayoutObstacle>();
        if (src.obstacles != null)
        {
            foreach (var o in src.obstacles)
            {
                copy.obstacles.Add(new MapLayoutDefinition.LayoutObstacle
                {
                    position = o.position * scale,
                    size = o.size * scale,
                    rotationDegrees = o.rotationDegrees,
                    color = o.color,
                    blocksMovement = o.blocksMovement,
                    label = o.label,
                });
            }
        }

        // Connection lines — scale every point, keep width/colour/closed flag.
        copy.connectionLines = new List<MapLayoutDefinition.ConnectionLine>();
        if (src.connectionLines != null)
        {
            foreach (var line in src.connectionLines)
            {
                if (line == null) continue;
                var newLine = new MapLayoutDefinition.ConnectionLine
                {
                    closed = line.closed,
                    color = line.color,
                    width = line.width,
                    points = new List<Vector2>(),
                };
                if (line.points != null)
                {
                    foreach (var pt in line.points)
                        newLine.points.Add(pt * scale);
                }
                copy.connectionLines.Add(newLine);
            }
        }

        // Mark as DontSave so the clone is never accidentally serialised
        // and gets garbage-collected naturally between layout swaps.
        copy.hideFlags = HideFlags.DontSave;
        return copy;
    }

    // Reveal one or more bonus slots from the active layout's bonusSlotPositions.
    // Called by the "additional_tower_slots" augment handler.

    public int AddBonusSlots(int count)
    {
        if (activeLayout == null ||
            activeLayout.bonusSlotPositions == null ||
            activeLayout.bonusSlotPositions.Count == 0)
        {
            Debug.LogWarning("[TowerDefenseMap] No bonus slot positions defined in the active layout.");
            return 0;
        }

        int available = activeLayout.bonusSlotPositions.Count - bonusSlotsAdded;
        if (available <= 0)
        {
            Debug.LogWarning("[TowerDefenseMap] All bonus slots already revealed.");
            return 0;
        }

        int toAdd = Mathf.Min(count, available);
        float size = activeLayout.bonusSlotSize;

        for (int i = 0; i < toAdd; i++)
        {
            Vector2 pos2d = activeLayout.bonusSlotPositions[bonusSlotsAdded + i];
            Vector3 pos = new Vector3(pos2d.x, pos2d.y, 0f);

            // Use the existing slotsContainer so everything stays tidy.
            if (slotsContainer == null)
            {
                slotsContainer = new GameObject("Tower Slots");
                slotsContainer.transform.parent = transform;
                slotsContainer.transform.localPosition = Vector3.zero;
            }

            int globalIndex = allTowerSlots.Count;
            GameObject slotObj = CreateTowerSlot(pos, size, globalIndex);
            slotObj.transform.parent = slotsContainer.transform;
            slotObj.name = $"BonusSlot_{bonusSlotsAdded + i}";

            TowerSlot slot = slotObj.GetComponent<TowerSlot>();
            slot.ringIndex = 99; // sentinel: bonus slot
            slot.slotIndex = bonusSlotsAdded + i;
            allTowerSlots.Add(slot);
        }

        bonusSlotsAdded += toAdd;
        Debug.Log($"[TowerDefenseMap] Added {toAdd} bonus slot(s). Total bonus slots: {bonusSlotsAdded}/{activeLayout.bonusSlotPositions.Count}");
        return toAdd;
    }

    //  Original ring API 

    public void AddRing(float radius, int slotCount, float slotSize = 1f, float rotationOffset = 0f)
    {
        RingConfiguration newRing = new RingConfiguration
        {
            radius = radius,
            slotCount = slotCount,
            slotSize = slotSize,
            rotationOffset = rotationOffset,
            enabled = true
        };
        rings.Add(newRing);
        // TODO: Handle regeneration after augmentation
    }

    public void RemoveRing(int ringIndex)
    {
        if (ringIndex >= 0 && ringIndex < rings.Count)
        {
            rings.RemoveAt(ringIndex);
        }
    }

    public TowerSlot GetSlot(int ringIndex, int slotIndex)
    {
        foreach (var slot in allTowerSlots)
        {
            if (slot.ringIndex == ringIndex && slot.slotIndex == slotIndex)
            {
                return slot;
            }
        }
        return null;
    }

    public List<TowerSlot> GetAllSlots()
    {
        return new List<TowerSlot>(allTowerSlots);
    }

    public List<TowerSlot> GetAvailableSlots()
    {
        return allTowerSlots.FindAll(slot => !slot.IsOccupied);
    }

    public CentralCore GetCentralCore()
    {
        return centralCore;
    }

    public bool HasCentralCore()
    {
        return centralCore != null;
    }

    // Runtime background switching methods
    public void SetBackgroundImage(string imagePath)
    {
        backgroundImagePath = imagePath;
        useBackgroundImage = true;
        if (terrainObject != null)
        {
            DestroyImmediate(terrainObject);
        }
        CreateTerrain();
    }

    public void UseGeneratedTerrain()
    {
        useBackgroundImage = false;
        if (terrainObject != null)
        {
            DestroyImmediate(terrainObject);
        }
        CreateTerrain();
    }

    // Utility methods
    [ContextMenu("Tile Background to Map Radius")]
    public void ScaleBackgroundToMapRadius()
    {
        if (backgroundGameObject != null)
        {
            // Keep native size — use tiling for coverage
            backgroundGameObject.transform.localScale = Vector3.one;

            BackgroundTiler tiler = backgroundGameObject.GetComponent<BackgroundTiler>();
            if (tiler == null)
                tiler = backgroundGameObject.AddComponent<BackgroundTiler>();
            tiler.autoCalculateGrid = true;
            tiler.coverageRadius = mapRadius + 5f;
            tiler.GenerateTiles();

            Debug.Log($"Tiled background at native size to cover map radius {mapRadius}");
        }
    }

    [ContextMenu("Fix Central Core Position")]
    public void FixCentralCorePosition()
    {
        if (centralCore != null)
        {
            centralCore.transform.position = Vector3.zero;
            centralCore.transform.localPosition = Vector3.zero;
            Debug.Log("Central Core position fixed to (0,0,0)");
        }
        else
        {
            Debug.LogError("Central Core not found!");
        }
    }

    void OnDestroy()
    {
        // Clean up event subscriptions
        if (centralCore != null)
        {
            centralCore.OnEnergyChanged -= OnCoreEnergyChanged;
            centralCore.OnEnergyDepleted -= OnCoreEnergyDepleted;
        }
    }
}
