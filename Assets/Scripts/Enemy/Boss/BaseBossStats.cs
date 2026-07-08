using UnityEngine;

/// Base class for all boss enemies
public abstract class BaseBossStats : EnemyStats
{
    [Header("Boss Armor System")]
    public float maxArmor = 1000f;
    protected float bossArmor;
    protected bool armorDestroyed = false;

    public bool IsArmorDestroyed => armorDestroyed;
    public float CurrentArmor => bossArmor;

    // Damage multiplier for a boss special-attack (laser, explosion, etc.), stacking:
    //   • Difficulty (Normal/Nightmare) — ALWAYS applies to bosses.
    //   • Per-stage scaling — only when scaleBossesWithStage is on (original opt-in).
    // Normal + boss-stage-scaling off → 1f, identical to before (no regression).
    protected float BossStageDamageMultiplier
    {
        get
        {
            var mgr = EnemyStatModifierManager.Instance;
            float stagePart = (mgr != null && mgr.StageScalingAffectsBosses)
                ? mgr.GetStageDamageMultiplier()
                : 1f;
            return EnemyStatModifierManager.DifficultyDamageMultiplier * stagePart;
        }
    }

    protected BossHead spawnedHead;

    protected override void Awake()
    {
        base.Awake();

        // NIGHTMARE HEALTH for bosses. Applied here (ungated by scaleBossesWithStage)
        // so a boss's health AND armour pools both scale with the run difficulty, on
        // top of whatever base.Awake() already set. Normal → ×1 (no change). Regular
        // enemies get their difficulty HP in EnemyStats.Awake, so it's never doubled.
        float diffHp = EnemyStatModifierManager.DifficultyHealthMultiplier;
        if (diffHp != 1f)
        {
            maxHealth *= diffHp;
            currentHealth *= diffHp;
            maxArmor *= diffHp;
        }

        bossArmor = maxArmor;
    }

    public override void TakeDamage(float amount)
    {
        //Debug.Log($"[BASE_BOSS] TakeDamage amount={amount}");
        if (!armorDestroyed && bossArmor > 0)
        {
            bossArmor -= amount;

            if (bossArmor <= 0)
            {
                float overflow = Mathf.Abs(bossArmor);
                bossArmor = 0;
                OnArmorDestroyed();

                if (overflow > 0)
                {
                    base.TakeDamage(overflow);
                }
            }

            CallStartDamageFlash();
        }
        else
        {
            base.TakeDamage(amount);
        }

        UpdateBossHealthBar();
    }

    // Called when armor is destroyed. Override for custom behavior.
    protected virtual void OnArmorDestroyed()
    {
        armorDestroyed = true;
        bossArmor = 0;
        //Debug.Log($"{enemyData?.enemyName ?? "Boss"} ARMOR DESTROYED!");
    }

    public virtual void DestroyArmor()
    {
        if (armorDestroyed) return;
        OnArmorDestroyed();
        UpdateBossHealthBar();
    }

    protected virtual void UpdateBossHealthBar()
    {
        if (HealthBar != null)
        {
            float totalCurrent = currentHealth + (armorDestroyed ? 0 : bossArmor);
            HealthBar.UpdateHealth(totalCurrent);
        }
    }

    public virtual void OnHeadDestroyed()
    {
        DestroyArmor();
        spawnedHead = null;
        // Validate armor state after head destruction
        Debug.Assert(armorDestroyed, $"{enemyData?.enemyName ?? "Boss"}: armorDestroyed should be true after head destroyed!");
        Debug.Assert(bossArmor <= 0f, $"{enemyData?.enemyName ?? "Boss"}: bossArmor should be 0 after head destroyed, but is {bossArmor}!");


    }

    // Called by boss subclasses from their death routines.
    // Rolls for a permanent weapon/tool blueprint drop using the current stage index.
    // Every boss inherits this — Boss1, Boss2, FinalBoss, etc. — so adding new
    // bosses doesn't require any per-boss wiring.
    protected void RollBlueprintDrop(Vector3 deathPos)
    {
        int stageIdx = GameOrchestrator.Instance != null
            ? GameOrchestrator.Instance.CurrentStageIndex
            : 0;
        BossBlueprintDropper.RollAndSpawn(deathPos, stageIdx);
    }
}
