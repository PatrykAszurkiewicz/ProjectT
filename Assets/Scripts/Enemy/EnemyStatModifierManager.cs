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
        if (Instance != null && Instance != this)
        {
            // FIX: this used to Destroy(gameObject). SetStageScaling() creates this
            // manager on a bare GameObject, but designers also drop it onto a shared
            // "Systems" object — in which case a duplicate took every other system on
            // that object down with it. Destroy only the duplicate COMPONENT, matching
            // RunPersistence / WaveCheckpointService.
            Destroy(this);
            return;
        }
        Instance = this;
        // Don't use DontDestroyOnLoad if using orchestrator
        // DontDestroyOnLoad(gameObject);
    }

    // Clear run-scoped statics between Play sessions when "Enter Play Mode without
    // domain reload" is on. Without this the ACTIVE difficulty of the previous session
    // leaked into the first frames of the next one, before StartRun re-locked it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        ActiveMode = DifficultyMode.Normal;
        _selectedMode = null;
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

        // Only the live singleton may clear the static handle. Without this guard a
        // duplicate instance being torn down would null out the REAL Instance, after
        // which SetStageScaling() would silently build a second manager and every
        // Instance-based read (BaseBossStats.BossStageDamageMultiplier, ...) would
        // fall back to 1x for the rest of the run.
        if (Instance == this) Instance = null;
    }
    #endregion

    #region Enemy Tracking (for retroactive health changes)
    public void RegisterEnemy(EnemyStats enemy)
    {
        if (enemy != null)
        {
            // Skip throwaway EDITOR/TEST fixtures.
            //
            // AugmentBossRegressionHarness builds live probe objects (~bossNormal,
            // ~bossNightmare, ~seed_Boss1..3) under a root flagged HideAndDontSave,
            // lets their Awake run so real components initialise, then destroys the
            // root. Five of them registered here on every suite run, and the harness's
            // own 'tracked_enemies_no_destroyed_entries' invariant then reported them
            // as a leak — it was detecting its own fixtures, five more per F5 press.
            //
            // A DontSave root means "not part of the running game", which is exactly
            // the objects this set should ignore: they are never damaged, so a
            // retroactive health rescale has nothing to do to them either. Real
            // spawned enemies are parented into the scene and never carry this flag.
            if (IsEditorFixture(enemy)) return;

            trackedEnemies.Add(enemy);
#if UNITY_EDITOR
            _trackedNames[enemy] = enemy.name;
#endif
        }
    }

    // True for objects living under a HideAndDontSave root — harness fixtures and
    // other editor-only scaffolding, never gameplay enemies.
    private static bool IsEditorFixture(EnemyStats enemy)
    {
        var root = enemy.transform.root;
        if (root == null) return false;
        return (root.gameObject.hideFlags & HideFlags.DontSave) != 0;
    }

    public void UnregisterEnemy(EnemyStats enemy)
    {
        trackedEnemies.Remove(enemy);
#if UNITY_EDITOR
        if (enemy != null) _trackedNames.Remove(enemy);
#endif
    }

#if UNITY_EDITOR
    // DIAGNOSTIC for the 'tracked_enemies_no_destroyed_entries' invariant.
    //
    // A destroyed Unity object throws on .name, so once an entry has leaked you can
    // no longer ask it what it was. The only way to identify a leaker is to record
    // the name at REGISTER time, which is what this map does. Editor-only, so it
    // costs a build nothing.
    //
    // Right-click the component header -> "Log Leaked Tracked Enemies" and it names
    // every prefab still in the set whose object is gone. Run it after the invariant
    // trips; whatever it names is the type whose OnDestroy is not reaching
    // UnregisterEnemy.
    private readonly Dictionary<EnemyStats, string> _trackedNames =
        new Dictionary<EnemyStats, string>();

    // Scans itself every couple of seconds and prints the moment the leak count
    // CHANGES, so the answer lands in the Console with no clicking. Silent while the
    // count is unchanged (including zero), so it will not spam.
    private float _leakScanTimer;
    private int _lastReportedLeakCount = 0;

    private void Update()
    {
        _leakScanTimer += Time.unscaledDeltaTime;
        if (_leakScanTimer < 2f) return;
        _leakScanTimer = 0f;
        ReportLeaksIfChanged();
    }

    private void ReportLeaksIfChanged()
    {
        int leaked = 0;
        foreach (var kv in _trackedNames) if (kv.Key == null) leaked++;

        if (leaked == _lastReportedLeakCount) return;
        _lastReportedLeakCount = leaked;

        if (leaked == 0)
        {
            Debug.Log($"[ENEMY_MODIFIER_LEAK] Clean again — 0 destroyed entries, " +
                      $"{trackedEnemies.Count} live enemy/enemies tracked.");
            return;
        }

        LogLeakedTrackedEnemies();
    }

    [ContextMenu("Log Leaked Tracked Enemies")]
    private void LogLeakedTrackedEnemies()
    {
        var counts = new Dictionary<string, int>();
        int total = 0;
        foreach (var kv in _trackedNames)
        {
            if (kv.Key != null) continue;
            total++;
            counts[kv.Value] = (counts.TryGetValue(kv.Value, out int c) ? c : 0) + 1;
        }

        if (total == 0)
        {
            Debug.Log($"[ENEMY_MODIFIER_LEAK] No leaked entries. Tracking {trackedEnemies.Count} enemy/enemies.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"[ENEMY_MODIFIER_LEAK] {total} destroyed enemy/enemies still in trackedEnemies. ");
        sb.Append($"Live count = {trackedEnemies.Count}. Culprits by prefab name:");
        foreach (var kv in counts) sb.Append($"\n    {kv.Value}x  {kv.Key}");
        sb.Append($"\n  Scene='{gameObject.scene.name}'  t={Time.timeSinceLevelLoad:F1}s");
        sb.Append("\n  (COPY THE LINES ABOVE.) Their OnDestroy is not reaching " +
                  "UnregisterEnemy — most likely an EnemyStats subclass declaring its own " +
                  "OnDestroy() without 'override' + base.OnDestroy(), or they were destroyed " +
                  "while Instance pointed at a different manager than the one they registered with.");
        Debug.LogWarning(sb.ToString());
    }
#endif

    private void ApplyHealthChangeToExistingEnemies(float oldMultiplier, float newMultiplier)
    {
        if (Mathf.Approximately(oldMultiplier, newMultiplier)) return;

        float ratio = newMultiplier / oldMultiplier;
        int affectedCount = 0;

        // Snapshot: an enemy whose health rescale kills it can unregister mid-iteration,
        // which would throw InvalidOperationException on the live HashSet.
        var snapshot = new List<EnemyStats>(trackedEnemies);
        foreach (var enemy in snapshot)
        {
            if (enemy == null || enemy.IsDead()) continue;

            // Scale both max and current health proportionally
            float oldMaxHealth = enemy.maxHealth;

            // Guard the division. An enemy with maxHealth <= 0 has nothing to
            // rescale and would produce NaN/Infinity here, which then propagates
            // into currentHealth and makes IsDead() permanently false.
            if (oldMaxHealth <= 0f) continue;

            float oldCurrentHealth = enemy.currentHealth;
            float healthPercentage = oldCurrentHealth / oldMaxHealth;

            enemy.maxHealth *= ratio;
            enemy.currentHealth = enemy.maxHealth * healthPercentage;

            // Re-push the CAPACITY to the world-space bar. The bar's maximum is
            // baked in at Initialize() time, so without this the bar keeps reading
            // against the pre-augment denominator and shows the wrong fill for the
            // rest of the fight. Bosses override this to report their combined
            // armour+health pool, matching how they initialised their bar.
            enemy.RefreshHealthBarCapacity();

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
        // Editor-only: every other log in this file is already commented out, so
        // these two were shipping leftovers rather than deliberate telemetry.
#if UNITY_EDITOR
        Debug.Log($"[ENEMY_MODIFIER] Damage: {oldValue:F3}x -> {damageMultiplier:F3}x (applied {multiplier:F2}x)");
#endif
    }

    public void ApplyHealthMultiplier(float multiplier)
    {
        float oldValue = healthMultiplier;
        healthMultiplier *= multiplier;
#if UNITY_EDITOR
        Debug.Log($"[ENEMY_MODIFIER] Health: {oldValue:F3}x -> {healthMultiplier:F3}x (applied {multiplier:F2}x)");
#endif
        // Apply to existing enemies retroactively.
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
    // per-stage scaling above. Nightmare = +40% to EVERY enemy AND boss (see the two
    // constants below — the comment used to say +30% and disagreed with them); Normal = ×1
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




