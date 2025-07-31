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
    public float buildRange = 0.5f; // How close the player needs to be to build
    public Transform playerTransform;
    public bool requirePlayerProximity = true; // Toggle for proximity requirement

    [Header("Tower Creation Animation")]
    public string towerCreationSpritePath = "Sprites/tower_creation_decay_spritesheet4";
    public float creationAnimationSpeed = 0.1f;
    public bool playCreationAnimation = true;

    [Header("UI References")]
    public GameObject towerSelectionUI;

    private TowerDefenseMap mapGenerator;
    private bool isPlacementMode = false;
    private List<TowerSlot> allSlots = new List<TowerSlot>();
    private bool clickProcessed = false; // Prevent multiple clicks per frame
    private TowerSlot currentHighlightedSlot = null; // Track currently highlighted slot

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
                Debug.Log("Player found automatically for tower placement proximity");
            }
            else
            {
                Debug.LogWarning("Player not found! Tower placement proximity will be disabled.");
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
        }
    }

    void UpdateSlotHighlights()
    {
        if (currentHighlightedSlot != null)
        {
            currentHighlightedSlot.SetHighlight(false);
            currentHighlightedSlot = null;
        }

        if (!requirePlayerProximity || playerTransform == null)
        {
            return;
        }

        // Get cursor direction from the Player
        Vector2 cursorDirection = GetCursorDirectionFromPlayer();
        // Find the closest available slot in the cursor direction
        TowerSlot closestSlotInDirection = null;
        float closestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot == null || !slot.IsAvailable) continue;

            Vector2 directionToSlot = (slot.transform.position - playerTransform.position).normalized;
            float distance = Vector2.Distance(playerTransform.position, slot.transform.position);

            // Check if slot is within the build range
            if (distance > buildRange) continue;

            // Check if slot is in the direction of the cursor 
            float dotProduct = Vector2.Dot(cursorDirection, directionToSlot);
            if (dotProduct > 0.1f && distance < closestDistance)
            {
                closestDistance = distance;
                closestSlotInDirection = slot;
            }
        }

        // Highlight the closest slot in cursor direction
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
        // Activate placement mode with spacebar
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlacementMode();
        }

        // Debug: Log current state periodically
        if (Time.frameCount % 300 == 0) // Every 5 seconds at 60fps
        {
            Debug.Log($"=== PERIODIC STATE CHECK ===");
            Debug.Log($"Placement mode: {isPlacementMode}");
            Debug.Log($"Total slots: {allSlots.Count}");
            Debug.Log($"Available slots: {GetAvailableSlots().Count}");
            Debug.Log($"Selected tower index: {selectedTowerIndex}");
            Debug.Log($"Player position: {(playerTransform != null ? playerTransform.position.ToString() : "null")}");
        }

        // TODO REMOVE this optional feature, selecting tower types with number keys
        for (int i = 1; i <= towerPrefabs.Count && i <= 9; i++)
        {
            if (Keyboard.current[(Key)(Key.Digit1 + i - 1)].wasPressedThisFrame)
            {
                selectedTowerIndex = i - 1;
                Debug.Log($"Selected tower: {towerPrefabs[selectedTowerIndex].name} (index {selectedTowerIndex})");
            }
        }

        // TODO Remove this optional feature, right clicking to remove towers
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
                HandleSlotClick();
            }
            StartCoroutine(ResetClickProcessing());
        }
    }

    System.Collections.IEnumerator ResetClickProcessing()
    {
        yield return null; // Wait one frame
        clickProcessed = false;
    }

    void HandleSlotClick()
    {
        if (currentHighlightedSlot != null && currentHighlightedSlot.IsAvailable)
        {
            Debug.Log($"Building tower at highlighted slot: Ring {currentHighlightedSlot.ringIndex}, Slot {currentHighlightedSlot.slotIndex}");
            OnSlotClicked(currentHighlightedSlot);
        }
        else
        {
            Debug.Log("No highlighted slot available for building");

            // Fallback: try to find a slot near cursor direction
            Vector2 cursorDirection = GetCursorDirectionFromPlayer();
            TowerSlot nearestSlot = FindNearestSlotInDirection(cursorDirection);

            if (nearestSlot != null)
            {
                Debug.Log($"Fallback: Building at nearest slot in cursor direction: Ring {nearestSlot.ringIndex}, Slot {nearestSlot.slotIndex}");
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

            // Check if within build range
            if (distance > buildRange) continue;

            // Check if the slot is in the cursor direction
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
            Debug.Log($"Registered slot: Ring {slot.ringIndex}, Slot {slot.slotIndex} (Total: {allSlots.Count})");
        }
        else
        {
            Debug.Log($"Slot already registered: Ring {slot.ringIndex}, Slot {slot.slotIndex}");
        }
    }

    public void UnregisterSlot(TowerSlot slot)
    {
        if (allSlots.Contains(slot))
        {
            allSlots.Remove(slot);
            Debug.Log($"Unregistered slot: Ring {slot.ringIndex}, Slot {slot.slotIndex} (Total: {allSlots.Count})");
        }
        else
        {
            Debug.Log($"Tried to unregister non-existing slot: Ring {slot.ringIndex}, Slot {slot.slotIndex}");
        }
    }

    public void TogglePlacementMode()
    {
        isPlacementMode = !isPlacementMode;
        Debug.Log($"Placement mode: {(isPlacementMode ? "ON" : "OFF")}");

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

            // Also clear any remaining highlights
            foreach (TowerSlot slot in allSlots)
            {
                if (slot != null)
                {
                    slot.SetHighlight(false);
                }
            }
        }
    }

    public void OnSlotClicked(TowerSlot slot)
    {
        //Debug.Log($"=== BUILDING TOWER ===");
        //Debug.Log($"Slot: Ring {slot.ringIndex}, Slot {slot.slotIndex}");
        //Debug.Log($"Placement mode: {isPlacementMode}");
        //Debug.Log($"Slot available: {slot.IsAvailable}");

        if (!isPlacementMode)
        {
            Debug.Log("Not in placement mode");
            return;
        }

        // Check if slot is available
        if (!slot.IsAvailable)
        {
            Debug.Log($"Slot is occupied. Current tower: {slot.currentTower}");
            return;
        }

        // Check player proximity
        if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position))
        {
            float actualDistance = Vector2.Distance(playerTransform.position, slot.transform.position);
            Debug.Log($"Player too far. Distance: {actualDistance:F2}, Required: {buildRange:F2}");
            return;
        }

        if (selectedTowerIndex >= 0 && selectedTowerIndex < towerPrefabs.Count)
        {
            Debug.Log($"Placing {towerPrefabs[selectedTowerIndex].name}");

            bool success = slot.PlaceTower(towerPrefabs[selectedTowerIndex]);

            if (success)
            {
                Debug.Log($"Tower placed successfully!");
                if (currentHighlightedSlot == slot)
                {
                    currentHighlightedSlot.SetHighlight(false);
                    currentHighlightedSlot = null;
                }
                // Play creation animation if enabled
                if (playCreationAnimation && slot.currentTower != null)
                {
                    StartCoroutine(PlayTowerCreationAnimation(slot.currentTower));
                }
            }
            else
            {
                Debug.Log("PlaceTower returned false");
            }
        }
        else
        {
            Debug.Log($"Invalid tower selection. Index: {selectedTowerIndex}, Count: {towerPrefabs.Count}");
        }
    }

    IEnumerator PlayTowerCreationAnimation(GameObject tower)
    {
        // Load the creation animation sprites
        Sprite[] creationSprites = Resources.LoadAll<Sprite>(towerCreationSpritePath);

        if (creationSprites == null || creationSprites.Length == 0)
        {
            Debug.LogWarning($"No creation animation sprites found at path: {towerCreationSpritePath}");
            yield break;
        }

        SpriteRenderer towerRenderer = tower.GetComponent<SpriteRenderer>();
        if (towerRenderer == null)
        {
            Debug.LogWarning("Tower has no SpriteRenderer for creation animation");
            yield break;
        }

        // Store the original sprite to restore later
        Sprite originalSprite = towerRenderer.sprite;

        Debug.Log($"Playing tower creation animation with {creationSprites.Length} frames");

        // Play animation manually for one cycle to avoid infinite loop
        for (int i = creationSprites.Length - 1; i >= 0; i--)
        {
            if (towerRenderer != null && creationSprites[i] != null)
            {
                towerRenderer.sprite = creationSprites[i];
                yield return new WaitForSeconds(creationAnimationSpeed);
            }
        }

        // Restore original sprite at the end
        if (towerRenderer != null && originalSprite != null)
        {
            towerRenderer.sprite = originalSprite;
        }

        Debug.Log("Tower creation animation completed");
    }

    void RemoveTowerAtMousePosition()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorldPos.z = 0f;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot != null && slot.IsClickedAt(mouseWorldPos) && slot.IsOccupied)
            {
                // Check player proximity for removal
                if (requirePlayerProximity && !IsPlayerInRange(slot.transform.position))
                {
                    Debug.Log($"Player is too far to remove tower! Distance: {Vector2.Distance(playerTransform.position, slot.transform.position):F2}");
                    continue;
                }

                slot.RemoveTower();
                Debug.Log($"Removed tower from Ring {slot.ringIndex}, Slot {slot.slotIndex}");
                break; // Only remove a single tower
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

    public List<TowerSlot> GetAllSlots()
    {
        return new List<TowerSlot>(allSlots);
    }

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

    // TODO Remove later debug visualization
    void OnDrawGizmos()
    {
        if (requirePlayerProximity && playerTransform != null)
        {
            // Draw Tower building range around player
            Gizmos.color = isPlacementMode ? Color.green : Color.gray;
            Gizmos.DrawWireSphere(playerTransform.position, buildRange);
        }
    }
}