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

    // The chooser's player index (0 in single player / for a null chooser).
    private int ChooserIndex(PlayerStats chooser)
    {
        if (chooser == null) return 0;
        var pref = chooser.GetComponent<PlayerRef>();
        return pref != null ? pref.PlayerIndex : 0;
    }

    // The chooser for a pick; defaults to player 0 / first player when unspecified.
    private PlayerStats ResolveChooser(PlayerStats chooser)
    {
        if (chooser != null) return chooser;
        if (PlayerRegistry.Count > 0)
        {
            var p0 = PlayerRegistry.Instance.Get(0);
            if (p0 != null && p0.Stats != null) return p0.Stats;
        }
        return FindAnyObjectByType<PlayerStats>();
    }

    // The chooser's own weapon (left/right slots live on one Weapon component),
    // falling back to the first Weapon found for the single-player path.
    private Weapon ResolveWeapon(PlayerStats chooser)
    {
        Weapon w = chooser != null ? chooser.GetComponentInChildren<Weapon>() : null;
        if (w == null) w = FindAnyObjectByType<Weapon>();
        return w;
    }


    // Back-compat entry (single-player / unspecified chooser → player 0).
    public void ApplyAugmentEffect(int augmentId)
    {
        ApplyAugmentEffect(augmentId, null);
    }

    // Phase 5: the chooser receives weapon/tool swaps and player-local effects.
    // Unlock pool, tower, enemy and global effects stay shared.
    public void ApplyAugmentEffect(int augmentId, PlayerStats chooser)
    {
        chooser = ResolveChooser(chooser);
        switch (augmentId)
        {
            case 3:
                ApplyObstacleArches();
                break;

            //  Weapons (left-click slot) 
            case 2:
                ApplyWeaponSwap("MeleeTest", "Melee", chooser);
                break;
            case 66:
                ApplyWeaponSwap("RangeTest", "Ranged", chooser);
                break;
            case 93:
                ApplyWeaponSwap("FlamethrowerTest", "Flamethrower", chooser);
                break;
            case 318:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(318, ChooserIndex(chooser));
                ApplyWeaponSwap("BoomerangTest", "Boomerang", chooser);
                break;

            //  Tools (right-click slot) 
            case 4:
                ApplyToolSwap("ObstacleTest", "ObstacleDrawer", chooser);
                break;
            case 65:
                ApplyToolSwap("HookTest", "Hook", chooser);
                break;
            case 81:
                ApplyToolSwap("ShieldTest", "Shield", chooser);
                break;

            case 314:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(314, ChooserIndex(chooser));
                ApplyToolSwap("BombLauncherTest", "BombLauncher", chooser);
                break;

            case 315:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(315, ChooserIndex(chooser));
                ApplyToolSwap("TrapTest", "Trap", chooser);
                break;

            case 316:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(316, ChooserIndex(chooser));
                ApplyToolSwap("DecoyTest", "Decoy", chooser);
                break;

            case 317:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(317, ChooserIndex(chooser));
                ApplyToolSwap("TurretTest", "Turret", chooser);
                break;

            case 323:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(323, ChooserIndex(chooser));
                ApplyToolSwap("CloakTest", "Cloak", chooser);
                break;

            case 324:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(324, ChooserIndex(chooser));
                ApplyToolSwap("TorchTest", "Torch", chooser);
                break;

            case 11:
                ApplyImmunityPhases(chooser);
                break;

            case 325:
                ApplyProjectileParryUnlock(chooser);
                break;

            //  Parry upgrade augments (select-once, no rarity scaling) 
            case 330:
                ApplyLongerParryStun(chooser);
                break;

            case 331:
                ApplyPowerfulParry(chooser);
                break;

            case 332:
                ApplyLongerParryWindow(chooser);
                break;

            case 333:
                ApplyHealOnParry(chooser);
                break;

            case 37:
                ApplyQuickRevive(chooser);
                break;

            case 326:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(326, ChooserIndex(chooser));
                ApplyToolSwap("ClockTest", "Clock", chooser);
                break;

            //  Mortar (left-click weapon slot) 
            case 327:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(327, ChooserIndex(chooser));
                ApplyWeaponSwap("MortarTest", "Mortar", chooser);
                break;

            //  Cooldown reduction (global) 
            case 328:
                ApplyCooldownReduction(chooser);
                break;

            //  Smoke Screen (right-click tool slot) 
            case 329:
                if (WeaponUnlockRegistry.Instance != null)
                    WeaponUnlockRegistry.Instance.TryUnlock(329, ChooserIndex(chooser));
                ApplyToolSwap("SmokeTest", "Smoke", chooser);
                break;

            //  Augments 334–347 (no rarity scaling) 
            case 334:
                ApplyLastStand(chooser);
                break;

            case 335:
                ApplyEnergyTithe();
                break;

            // 336 (Rich Harvest) is pure CSV (globalResourceMultiplier*1.2) — no code needed.

            case 337:
                ApplyLuckyStrikes();
                break;

            case 338:
                // Player penalty (damage / attack speed) is applied from the CSV.
                ApplySiegeDoctrine();
                break;

            case 339:
                // Fully CSV-driven (maxHealth*0.5, towerBuildCost*0.6, repairCostPerClick*0.6).
                Debug.Log("[AUGMENT] Martyr's Bargain — handled entirely by CSV stat modifiers.");
                break;

            case 340:
                ApplyPhalanx();
                break;

            case 341:
                ApplyMarksmansBounty();
                break;

            case 342:
                ApplyPlunderersPayload();
                break;

            case 343:
                ApplyEfficientGrid();
                break;

            case 344:
                ApplyPhoenixProtocol();
                break;

            case 345:
                ApplyOverloadAura();
                break;

            case 346:
                // Player attack penalty is applied from the CSV (damage*0.7).
                ApplyConscriptsSacrifice();
                break;

            case 347:
                // +15% tower damage is applied from the CSV (tower_damage*1.15).
                ApplyRecklessCalibration();
                break;

            case 348:
                ApplyEnergyAttunement();
                break;

            default:
                Debug.Log($"No special effect defined for augment {augmentId}");
                break;
        }
    }

    // Augment ID 3 — "Obstacle Generation".
    // Adds a ring of curved stone arches around the core (see
    // TowerDefenseMap.GenerateAugmentArches for the full design + safety guarantees)
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

    private void ApplyQuickRevive(PlayerStats chooser)
    {
        PlayerStats playerStats = chooser != null ? chooser : FindAnyObjectByType<PlayerStats>();
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
    private void ApplyProjectileParryUnlock(PlayerStats chooser)
    {
        int idx = ChooserIndex(chooser);
        ProjectileParry.SetUnlocked(idx);
        Debug.Log($"[AUGMENT] P{idx} Projectile Parry unlocked — equip a Shield and right-click to deflect enemy shots.");
    }

    //  Parry upgrade augments (330–333) 
    // Each reads its tunable value(s) straight from the CSV's Affected_Stats
    // column and writes them into ParryUpgrades. Reading the RAW CSV value here
    // (instead of going through the rarity-scaled StatModification pipeline) is
    // what keeps these four augments immune to rarity scaling.

    // Augment ID 330 — Longer Parry Stun. Extends parry stun duration.
    // CSV: parry_stun_bonus_seconds=<seconds>  (default 1.0)
    private void ApplyLongerParryStun(PlayerStats chooser)
    {
        float seconds = ReadAugmentStat(330, "parry_stun_bonus_seconds", 1.0f);
        ParryUpgrades.SetLongerParryStun(ChooserIndex(chooser), seconds);
        Debug.Log($"[AUGMENT] P{ChooserIndex(chooser)} Longer Parry Stun — parry stun extended by +{seconds:F2}s.");
    }

    // Augment ID 331 — Powerful Parry. Parried enemies take extra damage for a
    // fixed duration (applies to melee AND projectile parry).
    // CSV: parry_damage_bonus=<fraction>,parry_damage_duration=<seconds>
    //      (defaults 0.30 and 5.0)
    private void ApplyPowerfulParry(PlayerStats chooser)
    {
        float bonus = ReadAugmentStat(331, "parry_damage_bonus", 0.30f);
        float duration = ReadAugmentStat(331, "parry_damage_duration", 5.0f);

        ParryUpgrades.SetPowerfulParry(ChooserIndex(chooser), bonus, duration);
        Debug.Log($"[AUGMENT] P{ChooserIndex(chooser)} Powerful Parry — parried enemies take +{bonus * 100f:F0}% damage for {duration:F1}s.");
    }

    // Augment ID 332 — Longer Parry Window. Widens the parry window.
    // CSV: parry_window_frames=<frames>  (default 2)
    private void ApplyLongerParryWindow(PlayerStats chooser)
    {
        int frames = Mathf.RoundToInt(ReadAugmentStat(332, "parry_window_frames", 2f));
        ParryUpgrades.SetLongerParryWindow(ChooserIndex(chooser), frames);
        Debug.Log($"[AUGMENT] P{ChooserIndex(chooser)} Longer Parry Window — parry window widened by +{frames} frame(s).");
    }

    // Augment ID 333 — Heal on Parry. Heals on each successful parry (melee AND
    // projectile parry).
    // CSV: parry_heal_percent=<fraction of max HP>  (default 0.03)
    private void ApplyHealOnParry(PlayerStats chooser)
    {
        float percent = ReadAugmentStat(333, "parry_heal_percent", 0.03f);
        ParryUpgrades.SetHealOnParry(ChooserIndex(chooser), percent);
        Debug.Log($"[AUGMENT] P{ChooserIndex(chooser)} Heal on Parry — heal {percent * 100f:F1}% of max HP per parry.");
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
    private void ApplyCooldownReduction(PlayerStats chooser)
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

        int idx = ChooserIndex(chooser);
        CooldownModifier.SetReductionPercent(pct, idx);
        Debug.Log($"[AUGMENT] P{idx} cooldown reduction applied — their cooldowns x{CooldownModifier.MultiplierFor(idx):F2} ({pct:F0}% shorter).");
    }

    private void ApplyImmunityPhases(PlayerStats chooser)
    {
        PlayerStats playerStats = chooser != null ? chooser : FindAnyObjectByType<PlayerStats>();
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

    private void ApplyWeaponSwap(string weaponAssetName, string cursorType, PlayerStats chooser)
    {
        Weapon weapon = ResolveWeapon(chooser);
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

    private void ApplyToolSwap(string toolAssetName, string cursorType, PlayerStats chooser)
    {
        Weapon weapon = ResolveWeapon(chooser);
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

    //  Augments 334–348 
    //  Every tunable below is read straight from the CSV's Affected_Stats column
    //  via ReadAugmentStat (the same path the parry upgrades use). Edit the CSV
    //  value and re-run — no recompile needed. The `key:value` form is ignored by
    //  the stat-mod parser, so these never get rarity-scaled or mis-applied.

    // 334 — Last Stand: bonus energy & attack while below a health threshold.
    // CSV: aug_health_threshold, aug_energy_bonus, aug_attack_bonus
    private void ApplyLastStand(PlayerStats chooser)
    {
        var playerStats = chooser != null ? chooser : FindAnyObjectByType<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[AUGMENT] Last Stand — could not find PlayerStats.");
            return;
        }
        if (playerStats.GetComponent<LowHealthEmpowerEffect>() != null)
        {
            Debug.LogWarning("[AUGMENT] Last Stand already active — cannot stack.");
            return;
        }
        var effect = playerStats.gameObject.AddComponent<LowHealthEmpowerEffect>();
        effect.HealthThreshold = ReadAugmentStat(334, "aug_health_threshold", 0.30f);
        effect.EnergyBonus = ReadAugmentStat(334, "aug_energy_bonus", 0.30f);
        effect.DamageBonus = ReadAugmentStat(334, "aug_attack_bonus", 0.30f);
        //Debug.Log($"[AUGMENT] Last Stand — below {effect.HealthThreshold * 100f:F0}% HP: " +
        //          $"+{effect.EnergyBonus * 100f:F0}% energy, +{effect.DamageBonus * 100f:F0}% attack.");
    }

    // 335 — Energy Tithe: flat energy on every tower kill.
    // CSV: aug_energy_per_kill
    private void ApplyEnergyTithe()
    {
        TowerKillRewards.Enabled = true;
        TowerKillRewards.EnergyPerKill = Mathf.RoundToInt(ReadAugmentStat(335, "aug_energy_per_kill", 2f));
        //Debug.Log($"[AUGMENT] Energy Tithe — +{TowerKillRewards.EnergyPerKill} energy per tower kill.");
    }

    // 337 — Lucky Strikes: increase global enemy energy-drop chance.
    // CSV: aug_drop_chance_bonus  (e.g. 0.20 = +20%)
    private void ApplyLuckyStrikes()
    {
        float bonus = ReadAugmentStat(337, "aug_drop_chance_bonus", 0.20f);
        float current = EnergyDropManager.Instance != null
            ? EnergyDropManager.Instance.globalDropChance : 0.5f;
        EnergyDropManager.SetGlobalDropChance(current * (1f + bonus));
        //Debug.Log($"[AUGMENT] Lucky Strikes — enemy energy-drop chance +{bonus * 100f:F0}%.");
    }

    // 338 — Siege Doctrine: tower damage & fire rate up (player penalty via CSV).
    // CSV: aug_tower_damage_bonus, aug_tower_firerate_bonus
    private void ApplySiegeDoctrine()
    {
        float dmg = ReadAugmentStat(338, "aug_tower_damage_bonus", 0.30f);
        float fire = ReadAugmentStat(338, "aug_tower_firerate_bonus", 0.30f);
        TowerCombatModifiers.DamageMultiplier *= (1f + dmg);
        TowerCombatModifiers.BaseFireRateMultiplier *= (1f + fire);
        //Debug.Log($"[AUGMENT] Siege Doctrine — towers +{dmg * 100f:F0}% damage, +{fire * 100f:F0}% fire rate.");
    }

    // 340 — Phalanx: per-active-tower fire-rate & range buff to all towers.
    // CSV: aug_firerate_per_tower, aug_range_per_tower
    private void ApplyPhalanx()
    {
        float firePer = ReadAugmentStat(340, "aug_firerate_per_tower", 0.03f);
        float rangePer = ReadAugmentStat(340, "aug_range_per_tower", 0.02f);
        TowerCountScalingManager.Configure(firePer, rangePer);
        //Debug.Log($"[AUGMENT] Phalanx — +{firePer * 100f:F0}% fire rate & +{rangePer * 100f:F0}% range per active tower.");
    }

    // 341 — Marksman's Bounty: bonus drop chance on tower kills.
    // CSV: aug_tower_kill_chance_bonus
    private void ApplyMarksmansBounty()
    {
        float bonus = ReadAugmentStat(341, "aug_tower_kill_chance_bonus", 0.30f);
        EnemyDropAugments.TowerKillChanceBonus += bonus;
        //Debug.Log($"[AUGMENT] Marksman's Bounty — +{bonus * 100f:F0}% drop chance on tower kills.");
    }

    // 342 — Plunderer's Payload: bonus drop value on tower kills.
    // CSV: aug_tower_kill_value_bonus
    private void ApplyPlunderersPayload()
    {
        float bonus = ReadAugmentStat(342, "aug_tower_kill_value_bonus", 0.30f);
        EnemyDropAugments.TowerKillValueBonus += bonus;
        //Debug.Log($"[AUGMENT] Plunderer's Payload — +{bonus * 100f:F0}% energy from tower kills.");
    }

    // 343 — Efficient Grid: REDUCE tower energy decay only.
    // CSV: aug_tower_decay_mult — clamped to (0,1] so it can never speed decay up.
    private void ApplyEfficientGrid()
    {
        float mult = ReadAugmentStat(343, "aug_tower_decay_mult", 0.90f);
        mult = Mathf.Clamp(mult, 0.01f, 1f);
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.towerEnergyDecayRate *= mult;
            //Debug.Log($"[AUGMENT] Efficient Grid — tower energy decay x{mult:F2} (slower).");
        }
    }

    // 344 — Phoenix Protocol: revive destroyed towers next stage.
    // CSV: aug_revives_per_stage
    private void ApplyPhoenixProtocol()
    {
        int revives = Mathf.RoundToInt(ReadAugmentStat(344, "aug_revives_per_stage", 1f));
        TowerRevivalManager.Configure(revives);
        //Debug.Log($"[AUGMENT] Phoenix Protocol — {revives} destroyed tower(s) return each stage.");
    }

    // 345 — Overload Aura: generators damage nearby enemies for energy made.
    // CSV: aug_aoe_damage_mult  (1.0 = damage equal to the energy generated)
    private void ApplyOverloadAura()
    {
        GeneratorAoeDamage.Enabled = true;
        GeneratorAoeDamage.DamageMultiplier = ReadAugmentStat(345, "aug_aoe_damage_mult", 1.0f);
        //Debug.Log($"[AUGMENT] Overload Aura — generators deal x{GeneratorAoeDamage.DamageMultiplier:F2} of energy generated as AoE.");
    }

    // 346 — Conscript's Sacrifice: tower damage up (player penalty via CSV).
    // CSV: aug_tower_damage_bonus
    private void ApplyConscriptsSacrifice()
    {
        float dmg = ReadAugmentStat(346, "aug_tower_damage_bonus", 0.30f);
        TowerCombatModifiers.DamageMultiplier *= (1f + dmg);
        //Debug.Log($"[AUGMENT] Conscript's Sacrifice — towers +{dmg * 100f:F0}% damage.");
    }

    // 347 — Reckless Calibration: INCREASE tower decay only (+15% tower damage via CSV).
    // CSV: aug_tower_decay_mult — clamped to >=1 so it can never reduce decay.
    private void ApplyRecklessCalibration()
    {
        float mult = ReadAugmentStat(347, "aug_tower_decay_mult", 1.10f);
        mult = Mathf.Max(1f, mult);
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.towerEnergyDecayRate *= mult;
            //Debug.Log($"[AUGMENT] Reckless Calibration — tower energy decay x{mult:F2} (faster).");
        }
    }

    // 348 — Energy Attunement: increase energy from ALL sources (permanent).
    // CSV: aug_energy_gain_bonus  (e.g. 0.10 = +10%)
    private void ApplyEnergyAttunement()
    {
        float bonus = ReadAugmentStat(348, "aug_energy_gain_bonus", 0.10f);
        PlayerEconomyModifiers.EnergyGainMultiplier *= (1f + bonus);
        //Debug.Log($"[AUGMENT] Energy Attunement — all energy gains +{bonus * 100f:F0}%.");
    }
}


