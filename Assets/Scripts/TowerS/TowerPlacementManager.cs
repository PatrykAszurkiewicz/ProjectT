using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class TowerPlacementManager : MonoBehaviour
{
    public static TowerPlacementManager Instance;

    [Header("Tower Prefabs")]
    public List<GameObject> towerPrefabs = new List<GameObject>();
    public int selectedTowerIndex = 0;

    [Header("UI References")]
    public GameObject towerSelectionUI;

    private TowerDefenseMap mapGenerator;
    private bool isPlacementMode = false;
    private List<TowerSlot> allSlots = new List<TowerSlot>();
    private bool clickProcessed = false; // Prevent multiple clicks per frame

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
    }

    void Update()
    {
        HandleInput();
        HandleMouseClicks();
    }

    void HandleInput()
    {
        // Activate placement mode with spacebar
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TogglePlacementMode();
        }

        // TODO REMOVE this optional feature, selecting tower types with number keys
        for (int i = 1; i <= towerPrefabs.Count && i <= 9; i++)
        {
            if (Keyboard.current[(Key)(Key.Digit1 + i - 1)].wasPressedThisFrame)
            {
                selectedTowerIndex = i - 1;
                Debug.Log($"Selected tower: {towerPrefabs[selectedTowerIndex].name}");
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

            // Reset click processing next frame
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
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorldPos.z = 0f;

        Debug.Log($"Handling slot click at world position: {mouseWorldPos}");

        // Find the closest available slot to the click position
        TowerSlot closestSlot = null;
        float closestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            if (slot == null) continue;

            if (slot.IsClickedAt(mouseWorldPos))
            {
                float distance = Vector2.Distance(slot.transform.position, mouseWorldPos);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestSlot = slot;
                }
            }
        }

        if (closestSlot != null)
        {
            Debug.Log($"Found closest slot: Ring {closestSlot.ringIndex}, Slot {closestSlot.slotIndex}, Distance: {closestDistance}");
            OnSlotClicked(closestSlot);
        }
        else
        {
            Debug.Log("No slot found at click position");
        }
    }

    public void RegisterSlot(TowerSlot slot)
    {
        if (!allSlots.Contains(slot))
        {
            allSlots.Add(slot);
            Debug.Log($"Registered slot: Ring {slot.ringIndex}, Slot {slot.slotIndex}");
        }
    }

    public void UnregisterSlot(TowerSlot slot)
    {
        if (allSlots.Contains(slot))
        {
            allSlots.Remove(slot);
            Debug.Log($"Unregistered slot: Ring {slot.ringIndex}, Slot {slot.slotIndex}");
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
    }

    public void OnSlotClicked(TowerSlot slot)
    {
        Debug.Log($"OnSlotClicked called for slot at Ring {slot.ringIndex}, Slot {slot.slotIndex}");
        Debug.Log($"Placement mode: {isPlacementMode}");
        Debug.Log($"Selected tower index: {selectedTowerIndex}");
        Debug.Log($"Tower prefabs count: {towerPrefabs.Count}");

        if (!isPlacementMode)
        {
            Debug.Log("Not in placement mode");
            return;
        }

        if (selectedTowerIndex >= 0 && selectedTowerIndex < towerPrefabs.Count)
        {
            bool success = slot.PlaceTower(towerPrefabs[selectedTowerIndex]);
            if (success)
            {
                Debug.Log($"Successfully placed {towerPrefabs[selectedTowerIndex].name} at Ring {slot.ringIndex}, Slot {slot.slotIndex}");
            }
            else
            {
                Debug.Log("Failed to place tower");
            }
        }
        else
        {
            Debug.Log("Invalid tower selection");
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
                slot.RemoveTower();
                Debug.Log($"Removed tower from Ring {slot.ringIndex}, Slot {slot.slotIndex}");
                break; // Only remove one tower
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
}