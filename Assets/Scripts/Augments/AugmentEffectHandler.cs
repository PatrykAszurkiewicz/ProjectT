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
            case 2:
                ApplyWeaponSwap("MeleeTest", "Melee");
                break;

            case 3:
                if (obstacleGenerator != null)
                {
                    obstacleGenerator.GenerateObstacles();
                }
                break;

            case 4:
                ApplyWeaponSwap("ObstacleTest", "ObstacleDrawer");
                break;

            case 65:
                ApplyWeaponSwap("HookTest", "Hook");
                break;

            case 66:
                ApplyWeaponSwap("RangeTest", "Ranged");
                break;

            case 11:
                ApplyImmunityPhases();
                break;

            default:
                Debug.Log($"No special effect defined for augment {augmentId}");
                break;
        }
    }


    private void ApplyImmunityPhases()
    {
        //Debug.Log("[AUGMENT] ========== ApplyImmunityPhases CALLED ==========");

        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[AUGMENT] Could not find PlayerStats for Immunity Phases!");
            return;
        }

        //Debug.Log($"[AUGMENT] Found PlayerStats on: {playerStats.gameObject.name}");

        var playerObj = playerStats.gameObject;

        // Check if already exists
        var existing = playerObj.GetComponent<ImmunityPhasesEffect>();
        if (existing != null)
        {
            Debug.LogWarning("[AUGMENT] Immunity Phases already active - cannot stack");
            return;
        }

        // Add immunity phases effect - values are set in component's Awake
        var immunity = playerObj.AddComponent<ImmunityPhasesEffect>();

        //Debug.Log($"[AUGMENT] ✓✓✓ ADDED ImmunityPhasesEffect component! Duration: {immunity.immunityDuration}s, Cooldown: {immunity.immunityCooldown}s");
    }

    private void ApplyWeaponSwap(string weaponAssetName, string cursorType)
    {
        // Find weapon
        Weapon weapon = FindAnyObjectByType<Weapon>();
        if (weapon == null)
        {
            Debug.LogError($"[AUGMENT] Could not find Weapon component!");
            return;
        }

        // Load weapon data - try multiple paths including Weapons folder
        WeaponData newWeaponData = Resources.Load<WeaponData>("Weapons/" + weaponAssetName);

        if (newWeaponData == null)
        {
            Debug.LogError($"[AUGMENT] Could not load {weaponAssetName}! Move it to Assets/Resources/Weapons/{weaponAssetName}.asset");
            return;
        }

        // Get player stats
        PlayerStats playerStats = weapon.GetComponentInParent<PlayerStats>();
        WeaponData oldWeaponData = weapon.GetWeaponData();

        // Remove old armor bonus
        if (oldWeaponData != null && oldWeaponData.armorBonus > 0 && playerStats != null)
        {
            playerStats.currentArmor -= oldWeaponData.armorBonus;
        }

        // Update WeaponSelectionManager for persistence
        if (WeaponSelectionManager.Instance != null)
        {
            WeaponSelectionManager.Instance.SelectedWeapon = newWeaponData;
        }

        // Use reflection to swap weapon data
        var weaponDataField = typeof(Weapon).GetField("weaponData", BindingFlags.NonPublic | BindingFlags.Instance);
        var originalWeaponDataField = typeof(Weapon).GetField("originalWeaponData", BindingFlags.Public | BindingFlags.Instance);

        if (weaponDataField != null && originalWeaponDataField != null)
        {
            // Create runtime copy
            WeaponData runtimeWeaponData = newWeaponData.CreateRuntimeCopy();

            // Set both fields
            originalWeaponDataField.SetValue(weapon, newWeaponData);
            weaponDataField.SetValue(weapon, runtimeWeaponData);

            // Apply new armor bonus
            if (runtimeWeaponData.armorBonus > 0 && playerStats != null)
            {
                playerStats.currentArmor += runtimeWeaponData.armorBonus;
            }

            // Re-initialize weapon (visuals, collider, etc.)
            var setupWeaponMethod = typeof(Weapon).GetMethod("SetupWeapon", BindingFlags.NonPublic | BindingFlags.Instance);
            if (setupWeaponMethod != null)
            {
                setupWeaponMethod.Invoke(weapon, null);
            }

            //Debug.Log($"[AUGMENT] Successfully swapped to weapon: {runtimeWeaponData.weaponName}");
        }
        else
        {
            Debug.LogError($"[AUGMENT] Failed to access weapon fields via reflection!");
        }

        // Update cursor based on weapon type
        if (CursorManager.Instance != null)
        {
            switch (cursorType)
            {
                case "Melee":
                    CursorManager.Instance.SetCursor(CursorManager.CursorType.Melee);
                    break;
                case "Hook":
                    CursorManager.Instance.SetCursor(CursorManager.CursorType.Hook);
                    break;
                case "ObstacleDrawer":
                    CursorManager.Instance.SetCursor(CursorManager.CursorType.ObstacleDrawer);
                    break;
                case "Ranged":
                    CursorManager.Instance.SetCursor(CursorManager.CursorType.Ranged);
                    break;
                case "Default":
                default:
                    CursorManager.Instance.SetCursor(CursorManager.CursorType.Default);
                    break;
            }
        }
        else
        {
            Debug.LogWarning("[AUGMENT] CursorManager.Instance is null - cursor not updated");
        }
    }
}