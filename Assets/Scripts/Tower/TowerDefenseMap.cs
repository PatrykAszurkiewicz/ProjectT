using System.Collections.Generic;
using UnityEngine;

public class TowerDefenseMap : MonoBehaviour
{
    [Header("Map Configuration")]
    public float mapRadius = 10f;
    public GameObject backgroundGameObject; // Manual background GameObject reference
    public string backgroundImagePath = "Backgrounds/Background3"; // Fallback for generated terrain
    public bool useBackgroundImage = true;
    public Material terrainMaterial;
    public Color terrainColor = Color.green;

    [Header("Tower Slot Configuration")]
    public GameObject towerSlotPrefab;
    public List<RingConfiguration> rings = new List<RingConfiguration>();

    [Header("Central Core Configuration")]
    public bool enableCentralCore = true;
    public float coreSize = 2f;
    public float coreMaxEnergy = 100f;
    public float coreStartingEnergy = 100f;

    [Header("Visual Settings")]
    public bool showDebugCircles = true;
    public Color debugCircleColor = Color.white;
    public float debugCircleWidth = 0.02f;

    private List<TowerSlot> allTowerSlots = new List<TowerSlot>();
    private GameObject terrainObject;
    private GameObject slotsContainer;
    private CentralCore centralCore;

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
            rings.Add(new RingConfiguration { radius = 3.8f, slotCount = 8, slotSize = 1.9f });
        }
        GenerateMap();
    }

    [ContextMenu("Generate Map")]
    public void GenerateMap()
    {
        ClearExistingMap();
        CreateTerrain();
        CreateCentralCore();
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

                    // Scale to fit map radius
                    float textureSize = Mathf.Min(backgroundTexture.width, backgroundTexture.height) / 100f;
                    float desiredSize = mapRadius * 2f;
                    float scale = desiredSize / textureSize;
                    terrainObject.transform.localScale = Vector3.one * scale;
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

        foreach (var ring in rings)
        {
            if (!ring.enabled) continue;
            CreateRingSlots(ring);
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
        foreach (var ring in rings)
        {
            if (!ring.enabled) continue;

            GameObject debugCircle = new GameObject($"Debug_Ring_{ring.radius}");
            debugCircle.transform.parent = transform;
            debugCircle.transform.localPosition = Vector3.zero;

            LineRenderer lr = debugCircle.AddComponent<LineRenderer>();
            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.color = debugCircleColor;
            lr.material = lineMaterial;
            lr.startWidth = debugCircleWidth;
            lr.endWidth = debugCircleWidth;
            lr.useWorldSpace = false;
            lr.sortingOrder = 2;

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

    // Central Core event handlers
    private void OnCoreEnergyChanged(float newEnergy)
    {
        // Handle core energy changes if needed
    }

    private void OnCoreEnergyDepleted()
    {
        Debug.Log("Core energy depleted! Game Over?");
        // TODO: Handle Game Over in Orchestrator
    }

    // Public API for runtime modifications
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
    [ContextMenu("Scale Background to Map Radius")]
    public void ScaleBackgroundToMapRadius()
    {
        if (backgroundGameObject != null)
        {
            var renderer = backgroundGameObject.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                float spriteSize = Mathf.Min(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y);
                float desiredSize = mapRadius * 2f;
                float scale = desiredSize / spriteSize;
                backgroundGameObject.transform.localScale = Vector3.one * scale;
                Debug.Log($"Scaled background to {scale} to fit map radius {mapRadius}");
            }
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