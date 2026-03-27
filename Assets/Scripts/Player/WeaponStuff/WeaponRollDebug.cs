using UnityEngine;
using System.Collections.Generic;


// TODO Remove

public class WeaponRollDebug : MonoBehaviour
{
    void Start()
    {
        Debug.Log("═══════════════ WEAPON ROLL DIAGNOSTIC ═══════════════");

        // 1. Check WeaponUnlockRegistry
        if (WeaponUnlockRegistry.Instance == null)
            Debug.LogError("[DIAG] WeaponUnlockRegistry.Instance is NULL — script not running or Awake failed");
        else
            Debug.Log("[DIAG] WeaponUnlockRegistry OK");

        // 2. Check WeaponRollController
        var ctrl = FindFirstObjectByType<WeaponRollController>();
        if (ctrl == null)
        {
            Debug.LogError("[DIAG] WeaponRollController not found in scene");
        }
        else
        {
            Debug.Log($"[DIAG] WeaponRollController found on: '{ctrl.gameObject.name}'");
            Debug.Log($"[DIAG] allWeaponSlots length: {ctrl.allWeaponSlots.Length}");
            for (int i = 0; i < ctrl.allWeaponSlots.Length; i++)
            {
                var wd = ctrl.allWeaponSlots[i];
                Debug.Log($"[DIAG]   slot[{i}] = {(wd == null ? "NULL ← THIS IS A PROBLEM" : wd.weaponName)}");
            }
            Debug.Log($"[DIAG] ActiveCount after Start: {ctrl.ActiveCount}");
            Debug.Log($"[DIAG] CurrentActiveIndex: {ctrl.CurrentActiveIndex}");
        }

        // 3. Check Weapon component
        var weapon = FindFirstObjectByType<Weapon>();
        if (weapon == null)
            Debug.LogError("[DIAG] No Weapon component found anywhere in scene");
        else
            Debug.Log($"[DIAG] Weapon found on: '{weapon.gameObject.name}', parent: '{weapon.transform.parent?.name}'");

        // 4. Check WeaponRollUI
        var ui = FindFirstObjectByType<WeaponRollUI>();
        if (ui == null)
            Debug.LogError("[DIAG] WeaponRollUI not found");
        else
            Debug.Log($"[DIAG] WeaponRollUI found on: '{ui.gameObject.name}'");

        // 5. Check AugmentRegistry
        if (AugmentRegistry.Instance == null)
            Debug.LogError("[DIAG] AugmentRegistry.Instance is NULL — augments won't trigger unlocks");
        else
        {
            var applied = AugmentRegistry.Instance.GetAppliedAugments();
            Debug.Log($"[DIAG] AugmentRegistry OK — {applied.Count} augments already applied: [{string.Join(", ", applied)}]");
        }

        // 6. Check WeaponSelectionManager
        if (WeaponSelectionManager.Instance == null)
            Debug.LogError("[DIAG] WeaponSelectionManager.Instance is NULL");
        else
            Debug.Log($"[DIAG] WeaponSelectionManager OK — selected: {WeaponSelectionManager.Instance.SelectedWeapon?.weaponName ?? "NULL"}");

        // 7. Simulate unlocking slot 0 and slot 1 right now
        Debug.Log("[DIAG] Force-unlocking slots 0 and 1 to test flow...");
        WeaponUnlockRegistry.Instance?.ForceUnlock(0);
        WeaponUnlockRegistry.Instance?.ForceUnlock(1);

        Debug.Log($"[DIAG] After force-unlock — ActiveCount: {ctrl?.ActiveCount}");
        Debug.Log($"[DIAG] UnlockedSlots: [{string.Join(", ", WeaponUnlockRegistry.Instance?.UnlockedSlots ?? new HashSet<int>())}]");

        Debug.Log("═══════════════════════════════════════════════════════");
    }

    void Update()
    {
        // Live log when scroll happens so we can confirm input is being read
        float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null)
            scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y;
#else
        scroll = Input.GetAxis("Mouse ScrollWheel");
#endif
        if (Mathf.Abs(scroll) > 0.01f)
            Debug.Log($"[DIAG] Scroll input detected: {scroll}");
    }
}
