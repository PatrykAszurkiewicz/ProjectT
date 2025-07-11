using UnityEngine;
using UnityEngine.InputSystem;

public class ClickDebugger : MonoBehaviour
{
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            DebugClick();
        }
    }

    void DebugClick()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorldPos.z = 0f;

        Debug.Log($"=== CLICK DEBUG ===");
        Debug.Log($"Mouse World Position: {mouseWorldPos}");

        // Check what's at the click position
        Collider2D[] allColliders = Physics2D.OverlapPointAll(mouseWorldPos);
        Debug.Log($"Found {allColliders.Length} colliders at click position:");

        for (int i = 0; i < allColliders.Length; i++)
        {
            var col = allColliders[i];
            Debug.Log($"  {i}: {col.gameObject.name}");
            Debug.Log($"     Layer: {col.gameObject.layer} ({LayerMask.LayerToName(col.gameObject.layer)})");
            Debug.Log($"     Sorting Layer: {col.GetComponent<SpriteRenderer>()?.sortingLayerName ?? "None"}");
            Debug.Log($"     Sorting Order: {col.GetComponent<SpriteRenderer>()?.sortingOrder ?? 0}");
            Debug.Log($"     Has TowerSlot: {col.GetComponent<TowerSlot>() != null}");
        }

        // Specifically check for TowerSlots
        //TowerSlot[] allSlots = FindObjectsOfType<TowerSlot>();
        TowerSlot[] allSlots = Object.FindObjectsByType<TowerSlot>(FindObjectsSortMode.None);

        Debug.Log($"Total TowerSlots in scene: {allSlots.Length}");

        TowerSlot closestSlot = null;
        float closestDistance = float.MaxValue;

        foreach (TowerSlot slot in allSlots)
        {
            float distance = Vector2.Distance(mouseWorldPos, slot.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSlot = slot;
            }
        }

        if (closestSlot != null)
        {
            Debug.Log($"Closest slot: Ring {closestSlot.ringIndex}, Slot {closestSlot.slotIndex}");
            Debug.Log($"Distance: {closestDistance}");
            Debug.Log($"Slot position: {closestSlot.transform.position}");
        }

        Debug.Log($"=== END CLICK DEBUG ===");
    }
}