using UnityEngine;
using System.Collections.Generic;

//  Tower-kill economy — augments 335, 341, 342
//  These three augments all care about "was this enemy killed by a tower?", so
//  they share one attribution helper and live together.
//  Tunables (pushed in from the CSV by AugmentEffectHandler):
//    335  aug_energy_per_kill         : flat energy per tower kill (default 2)
//    341  aug_tower_kill_chance_bonus : +drop chance on tower kills (default 0.30)
//    342  aug_tower_kill_value_bonus  : +drop value  on tower kills (default 0.30)



// Records the last time each enemy took tower damage. An enemy counts as
// "tower-killed" if it died within AttributionWindow of such a hit.
public static class TowerKillAttribution
{
    private const float AttributionWindow = 1.5f; // generous: covers projectile travel

    private static readonly Dictionary<int, float> lastTowerHitTime = new Dictionary<int, float>();

    // HOOK: every tower->enemy damage site calls this (see integration notes).
    public static void MarkTowerHit(GameObject enemy)
    {
        if (enemy == null) return;
        lastTowerHitTime[enemy.GetInstanceID()] = Time.time;
    }

    public static bool WasRecentlyHitByTower(GameObject enemy)
    {
        if (enemy == null) return false;
        if (lastTowerHitTime.TryGetValue(enemy.GetInstanceID(), out float t))
            return (Time.time - t) <= AttributionWindow;
        return false;
    }

    // Call when an enemy dies so the lookup table doesn't grow forever.
    public static void Forget(GameObject enemy)
    {
        if (enemy == null) return;
        lastTowerHitTime.Remove(enemy.GetInstanceID());
    }
}


// 335 — Energy Tithe: flat energy whenever a tower lands the kill.
public static class TowerKillRewards
{
    public static bool Enabled = false;
    public static int EnergyPerKill = 2;

    // HOOK: EnemyStats.PerformDeath calls this once per death.
    public static void OnEnemyKilled(GameObject enemy)
    {
        if (!Enabled) return;
        if (!TowerKillAttribution.WasRecentlyHitByTower(enemy)) return;
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.GivePlayerEnergy(EnergyPerKill);
            Debug.Log($"[AUGMENT] Energy Tithe — tower kill granted {EnergyPerKill} energy.");
        }
    }
}


// 341 / 342 — tower-kill drop bonuses. Centralises the death-drop spawn so the
// per-enemy override path (e.g. the Wolf's guaranteed drop) is preserved.
public static class EnemyDropAugments
{
    public static float TowerKillChanceBonus = 0f; // 341 (additive fraction)
    public static float TowerKillValueBonus = 0f; // 342 (additive fraction)

    // HOOK: EnemyStats.PerformDeath calls this INSTEAD of its inline drop block.
    public static void SpawnEnemyDrop(Vector3 position, int stageIndex,
                                      GameObject enemy,
                                      float overrideChance, int overrideValue)
    {
        bool towerKill = TowerKillAttribution.WasRecentlyHitByTower(enemy);
        bool haveBonus = TowerKillChanceBonus > 0f || TowerKillValueBonus > 0f;

        //  Per-enemy override path 
        if (overrideValue > 0 && overrideChance >= 0f)
        {
            float chance = overrideChance;
            int value = overrideValue;
            if (towerKill && haveBonus)
            {
                chance = Mathf.Clamp01(chance * (1f + TowerKillChanceBonus));
                value = Mathf.RoundToInt(value * (1f + TowerKillValueBonus));
                //Debug.Log($"[AUGMENT] Tower-kill drop bonus applied (override path): chance {chance:F2}, value {value}.");
            }
            EnergyDropManager.TrySpawnEnergyDrop(position, chance, value);
            return;
        }

        //  Stage-scaled default path 
        if (!towerKill || !haveBonus)
        {
            // No tower-kill bonus in play: behave exactly like before.
            EnergyDropManager.TrySpawnEnemyDrop(position, stageIndex);
            return;
        }

        var cfg = GameOrchestrator.Instance != null ? GameOrchestrator.Instance.runConfig : null;
        int baseValue = StageEnergyScaling.EnemyDropValue(cfg, stageIndex);
        float baseChance = EnergyDropManager.Instance != null
            ? EnergyDropManager.Instance.globalDropChance
            : 0.5f;

        float finalChance = Mathf.Clamp01(baseChance * (1f + TowerKillChanceBonus));
        int finalValue = Mathf.RoundToInt(baseValue * (1f + TowerKillValueBonus));

        //Debug.Log($"[AUGMENT] Tower-kill drop bonus applied: base value {baseValue} -> {finalValue}, chance {finalChance:F2}.");
        EnergyDropManager.TrySpawnEnergyDrop(position, finalChance, finalValue);
    }
}

