using System.Collections.Generic;
using UnityEngine;

public class TowerDefenseMap : MonoBehaviour
{
    [Header("Map Configuration")]
    public float mapRadius = 10f;
    public string backgroundImagePath = "Backgrounds/Background3"; // Path relative to Resources folder
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
        // Destroy existing terrain
        if (terrainObject != null)
        {
            DestroyImmediate(terrainObject);
        }
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
        // Create terrain object
        terrainObject = new GameObject("Terrain");
        terrainObject.transform.parent = transform;
        terrainObject.transform.localPosition = Vector3.zero;
        // Add sprite renderer
        var renderer = terrainObject.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = -1;
        if (useBackgroundImage && !string.IsNullOrEmpty(backgroundImagePath))
        {
            // Load background image from Resources
            Texture2D backgroundTexture = Resources.Load<Texture2D>(backgroundImagePath);
            if (backgroundTexture != null)
            {
                // Create sprite from the loaded texture
                Sprite backgroundSprite = Sprite.Create(
                    backgroundTexture,
                    new Rect(0, 0, backgroundTexture.width, backgroundTexture.height),
                    Vector2.one * 0.5f,
                    100f // pixels per unit
                );

                renderer.sprite = backgroundSprite;
                renderer.color = Color.white;
                // Scale the background to fit the desired map radius
                float textureSize = Mathf.Min(backgroundTexture.width, backgroundTexture.height) / 100f; // Convert to world units
                float desiredSize = mapRadius * 2f;
                float scale = desiredSize / textureSize;
                terrainObject.transform.localScale = Vector3.one * scale;
                //Debug.Log($"Background image loaded: {backgroundImagePath}, scaled to {scale}");
            }
            else
            {
                Debug.LogWarning($"Background image not found at path: {backgroundImagePath}. Using fallback circle.");
                CreateFallbackTerrain(renderer);
            }
        }
        else
        {
            // Fallback to pattern
            CreateFallbackTerrain(renderer);
        }
        // TODO fix collider for boundaries
        var collider = terrainObject.AddComponent<CircleCollider2D>();
        collider.radius = mapRadius;
        collider.isTrigger = true;
    }

    void CreateFallbackTerrain(SpriteRenderer renderer)
    {
        // Create the simple circle sprite as fallback
        renderer.sprite = CreateSimpleCircleSprite();
        renderer.color = terrainColor;
        // Scale the terrain to the desired size
        float desiredDiameter = mapRadius * 2f;
        float currentSize = 0.64f; // Our sprite size in world units
        float scale = desiredDiameter / currentSize;
        terrainObject.transform.localScale = Vector3.one * scale;
    }


    void CreateCentralCore()
    {
        if (!enableCentralCore) return;

        // Create central core GameObject
        GameObject coreObject = new GameObject("CentralCore");
        coreObject.transform.parent = transform;

        // FIX: Force the Central Core to be at exactly (0,0,0) in world space
        coreObject.transform.position = Vector3.zero;
        coreObject.transform.localPosition = Vector3.zero;

        // Double-check the position is correct
        Debug.Log($"Central Core created at world position: {coreObject.transform.position}");
        Debug.Log($"Central Core local position: {coreObject.transform.localPosition}");

        // Add CentralCore component
        centralCore = coreObject.AddComponent<CentralCore>();
        centralCore.maxEnergy = coreMaxEnergy;
        centralCore.currentEnergy = coreStartingEnergy;
        centralCore.coreSize = coreSize;

        // Subscribe to core events
        centralCore.OnEnergyChanged += OnCoreEnergyChanged;
        centralCore.OnEnergyDepleted += OnCoreEnergyDepleted;

        // Verify position after component setup
        Debug.Log($"Final Central Core position after setup: {coreObject.transform.position}");
    }
    [ContextMenu("Fix Central Core Position")]
    public void FixCentralCorePosition()
    {
        if (centralCore != null)
        {
            Debug.Log($"Before fix - Central Core position: {centralCore.transform.position}");
            centralCore.transform.position = Vector3.zero;
            centralCore.transform.localPosition = Vector3.zero;
            Debug.Log($"After fix - Central Core position: {centralCore.transform.position}");
        }
        else
        {
            Debug.LogError("Central Core not found!");
        }
    }
    // Add this new coroutine to TowerDefenseMap.cs:
    private System.Collections.IEnumerator ForceRegisterCore()
    {
        // Wait a bit to ensure EnergyManager is ready
        yield return new WaitForSeconds(0.5f);

        if (centralCore != null && EnergyManager.Instance != null)
        {
            Debug.Log("FORCE REGISTERING Central Core with EnergyManager");
            EnergyManager.Instance.RegisterEnergyConsumer(centralCore);

            // Verify registration
            var consumers = EnergyManager.Instance.GetAllEnergyConsumers();
            bool found = consumers.Contains(centralCore);
            Debug.Log($"Central Core registration verified: {found}");
        }
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

            var sr = slot.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                // Get current diameter in world units (x-size of the sprite)
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
            // Add visual representation
            var renderer = slot.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSimpleCircleSprite();
            renderer.color = new Color(1f, 1f, 1f, 0.5f);
            renderer.sortingOrder = 1;
            // Scale the slot to the desired size
            float desiredDiameter = size;
            float currentSize = 0.64f; // Our sprite size in world units
            float scale = desiredDiameter / currentSize;
            slot.transform.localScale = Vector3.one * scale;
            // Add collider
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
        int size = 64; // Small, fixed size
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f; // Leave some border
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

        // Create sprite with 100 pixels per unit for consistent scaling
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
        //Debug.Log($"Core energy changed: {newEnergy}/{coreMaxEnergy}");
    }

    private void OnCoreEnergyDepleted()
    {
        Debug.Log("Core energy depleted! Game Over?");
        // TODO Handle Game Over in Orchestrator
    }

    // Public methods for runtime modifications
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
        // TODO How to handle regeneration after ring addition after Augment
        // GenerateMap();
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

    // Central Core access methods
    public CentralCore GetCentralCore()
    {
        return centralCore;
    }

    public bool HasCentralCore()
    {
        return centralCore != null;
    }

    // Method to change background image at runtime
    public void SetBackgroundImage(string imagePath)
    {
        backgroundImagePath = imagePath;
        useBackgroundImage = true;
        // Regenerate only the terrain part
        if (terrainObject != null)
        {
            DestroyImmediate(terrainObject);
        }
        CreateTerrain();
    }

    // Method to switch back to generated circle terrain
    public void UseGeneratedTerrain()
    {
        useBackgroundImage = false;
        // Regenerate only the terrain part
        if (terrainObject != null)
        {
            DestroyImmediate(terrainObject);
        }
        CreateTerrain();
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