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
    public Color unaffordableColor = Color.gray; // Color when player can't afford tower
    public bool hideWhenOccupied = true; // Hide slot sprite when tower is placed
    private SpriteRenderer spriteRenderer;
    private bool isHighlighted = false;

    [Header("Click Detection")]
    public float clickRadius = 0.3f; // Click detection radius

    public bool IsOccupied => isOccupied;
    public bool IsAvailable => !isOccupied;
    public bool IsAffordable => EnergyManager.Instance?.CanAffordTower() ?? false;

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

    void Start()
    {
        // Subscribe to player energy changes to update visuals
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnPlayerEnergyChanged += OnPlayerEnergyChanged;
        }
    }

    void OnDestroy()
    {
        // Unregister from energy events
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnPlayerEnergyChanged -= OnPlayerEnergyChanged;
        }

        // Unregister the slot when destroyed
        if (TowerPlacementManager.Instance != null)
        {
            TowerPlacementManager.Instance.UnregisterSlot(this);
        }
    }

    void OnPlayerEnergyChanged(int newEnergy)
    {
        // Update visuals when player energy changes
        UpdateVisuals();
    }

    public bool IsClickedAt(Vector2 worldPosition)
    {
        float distance = Vector2.Distance(transform.position, worldPosition);
        return distance <= clickRadius;
    }

    public void OnSlotClicked()
    {
        //Debug.Log($"Slot clicked: Ring {ringIndex}, Slot {slotIndex}, Available: {IsAvailable}, Affordable: {IsAffordable}");

        if (IsAvailable)
        {
            if (IsAffordable)
            {
                //Debug.Log($"Notifying TowerPlacementManager...");
                TowerPlacementManager.Instance?.OnSlotClicked(this);
            }
            else
            {
                //Debug.Log($"Cannot afford tower! Need {EnergyManager.Instance?.GetTowerBuildCost() ?? 0} energy, have {EnergyManager.Instance?.GetPlayerEnergy() ?? 0}");

                // Show feedback for insufficient funds
                ShowInsufficientFundsEffect();
            }
        }
        else
        {
            //Debug.Log($"Slot is occupied, cannot place tower");
        }
    }

    public bool PlaceTower(GameObject towerPrefab)
    {
        if (isOccupied)
        {
            //Debug.Log("Cannot place tower - slot is occupied");
            return false;
        }

        // Check if player can afford the tower
        if (!EnergyManager.Instance?.CanAffordTower() ?? true)
        {
            //Debug.Log("Cannot place tower - insufficient energy");
            ShowInsufficientFundsEffect();
            return false;
        }

        // Attempt to spend energy
        if (!EnergyManager.Instance?.TryBuyTower() ?? false)
        {
            //Debug.Log("Failed to spend energy for tower");
            return false;
        }

        //Debug.Log($"Placing tower {towerPrefab.name} at Ring {ringIndex}, Slot {slotIndex}");
        currentTower = Instantiate(towerPrefab, transform.position, Quaternion.identity);
        currentTower.transform.parent = transform;
        isOccupied = true;
        UpdateVisuals();
        //Debug.Log($"Tower placed successfully at Ring {ringIndex}, Slot {slotIndex}. Energy spent: {EnergyManager.Instance?.GetTowerBuildCost() ?? 0}");
        return true;
    }

    public bool RemoveTower()
    {
        if (!isOccupied) return false;

        // Give energy back to player when tower is removed
        if (EnergyManager.Instance != null)
        {
            int sellValue = EnergyManager.Instance.GetTowerSellValue();
            EnergyManager.Instance.GivePlayerEnergy(sellValue);
            //Debug.Log($"Tower removed, refunded {sellValue} energy");
        }

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
        else if (!IsAffordable)
        {
            targetColor = unaffordableColor;
        }
        else
        {
            targetColor = availableColor;
        }

        spriteRenderer.color = targetColor;

        // Debug the color changes (remove this later)
        //Debug.Log($"Slot R{ringIndex}S{slotIndex}: isOccupied={isOccupied}, isHighlighted={isHighlighted}, isAffordable={IsAffordable}, color={targetColor}, visible={spriteRenderer.enabled}");
    }

    // Visual feedback for insufficient funds
    void ShowInsufficientFundsEffect()
    {
        StartCoroutine(InsufficientFundsFlash());
    }

    System.Collections.IEnumerator InsufficientFundsFlash()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;
        Color flashColor = Color.red;

        // Flash red briefly if insufficient Energy to build Tower
        for (int i = 0; i < 3; i++)
        {
            spriteRenderer.color = flashColor;
            yield return new UnityEngine.WaitForSeconds(0.1f);
            spriteRenderer.color = originalColor;
            yield return new UnityEngine.WaitForSeconds(0.1f);
        }
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Draw the actual clickable area
        Gizmos.color = IsAvailable ? (IsAffordable ? Color.green : Color.yellow) : Color.red;
        Gizmos.DrawWireSphere(transform.position, clickRadius);

        // Draw slot index for debugging
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, $"R{ringIndex}S{slotIndex}");

        // Show affordability status
        string affordabilityText = IsAvailable ? (IsAffordable ? "AFFORDABLE" : "TOO EXPENSIVE") : "OCCUPIED";
        UnityEditor.Handles.Label(transform.position + Vector3.down * 0.4f, affordabilityText);

        // Show energy info
        if (EnergyManager.Instance != null)
        {
            string energyInfo = $"Cost: {EnergyManager.Instance.GetTowerBuildCost()}, Have: {EnergyManager.Instance.GetPlayerEnergy()}";
            UnityEditor.Handles.Label(transform.position + Vector3.down * 0.7f, energyInfo);
        }
#endif
    }
}