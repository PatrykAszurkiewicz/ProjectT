using System.Collections;
using UnityEngine;

// Base class for all boss enemies
public abstract class BaseBossStats : EnemyStats
{
    [Header("Boss Armor System")]
    public float maxArmor = 1000f;

    [Tooltip("Extra boss HP + armour per stage. 0.6 = stage 2 has 1.6x, stage 3 has 2.2x... " +
             "Set to 0 to disable (identical to the old behaviour). Skipped automatically when " +
             "EnemyStatModifierManager already stage-scales bosses, so it can never double-scale.")]
    public float stageHealthGrowth = 0.6f;
    protected float bossArmor;
    protected bool armorDestroyed = false;

    public bool IsArmorDestroyed => armorDestroyed;
    public float CurrentArmor => bossArmor;

    // Top-of-screen boss bar 
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

    [Tooltip("Hide the small world-space health bar that floats above this boss, since the big " +
             "top-of-screen bar already shows the same pool. On by default now that every boss " +
             "has the big bar. Turn OFF for a boss that has NO top bar (showTopScreenBossBar off) " +
             "but should still show its floating bar.")]
    public bool hideWorldHealthBarWhenTopBarShown = true;

    [Header("Boss Name (UI)")]
    [Tooltip("Name shown by the boss-intro flash, the wave counter and the top-of-screen boss " +
             "bar. Type it here for bosses that have NO EnemyData asset (procedurally built " +
             "bosses). Leave EMPTY for bosses that do have one - then EnemyData.enemyName wins " +
             "and the name stays authored in a single place.")]
    public string bossDisplayName = "";

    /// Friendly name for UI (the top-of-screen boss bar, kill feed, etc.).
    /// Priority: this component's bossDisplayName -> EnemyData.enemyName -> tidied
    /// GameObject name (so the bar shows "Boss3", never "Boss3(Clone)").
    /// Every source is serialized data, so this is readable on a PREFAB too - which is
    /// what lets GameOrchestrator flash the name before the boss is instantiated.
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(bossDisplayName)
            ? bossDisplayName.Trim()
            : (enemyData != null && !string.IsNullOrEmpty(enemyData.enemyName)
                ? enemyData.enemyName.Trim()
                : name.Replace("(Clone)", "").Replace('_', ' ').Trim());

    // The full bar pool: armour and health as one continuous track. Matches what
    // Boss1/Boss2 already feed the small world-space bar (maxHealth + maxArmor).
    public float TotalMaxPool => maxHealth + maxArmor;

    public float TotalCurrentPool =>
        Mathf.Max(0f, currentHealth) + (armorDestroyed ? 0f : Mathf.Max(0f, bossArmor));

    // Damage multiplier for a boss special-attack (laser, explosion, etc.), stacking:
    //   Difficulty (Normal/Nightmare) — ALWAYS applies to bosses.
    //   Per-stage scaling — only when scaleBossesWithStage is on (original opt-in).
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

        // STAGE HEALTH for bosses. Bosses are drawn randomly per stage, so their pools
        // must grow with the stage rather than being fixed per boss. Skipped when the
        // modifier manager already stage-scales bosses (avoids double scaling).
        // stageHealthGrowth = 0 → ×1, identical to before.
        var statMgr = EnemyStatModifierManager.Instance;
        bool alreadyStageScaled = statMgr != null && statMgr.StageScalingAffectsBosses;
        int stage = GameOrchestrator.Instance != null ? GameOrchestrator.Instance.CurrentStageIndex : 0;
        float stageHp = alreadyStageScaled ? 1f : 1f + Mathf.Max(0f, stageHealthGrowth) * Mathf.Max(0, stage);
        if (stageHp != 1f)
        {
            maxHealth *= stageHp;
            currentHealth *= stageHp;
            maxArmor *= stageHp;
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
            StartCoroutine(HideWorldBarRoutine());
    }


    private IEnumerator HideWorldBarRoutine()
    {
        // Wait one frame so the subclass's InitializeBossHealthBar() has run.
        yield return null;

        CanvasGroup cg = null;

        while (this != null && currentHealth > 0f)
        {
            var worldBar = GetHealthBar();
            if (worldBar != null)
            {
                // Re-grab the CanvasGroup if the bar was rebuilt under a new object.
                if (cg == null || cg.gameObject != worldBar.gameObject)
                    cg = worldBar.EnsureCanvasGroup();

                if (cg != null && cg.alpha != 0f)
                {
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
            }
            else
            {
                cg = null;   // bar not up yet (or already gone); keep watching
            }

            yield return null;
        }
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

    /// Bosses initialise their world bar with the COMBINED pool
    /// (maxHealth + maxArmor) and feed it health+armour together, so a plain
    /// SetMaxHealth(maxHealth, currentHealth) would halve the denominator and make
    /// the bar jump. Report the same combined pool the bar was built with.
    public override void RefreshHealthBarCapacity()
    {
        var bar = GetHealthBar();
        if (bar != null)
            bar.SetMaxHealth(TotalMaxPool, TotalCurrentPool);

        // The top-of-screen bar reads TotalMaxPool / TotalCurrentPool every frame,
        // so it needs no push here.
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



