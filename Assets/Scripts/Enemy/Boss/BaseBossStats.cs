using System.Collections;
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

    // ── Top-of-screen boss bar ────────────────────────────────────────────
    // Registration lives here rather than in Boss1/Boss2 so that EVERY boss —
    // including ones added later — gets the bar for free with no per-boss wiring.
    // BossHealthBarManager creates itself on demand, so a scene with no manager
    // still works; it also creates its own overlay canvas, so the bar is
    // identical in single player and split screen.
    [Header("Top-of-Screen Boss Bar")]
    [Tooltip("Show the large boss health bar at the top of the screen once the boss-intro " +
             "camera zoom has finished. Turn off for minor / summoned bosses that shouldn't " +
             "take over the HUD.")]
    public bool showTopScreenBossBar = true;

    [Tooltip("Optional per-boss override of the bar prefab. Leave empty to use the one on " +
             "BossHealthBarManager (or Resources/UI/BossBar).")]
    public GameObject bossBarPrefabOverride;

    [Tooltip("Also hide the small world-space health bar that floats above this boss, " +
             "since the big bar already shows the same pool. Off by default so nothing " +
             "about the existing setup changes unless you ask for it.")]
    public bool hideWorldHealthBarWhenTopBarShown = false;

    /// Friendly name for UI (a boss-name label, kill feed, etc.).
    public string DisplayName =>
        enemyData != null && !string.IsNullOrEmpty(enemyData.enemyName) ? enemyData.enemyName : name;

    /// The full bar pool: armour and health as one continuous track. Matches what
    /// Boss1/Boss2 already feed the small world-space bar (maxHealth + maxArmor).
    public float TotalMaxPool => maxHealth + maxArmor;

    public float TotalCurrentPool =>
        Mathf.Max(0f, currentHealth) + (armorDestroyed ? 0f : Mathf.Max(0f, bossArmor));

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

    // Runs AFTER the boss's pools are final (Awake) and after EnemyStats.Start has
    // created the world-space bar. Boss1/Boss2 both call base.Start() from their own
    // Start override, so they pass through here automatically.
    protected override void Start()
    {
        base.Start();

        if (!showTopScreenBossBar) return;

        BossHealthBarManager.Show(this, bossBarPrefabOverride);

        if (hideWorldHealthBarWhenTopBarShown)
            StartCoroutine(HideWorldBarNextFrame());
    }

    // Deferred by one frame so it lands after the subclass's own
    // InitializeBossHealthBar() has finished configuring that bar.
    private IEnumerator HideWorldBarNextFrame()
    {
        yield return null;
        var worldBar = GetHealthBar();
        if (worldBar != null) worldBar.SetVisible(false);
    }

    public override void TakeDamage(float amount)
    {
        //Debug.Log($"[BASE_BOSS] TakeDamage amount={amount}");
        if (DebugCheats.DamageBlocked(this)) return;

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

        // The top-of-screen bar reads the pool itself every frame, so there is
        // nothing to push here — it stays correct even for bosses that change
        // health outside TakeDamage (execution thresholds, scripted phases, …).
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

