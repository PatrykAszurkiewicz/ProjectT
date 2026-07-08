using UnityEngine;
using System.Collections.Generic;

public class EnemyStatModifierManager : MonoBehaviour, IGameSystem, IEnemyStatProvider
{
    public static EnemyStatModifierManager Instance { get; private set; }

    [Header("Global Enemy Modifiers (augment-driven)")]
    [SerializeField] private float moveSpeedMultiplier = 1f;
    [SerializeField] private float damageMultiplier = 1f;
    [SerializeField] private float healthMultiplier = 1f;

    // Per-stage difficulty scaling
    // A SEPARATE dimension from the augment multipliers above. GameOrchestrator
    // sets these once at the start of each stage via SetStageScaling(), sourced from
    // RunConfig.enemyHealthScalePerStage / enemyDamageScalePerStage / scaleBossesWithStage.
    [Header("Per-Stage Scaling (orchestrator-driven, read-only)")]
    [SerializeField] private float stageHealthMultiplier = 1f;
    [SerializeField] private float stageDamageMultiplier = 1f;
    [SerializeField] private bool stageScalingAffectsBosses = false;

    // Track all living enemies for retroactive health changes
    private HashSet<EnemyStats> trackedEnemies = new HashSet<EnemyStats>();

    private GameOrchestrator orchestrator;

    #region Singleton (can be replaced by orchestrator injection)
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Don't use DontDestroyOnLoad if using orchestrator
            // DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion

    #region IGameSystem
    public void Initialize(GameOrchestrator orchestrator)
    {
        this.orchestrator = orchestrator;
        //Debug.Log("[ENEMY_MODIFIER] Initialized by GameOrchestrator");
    }

    public void Shutdown()
    {
        trackedEnemies.Clear();
        Instance = null;
    }
    #endregion

    #region Enemy Tracking (for retroactive health changes)
    public void RegisterEnemy(EnemyStats enemy)
    {
        if (enemy != null)
        {
            trackedEnemies.Add(enemy);
        }
    }

    public void UnregisterEnemy(EnemyStats enemy)
    {
        trackedEnemies.Remove(enemy);
    }

    private void ApplyHealthChangeToExistingEnemies(float oldMultiplier, float newMultiplier)
    {
        if (Mathf.Approximately(oldMultiplier, newMultiplier)) return;

        float ratio = newMultiplier / oldMultiplier;
        int affectedCount = 0;

        foreach (var enemy in trackedEnemies)
        {
            if (enemy == null || enemy.IsDead()) continue;

            // Scale both max and current health proportionally
            float oldMaxHealth = enemy.maxHealth;
            float oldCurrentHealth = enemy.currentHealth;
            float healthPercentage = oldCurrentHealth / oldMaxHealth;

            enemy.maxHealth *= ratio;
            enemy.currentHealth = enemy.maxHealth * healthPercentage;

            affectedCount++;
        }

        //Debug.Log($"[ENEMY_MODIFIER] Health changed: {oldMultiplier:F3}x -> {newMultiplier:F3}x. Affected {affectedCount} living enemies.");
    }
    #endregion

    #region IEnemyStatProvider
    public void ApplyMoveSpeedMultiplier(float multiplier)
    {
        float oldValue = moveSpeedMultiplier;
        moveSpeedMultiplier *= multiplier;
        //Debug.Log($"[ENEMY_MODIFIER] Move speed: {oldValue:F3}x -> {moveSpeedMultiplier:F3}x (applied {multiplier:F2}x)");
    }

    public void ApplyDamageMultiplier(float multiplier)
    {
        float oldValue = damageMultiplier;
        damageMultiplier *= multiplier;
        Debug.Log($"[ENEMY_MODIFIER] Damage: {oldValue:F3}x -> {damageMultiplier:F3}x (applied {multiplier:F2}x)");
    }

    public void ApplyHealthMultiplier(float multiplier)
    {
        float oldValue = healthMultiplier;
        healthMultiplier *= multiplier;
        // Apply to existing enemies retroactively
        Debug.Log($"[ENEMY_MODIFIER] Health: {oldValue:F3}x -> {healthMultiplier:F3}x (applied {multiplier:F2}x)");
        ApplyHealthChangeToExistingEnemies(oldValue, healthMultiplier);
    }

    public float GetMoveSpeedMultiplier() => moveSpeedMultiplier;
    public float GetDamageMultiplier() => damageMultiplier;
    public float GetHealthMultiplier() => healthMultiplier;
    #endregion

    #region Per-stage scaling
    public float GetStageHealthMultiplier() => stageHealthMultiplier;
    public float GetStageDamageMultiplier() => stageDamageMultiplier;
    public bool StageScalingAffectsBosses => stageScalingAffectsBosses;

    /// Set the per-stage enemy HP/damage scaling. Called by GameOrchestrator at the
    /// start of every stage, before that stage's enemies spawn. The values come from
    /// RunConfig (enemyHealthScalePerStage / enemyDamageScalePerStage compounded per
    /// stage in GenerateRunPlan, and scaleBossesWithStage for the boss flag).
    public static void SetStageScaling(float healthMultiplier, float damageMultiplier, bool affectBosses)
    {
        if (Instance == null)
        {
            var managerGO = new GameObject("EnemyStatModifierManager");
            managerGO.AddComponent<EnemyStatModifierManager>(); // Awake sets Instance synchronously
        }

        Instance.stageHealthMultiplier = Mathf.Max(0f, healthMultiplier);
        Instance.stageDamageMultiplier = Mathf.Max(0f, damageMultiplier);
        Instance.stageScalingAffectsBosses = affectBosses;
    }
    #endregion

    #region Difficulty (Normal / Nightmare)
    // A run-wide, CONSTANT HP/damage factor that stacks MULTIPLICATIVELY on top of the
    // per-stage scaling above. Nightmare = +30% to EVERY enemy AND boss; Normal = ×1
    // (identical to the original behaviour → no regression). Kept here as a small static
    // block so it needs no extra script: enemies/bosses read the two multipliers where
    // they already read their other scaling, the Options menu calls SelectNormal/
    // SelectNightmare, and GameOrchestrator/RunPersistence drive the run lifecycle.
    //
    //   SelectedMode — the menu choice; DEFAULT for the NEXT run; persisted in PlayerPrefs.
    //   ActiveMode   — what the LIVE run locked in; saved into RunSaveData and restored on
    //                  resume, so changing the menu mid-run never alters a run in progress.
    public enum DifficultyMode { Normal = 0, Nightmare = 1 }

    public const float NightmareHealthMultiplier = 1.40f;
    public const float NightmareDamageMultiplier = 1.40f;
    private const string DifficultyPrefKey = "game.difficultyMode";

    // Loaded lazily on first ACCESS (from Awake/Start/menu — all legal) rather than in
    // a static field initializer: Unity forbids PlayerPrefs calls during type init on a
    // MonoBehaviour, which would throw TypeInitializationException and break every access.
    private static DifficultyMode? _selectedMode;
    public static DifficultyMode SelectedMode
    {
        get
        {
            if (_selectedMode == null)
                _selectedMode = PlayerPrefs.GetInt(DifficultyPrefKey, 0) == (int)DifficultyMode.Nightmare
                    ? DifficultyMode.Nightmare : DifficultyMode.Normal;
            return _selectedMode.Value;
        }
        private set => _selectedMode = value;
    }

    public static DifficultyMode ActiveMode { get; private set; } = DifficultyMode.Normal;

    // Multipliers the ACTIVE run scales by. Read by EnemyStats (regular enemies) and
    // BaseBossStats (bosses). Static, so no live EnemyStatModifierManager.Instance needed.
    public static float DifficultyHealthMultiplier =>
        ActiveMode == DifficultyMode.Nightmare ? NightmareHealthMultiplier : 1f;
    public static float DifficultyDamageMultiplier =>
        ActiveMode == DifficultyMode.Nightmare ? NightmareDamageMultiplier : 1f;

    // ── Options-menu hooks (wire your Normal / Nightmare buttons to these) ──
    public static void SelectNormal() => SelectDifficulty(DifficultyMode.Normal);
    public static void SelectNightmare() => SelectDifficulty(DifficultyMode.Nightmare);
    public static void SelectDifficulty(DifficultyMode mode)
    {
        SelectedMode = mode;
        PlayerPrefs.SetInt(DifficultyPrefKey, (int)mode);
        PlayerPrefs.Save();
    }

    // ── Run lifecycle (GameOrchestrator on start / resume, RunPersistence on adopt) ──
    // Lock the ACTIVE difficulty in from the current menu selection. Called at run start
    // AND at the start of every FRESH stage, so a menu change takes effect from the next
    // stage — never mid-stage. Stage starts are enemy-free, so nothing needs rescaling.
    public static DifficultyMode LockActiveFromSelected() => ActiveMode = SelectedMode;
    public static void SetActiveMode(int mode) =>
        ActiveMode = mode == (int)DifficultyMode.Nightmare ? DifficultyMode.Nightmare : DifficultyMode.Normal;
    #endregion

    public void ResetModifiers()
    {
        float oldHealthMultiplier = healthMultiplier;
        moveSpeedMultiplier = 1f;
        damageMultiplier = 1f;
        healthMultiplier = 1f;

        // Reset the per-stage dimension too. No retroactive rescale: the stage
        // value is re-set by GameOrchestrator at the next stage start and is only
        // read by enemies as they spawn, so there is nothing live to correct here.
        stageHealthMultiplier = 1f;
        stageDamageMultiplier = 1f;
        stageScalingAffectsBosses = false;

        ApplyHealthChangeToExistingEnemies(oldHealthMultiplier, 1f);
        //Debug.Log("[ENEMY_MODIFIER] All modifiers reset to 1.0x");
    }

#if UNITY_EDITOR
    [ContextMenu("Log Current State")]
    void LogState()
    {
        //Debug.Log($"=== Enemy Modifier State ===");
        //Debug.Log($"Move Speed: {moveSpeedMultiplier:F3}x");
        //Debug.Log($"Damage: {damageMultiplier:F3}x");
        //Debug.Log($"Health: {healthMultiplier:F3}x");
        //Debug.Log($"Tracked Enemies: {trackedEnemies.Count}");
    }
#endif
}
