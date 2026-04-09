using UnityEngine;
using System.Reflection;

public class AugmentEffectHandler : MonoBehaviour
{
    private ObstacleGenerator obstacleGenerator;

    private void Awake()
    {
        obstacleGenerator = FindAnyObjectByType<ObstacleGenerator>();
    }


    public void ApplyAugmentEffect(int augmentId)
    {
        switch (augmentId)
        {
            case 3:
                if (obstacleGenerator != null)
                {
                    obstacleGenerator.GenerateObstacles();
                }
                break;

            //  Weapons (left-click slot) 
            case 2:
                ApplyWeaponSwap("MeleeTest", "Melee");
                break;
            case 66:
                ApplyWeaponSwap("RangeTest", "Ranged");
                break;
            case 93:
                ApplyWeaponSwap("FlamethrowerTest", "Flamethrower");
                break;
            case 318:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(318);
                ApplyWeaponSwap("BoomerangTest", "Boomerang");
                break;

            //  Tools (right-click slot) 
            case 4:
                ApplyToolSwap("ObstacleTest", "ObstacleDrawer");
                break;
            case 65:
                ApplyToolSwap("HookTest", "Hook");
                break;
            case 81:
                ApplyToolSwap("ShieldTest", "Shield");
                break;

            case 314:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(314);
                ApplyToolSwap("BombLauncherTest", "BombLauncher");
                break;

            case 315:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(315);
                ApplyToolSwap("TrapTest", "Trap");
                break;

            case 316:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(316);
                ApplyToolSwap("DecoyTest", "Decoy");
                break;

            case 317:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(317);
                ApplyToolSwap("TurretTest", "Turret");
                break;

            case 11:
                ApplyImmunityPhases();
                break;

            case 37:
                ApplyQuickRevive();
                break;

            default:
                Debug.Log($"No special effect defined for augment {augmentId}");
                break;
        }
    }

    private void ApplyQuickRevive()
    {
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[AUGMENT] Could not find PlayerStats for Quick Revive");
            return;
        }

        var playerObj = playerStats.gameObject;

        var existing = playerObj.GetComponent<QuickReviveEffect>();
        if (existing != null)
        {
            Debug.LogWarning("[AUGMENT] Quick Revive already active - cannot stack");
            return;
        }

        var quickRevive = playerObj.AddComponent<QuickReviveEffect>();
    }

    private void ApplyImmunityPhases()
    {
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[AUGMENT] Could not find PlayerStats for Immunity Phases!");
            return;
        }

        var playerObj = playerStats.gameObject;

        var existing = playerObj.GetComponent<ImmunityPhasesEffect>();
        if (existing != null)
        {
            Debug.LogWarning("[AUGMENT] Immunity Phases already active - cannot stack");
            return;
        }

        var immunity = playerObj.AddComponent<ImmunityPhasesEffect>();
    }


    // Swap weapon in the left-click (weapon) slot.

    private void ApplyWeaponSwap(string weaponAssetName, string cursorType)
    {
        Weapon weapon = FindAnyObjectByType<Weapon>();
        if (weapon == null)
        {
            Debug.LogError($"[AUGMENT] Could not find Weapon component!");
            return;
        }

        WeaponData newWeaponData = Resources.Load<WeaponData>("Weapons/" + weaponAssetName);
        if (newWeaponData == null)
        {
            Debug.LogError($"[AUGMENT] Could not load {weaponAssetName}! Move it to Assets/Resources/Weapons/{weaponAssetName}.asset");
            return;
        }

        // Use HotSwapWeapon which routes to the weapon slot
        weapon.HotSwapWeapon(newWeaponData);

        // Update WeaponSelectionManager for persistence
        if (WeaponSelectionManager.Instance != null)
            WeaponSelectionManager.Instance.SelectedWeapon = newWeaponData;
    }


    // Swap tool in the right-click (tool) slot.

    private void ApplyToolSwap(string toolAssetName, string cursorType)
    {
        Weapon weapon = FindAnyObjectByType<Weapon>();
        if (weapon == null)
        {
            Debug.LogError($"[AUGMENT] Could not find Weapon component!");
            return;
        }

        WeaponData newToolData = Resources.Load<WeaponData>("Weapons/" + toolAssetName);
        if (newToolData == null)
        {
            Debug.LogError($"[AUGMENT] Could not load {toolAssetName}! Move it to Assets/Resources/Weapons/{toolAssetName}.asset");
            return;
        }

        // Equip into the tool slot directly
        weapon.HotSwapTool(newToolData);
    }
}
