// Static helper — centralises all stage-based energy drop / reward scaling.
// Both EnergyDropManager and GameOrchestrator use it
public static class StageEnergyScaling
{
    // Enemy drops 
    // Energy value for a drop from a regular enemy at the given stage (0-based).
    // Formula: baseEnemyDropValue × (1 + enemyDropScalePerStage) ^ stageIndex
    public static int EnemyDropValue(RunConfig cfg, int stageIndex)
    {
        if (cfg == null) return 10;
        return ScaledValue(cfg.baseEnemyDropValue, cfg.enemyDropScalePerStage, stageIndex);
    }

    //  Boss drops 
    // Energy value for a drop burst from a stage boss at the given stage (0-based).
    // Formula: baseBossDropValue × (1 + bossDropScalePerStage) ^ stageIndex
    public static int BossDropValue(RunConfig cfg, int stageIndex)
    {
        if (cfg == null) return 50;
        return ScaledValue(cfg.baseBossDropValue, cfg.bossDropScalePerStage, stageIndex);
    }

    // Post-stage rewards 
    // Energy bonus for choosing HEAL at end of stage stageIndex (0-based).
    public static int HealChoiceEnergy(RunConfig cfg, int stageIndex, int baseHealBonus)
    {
        if (cfg == null) return baseHealBonus;
        return ScaledValue(baseHealBonus, cfg.postStageRewardScalePerStage, stageIndex);
    }

    // Energy bonus for choosing AUGMENT at end of stage stageIndex (0-based).
    public static int AugmentChoiceEnergy(RunConfig cfg, int stageIndex, int baseAugmentBonus)
    {
        if (cfg == null) return baseAugmentBonus;
        return ScaledValue(baseAugmentBonus, cfg.postStageRewardScalePerStage, stageIndex);
    }

    // Shared math
    // base × (1 + scalePerStage)^stageIndex, rounded to int, minimum 1.
    private static int ScaledValue(int baseValue, float scalePerStage, int stageIndex)
    {
        if (stageIndex <= 0) return UnityEngine.Mathf.Max(1, baseValue);
        float multiplier = UnityEngine.Mathf.Pow(1f + scalePerStage, stageIndex);
        return UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(baseValue * multiplier));
    }
}
