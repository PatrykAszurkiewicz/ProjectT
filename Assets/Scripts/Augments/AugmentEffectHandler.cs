using UnityEngine;
using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;

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
                ApplyObstacleArches();
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

            case 323:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(323);
                ApplyToolSwap("CloakTest", "Cloak");
                break;

            case 324:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(324);
                ApplyToolSwap("TorchTest", "Torch");
                break;

            case 11:
                ApplyImmunityPhases();
                break;

            case 325:
                ApplyProjectileParryUnlock();
                break;

            //  Parry upgrade augments (select-once, no rarity scaling) 
            case 330:
                ApplyLongerParryStun();
                break;

            case 331:
                ApplyPowerfulParry();
                break;

            case 332:
                ApplyLongerParryWindow();
                break;

            case 333:
                ApplyHealOnParry();
                break;

            case 37:
                ApplyQuickRevive();
                break;

            case 326:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(326);
                ApplyToolSwap("ClockTest", "Clock");
                break;

            //  Mortar (left-click weapon slot) 
            case 327:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(327);
                ApplyWeaponSwap("MortarTest", "Mortar");
                break;

            //  Cooldown reduction (global) 
            case 328:
                ApplyCooldownReduction();
                break;

            //  Smoke Screen (right-click tool slot) 
            case 329:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(329);
                ApplyToolSwap("SmokeTest", "Smoke");
                break;

            default:
                Debug.Log($"No special effect defined for augment {augmentId}");
                break;
        }
    }

    // Augment ID 3 — "Obstacle Generation".
    // Adds a ring of curved stone arches around the core (see
    // TowerDefenseMap.GenerateAugmentArches for the full design + safety
    // guarantees)
    private void ApplyObstacleArches()
    {
        TowerDefenseMap map = FindAnyObjectByType<TowerDefenseMap>();
        if (map == null)
        {
            Debug.LogError("[AUGMENT] Could not find TowerDefenseMap for Obstacle Generation arches!");
            return;
        }

        int added = map.GenerateAugmentArches();
        if (added <= 0)
            Debug.LogWarning("[AUGMENT] Obstacle Generation: no arches could be placed this time.");
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

    // Augment ID 325 — "Unlock Projectile Parry".
    // Enables the shield's projectile-parry mechanic. With a Shield equipped the
    // player can right-click to block, or time a fresh press to parry, an incoming
    // Mort shell / Pitcher shot and bounce it back at the enemy that fired it.
    // (Just flips a global gate — the ShieldSystem + projectiles do the rest.)
    private void ApplyProjectileParryUnlock()
    {
        ProjectileParry.Unlocked = true;
        Debug.Log("[AUGMENT] Projectile Parry unlocked — equip a Shield and right-click to deflect enemy shots.");
    }

    //  Parry upgrade augments (330–333) 
    // Each reads its tunable value(s) straight from the CSV's Affected_Stats
    // column and writes them into ParryUpgrades. Reading the RAW CSV value here
    // (instead of going through the rarity-scaled StatModification pipeline) is
    // what keeps these four augments immune to rarity scaling.

    // Augment ID 330 — Longer Parry Stun. Extends parry stun duration.
    // CSV: parry_stun_bonus_seconds=<seconds>  (default 1.0)
    private void ApplyLongerParryStun()
    {
        float seconds = ReadAugmentStat(330, "parry_stun_bonus_seconds", 1.0f);
        ParryUpgrades.ExtraStunSeconds = seconds;
        Debug.Log($"[AUGMENT] Longer Parry Stun — parry stun extended by +{seconds:F2}s.");
    }

    // Augment ID 331 — Powerful Parry. Parried enemies take extra damage for a
    // fixed duration (applies to melee AND projectile parry).
    // CSV: parry_damage_bonus=<fraction>,parry_damage_duration=<seconds>
    //      (defaults 0.30 and 5.0)
    private void ApplyPowerfulParry()
    {
        float bonus = ReadAugmentStat(331, "parry_damage_bonus", 0.30f);
        float duration = ReadAugmentStat(331, "parry_damage_duration", 5.0f);

        ParryUpgrades.PowerfulParryEnabled = true;
        ParryUpgrades.PowerfulParryDamageBonus = bonus;
        ParryUpgrades.PowerfulParryDuration = duration;
        Debug.Log($"[AUGMENT] Powerful Parry — parried enemies take +{bonus * 100f:F0}% damage for {duration:F1}s.");
    }

    // Augment ID 332 — Longer Parry Window. Widens the parry window.
    // CSV: parry_window_frames=<frames>  (default 2)
    private void ApplyLongerParryWindow()
    {
        int frames = Mathf.RoundToInt(ReadAugmentStat(332, "parry_window_frames", 2f));
        ParryUpgrades.ExtraParryFrames = frames;
        Debug.Log($"[AUGMENT] Longer Parry Window — parry window widened by +{frames} frame(s).");
    }

    // Augment ID 333 — Heal on Parry. Heals on each successful parry (melee AND
    // projectile parry).
    // CSV: parry_heal_percent=<fraction of max HP>  (default 0.03)
    private void ApplyHealOnParry()
    {
        float percent = ReadAugmentStat(333, "parry_heal_percent", 0.03f);
        ParryUpgrades.HealOnParryEnabled = true;
        ParryUpgrades.HealOnParryPercent = percent;
        Debug.Log($"[AUGMENT] Heal on Parry — heal {percent * 100f:F1}% of max HP per parry.");
    }

    // Reads a "key=value" (also accepts key:value) number out of an augment's
    // Affected_Stats CSV column. Falls back to `fallback` if the registry, the
    // augment, or the key is missing — so the augment still works even before the
    // CSV row is filled in.
    private float ReadAugmentStat(int augmentId, string key, float fallback)
    {
        var ad = AugmentRegistry.Instance != null
            ? AugmentRegistry.Instance.GetAugmentData(augmentId)
            : null;

        if (ad == null || string.IsNullOrEmpty(ad.AffectedStats) || ad.AffectedStats == "NULL")
            return fallback;

        // Match: key , optional spaces, one of = : * + , optional spaces, number
        var m = Regex.Match(ad.AffectedStats,
            key + @"\s*[=:*+]\s*([0-9]*\.?[0-9]+)",
            RegexOptions.IgnoreCase);

        if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out float val))
            return val;

        return fallback;
    }

    // Augment ID 328 — "Cooldown reduction".
    // Reads the reduction percent from the CSV's Affected_Stats column
    // (e.g. "cooldown_reduction%20") and pushes it into the global
    // CooldownModifier. Every cooldown-arming site multiplies through that.
    private void ApplyCooldownReduction()
    {
        float pct = 20f; // fallback if the CSV value can't be read

        var ad = AugmentRegistry.Instance != null
            ? AugmentRegistry.Instance.GetAugmentData(328)
            : null;

        if (ad != null && !string.IsNullOrEmpty(ad.AffectedStats) && ad.AffectedStats != "NULL")
        {
            var m = Regex.Match(ad.AffectedStats, @"([0-9]*\.?[0-9]+)");
            if (m.Success)
                float.TryParse(m.Groups[1].Value, NumberStyles.Float,
                               CultureInfo.InvariantCulture, out pct);
        }

        CooldownModifier.SetReductionPercent(pct);
        Debug.Log($"[AUGMENT] Cooldown reduction applied — all cooldowns x{CooldownModifier.Multiplier:F2} ({pct:F0}% shorter).");
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

