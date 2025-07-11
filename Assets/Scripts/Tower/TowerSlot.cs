using UnityEngine;

public class TowerSlot : MonoBehaviour
{
    [Header("Slot Properties")]
    public bool isOccupied = false;
    public GameObject currentTower;
    public int ringIndex;
    public int slotIndex;

    [Header("Visual Feedback")]
    public Color availableColor = Color.white;
    public Color occupiedColor = Color.red;
    public Color highlightColor = Color.yellow;
    public bool hideWhenOccupied = true; // Hide slot sprite when tower is placed
    private SpriteRenderer spriteRenderer;
    private bool isHighlighted = false;

    [Header("Click Detection")]
    public float clickRadius = 0.3f; // Click detection radius

    public bool IsOccupied => isOccupied;
    public bool IsAvailable => !isOccupied;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        // Set sorting layer and order
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingLayerName = "Default";
            spriteRenderer.sortingOrder = 1;
        }
        UpdateVisuals();
        // Register the slot with the placement manager
        if (TowerPlacementManager.Instance != null)
        {
            TowerPlacementManager.Instance.RegisterSlot(this);
        }
    }

    void OnDestroy()
    {
        // Unregister the slot when destroyed
        if (TowerPlacementManager.Instance != null)
        {
            TowerPlacementManager.Instance.UnregisterSlot(this);
        }
    }

    public bool IsClickedAt(Vector2 worldPosition)
    {
        float distance = Vector2.Distance(transform.position, worldPosition);
        return distance <= clickRadius;
    }

    public void OnSlotClicked()
    {
        Debug.Log($"Slot clicked: Ring {ringIndex}, Slot {slotIndex}, Available: {IsAvailable}");
        if (IsAvailable)
        {
            Debug.Log($"Notifying TowerPlacementManager...");
            TowerPlacementManager.Instance?.OnSlotClicked(this);
        }
        else
        {
            Debug.Log($"Slot is occupied, cannot place tower");
        }
    }

    public bool PlaceTower(GameObject towerPrefab)
    {
        if (isOccupied)
        {
            Debug.Log("Cannot place tower - slot is occupied");
            return false;
        }
        //TODO Remove debug info after tests
        Debug.Log($"Placing tower {towerPrefab.name} at Ring {ringIndex}, Slot {slotIndex}");
        currentTower = Instantiate(towerPrefab, transform.position, Quaternion.identity);
        currentTower.transform.parent = transform;
        isOccupied = true;
        UpdateVisuals();
        Debug.Log($"Tower placed successfully at Ring {ringIndex}, Slot {slotIndex}");
        return true;
    }

    public bool RemoveTower()
    {
        if (!isOccupied) return false;

        if (currentTower != null)
        {
            DestroyImmediate(currentTower);
        }
        currentTower = null;
        isOccupied = false;
        UpdateVisuals();
        return true;
    }

    public void SetHighlight(bool highlight)
    {
        isHighlighted = highlight;
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (spriteRenderer == null) return;
        // Hide slot sprite when occupied if hideWhenOccupied is true
        if (isOccupied && hideWhenOccupied)
        {
            spriteRenderer.enabled = false;
            return;
        }

        // Show sprite if not occupied or if we don't hide when occupied
        spriteRenderer.enabled = true;

        Color targetColor;

        if (isHighlighted && IsAvailable)
        {
            targetColor = highlightColor;
        }
        else if (isOccupied)
        {
            targetColor = occupiedColor;
        }
        else
        {
            targetColor = availableColor;
        }

        spriteRenderer.color = targetColor;

        // Debug the color changes (remove this later)
        Debug.Log($"Slot R{ringIndex}S{slotIndex}: isOccupied={isOccupied}, isHighlighted={isHighlighted}, color={targetColor}, visible={spriteRenderer.enabled}");
    }

    // Debug visualization
    void OnDrawGizmos()
    {
        // Draw the actual clickable area
        Gizmos.color = IsAvailable ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, clickRadius);

        // Draw slot index for debugging
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, $"R{ringIndex}S{slotIndex}");
#endif
    }
}