using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;

public class TowerPlacementManager : MonoBehaviour
{
    public static TowerPlacementManager Instance;

    [Header("Tower Prefabs - Assign Individual Towers")]
    public GameObject towerPrefab1;
    public GameObject towerPrefab2;
    public GameObject towerPrefab3;
    public GameObject towerPrefab4;
    public GameObject towerPrefab5;
    public GameObject towerPrefab6;

    [System.NonSerialized]
    private List<GameObject> towerPrefabs = new List<GameObject>();

    public int selectedTowerIndex = 0;

    [Header("Player Proximity Settings")]
    public float buildRange = 1.2f;
    public Transform playerTransform;
    public bool requirePlayerProximity = true;

    [Header("Tower Creation Animation")]
    public string towerCreationSpritePath = "Sprites/tower_creation_decay_spritesheet4";
    public float creationAnimationSpeed = 0.1f;
    public bool playCreationAnimation = true;

    [Header("Energy Repair Settings")]
    public int energyRepairAmount = 10;
    public float energyRepairCooldown = 0.2f;

    [Header("Continuous Supply Settings")]
    public bool useContinuousSupply = true; // Toggle between old and new behavior

    [Header("UI References")]
    public GameObject towerSelectionUI;

    private TowerDefenseMap mapGenerator;
    private bool isPlacementMode = false;

    [System.NonSerialized]
    private List<TowerSlot> allSlots = new List<TowerSlot>();
    [System.NonSerialized]
    private TowerSlot currentHighlightedSlot = null;
    [System.NonSerialized]
    private IEnergyConsumer currentHighlightedConsumer = null;
    [System.NonSerialized]
    private TowerSelectionWheel selectionWheel;

    private bool clickProcessed = false;
    private float lastRepairTime = -Mathf.Infinity;

    // Continuous supply system
    private bool isCurrentlySupplying = false;
    private IEnergyConsumer currentSupplyTarget = null;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        // Initialize the Non-Serialized fields
        allSlots = new List<TowerSlot>();
        towerPrefabs = new List<GameObject>();
    }

    void Start()
    {
        mapGenerator = FindFirstObjectByType<TowerDefenseMap>();
        if (playerTransform == null)
        {
            PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
            if (player != null)
            {
                playerTransform = player.transform;
            }
            else
            {
                requirePlayerProximity = false;
            }
        }

        // Build tower list from individual prefab fields
        BuildTowerList();

        // Create selection wheel
        CreateSelectionWheel();
    }

    void BuildTowerList()
    {
        towerPrefabs = new List<GameObject>();

        if (towerPrefab1 != null) towerPrefabs.Add(towerPrefab1);
        if (towerPrefab2 != null) towerPrefabs.Add(towerPrefab2);
        if (towerPrefab3 != null) towerPrefabs.Add(towerPrefab3);
        if (towerPrefab4 != null) towerPrefabs.Add(towerPrefab4);
        if (towerPrefab5 != null) towerPrefabs.Add(towerPrefab5);
        if (towerPrefab6 != null) towerPrefabs.Add(towerPrefab6);
    }

    void CreateSelectionWheel()
    {
        GameObject wheelObj = new GameObject("TowerSelectionWheel");
        selectionWheel = wheelObj.AddComponent<TowerSelectionWheel>();
        wheelObj.SetActive(false);
    }

    // Public method to expose placement mode state for EnergyManager
    public bool IsInPlacementMode()
    {
        return isPlacementMode;
    }

    void Update()
    {
        HandleInput();
        HandleMouseClicks();
        if (isPlacementMode && requirePlayerProximity && playerTransform != null && Time.frameCount % 5 == 0)
        {
            UpdateSlotHighlights();
            UpdateEnergyConsumerHighlights();
        }
    }

    void UpdateEnergyConsumerHighlights()
    {
        // Clear previous highlight
        if (currentHighlightedConsumer != null)
        {
            SetConsumerHighlight(currentHighlightedConsumer, false);
            currentHighlightedConsumer = null;
        }

        if (!requirePlayerProximity || playerTransform == null || EnergyManager.Instance == null)
            return;

        // Find closest energy consumer in range that needs energy
        IEnergyConsumer closestConsumer = null;
        float closestDistance = float.MaxValue;

        var consumersInRange = EnergyManager.Instance.GetEnergyConsumersInRange(playerTransform.position, buildRange);

        foreach (var consumer in consumersInRange)
        {
            if (consumer == null) continue;

            float distance = Vector3.Distance(playerTransform.position, consumer.GetPosition());
            bool canRepair = consumer.GetEnergyPercentage() < 0.999f;
            bool isCentralCore = consumer is CentralCore;

            if (!canRepair && !isCentralCore) continue;

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestConsumer = consumer;
            }
        }

        // Highlight the closest consumer
        if (closestConsumer != null)
        {
            SetConsumerHighlight(closestConsumer, true);
            currentHighlightedConsumer = closestConsumer;
        }
    }

    void SetConsumerHighlight(IEnergyConsumer consumer, bool highlight)
    {
        if (consumer is CentralCore core)
        {
            core.SetHighlight(highlight);
        }
        else if (consumer is MonoBehaviour mb)
        {
            var spriteRenderer = mb.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                if (highlight)
                {
                    Color currentColor = spriteRenderer.color;
                    spriteRenderer.color = Color.Lerp(currentColor, Color.cyan, 0.3f);
                }
                else
                {
                    EnergyManager.Instance?.UpdateConsumerVisuals(consumer, spriteRenderer);
                }
            }
        }
    }

    void UpdateSlotHighlights()
    {
        if (currentHighlightedSlot != null)
        {
            currentHighlightedSlot.SetHighlight(false);
            currentHighlightedSlot = null;
        }

        if (!requirePlayerProximity || playerTransform == null) return;

        Vector2 cursorDirection = GetCursorDirectionFromPlayer();
        TowerSlot closestSlotInDirection = null;
        float closestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot == null || !slot.IsAvailable) continue;

            Vector2 directionToSlot = (slot.transform.position - playerTransform.position).normalized;
            float distance = Vector2.Distance(playerTransform.position, slot.transform.position);

            if (distance > buildRange) continue;

            float dotProduct = Vector2.Dot(cursorDirection, directionToSlot);
            if (dotProduct > 0.1f && distance < closestDistance)
            {
                closestDistance = distance;
                closestSlotInDirection = slot;
            }
        }

        if (closestSlotInDirection != null)
        {
            closestSlotInDirection.SetHighlight(true);
            currentHighlightedSlot = closestSlotInDirection;
        }
    }

    Vector2 GetCursorDirectionFromPlayer()
    {
        if (Mouse.current == null || playerTransform == null) return Vector2.right;

        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0;
        Vector3 direction = (mouseWorldPos - playerTransform.position).normalized;
        return new Vector2(direction.x, direction.y);
    }

    bool IsPlayerInRange(Vector3 slotPosition)
    {
        if (!requirePlayerProximity || playerTransform == null) return true;
        float distance = Vector2.Distance(playerTransform.position, slotPosition);
        return distance <= buildRange;
    }

    void HandleInput()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlacementMode();
        }

        // Tower selection with number keys
        if (towerPrefabs != null)
        {
            for (int i = 1; i <= towerPrefabs.Count && i <= 9; i++)
            {
                if (Keyboard.current[(Key)(Key.Digit1 + i - 1)].wasPressedThisFrame)
                {
                    selectedTowerIndex = i - 1;
                }
            }
        }

        // Right click to remove towers
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            RemoveTowerAtMousePosition();
        }
    }

    void HandleMouseClicks()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame && !clickProcessed)
        {
            clickProcessed = true;

            if (isPlacementMode)
            {
                if (currentHighlightedConsumer != null)
                {
                    if (useContinuousSupply)
                    {
                        // Start continuous supply
                        StartContinuousSupplyToConsumer(currentHighlightedConsumer);
                    }
                    else
                    {
                        // Old discrete repair with cooldown
                        if (Time.time - lastRepairTime >= energyRepairCooldown)
                        {
                            TryRepairEnergyConsumer(currentHighlightedConsumer);
                        }
                    }
                }
                else if (currentHighlightedConsumer == null)
                {
                    HandleSlotClick();
                }
            }
            StartCoroutine(ResetClickProcessing());
        }

        // Handle mouse button release
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            StopContinuousSupply();
        }

        // Continue supplying while button is held
        if (Mouse.current.leftButton.isPressed && isCurrentlySupplying && currentSupplyTarget != null)
        {
            ContinuouslySupplyEnergy();
        }
    }

    // Continuous supply methods
    private void StartContinuousSupplyToConsumer(IEnergyConsumer consumer)
    {
        if (consumer == null || EnergyManager.Instance == null) return;

        isCurrentlySupplying = true;
        currentSupplyTarget = consumer;

        // Tell EnergyManager to start continuous supply
        EnergyManager.Instance.StartContinuousSupply(consumer);

        // Immediate first supply
        ContinuouslySupplyEnergy();
    }

    private void ContinuouslySupplyEnergy()
    {
        if (currentSupplyTarget == null || EnergyManager.Instance == null) return;

        // Check if target still needs energy
        if (currentSupplyTarget.GetEnergyPercentage() >= 0.999f && !(currentSupplyTarget is CentralCore))
        {
            StopContinuousSupply();
            return;
        }

        // Use EnergyManager's continuous supply system
        EnergyManager.Instance.SupplyEnergyToTarget(currentSupplyTarget, 0);
    }

    private void StopContinuousSupply()
    {
        isCurrentlySupplying = false;
        currentSupplyTarget = null;
    }

    // Keep the original TryRepairEnergyConsumer for discrete repairs when useContinuousSupply is false
    void TryRepairEnergyConsumer(IEnergyConsumer consumer)
    {
        if (consumer == null || EnergyManager.Instance == null) return;

        if (!EnergyManager.Instance.CanPlayerAfford(energyRepairAmount)) return;

        float energyPercent = consumer.GetEnergyPercentage();

        if (energyPercent >= 0.999f && !(consumer is CentralCore)) return;

        if (EnergyManager.Instance.TrySpendPlayerEnergy(energyRepairAmount))
        {
            consumer.SupplyEnergy(energyRepairAmount);
            lastRepairTime = Time.time;
        }
    }

    string GetConsumerDisplayName(IEnergyConsumer consumer)
    {
        if (consumer is Tower tower)
            return $"Tower ({tower.towerName})";
        else if (consumer is CentralCore)
            return "Central Core";
        else
            return $"Unknown Consumer ({consumer.GetType().Name})";
    }

    System.Collections.IEnumerator ResetClickProcessing()
    {
        yield return null;
        clickProcessed = false;
    }

    void HandleSlotClick()
    {
        if (currentHighlightedSlot != null && currentHighlightedSlot.IsAvailable)
        {
            OnSlotClicked(currentHighlightedSlot);
        }
        else
        {
            Vector2 cursorDirection = GetCursorDirectionFromPlayer();
            TowerSlot nearestSlot = FindNearestSlotInDirection(cursorDirection);
            if (nearestSlot != null)
            {
                OnSlotClicked(nearestSlot);
            }
        }
    }

    TowerSlot FindNearestSlotInDirection(Vector2 direction)
    {
        if (playerTransform == null) return null;

        TowerSlot nearestSlot = null;
        float nearestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot == null || !slot.IsAvailable) continue;

            Vector2 directionToSlot = (slot.transform.position - playerTransform.position).normalized;
            float distance = Vector2.Distance(playerTransform.position, slot.transform.position);

            if (distance > buildRange) continue;

            float dotProduct = Vector2.Dot(direction, directionToSlot);
            if (dotProduct > -0.5f && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
    }

    public void RegisterSlot(TowerSlot slot)
    {
        if (allSlots == null) allSlots = new List<TowerSlot>();
        if (slot != null && !allSlots.Contains(slot))
        {
            allSlots.Add(slot);
        }
    }

    public void UnregisterSlot(TowerSlot slot)
    {
        if (allSlots != null)
        {
            allSlots.Remove(slot);
        }
    }

    public void TogglePlacementMode()
    {
        isPlacementMode = !isPlacementMode;

        if (CursorManager.Instance != null)
        {
            if (isPlacementMode)
            {
                CursorManager.Instance.SetCursor(CursorManager.CursorType.Repair);
            }
            else
            {
                CursorManager.Instance.ReturnToPreviousCursor();
            }
        }

        if (towerSelectionUI != null)
        {
            towerSelectionUI.SetActive(isPlacementMode);
        }

        // Clear highlights when exiting placement mode
        if (!isPlacementMode)
        {
            // Stop continuous supply when exiting placement mode
            StopContinuousSupply();

            if (currentHighlightedSlot != null)
            {
                currentHighlightedSlot.SetHighlight(false);
                currentHighlightedSlot = null;
            }
            if (currentHighlightedConsumer != null)
            {
                SetConsumerHighlight(currentHighlightedConsumer, false);
                currentHighlightedConsumer = null;
            }
            if (allSlots != null)
            {
                foreach (TowerSlot slot in allSlots)
                {
                    slot?.SetHighlight(false);
                }
            }

            // Hide wheel if visible
            if (selectionWheel != null)
            {
                selectionWheel.CloseWheel();
            }
        }
    }

    public void OnSlotClicked(TowerSlot slot)
    {
        if (!isPlacementMode || !slot.IsAvailable) return;
        if (towerPrefabs == null || towerPrefabs.Count == 0) return;

        if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position)) return;

        // Show wheel if we have multiple tower types, otherwise place directly
        if (towerPrefabs.Count > 1 && selectionWheel != null)
        {
            selectionWheel.OpenWheel(towerPrefabs.ToArray(), slot);
        }
        else if (towerPrefabs.Count > 0)
        {
            PlaceTowerDirectly(slot, selectedTowerIndex);
        }
    }

    // Called by the wheel when a tower is selected
    public void PlaceTowerFromWheel(int towerIndex, GameObject towerPrefab, TowerSlot slot)
    {
        PlaceTowerDirectly(slot, towerIndex);
    }

    // Helper method to place tower directly
    private void PlaceTowerDirectly(TowerSlot slot, int towerIndex)
    {
        if (towerPrefabs == null || towerIndex < 0 || towerIndex >= towerPrefabs.Count) return;

        bool success = slot.PlaceTower(towerPrefabs[towerIndex]);

        if (success)
        {
            if (currentHighlightedSlot == slot)
            {
                currentHighlightedSlot.SetHighlight(false);
                currentHighlightedSlot = null;
            }

            if (playCreationAnimation && slot.currentTower != null)
            {
                StartCoroutine(PlayTowerCreationAnimation(slot.currentTower));
            }
        }
    }

    IEnumerator PlayTowerCreationAnimation(GameObject tower)
    {
        Sprite[] creationSprites = Resources.LoadAll<Sprite>(towerCreationSpritePath);
        if (creationSprites == null || creationSprites.Length == 0) yield break;

        SpriteRenderer towerRenderer = tower.GetComponent<SpriteRenderer>();
        if (towerRenderer == null) yield break;

        Sprite originalSprite = towerRenderer.sprite;

        for (int i = creationSprites.Length - 1; i >= 0; i--)
        {
            if (towerRenderer != null && creationSprites[i] != null)
            {
                towerRenderer.sprite = creationSprites[i];
                yield return new WaitForSeconds(creationAnimationSpeed);
            }
        }

        if (towerRenderer != null && originalSprite != null)
        {
            towerRenderer.sprite = originalSprite;
        }
    }

    void RemoveTowerAtMousePosition()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorldPos.z = 0f;

        if (allSlots != null)
        {
            foreach (TowerSlot slot in allSlots)
            {
                if (slot != null && slot.IsClickedAt(mouseWorldPos) && slot.IsOccupied)
                {
                    if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position)) continue;

                    slot.RemoveTower();
                    break;
                }
            }
        }
    }

    public void SelectTower(int index)
    {
        if (towerPrefabs != null && index >= 0 && index < towerPrefabs.Count)
        {
            selectedTowerIndex = index;
        }
    }

    public void AddNewRing(float radius, int slotCount)
    {
        if (mapGenerator != null)
        {
            mapGenerator.AddRing(radius, slotCount);
            mapGenerator.GenerateMap();
        }
    }

    public List<TowerSlot> GetAllSlots() => allSlots != null ? new List<TowerSlot>(allSlots) : new List<TowerSlot>();

    public List<TowerSlot> GetAvailableSlots()
    {
        List<TowerSlot> availableSlots = new List<TowerSlot>();
        if (allSlots != null)
        {
            foreach (TowerSlot slot in allSlots)
            {
                if (slot != null && slot.IsAvailable)
                {
                    availableSlots.Add(slot);
                }
            }
        }
        return availableSlots;
    }

    // Utility method to get tower count
    public int GetTowerTypeCount()
    {
        return towerPrefabs != null ? towerPrefabs.Count : 0;
    }

    // Debug method to rebuild tower list manually if needed
    [ContextMenu("Rebuild Tower List")]
    public void RebuildTowerList()
    {
        BuildTowerList();
        Debug.Log($"Tower list rebuilt with {GetTowerTypeCount()} towers");
    }

    void OnDestroy()
    {
        // Cleanup if needed
    }

    void OnDrawGizmos()
    {
        if (requirePlayerProximity && playerTransform != null)
        {
            Gizmos.color = isPlacementMode ? Color.green : Color.gray;
            Gizmos.DrawWireSphere(playerTransform.position, buildRange);
        }
    }
}