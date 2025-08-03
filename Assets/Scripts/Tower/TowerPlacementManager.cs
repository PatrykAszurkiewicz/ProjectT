using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;

public class TowerPlacementManager : MonoBehaviour
{
    public static TowerPlacementManager Instance;

    [Header("Tower Prefabs")]
    public List<GameObject> towerPrefabs = new List<GameObject>();
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

    [Header("UI References")]
    public GameObject towerSelectionUI;

    private TowerDefenseMap mapGenerator;
    private bool isPlacementMode = false;
    private List<TowerSlot> allSlots = new List<TowerSlot>();
    private bool clickProcessed = false;
    private TowerSlot currentHighlightedSlot = null;
    private IEnergyConsumer currentHighlightedConsumer = null;
    private float lastRepairTime = -Mathf.Infinity;

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
        for (int i = 1; i <= towerPrefabs.Count && i <= 9; i++)
        {
            if (Keyboard.current[(Key)(Key.Digit1 + i - 1)].wasPressedThisFrame)
            {
                selectedTowerIndex = i - 1;
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
                if (currentHighlightedConsumer != null && Time.time - lastRepairTime >= energyRepairCooldown)
                {
                    TryRepairEnergyConsumer(currentHighlightedConsumer);
                }
                else if (currentHighlightedConsumer == null)
                {
                    HandleSlotClick();
                }
            }
            StartCoroutine(ResetClickProcessing());
        }
    }

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
        if (!allSlots.Contains(slot))
        {
            allSlots.Add(slot);
        }
    }

    public void UnregisterSlot(TowerSlot slot)
    {
        allSlots.Remove(slot);
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
            foreach (TowerSlot slot in allSlots)
            {
                slot?.SetHighlight(false);
            }
        }
    }

    public void OnSlotClicked(TowerSlot slot)
    {
        if (!isPlacementMode || !slot.IsAvailable) return;

        if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position)) return;

        if (selectedTowerIndex >= 0 && selectedTowerIndex < towerPrefabs.Count)
        {
            bool success = slot.PlaceTower(towerPrefabs[selectedTowerIndex]);

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

    public void SelectTower(int index)
    {
        if (index >= 0 && index < towerPrefabs.Count)
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

    public List<TowerSlot> GetAllSlots() => new List<TowerSlot>(allSlots);

    public List<TowerSlot> GetAvailableSlots()
    {
        List<TowerSlot> availableSlots = new List<TowerSlot>();
        foreach (TowerSlot slot in allSlots)
        {
            if (slot != null && slot.IsAvailable)
            {
                availableSlots.Add(slot);
            }
        }
        return availableSlots;
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