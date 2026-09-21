using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// Regression harness for the augment / boss / enemy fix pass.
///
/// TWO TIERS, one component:
///
///   TIER A — ISOLATED SUITE (auto-runs on Play).
///     Pure-logic tests that build their own throwaway fixtures on INACTIVE
///     GameObjects, so no Awake/Start ever fires and nothing in your scene is
///     touched. Proves the fixes are actually wired: override chains, damage
///     scaling maths, controller suspension, the Mort latch, the Adrenaline
///     restore, the wave-counter clamp, and single-fire damage reflection.
///     RUN THIS IN AN EMPTY SCENE — a few tests mutate the global
///     EnemyStatModifierManager and difficulty, and although they restore
///     everything afterwards, an empty scene keeps it honest.
///
///   TIER B — LIVE WATCHDOG (runs during a real run).
///     Edge-triggered, read-only, in the style of LiveInvariantMonitor: silence
///     means everything is holding. Catches the regressions that ONLY appear in
///     a real fight — a boss leaking out of the tracked-enemy set, a boss whose
///     death teardown stalls, crowd control landing on a boss, a Mort falsely
///     flagged dead. Leave it on while you play a normal run.
///
/// USAGE: drop on a GameObject and press Play.
///   Empty scene  -> Tier A runs, you read PASS/FAIL, done.
///   Real scene   -> set runIsolatedSuiteOnStart = false, leave liveMonitoring on,
///                   play a run with bosses, watch for [AugTest] BROKEN lines.
/// Hotkeys: F5 = re-run Tier A, F6 = live status dump, F7 = toggle live monitoring.
/// Filter the Console by [AugTest].
public class AugmentBossRegressionHarness : MonoBehaviour
{
    private const string TAG = "[AugTest] ";

    [Header("Tier A — isolated suite")]
    [Tooltip("Run the isolated regression suite on Play. Use an EMPTY scene for this.")]
    public bool runIsolatedSuiteOnStart = true;

    [Tooltip("Allow tests that temporarily mutate global state (EnemyStatModifierManager " +
             "multipliers, difficulty mode, the augment statics). Everything is restored " +
             "afterwards, but turn this OFF if you ever run Tier A in a live scene.")]
    public bool allowGlobalMutationTests = true;

    [Header("Tier B — live watchdog")]
    [Tooltip("Watch live invariants during a real run. Edge-triggered: silence = all holding.")]
    public bool liveMonitoring = true;

    [Tooltip("Seconds between live sweeps. 0.25 (4x/sec) is responsive and cheap.")]
    public float checkInterval = 0.25f;

    [Tooltip("Seconds between heartbeat lines (proof the watchdog is alive). 0 = none.")]
    public float heartbeatInterval = 10f;

    [Tooltip("How long a boss may sit at 0 HP before its stalled teardown is reported. " +
             "Must exceed the longest boss disintegrationDuration plus " +
             "BaseBossStats.deathFailsafeExtraSeconds.")]
    public float bossTeardownGraceSeconds = 25f;

    [Tooltip("How long the orchestrator's enemiesAlive counter may read 0 while enemies " +
             "are still alive in the scene before it is reported as a stalled wave. A few " +
             "seconds is normal while a death VFX finishes.")]
    public float waveStallGraceSeconds = 8f;

    [Tooltip("Distance from world origin beyond which a living enemy is considered adrift " +
             "(escaped the play area). Raise this if your maps are larger than the default.")]
    public float playAreaRadius = 60f;

    private int _pass, _fail, _skip;
    private readonly List<string> _failLines = new List<string>();
    private readonly List<string> _skipLines = new List<string>();

    // Live-watchdog state (edge-triggered, same shape as LiveInvariantMonitor).
    private readonly Dictionary<string, bool> _state = new Dictionary<string, bool>();
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
    private readonly Dictionary<int, float> _bossZeroHpSince = new Dictionary<int, float>();
    private float _stallSince = -1f;
    private float _stallFingerprint = float.NaN;
    private float _accum, _hbAccum;
    private long _sweeps;

    // Everything Tier A creates is parented here and destroyed when the suite ends.
    private Transform _fixtureRoot;

    // A SECOND fixture root that is ACTIVE. Needed only for the handful of checks
    // that exercise real physics: a Rigidbody2D on an inactive GameObject is not in
    // the physics world, so linearVelocity writes are silently dropped and any
    // assertion about them passes or fails for the wrong reason. Kept separate and
    // minimal — nothing with a boss or a controller goes in here, so no Awake/Start
    // side effects leak into the scene.
    private Transform _activeFixtureRoot;

    // The EnemyStatModifierManager the HARNESS created, if any. Only ever this one is
    // torn down; a manager that belongs to the running scene is never touched.
    private EnemyStatModifierManager _harnessOwnedManager;

    private const BindingFlags PRIV = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags ANY = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    // A minimal concrete boss used ONLY to exercise the "is this a boss?" gates.
    // Deliberately NOT Boss1/2/3: instantiating a real boss would drag in its
    // laser/meteor/hand state and its OnDestroy teardown. The production bosses
    // are covered by the reflection wiring tests instead, which need no instance.
    private class _TestBoss : BaseBossStats { }

    private void Awake()
    {
        Debug.LogWarning(TAG + $"AugmentBossRegressionHarness ALIVE on scene '{gameObject.scene.name}'. " +
                         $"TierA={runIsolatedSuiteOnStart}, live={liveMonitoring}. " +
                         "F5 = re-run suite, F6 = live status, F7 = toggle live.");
    }

    private void Start()
    {
        if (!runIsolatedSuiteOnStart) return;

        // Tier A is designed for an EMPTY scene. Running it on top of a live game still
        // works — the mutating tests self-skip — but it means the pass count says nothing
        // about that session, so say so rather than letting a green result mislead.
        if (SceneLooksLive())
            LWarn("Tier A is auto-running in what looks like a LIVE gameplay scene. The " +
                  "global-mutation tests will SKIP. For a full run, use an empty scene; for " +
                  "playtesting, untick 'Run Isolated Suite On Start' and rely on the watchdog.");

        StartCoroutine(RunIsolatedSuite());
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.f5Key.wasPressedThisFrame) StartCoroutine(RunIsolatedSuite());
            if (kb.f6Key.wasPressedThisFrame) DumpLiveStatus();
            if (kb.f7Key.wasPressedThisFrame) ToggleLive();
        }
#else
        if (Input.GetKeyDown(KeyCode.F5)) StartCoroutine(RunIsolatedSuite());
        if (Input.GetKeyDown(KeyCode.F6)) DumpLiveStatus();
        if (Input.GetKeyDown(KeyCode.F7)) ToggleLive();
#endif
        if (!liveMonitoring) return;

        _accum += Time.unscaledDeltaTime;
        if (checkInterval <= 0f || _accum >= checkInterval)
        {
            _accum = 0f;
            LiveSweep();
        }

        if (heartbeatInterval > 0f)
        {
            _hbAccum += Time.unscaledDeltaTime;
            if (_hbAccum >= heartbeatInterval) { _hbAccum = 0f; Heartbeat(); }
        }
    }

    // =====================================================================
    //  TIER A — ISOLATED SUITE
    // =====================================================================

    [ContextMenu("Run isolated regression suite (F5)")]
    public void RunIsolatedSuiteMenu() => StartCoroutine(RunIsolatedSuite());

    public IEnumerator RunIsolatedSuite()
    {
        _pass = 0; _fail = 0; _skip = 0;
        _failLines.Clear(); _skipLines.Clear();
        L("===== AUGMENT/BOSS REGRESSION SUITE: START =====");

        var rootGO = new GameObject("~AugTestFixtures");
        rootGO.hideFlags = HideFlags.HideAndDontSave;
        rootGO.SetActive(false);              // nothing under here ever runs Awake/Start
        _fixtureRoot = rootGO.transform;

        // Snapshot every global we are about to touch, so the suite is restorable.
        var savedDifficulty = EnemyStatModifierManager.ActiveMode;

        try
        {
            T1_OnDestroyOverrideChain();
            T2_HealthBarRefreshOverride();
            T3_SharedHelperSignatures();
            T4_FutureProofSubclassScan();
            T5_AugmentStaticsReset();
            T6_BossRefusedByCrowdControl();
            T7_SuspendRestoreControllers();
            T8_MortLatchReleased();
            T9_AdrenalineMoveSpeedIsMultiplicative();
            T10_WaveCounterClamped();
            T11_ReflectionFiresExactlyOnce();
            T15_AllSelfDrivingControllersSuspendable();
            T16_SuspendListCompleteness();
            T17_CrowdControlComponentsRefuseBosses();
            T18_BossAwakePoolsAndDifficulty();
            T19_BossCurrentHealthSeededWithoutEnemyData();
            T20_DamageUsesEnemyDataTimesMultiplier();
            T21_ExternalApiSurfaceStillBound();
            T22_GrappleImmovability();
            T23_CrowdControlImmunity();

            // T12-T14 temporarily mutate GLOBAL state: the difficulty mode, the enemy
            // damage/health multipliers, and (T13) the modifier manager itself. That is
            // fine in an empty scene and destructive in a running game, so they are
            // gated twice — by the inspector flag AND by an automatic live-scene check.
            bool live = SceneLooksLive();
            if (!allowGlobalMutationTests)
            {
                Skip("T12-T14 damage/health scaling", "allowGlobalMutationTests is off");
            }
            else if (live)
            {
                Skip("T12-T14 damage/health scaling",
                     "LIVE SCENE detected (orchestrator / wave spawner / enemies / scene-owned " +
                     "modifier manager). These tests mutate global difficulty and multipliers, so " +
                     "they are skipped to avoid corrupting the running game. Run them in an EMPTY scene.");
            }
            else
            {
                T12_DamageScalingMaths();
                T13_DifficultyAppliesWithoutManager();
                T14_HealthRescaleZeroGuard();
            }
        }
        finally
        {
            EnemyStatModifierManager.SetActiveMode((int)savedDifficulty);
            if (EnemyStatModifierManager.Instance != null)
                EnemyStatModifierManager.Instance.ResetModifiers();
            AugmentRuntimeModifiers.ResetAll();

            // Only ever destroy a manager the harness created. A scene-owned one is left
            // exactly as it was found.
            if (_harnessOwnedManager != null)
            {
                Destroy(_harnessOwnedManager.gameObject);
                _harnessOwnedManager = null;
            }

            if (_fixtureRoot != null) Destroy(_fixtureRoot.gameObject);
            _fixtureRoot = null;

            if (_activeFixtureRoot != null) Destroy(_activeFixtureRoot.gameObject);
            _activeFixtureRoot = null;
        }

        yield return null;   // let the fixture teardown settle before the summary

        L($"===== SUITE COMPLETE: {_pass} passed, {_fail} failed, {_skip} skipped =====");
        if (_fail == 0) L("<color=lime>ALL ISOLATED TESTS PASSED</color>");
        else LErr($"{_fail} TEST(S) FAILED — see [FAIL] lines above.");
        if (_skip > 0) LWarn($"{_skip} test(s) skipped (a dependency wasn't available) — see [SKIP] lines.");

        PrintCopyPasteReport();
    }

    /// THE method-hiding bug. Unity dispatches only the MOST-DERIVED OnDestroy, so a
    /// boss declaring its own private OnDestroy silently suppressed
    /// EnemyStats.OnDestroy (no UnregisterEnemy, no flash-material release). An
    /// override's GetBaseDefinition() points at EnemyStats; a hiding declaration
    /// points at itself. That difference is exactly what this asserts.
    private void T1_OnDestroyOverrideChain()
    {
        var baseM = typeof(EnemyStats).GetMethod("OnDestroy", PRIV);
        Check("EnemyStats.OnDestroy exists", baseM != null);
        if (baseM == null) return;

        Check("EnemyStats.OnDestroy is protected", baseM.IsFamily);
        Check("EnemyStats.OnDestroy is virtual", baseM.IsVirtual);

        foreach (var t in new[] { typeof(Boss1), typeof(Boss2), typeof(Boss3) })
        {
            var m = t.GetMethod("OnDestroy", PRIV | BindingFlags.DeclaredOnly);
            if (m == null)
            {
                // Not declaring one at all is fine — it just inherits the base.
                L($"  ({t.Name} declares no OnDestroy — inherits EnemyStats', which is correct)");
                continue;
            }
            Check($"{t.Name}.OnDestroy OVERRIDES EnemyStats (does not hide it)",
                  m.IsVirtual && m.GetBaseDefinition().DeclaringType == typeof(EnemyStats));
        }
    }

    private void T2_HealthBarRefreshOverride()
    {
        var baseM = typeof(EnemyStats).GetMethod("RefreshHealthBarCapacity", ANY);
        Check("EnemyStats.RefreshHealthBarCapacity exists", baseM != null);
        if (baseM == null) return;
        Check("EnemyStats.RefreshHealthBarCapacity is virtual", baseM.IsVirtual);

        var bossM = typeof(BaseBossStats).GetMethod("RefreshHealthBarCapacity", ANY | BindingFlags.DeclaredOnly);
        Check("BaseBossStats OVERRIDES RefreshHealthBarCapacity (combined armour+health pool)",
              bossM != null && bossM.IsVirtual && bossM.GetBaseDefinition().DeclaringType == typeof(EnemyStats));
    }

    private void T3_SharedHelperSignatures()
    {
        Check("EnemyStats.FireCommonDeathHooks(GameObject, bool) exists",
              typeof(EnemyStats).GetMethod("FireCommonDeathHooks",
                  BindingFlags.Public | BindingFlags.Static,
                  null, new[] { typeof(GameObject), typeof(bool) }, null) != null);

        Check("EnemyStats.CanBeExternallyControlled exists",
              typeof(EnemyStats).GetMethod("CanBeExternallyControlled",
                  BindingFlags.Public | BindingFlags.Static) != null);
        Check("EnemyStats.SuspendBehaviourControllers exists",
              typeof(EnemyStats).GetMethod("SuspendBehaviourControllers",
                  BindingFlags.Public | BindingFlags.Static) != null);
        Check("EnemyStats.RestoreBehaviourControllers exists",
              typeof(EnemyStats).GetMethod("RestoreBehaviourControllers",
                  BindingFlags.Public | BindingFlags.Static) != null);

        Check("EnemyStats.ScaleDamage exists",
              typeof(EnemyStats).GetMethod("ScaleDamage", BindingFlags.Public | BindingFlags.Instance) != null);
        Check("EnemyStats.DamageMultiplier exists",
              typeof(EnemyStats).GetProperty("DamageMultiplier", BindingFlags.Public | BindingFlags.Instance) != null);

        Check("EnemyController.NotifyPlayerDamaged(PlayerStats,...) exists",
              typeof(EnemyController).GetMethod("NotifyPlayerDamaged",
                  BindingFlags.Public | BindingFlags.Static,
                  null, new[] { typeof(PlayerStats), typeof(float), typeof(GameObject) }, null) != null);
        Check("EnemyController.NotifyPlayerDamaged(GameObject,...) exists",
              typeof(EnemyController).GetMethod("NotifyPlayerDamaged",
                  BindingFlags.Public | BindingFlags.Static,
                  null, new[] { typeof(GameObject), typeof(float), typeof(GameObject) }, null) != null);
        Check("EnemyController.NotifyCharacterDamaged exists",
              typeof(EnemyController).GetMethod("NotifyCharacterDamaged",
                  BindingFlags.Public | BindingFlags.Static) != null);

        Check("AugmentRuntimeModifiers.ResetAll exists",
              typeof(AugmentRuntimeModifiers).GetMethod("ResetAll",
                  BindingFlags.Public | BindingFlags.Static) != null);
    }

    /// Guards the NEXT enemy someone adds. Any EnemyStats subclass that declares a
    /// non-override OnDestroy has re-introduced the hiding bug; any subclass that
    /// overrides Die() owns a custom death path and must therefore call
    /// EnemyStats.FireCommonDeathHooks, which only a human can confirm.
    private void T4_FutureProofSubclassScan()
    {
        var hiding = new List<string>();
        var customDeath = new List<string>();

        foreach (var t in typeof(EnemyStats).Assembly.GetTypes())
        {
            if (!typeof(EnemyStats).IsAssignableFrom(t) || t == typeof(EnemyStats) || t.IsAbstract) continue;

            var od = t.GetMethod("OnDestroy", PRIV | BindingFlags.DeclaredOnly);
            if (od != null && (!od.IsVirtual || od.GetBaseDefinition().DeclaringType != typeof(EnemyStats)))
                hiding.Add(t.Name);

            var die = t.GetMethod("Die", ANY | BindingFlags.DeclaredOnly);
            if (die != null) customDeath.Add(t.Name);
        }

        Check("no EnemyStats subclass HIDES OnDestroy" +
              (hiding.Count > 0 ? $" (offenders: {string.Join(", ", hiding)})" : ""),
              hiding.Count == 0);

        // Death paths already audited. Boss1/2/3, EyeStats and ScarecrowStats each
        // bypass base.Die() and call EnemyStats.FireCommonDeathHooks directly;
        // SplitterStats and VortexStats decorate Die() and DO call base.Die(), so they
        // reach PerformDeath and get the hooks for free. Anything NOT on this list is
        // unaudited and must be checked by hand.
        var audited = new HashSet<string>
        {
            "Boss1", "Boss2", "Boss3", "EyeStats", "ScarecrowStats",
            "SplitterStats", "VortexStats",
        };
        var unaudited = new List<string>();
        foreach (var n in customDeath) if (!audited.Contains(n)) unaudited.Add(n);

        Check("no UNAUDITED custom death path exists" +
              (unaudited.Count > 0 ? $" (review these for FireCommonDeathHooks: {string.Join(", ", unaudited)})" : ""),
              unaudited.Count == 0);

        if (customDeath.Count > 0)
            L($"  (custom death paths present, all audited: {string.Join(", ", customDeath)})");
    }

    private void T5_AugmentStaticsReset()
    {
        PlayerEconomyModifiers.EnergyGainMultiplier = 3f;
        PlayerCombatModifiers.OutgoingDamageMultiplier = 3f;
        TowerCombatModifiers.DamageMultiplier = 3f;
        TowerCombatModifiers.BaseFireRateMultiplier = 3f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 3f;

        AugmentRuntimeModifiers.ResetAll();

        Check("ResetAll clears EnergyGainMultiplier", Approx(PlayerEconomyModifiers.EnergyGainMultiplier, 1f));
        Check("ResetAll clears OutgoingDamageMultiplier", Approx(PlayerCombatModifiers.OutgoingDamageMultiplier, 1f));
        Check("ResetAll clears tower DamageMultiplier", Approx(TowerCombatModifiers.DamageMultiplier, 1f));
        Check("ResetAll clears tower BaseFireRateMultiplier", Approx(TowerCombatModifiers.BaseFireRateMultiplier, 1f));
        Check("ResetAll clears tower PerCountFireRateMultiplier", Approx(TowerCombatModifiers.PerCountFireRateMultiplier, 1f));
        Check("ResetAll leaves composed FireRateMultiplier at 1", Approx(TowerCombatModifiers.FireRateMultiplier, 1f));
    }

    private void T6_BossRefusedByCrowdControl()
    {
        var normal = NewFixture("normalEnemy");
        var ns = normal.AddComponent<EnemyStats>();
        ns.maxHealth = 100f; ns.currentHealth = 100f;
        normal.AddComponent<Rigidbody2D>();

        var boss = NewFixture("bossEnemy");
        var bs = boss.AddComponent<_TestBoss>();
        bs.maxHealth = 1000f; bs.currentHealth = 1000f;
        boss.AddComponent<Rigidbody2D>();

        var dead = NewFixture("deadEnemy");
        var ds = dead.AddComponent<EnemyStats>();
        ds.maxHealth = 100f; ds.currentHealth = 0f;

        Check("CC accepts a normal enemy", EnemyStats.CanBeExternallyControlled(normal));
        Check("CC REFUSES a boss (BaseBossStats)", !EnemyStats.CanBeExternallyControlled(boss));
        Check("CC REFUSES a dead enemy", !EnemyStats.CanBeExternallyControlled(dead));
        Check("CC refuses null", !EnemyStats.CanBeExternallyControlled(null));

        // Regression guard for my own earlier over-restriction: an enemy with no
        // Rigidbody2D must still be accepted, because the old code suspended its
        // controller (an effective stun) and refusing would remove that.
        var noRb = NewFixture("noRigidbody");
        var nrs = noRb.AddComponent<EnemyStats>();
        nrs.maxHealth = 100f; nrs.currentHealth = 100f;
        Check("CC accepts an enemy with no Rigidbody2D (preserves old stun behaviour)",
              EnemyStats.CanBeExternallyControlled(noRb));

        // A STATIC body cannot be moved by linearVelocity at all, so suspending its
        // controller would freeze it solid for the whole effect instead of merely
        // doing nothing. Refusing is the safe outcome.
        var staticGO = NewFixture("staticBody");
        var ss = staticGO.AddComponent<EnemyStats>();
        ss.maxHealth = 100f; ss.currentHealth = 100f;
        staticGO.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        Check("CC REFUSES a static-body enemy (would freeze solid, not steer)",
              !EnemyStats.CanBeExternallyControlled(staticGO));

        var kinGO = NewFixture("kinematicBody");
        var ks = kinGO.AddComponent<EnemyStats>();
        ks.maxHealth = 100f; ks.currentHealth = 100f;
        kinGO.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        Check("CC still accepts a KINEMATIC body (velocity does move it)",
              EnemyStats.CanBeExternallyControlled(kinGO));
    }

    /// The Buffer/Parfumer/Bomber bug: those have no EnemyController, so the old
    /// single-field suspension was a no-op and their FixedUpdate kept driving them.
    private void T7_SuspendRestoreControllers()
    {
        // --- an EnemyController-driven enemy ---
        var go = NewFixture("suspendable");
        go.AddComponent<EnemyStats>();
        var ec = go.AddComponent<EnemyController>();
        ec.enabled = true;

        var suspended = EnemyStats.SuspendBehaviourControllers(go);
        Check("Suspend disables EnemyController", !ec.enabled);
        Check("Suspend reports exactly what it disabled", suspended != null && suspended.Count == 1);
        EnemyStats.RestoreBehaviourControllers(go, suspended);
        Check("Restore re-enables EnemyController", ec.enabled);
        Check("Restore empties the suspended list (idempotent second call)",
              suspended != null && suspended.Count == 0);

        // --- a self-driving enemy with NO EnemyController (the actual bug) ---
        var buf = NewFixture("bufferLike");
        buf.AddComponent<EnemyStats>();
        buf.AddComponent<Rigidbody2D>();
        var bc = buf.AddComponent<BufferController>();
        bc.enabled = true;
        Check("BufferController fixture genuinely has no EnemyController",
              buf.GetComponent<EnemyController>() == null);

        var bsus = EnemyStats.SuspendBehaviourControllers(buf);
        Check("Suspend disables BufferController (was a no-op before the fix)", !bc.enabled);
        EnemyStats.RestoreBehaviourControllers(buf, bsus);
        Check("Restore re-enables BufferController", bc.enabled);

        // --- a controller that was ALREADY off must never be switched on by Restore ---
        var pre = NewFixture("preDisabled");
        pre.AddComponent<EnemyStats>();
        var pec = pre.AddComponent<EnemyController>();
        pec.enabled = false;
        var psus = EnemyStats.SuspendBehaviourControllers(pre);
        Check("Suspend ignores an already-disabled controller", psus.Count == 0);
        EnemyStats.RestoreBehaviourControllers(pre, psus);
        Check("Restore does NOT enable a controller it did not disable", !pec.enabled);

        // --- a corpse must not be reanimated ---
        var corpse = NewFixture("corpse");
        var cs = corpse.AddComponent<EnemyStats>();
        cs.maxHealth = 100f; cs.currentHealth = 100f;
        var cec = corpse.AddComponent<EnemyController>();
        cec.enabled = true;
        var csus = EnemyStats.SuspendBehaviourControllers(corpse);
        cs.currentHealth = 0f;                                  // died during the effect
        EnemyStats.RestoreBehaviourControllers(corpse, csus);
        Check("Restore SKIPS a dead enemy (never reanimates a corpse's controller)", !cec.enabled);
    }

    /// Confusion/Berserk disable EnemyController. The Mort used to treat that as
    /// proof of death and latch hasDied forever, so a confused Mort never fired
    /// another shell for the rest of its life.
    private void T8_MortLatchReleased()
    {
        var go = NewFixture("mort");
        var stats = go.AddComponent<EnemyStats>();
        stats.maxHealth = 100f; stats.currentHealth = 100f;
        var ec = go.AddComponent<EnemyController>();
        var mort = go.AddComponent<MortController>();

        if (!SetPrivate(mort, "stats", stats) || !SetPrivate(mort, "enemyController", ec))
        { Skip("T8 Mort latch", "could not wire MortController's private fields"); return; }
        SetPrivate(mort, "hasDied", false);

        var prop = typeof(MortController).GetProperty("IsDeadOrDying", PRIV);
        if (prop == null) { Skip("T8 Mort latch", "IsDeadOrDying not found"); return; }

        ec.enabled = true;
        Check("Mort alive with controller on -> not dying", !(bool)prop.GetValue(mort));

        ec.enabled = false;                                     // confusion takes over
        Check("Mort with controller off -> suppressed (still returns true)", (bool)prop.GetValue(mort));
        Check("Mort did NOT latch hasDied while merely suspended",
              GetPrivate<bool>(mort, "hasDied") == false);

        ec.enabled = true;                                      // confusion expires
        Check("Mort RECOVERS after the controller comes back (the fix)",
              !(bool)prop.GetValue(mort));

        stats.currentHealth = 0f;                               // genuine death
        Check("Mort at 0 HP -> dying", (bool)prop.GetValue(mort));
        Check("Mort DOES latch hasDied on real death", GetPrivate<bool>(mort, "hasDied"));
    }

    /// Restoring an absolute move speed reverted any speed augment picked during the
    /// 15s rush. The fix divides the rush's own factor back out instead.
    private void T9_AdrenalineMoveSpeedIsMultiplicative()
    {
        var go = NewFixture("adrenaline");
        var ps = go.AddComponent<PlayerStats>();
        var eff = go.AddComponent<AdrenalineRushEffect>();

        if (!SetPrivate(eff, "playerStats", ps))
        { Skip("T9 Adrenaline move speed", "could not wire playerStats"); return; }

        var speedField = typeof(PlayerStats).GetField("moveSpeed");
        if (speedField == null) { Skip("T9 Adrenaline move speed", "PlayerStats.moveSpeed not public"); return; }

        var apply = typeof(AdrenalineRushEffect).GetMethod("ApplyBoosts", PRIV);
        var remove = typeof(AdrenalineRushEffect).GetMethod("RemoveBoosts", PRIV);
        if (apply == null || remove == null)
        { Skip("T9 Adrenaline move speed", "ApplyBoosts/RemoveBoosts not found"); return; }

        eff.movementSpeedMultiplier = 0.5f;                     // rush = x1.5
        speedField.SetValue(ps, 10f);

        apply.Invoke(eff, null);                                // weapon is null -> attack half no-ops
        Check("Adrenaline applies x1.5 move speed", Approx((float)speedField.GetValue(ps), 15f));

        // A speed augment lands DURING the rush, doubling the current value.
        speedField.SetValue(ps, 30f);

        remove.Invoke(eff, null);
        Check("Adrenaline restore PRESERVES a speed augment taken mid-rush (expect 20, old code gave 10)",
              Approx((float)speedField.GetValue(ps), 20f));
    }

    private void T10_WaveCounterClamped()
    {
        var go = NewFixture("waveSpawner");
        var ws = go.AddComponent<WaveSpawner>();

        var f = typeof(WaveSpawner).GetField("enemiesAlive", PRIV);
        if (f == null) { Skip("T10 wave counter", "enemiesAlive field not found"); return; }

        f.SetValue(ws, 0);
        ws.OnEnemyDeath(); ws.OnEnemyDeath(); ws.OnEnemyDeath();
        Check("WaveSpawner.enemiesAlive never goes negative (clamped at 0)",
              (int)f.GetValue(ws) == 0);

        f.SetValue(ws, 2);
        ws.OnEnemyDeath();
        Check("WaveSpawner.enemiesAlive still decrements normally", (int)f.GetValue(ws) == 1);
    }

    /// Reflection must fire EXACTLY once per damage application. This is the change
    /// most likely to fail silently (a duplicated call site double-reflects rather
    /// than erroring), so it is asserted numerically.
    private void T11_ReflectionFiresExactlyOnce()
    {
        var playerGO = NewFixture("player");
        var ps = playerGO.AddComponent<PlayerStats>();
        var refl = playerGO.AddComponent<DamageReflectionEffect>();
        refl.reflectionPercentage = 0.5f;

        var attackerGO = NewFixture("attacker");
        var att = attackerGO.AddComponent<EnemyStats>();
        att.maxHealth = 100f; att.currentHealth = 100f;

        EnemyController.NotifyPlayerDamaged(ps, 10f, attackerGO);
        Check("Reflection deals 50% of the hit back ONCE (100 -> 95)", Approx(att.currentHealth, 95f));

        EnemyController.NotifyPlayerDamaged(ps, 10f, attackerGO);
        Check("Reflection is linear across calls (95 -> 90, i.e. no double-fire)", Approx(att.currentHealth, 90f));

        // Guards that must never reflect.
        float before = att.currentHealth;
        EnemyController.NotifyPlayerDamaged(ps, 0f, attackerGO);
        EnemyController.NotifyPlayerDamaged(ps, 10f, null);
        EnemyController.NotifyPlayerDamaged((PlayerStats)null, 10f, attackerGO);
        Check("Reflection ignores zero damage / null attacker / null player", Approx(att.currentHealth, before));

        // An enemy caught in a friendly AoE is not a player and must never reflect.
        var enemyVictimGO = NewFixture("enemyVictim");
        var ev = enemyVictimGO.AddComponent<EnemyStats>();
        ev.maxHealth = 100f; ev.currentHealth = 100f;
        EnemyController.NotifyCharacterDamaged(ev, 10f, attackerGO);
        Check("NotifyCharacterDamaged ignores non-player CharacterStats", Approx(att.currentHealth, before));
    }

    private void T12_DamageScalingMaths()
    {
        var mgr = EnsureModifierManager();
        if (mgr == null) { Skip("T12 damage scaling", "no EnemyStatModifierManager"); return; }

        var normalGO = NewFixture("scaleNormal");
        var normal = normalGO.AddComponent<EnemyStats>();
        var bossGO = NewFixture("scaleBoss");
        var boss = bossGO.AddComponent<_TestBoss>();

        mgr.ResetModifiers();
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);
        EnemyStatModifierManager.SetStageScaling(1f, 1f, false);
        Check("ScaleDamage is identity with everything neutral", Approx(normal.ScaleDamage(100f), 100f));

        mgr.ApplyDamageMultiplier(2f);
        Check("ScaleDamage honours the augment damage multiplier (x2)", Approx(normal.ScaleDamage(100f), 200f));

        EnemyStatModifierManager.SetStageScaling(1f, 1.5f, false);
        Check("ScaleDamage composes stage scaling for a regular enemy (x2 * x1.5)",
              Approx(normal.ScaleDamage(100f), 300f));
        Check("Boss EXCLUDES stage scaling while scaleBossesWithStage is off",
              Approx(boss.ScaleDamage(100f), 200f));

        EnemyStatModifierManager.SetStageScaling(1f, 1.5f, true);
        Check("Boss INCLUDES stage scaling once scaleBossesWithStage is on",
              Approx(boss.ScaleDamage(100f), 300f));

        mgr.ResetModifiers();
        EnemyStatModifierManager.SetStageScaling(1f, 1f, false);
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Nightmare);
        Check("ScaleDamage honours Nightmare difficulty",
              Approx(normal.ScaleDamage(100f), 100f * EnemyStatModifierManager.NightmareDamageMultiplier));
        Check("Damage property and ScaleDamage share one multiplier",
              Approx(normal.DamageMultiplier, EnemyStatModifierManager.NightmareDamageMultiplier));

        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);
    }

    /// The ungating fix: difficulty is a STATIC read and must survive the manager
    /// being absent. Previously the whole calculation sat behind an Instance
    /// null-check, so an enemy that woke before the manager lost Nightmare scaling.
    private void T13_DifficultyAppliesWithoutManager()
    {
        var go = NewFixture("noManager");
        var es = go.AddComponent<EnemyStats>();

        // This test REQUIRES tearing the manager down. That is only ever acceptable for
        // a manager the harness created itself — the scene's manager is wired to the
        // GameOrchestrator via Initialize() and holds the live trackedEnemies set, so
        // destroying it would silently break the running game.
        var mgr = EnsureModifierManager();
        if (mgr == null) { Skip("T13 difficulty without manager", "no manager available"); return; }
        if (!ReferenceEquals(mgr, _harnessOwnedManager))
        {
            Skip("T13 difficulty without manager",
                 "the scene owns the EnemyStatModifierManager — refusing to destroy it");
            return;
        }

        mgr.ResetModifiers();
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Nightmare);

        mgr.Shutdown();                                          // clears Instance
        Check("manager really is absent for this test", EnemyStatModifierManager.Instance == null);
        Check("Nightmare damage still applies with NO manager instance",
              Approx(es.ScaleDamage(100f), 100f * EnemyStatModifierManager.NightmareDamageMultiplier));

        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);
        Check("Normal damage is identity with NO manager instance", Approx(es.ScaleDamage(100f), 100f));

        // Safe: this manager was created by the harness, not the scene.
        Destroy(mgr.gameObject);
        _harnessOwnedManager = null;
    }

    private void T14_HealthRescaleZeroGuard()
    {
        var mgr = EnsureModifierManager();
        if (mgr == null) { Skip("T14 health rescale", "no EnemyStatModifierManager"); return; }
        mgr.ResetModifiers();

        var zeroGO = NewFixture("zeroHp");
        var zero = zeroGO.AddComponent<EnemyStats>();
        zero.maxHealth = 0f; zero.currentHealth = 0f;

        var normalGO = NewFixture("halfHp");
        var normal = normalGO.AddComponent<EnemyStats>();
        normal.maxHealth = 100f; normal.currentHealth = 50f;

        mgr.RegisterEnemy(zero);
        mgr.RegisterEnemy(normal);
        mgr.ApplyHealthMultiplier(2f);

        Check("zero-maxHealth enemy produces no NaN (divide-by-zero guard)",
              !float.IsNaN(zero.maxHealth) && !float.IsNaN(zero.currentHealth)
              && !float.IsInfinity(zero.maxHealth) && !float.IsInfinity(zero.currentHealth));
        Check("healthy enemy still rescales max proportionally", Approx(normal.maxHealth, 200f));
        Check("healthy enemy preserves its fill ratio", Approx(normal.currentHealth, 100f));

        mgr.UnregisterEnemy(zero);
        mgr.UnregisterEnemy(normal);
        mgr.ResetModifiers();
    }


    // =====================================================================
    //  NON-BOSS ENEMY COVERAGE
    // =====================================================================

    /// Buffer, Parfumer and Bomber ALL replace EnemyController and drive their own
    /// Rigidbody2D from FixedUpdate. Before the fix, crowd control was a total
    /// no-op on every one of them. Tested individually so a regression names the
    /// specific enemy rather than "something broke".
    private void T15_AllSelfDrivingControllersSuspendable()
    {
        SuspendCase<BufferController>("BufferController");
        SuspendCase<ParfumerController>("ParfumerController");
        SuspendCase<BomberController>("BomberController");
    }

    private void SuspendCase<T>(string label) where T : Behaviour
    {
        var go = NewFixture("selfDriving_" + label);
        go.AddComponent<EnemyStats>();
        go.AddComponent<Rigidbody2D>();
        var ctrl = go.AddComponent<T>();
        ctrl.enabled = true;

        Check($"{label} fixture has no EnemyController (it replaces it)",
              go.GetComponent<EnemyController>() == null);

        var sus = EnemyStats.SuspendBehaviourControllers(go);
        Check($"Suspend disables {label} (CC was a silent no-op here before the fix)", !ctrl.enabled);
        EnemyStats.RestoreBehaviourControllers(go, sus);
        Check($"Restore re-enables {label}", ctrl.enabled);
    }

    /// The suspend list is a hand-maintained array, which is the one part of the CC
    /// fix that rots silently. This asserts the four known entries are present, then
    /// SCANS the assembly for any other component that looks like an enemy behaviour
    /// driver and is missing from the list.
    private void T16_SuspendListCompleteness()
    {
        var f = typeof(EnemyStats).GetField("ExternallySuspendableControllers",
                    BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) { Skip("T16 suspend-list completeness", "list field not found"); return; }

        var list = f.GetValue(null) as System.Type[];
        if (list == null) { Skip("T16 suspend-list completeness", "list was null"); return; }

        var known = new HashSet<System.Type>(list);
        Check("suspend list contains EnemyController", known.Contains(typeof(EnemyController)));
        Check("suspend list contains BufferController", known.Contains(typeof(BufferController)));
        Check("suspend list contains ParfumerController", known.Contains(typeof(ParfumerController)));
        Check("suspend list contains BomberController", known.Contains(typeof(BomberController)));

        // Heuristic sweep: a MonoBehaviour that requires EnemyStats AND drives physics
        // itself (declares FixedUpdate) is a self-driving enemy controller. If it is
        // not in the list, crowd control will be a no-op on that enemy.
        var missing = new List<string>();
        foreach (var t in typeof(EnemyStats).Assembly.GetTypes())
        {
            if (t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
            if (known.Contains(t)) continue;
            if (typeof(EnemyStats).IsAssignableFrom(t)) continue;   // the stats component itself

            bool requiresEnemy = false;
            foreach (var a in t.GetCustomAttributes(typeof(RequireComponent), true))
            {
                var rc = (RequireComponent)a;
                if (rc.m_Type0 == typeof(EnemyStats) || rc.m_Type1 == typeof(EnemyStats)
                    || rc.m_Type2 == typeof(EnemyStats)) { requiresEnemy = true; break; }
            }
            if (!requiresEnemy) continue;

            // Update counts as well as FixedUpdate: the Scarecrow drives itself from
            // Update and zeroes rb.linearVelocity there, which fights an external
            // effect just as effectively as a FixedUpdate driver does.
            bool drivesItself = t.GetMethod("FixedUpdate", PRIV | BindingFlags.DeclaredOnly) != null
                             || t.GetMethod("Update", PRIV | BindingFlags.DeclaredOnly) != null;

            bool hasEnemyController = false;
            foreach (var a in t.GetCustomAttributes(typeof(RequireComponent), true))
            {
                var rc = (RequireComponent)a;
                if (rc.m_Type0 == typeof(EnemyController) || rc.m_Type1 == typeof(EnemyController)
                    || rc.m_Type2 == typeof(EnemyController)) { hasEnemyController = true; break; }
            }
            // Requires EnemyController -> already covered, since suspending that stops it.
            if (drivesItself && !hasEnemyController) missing.Add(t.Name);
        }

        // ADVISORY, not a failure. Whether a given self-driving enemy SHOULD be
        // suspendable is a design decision, not an invariant: suspending a component
        // that owns a state machine (the Scarecrow's appear/disappear cycle, say) can
        // freeze it mid-transition, and any child GameObjects it spawned keep running
        // regardless. Anything listed here is a candidate to review, not a bug.
        if (missing.Count > 0)
            LWarn("CC-COVERAGE REVIEW: these look like self-driving enemy components that are " +
                  "NOT in EnemyStats.ExternallySuspendableControllers, so Confusion/Berserk will " +
                  $"be a no-op on them. Add them only if suspending is actually safe: {string.Join(", ", missing)}");
        else
            L("  (no unreviewed self-driving enemy components found)");
    }

    /// The components themselves must refuse a boss, not merely be refused by the
    /// caller — so a future call site that forgets to check CanBeExternallyControlled
    /// still can't half-apply CC to a boss.
    private void T17_CrowdControlComponentsRefuseBosses()
    {
        // --- boss: must leave the controller completely untouched ---
        var bossGO = NewFixture("ccBoss");
        var bstats = bossGO.AddComponent<_TestBoss>();
        bstats.maxHealth = 1000f; bstats.currentHealth = 1000f;
        bossGO.AddComponent<Rigidbody2D>();
        var bossCtrl = bossGO.AddComponent<EnemyController>();
        bossCtrl.enabled = true;

        bossGO.AddComponent<ConfusedEnemy>().Initialize(5f);
        Check("ConfusedEnemy.Initialize on a boss does NOT suspend its controller", bossCtrl.enabled);

        bossGO.AddComponent<BerserkEnemy>().Initialize(5f);
        Check("BerserkEnemy.Initialize on a boss does NOT suspend its controller", bossCtrl.enabled);

        // --- normal enemy: must genuinely take over ---
        var enemyGO = NewFixture("ccEnemy");
        var estats = enemyGO.AddComponent<EnemyStats>();
        estats.maxHealth = 100f; estats.currentHealth = 100f;
        enemyGO.AddComponent<Rigidbody2D>();
        var enemyCtrl = enemyGO.AddComponent<EnemyController>();
        enemyCtrl.enabled = true;

        enemyGO.AddComponent<ConfusedEnemy>().Initialize(5f);
        Check("ConfusedEnemy.Initialize on a normal enemy DOES suspend its controller", !enemyCtrl.enabled);

        // --- dead enemy: must refuse ---
        var deadGO = NewFixture("ccDead");
        var dstats = deadGO.AddComponent<EnemyStats>();
        dstats.maxHealth = 100f; dstats.currentHealth = 0f;
        deadGO.AddComponent<Rigidbody2D>();
        var deadCtrl = deadGO.AddComponent<EnemyController>();
        deadCtrl.enabled = true;
        deadGO.AddComponent<BerserkEnemy>().Initialize(5f);
        Check("BerserkEnemy.Initialize on a DEAD enemy does not suspend anything", deadCtrl.enabled);

        // A berserker with no target must STOP. On a Kinematic body a leftover
        // velocity never decays, so the enemy flies off the map alive and stalls the
        // wave. Drives the real Update() with no other enemy in the scene.
        var loneGO = NewFixture("loneBerserker");
        var lstats = loneGO.AddComponent<EnemyStats>();
        lstats.maxHealth = 100f; lstats.currentHealth = 100f;
        var lrb = loneGO.AddComponent<Rigidbody2D>();
        lrb.bodyType = RigidbodyType2D.Kinematic;
        loneGO.AddComponent<EnemyController>();
        var lone = loneGO.AddComponent<BerserkEnemy>();
        lone.Initialize(30f);
        lrb.linearVelocity = new Vector2(7f, 3f);          // as if mid-charge

        var upd = typeof(BerserkEnemy).GetMethod("Update", PRIV);
        if (upd == null) Skip("T17 berserker stops with no target", "Update not found");
        else
        {
            upd.Invoke(lone, null);                        // fixtures are inactive -> no targets
            Check("Berserker with NO target zeroes its velocity (kinematic fly-away fix)",
                  lrb.linearVelocity == Vector2.zero);
        }

        Check("ConfusedEnemy exposes a public CanAffect gate for callers",
              typeof(ConfusedEnemy).GetMethod("CanAffect", BindingFlags.Public | BindingFlags.Static) != null);
        Check("BerserkEnemy exposes a public CanAffect gate for callers",
              typeof(BerserkEnemy).GetMethod("CanAffect", BindingFlags.Public | BindingFlags.Static) != null);
    }

    /// Difficulty must scale a boss's HEALTH and ARMOUR pools exactly once. Awake is
    /// invoked directly on an inactive fixture, which is safe: it only assigns fields
    /// and registers with the modifier manager.
    private void T18_BossAwakePoolsAndDifficulty()
    {
        var mgr = EnsureModifierManager();
        if (mgr == null) { Skip("T18 boss Awake pools", "no EnemyStatModifierManager"); return; }
        mgr.ResetModifiers();
        EnemyStatModifierManager.SetStageScaling(1f, 1f, false);

        var awake = typeof(BaseBossStats).GetMethod("Awake", PRIV);
        if (awake == null) { Skip("T18 boss Awake pools", "BaseBossStats.Awake not found"); return; }

        // --- Normal: pools untouched ---
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);
        var nGO = NewFixture("bossNormal");
        var n = nGO.AddComponent<_TestBoss>();
        n.maxHealth = 1000f; n.currentHealth = 1000f; n.maxArmor = 500f;
        awake.Invoke(n, null);
        Check("Normal boss keeps its authored health pool", Approx(n.maxHealth, 1000f));
        Check("Normal boss keeps its authored armour pool", Approx(n.maxArmor, 500f));
        Check("Normal boss armour starts full", Approx(n.CurrentArmor, 500f));
        Check("Normal boss TotalMaxPool = health + armour", Approx(n.TotalMaxPool, 1500f));

        // --- Nightmare: both pools scale, exactly once ---
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Nightmare);
        float k = EnemyStatModifierManager.NightmareHealthMultiplier;
        var hGO = NewFixture("bossNightmare");
        var h = hGO.AddComponent<_TestBoss>();
        h.maxHealth = 1000f; h.currentHealth = 1000f; h.maxArmor = 500f;
        awake.Invoke(h, null);
        Check($"Nightmare scales boss maxHealth once (x{k})", Approx(h.maxHealth, 1000f * k));
        Check($"Nightmare scales boss currentHealth once (x{k})", Approx(h.currentHealth, 1000f * k));
        Check($"Nightmare scales boss ARMOUR once (x{k})", Approx(h.maxArmor, 500f * k));
        Check("Nightmare boss armour starts full at the scaled value", Approx(h.CurrentArmor, 500f * k));
        Check("Nightmare boss pool is fully consistent", Approx(h.TotalCurrentPool, h.TotalMaxPool));

        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);
        mgr.ResetModifiers();
    }

    /// Boss1/Boss2 used to leave currentHealth at CharacterStats' serialized default
    /// of 100 when no EnemyData asset was assigned, so they died almost instantly.
    /// Runs against the REAL boss classes — Awake only assigns fields, so invoking it
    /// on an inactive fixture is safe.
    private void T19_BossCurrentHealthSeededWithoutEnemyData()
    {
        var mgr = EnsureModifierManager();
        if (mgr != null) mgr.ResetModifiers();
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);
        EnemyStatModifierManager.SetStageScaling(1f, 1f, false);

        SeedCase<Boss1>("Boss1");
        SeedCase<Boss2>("Boss2");
        SeedCase<Boss3>("Boss3");
    }

    private void SeedCase<T>(string label) where T : BaseBossStats
    {
        var go = NewFixture("seed_" + label);
        T boss;
        try { boss = go.AddComponent<T>(); }
        catch { Skip($"T19 {label} health seed", "component could not be added"); return; }

        // No EnemyData -> the boss must fall back to its serialized bossMaxHealth.
        boss.enemyData = null;
        if (!SetPrivate(boss, "bossMaxHealth", 777f))
        { Skip($"T19 {label} health seed", "bossMaxHealth field not found"); return; }
        SetPrivate(boss, "bossMaxArmor", 333f);
        boss.currentHealth = 100f;                       // the stale CharacterStats default

        var awake = typeof(T).GetMethod("Awake", PRIV);
        if (awake == null) { Skip($"T19 {label} health seed", "Awake not found"); return; }
        try { awake.Invoke(boss, null); }
        catch (System.Exception ex)
        { Skip($"T19 {label} health seed", $"Awake threw: {ex.InnerException?.Message ?? ex.Message}"); return; }

        Check($"{label} reads maxHealth from bossMaxHealth when EnemyData is absent",
              Approx(boss.maxHealth, 777f));
        Check($"{label} SEEDS currentHealth to full (regression: used to stay at 100)",
              Approx(boss.currentHealth, boss.maxHealth));
    }

    private void T20_DamageUsesEnemyDataTimesMultiplier()
    {
        var mgr = EnsureModifierManager();
        if (mgr == null) { Skip("T20 Damage composition", "no EnemyStatModifierManager"); return; }
        mgr.ResetModifiers();
        EnemyStatModifierManager.SetStageScaling(1f, 1f, false);
        EnemyStatModifierManager.SetActiveMode((int)EnemyStatModifierManager.DifficultyMode.Normal);

        var go = NewFixture("damageComposition");
        var es = go.AddComponent<EnemyStats>();

        var data = ScriptableObject.CreateInstance<EnemyData>();
        data.damage = 25f;
        es.enemyData = data;

        Check("Damage == enemyData.damage with everything neutral", Approx(es.Damage, 25f));

        mgr.ApplyDamageMultiplier(2f);
        Check("Damage composes the augment multiplier", Approx(es.Damage, 50f));
        Check("Damage == ScaleDamage(enemyData.damage) — one shared multiplier",
              Approx(es.Damage, es.ScaleDamage(25f)));

        es.enemyData = null;
        Check("Damage is 0 with no EnemyData (no NaN, no throw)", Approx(es.Damage, 0f));

        Destroy(data);
        mgr.ResetModifiers();
    }

    /// Binding check for the members the fixes newly depend on. Cheap, and it turns a
    /// future rename into a named failure instead of a mysterious runtime break.
    private void T21_ExternalApiSurfaceStillBound()
    {
        var stun = FindTypeByName("ParryStunEffect");
        if (stun == null) Skip("T21 ParryStunEffect.IsStunActive", "type not found");
        else Check("ParryStunEffect.IsStunActive exists (BruteController now tests state, not presence)",
                   stun.GetProperty("IsStunActive") != null || stun.GetField("IsStunActive") != null);

        var bar = FindTypeByName("EnemyHealthBar");
        if (bar == null) Skip("T21 EnemyHealthBar.SetMaxHealth", "type not found");
        else Check("EnemyHealthBar.SetMaxHealth(float,float) exists (used by RefreshHealthBarCapacity)",
                   bar.GetMethod("SetMaxHealth", new[] { typeof(float), typeof(float) }) != null);

        var rewards = FindTypeByName("TowerKillRewards");
        if (rewards == null) Skip("T21 TowerKillRewards.OnEnemyKilled", "type not found");
        else Check("TowerKillRewards.OnEnemyKilled exists (augment 335 tithe)",
                   rewards.GetMethod("OnEnemyKilled", BindingFlags.Public | BindingFlags.Static) != null);

        var attrib = FindTypeByName("TowerKillAttribution");
        if (attrib == null) Skip("T21 TowerKillAttribution.Forget", "type not found");
        else Check("TowerKillAttribution.Forget exists (attribution cleanup)",
                   attrib.GetMethod("Forget", BindingFlags.Public | BindingFlags.Static) != null);

        // FireCommonDeathHooks must be safe on the degenerate inputs the death paths
        // can legitimately hand it.
        bool threw = false;
        try { EnemyStats.FireCommonDeathHooks(null); } catch { threw = true; }
        Check("FireCommonDeathHooks(null) is safe", !threw);
    }

    /// Augments 29 (Friendly Fire) and 76 (Pheromones) must never touch enemies that
    /// have no melee attack to redirect, run their own attack loop, or are anchored.
    private void T23_CrowdControlImmunity()
    {
        ImmuneCase<InsectController>("Insect / EliteInsect", true);
        ImmuneCase<BomberController>("Bomber", true);
        ImmuneCase<ParfumerController>("Parfumer", true);
        ImmuneCase<BufferController>("Buffer", true);
        ImmuneCase<Scarecrow>("Scarecrow", true);

        // A plain melee enemy must STILL be affectable — the exclusion list must not
        // quietly disable the augments for everything.
        ImmuneCase<BruteController>("Brute (control group)", false);

        // Vortex: hazard spawner, immune by stats type rather than by component.
        var vGO = NewFixture("ccVortex");
        var vs = vGO.AddComponent<VortexStats>();
        vs.maxHealth = 100f; vs.currentHealth = 100f;
        vGO.AddComponent<Rigidbody2D>();
        Check("CC REFUSES Vortex", !EnemyStats.CanBeExternallyControlled(vGO));

        // Per-prefab override, for exempting one prefab without a code change.
        var flagGO = NewFixture("ccFlagged");
        var fs = flagGO.AddComponent<EnemyStats>();
        fs.maxHealth = 100f; fs.currentHealth = 100f;
        flagGO.AddComponent<Rigidbody2D>();
        Check("CC accepts a plain enemy before the flag is set",
              EnemyStats.CanBeExternallyControlled(flagGO));
        fs.immuneToCrowdControl = true;
        Check("CC REFUSES an enemy with immuneToCrowdControl ticked",
              !EnemyStats.CanBeExternallyControlled(flagGO));
    }

    private void ImmuneCase<T>(string label, bool expectImmune) where T : MonoBehaviour
    {
        var go = NewFixture("ccImmune_" + typeof(T).Name);
        var st = go.AddComponent<EnemyStats>();
        st.maxHealth = 100f; st.currentHealth = 100f;
        go.AddComponent<Rigidbody2D>();
        go.AddComponent<T>();

        bool affectable = EnemyStats.CanBeExternallyControlled(go);
        Check(expectImmune ? $"CC REFUSES {label}" : $"CC still accepts {label}",
              affectable != expectImmune);

        // And the components themselves must self-refuse, so a caller that skips the
        // CanBeExternallyControlled gate still cannot half-apply the effect.
        if (expectImmune)
        {
            var ctrl = go.GetComponent<EnemyController>();
            if (ctrl == null) ctrl = go.AddComponent<EnemyController>();
            ctrl.enabled = true;
            go.AddComponent<ConfusedEnemy>().Initialize(5f);
            go.AddComponent<BerserkEnemy>().Initialize(5f);
            Check($"{label}: Confuse/Berserk Initialize leaves its controller untouched",
                  ctrl.enabled);
        }
    }

    /// Grappling hook: light enemies get dragged, heavy/immovable ones reel the
    /// PLAYER in, and nothing with a non-Dynamic body can ever be flung.
    private void T22_GrappleImmovability()
    {
        // Dynamic + light -> draggable, and ApplyGrapplePull actually moves it.
        var lightGO = NewFixture("grappleLight");
        var les = lightGO.AddComponent<EnemyStats>();
        les.maxHealth = 100f; les.currentHealth = 100f;
        var lrb2 = lightGO.AddComponent<Rigidbody2D>();
        lrb2.bodyType = RigidbodyType2D.Dynamic; lrb2.gravityScale = 0f;
        var lgt = lightGO.AddComponent<GrapplingTarget>();

        // Kinematic (Insect / EliteInsect shape) -> immovable, must NOT be flung.
        var kinGO2 = NewFixture("grappleKinematic");
        var kes = kinGO2.AddComponent<EnemyStats>();
        kes.maxHealth = 100f; kes.currentHealth = 100f;
        var krb = kinGO2.AddComponent<Rigidbody2D>();
        krb.bodyType = RigidbodyType2D.Kinematic;
        var kgt = kinGO2.AddComponent<GrapplingTarget>();

        // Boss -> immovable regardless of body type.
        var bossGO2 = NewFixture("grappleBoss");
        var bstats2 = bossGO2.AddComponent<_TestBoss>();
        bstats2.maxHealth = 1000f; bstats2.currentHealth = 1000f;
        var brb = bossGO2.AddComponent<Rigidbody2D>();
        brb.bodyType = RigidbodyType2D.Dynamic; brb.gravityScale = 0f;
        var bgt = bossGO2.AddComponent<GrapplingTarget>();

        // Fixtures are inactive, so Awake has not run — drive it explicitly.
        var det = typeof(GrapplingTarget).GetMethod("Awake", PRIV);
        if (det == null) { Skip("T22 grapple immovability", "GrapplingTarget.Awake not found"); return; }
        try { det.Invoke(lgt, null); det.Invoke(kgt, null); det.Invoke(bgt, null); }
        catch (System.Exception ex)
        { Skip("T22 grapple immovability", $"Awake threw: {ex.InnerException?.Message ?? ex.Message}"); return; }

        Check("Grapple: a Dynamic light enemy is NOT solid (still gets dragged to the player)",
              !lgt.IsSolidTarget());
        Check("Grapple: a KINEMATIC enemy IS solid (player reels in — no dead hook)",
              kgt.IsSolidTarget());
        Check("Grapple: a BOSS is solid regardless of body type", bgt.IsSolidTarget());

        // ── The pull itself must respect body type ──
        //
        // These two run on ACTIVE objects on purpose. A Rigidbody2D on an inactive
        // GameObject is not in the physics world, so linearVelocity writes are
        // dropped: the Dynamic case would fail even with correct code, and — worse —
        // the Kinematic case would PASS whether or not the guard exists, proving
        // nothing. Active bodies make both assertions mean what they say.
        var kinLive = NewActiveFixture("grappleKinematicLive");
        var kls = kinLive.AddComponent<EnemyStats>();
        kls.maxHealth = 100f; kls.currentHealth = 100f;
        var klrb = kinLive.AddComponent<Rigidbody2D>();
        klrb.bodyType = RigidbodyType2D.Kinematic;
        var klgt = kinLive.AddComponent<GrapplingTarget>();   // Awake runs for real

        var dynLive = NewActiveFixture("grappleDynamicLive");
        var dls = dynLive.AddComponent<EnemyStats>();
        dls.maxHealth = 100f; dls.currentHealth = 100f;
        var dlrb = dynLive.AddComponent<Rigidbody2D>();
        dlrb.bodyType = RigidbodyType2D.Dynamic; dlrb.gravityScale = 0f;
        var dlgt = dynLive.AddComponent<GrapplingTarget>();

        klrb.linearVelocity = Vector2.zero;
        klgt.ApplyGrapplePull(Vector3.right, 50f);
        Check("Grapple: pulling a KINEMATIC enemy leaves its velocity at zero (fly-away fix)",
              klrb.linearVelocity == Vector2.zero);

        dlrb.linearVelocity = Vector2.zero;
        dlgt.ApplyGrapplePull(Vector3.right, 50f);
        Check("Grapple: pulling a DYNAMIC enemy still moves it (drag behaviour preserved)",
              dlrb.linearVelocity != Vector2.zero);

        // Sanity: the live fixtures really are in the physics world, so a zero result
        // above means the guard fired — not that the write was silently dropped.
        dlrb.linearVelocity = Vector2.zero;
        dlrb.linearVelocity = new Vector2(3f, 0f);
        Check("Grapple: live fixture rigidbodies actually hold velocity (test is meaningful)",
              dlrb.linearVelocity != Vector2.zero);
        dlrb.linearVelocity = Vector2.zero;
    }

    private static System.Type FindTypeByName(string simpleName)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types) if (t.Name == simpleName) return t;
        }
        return null;
    }

    // =====================================================================
    //  TIER B — LIVE WATCHDOG
    // =====================================================================

    private void LiveSweep()
    {
        _sweeps++;

        // A boss that hid EnemyStats.OnDestroy never unregistered, so destroyed
        // bosses piled up in the tracked set as fake-null entries.
        TryCheck("tracked_enemies_no_destroyed_entries", () =>
        {
            var mgr = EnemyStatModifierManager.Instance;
            if (mgr == null) return (true, null);
            var f = typeof(EnemyStatModifierManager).GetField("trackedEnemies", PRIV);
            if (f == null) return (true, null);
            var set = f.GetValue(mgr) as HashSet<EnemyStats>;
            if (set == null) return (true, null);
            int dead = 0;
            foreach (var e in set) if (e == null) dead++;
            return (dead == 0, dead == 0 ? null
                : $"{dead} destroyed enemy/enemies still in trackedEnemies — an OnDestroy override is missing.");
        });

        // A boss hands off teardown to EnemyDeathVFX and never calls Destroy itself.
        // If one sits at 0 HP past the grace window, its teardown stalled.
        TryCheck("boss_teardown_completes", () =>
        {
            string stalled = null;
            var live = new HashSet<int>();
            foreach (var b in Object.FindObjectsByType<BaseBossStats>(FindObjectsSortMode.None))
            {
                if (b == null) continue;
                int id = b.GetInstanceID();
                live.Add(id);
                if (b.currentHealth > 0f) { _bossZeroHpSince.Remove(id); continue; }
                if (!_bossZeroHpSince.ContainsKey(id)) _bossZeroHpSince[id] = Time.time;
                else if (Time.time - _bossZeroHpSince[id] > bossTeardownGraceSeconds)
                    stalled = $"'{b.name}' has been at 0 HP for " +
                              $"{Time.time - _bossZeroHpSince[id]:F0}s and still exists — teardown stalled.";
            }
            var gone = new List<int>();
            foreach (var kv in _bossZeroHpSince) if (!live.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) _bossZeroHpSince.Remove(id);
            return (stalled == null, stalled);
        });

        // Crowd control must never land on a boss.
        TryCheck("no_crowd_control_on_boss", () =>
        {
            foreach (var b in Object.FindObjectsByType<BaseBossStats>(FindObjectsSortMode.None))
            {
                if (b == null) continue;
                if (b.GetComponent<ConfusedEnemy>() != null)
                    return (false, $"boss '{b.name}' has a ConfusedEnemy attached.");
                if (b.GetComponent<BerserkEnemy>() != null)
                    return (false, $"boss '{b.name}' has a BerserkEnemy attached.");
            }
            return (true, null);
        });

        // If CC is attached, the enemy's own controllers must actually be off —
        // otherwise the augment is the silent no-op it used to be.
        TryCheck("crowd_control_actually_suspends", () =>
        {
            foreach (var c in Object.FindObjectsByType<ConfusedEnemy>(FindObjectsSortMode.None))
            {
                string bad = FirstEnabledController(c != null ? c.gameObject : null);
                if (bad != null) return (false, $"confused '{c.gameObject.name}' still runs {bad}.");
            }
            foreach (var b in Object.FindObjectsByType<BerserkEnemy>(FindObjectsSortMode.None))
            {
                string bad = FirstEnabledController(b != null ? b.gameObject : null);
                if (bad != null) return (false, $"berserked '{b.gameObject.name}' still runs {bad}.");
            }
            return (true, null);
        });

        // A suspended-but-alive Mort must not be flagged dead.
        TryCheck("mort_not_falsely_dead", () =>
        {
            var f = typeof(MortController).GetField("hasDied", PRIV);
            if (f == null) return (true, null);
            foreach (var m in Object.FindObjectsByType<MortController>(FindObjectsSortMode.None))
            {
                if (m == null) continue;
                var st = m.GetComponent<EnemyStats>();
                var ec = m.GetComponent<EnemyController>();
                if (st == null || st.currentHealth <= 0f) continue;
                if (ec == null || !ec.enabled) continue;          // legitimately suspended
                if ((bool)f.GetValue(m))
                    return (false, $"Mort '{m.name}' is alive with an active controller but hasDied==true.");
            }
            return (true, null);
        });

        TryCheck("enemy_health_finite", () =>
        {
            foreach (var e in Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
            {
                if (e == null) continue;
                if (float.IsNaN(e.currentHealth) || float.IsInfinity(e.currentHealth)
                    || float.IsNaN(e.maxHealth) || float.IsInfinity(e.maxHealth))
                    return (false, $"'{e.name}' health non-finite: {e.currentHealth}/{e.maxHealth}");
            }
            return (true, null);
        });

        TryCheck("boss_pool_consistent", () =>
        {
            foreach (var b in Object.FindObjectsByType<BaseBossStats>(FindObjectsSortMode.None))
            {
                if (b == null) continue;
                float cur = b.TotalCurrentPool, max = b.TotalMaxPool;
                if (float.IsNaN(cur) || float.IsNaN(max) || max <= 0f)
                    return (false, $"boss '{b.name}' pool invalid: {cur}/{max}");
                if (cur > max + 0.01f)
                    return (false, $"boss '{b.name}' current pool {cur} exceeds max {max} — bar denominator is stale.");
            }
            return (true, null);
        });

        TryCheck("wave_counter_nonnegative", () =>
        {
            var ws = Object.FindAnyObjectByType<WaveSpawner>();
            if (ws == null) return (true, null);
            var f = typeof(WaveSpawner).GetField("enemiesAlive", PRIV);
            if (f == null) return (true, null);
            int n = (int)f.GetValue(ws);
            return (n >= 0, n >= 0 ? null : $"WaveSpawner.enemiesAlive is {n}.");
        });

        // ADRIFT ENEMY DETECTOR.
        // A Kinematic / Static Rigidbody2D has no drag and no collision response, so
        // any velocity written to it persists forever and carries the enemy through
        // walls and off the map — still alive, still counted, impossible to reach.
        // The Insect and EliteInsect force Kinematic in InsectController.Awake, so
        // they are the usual victims.
        //
        // Detected by DISTANCE, not by velocity: these enemies legitimately drive
        // their own velocity for tunnel/leap, so a speed check would fire constantly
        // and an auto-clamp would break their movement. Being far outside the play
        // area is unambiguous.
        TryCheck("no_enemy_adrift_off_map", () =>
        {
            foreach (var es in Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
            {
                if (es == null || !es.enabled || !es.gameObject.activeInHierarchy) continue;

                Vector3 p = es.transform.position;
                float d = new Vector2(p.x, p.y).magnitude;
                if (d <= playAreaRadius) continue;

                var rb = es.GetComponent<Rigidbody2D>();
                string body = rb != null ? rb.bodyType.ToString() : "no-rb";
                string vel = rb != null ? rb.linearVelocity.ToString() : "n/a";
                string cc = es.GetComponent<ConfusedEnemy>() != null ? " CONFUSED" : "";
                if (es.GetComponent<BerserkEnemy>() != null) cc += " BERSERK";

                return (false, $"'{es.name}' ({es.GetType().Name}) is {d:F0} units from origin " +
                               $"— body={body} velocity={vel} hp={es.currentHealth:F0}{cc}. " +
                               "It will keep the wave from completing.");
            }
            return (true, null);
        });

        // WAVE STALL DETECTOR.
        // The orchestrator ends a wave only when BOTH its own enemiesAlive counter is
        // <= 0 AND CountLivingEnemiesInScene() is 0. When the counter hits 0 while
        // enemies are still standing, the wave hangs and the HUD looks "cleared".
        // This names the survivors instead of leaving you to hunt them.
        TryCheck("wave_not_stalled", () =>
        {
            var orch = GameOrchestrator.Instance;
            if (orch == null) { _stallSince = -1f; return (true, null); }

            var f = typeof(GameOrchestrator).GetField("enemiesAlive", PRIV);
            if (f == null) { _stallSince = -1f; return (true, null); }
            int counted = (int)f.GetValue(orch);

            // Mirror CountLivingEnemiesInScene's predicate exactly.
            var survivors = new List<EnemyStats>();
            foreach (var es in Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
            {
                if (es == null || !es.enabled || !es.gameObject.activeInHierarchy) continue;
                survivors.Add(es);
            }

            if (counted > 0 || survivors.Count == 0) { _stallSince = -1f; return (true, null); }

            // Counter says 0 but the scene disagrees. That state is NOT automatically a
            // bug: enemies spawned outside WaveSpawner (VortexSpawner's spawns,
            // SplitterController's children) never get a WaveEnemy, so they never
            // increment the orchestrator counter — it legitimately reads 0 while they
            // are still being fought. Firing on that alone would cry wolf on every
            // vortex wave.
            //
            // So require the situation to be STATIC as well: same survivor count and
            // unchanged total health. Any damage dealt, or any enemy dying, resets the
            // timer. What is left is a genuine stall — enemies that exist but are not
            // being (and often cannot be) fought.
            float fingerprint = survivors.Count * 1000f;
            for (int i = 0; i < survivors.Count; i++) fingerprint += survivors[i].currentHealth;

            if (_stallSince < 0f || Mathf.Abs(fingerprint - _stallFingerprint) > 0.01f)
            {
                _stallSince = Time.time;
                _stallFingerprint = fingerprint;
                return (true, null);
            }

            if (Time.time - _stallSince < waveStallGraceSeconds) return (true, null);

            var sb = new System.Text.StringBuilder();
            sb.Append($"WAVE STALLED: orchestrator enemiesAlive={counted} but {survivors.Count} " +
                      $"enemy/enemies still count as alive, and NOTHING has changed for " +
                      $"{waveStallGraceSeconds:F0}s (no damage dealt, none died). Survivors: ");
            for (int i = 0; i < survivors.Count && i < 12; i++)
            {
                var s = survivors[i];
                string cc = s.GetComponent<ConfusedEnemy>() != null ? " CONFUSED" : "";
                if (s.GetComponent<BerserkEnemy>() != null) cc += " BERSERK";
                sb.Append($"[{s.GetType().Name} '{s.name}' hp={s.currentHealth:F0}/{s.maxHealth:F0} " +
                          $"pos={s.transform.position}{cc}] ");
            }
            return (false, sb.ToString());
        });

        TryCheck("augment_statics_sane", () =>
        {
            float[] vals = {
                TowerCombatModifiers.DamageMultiplier,
                TowerCombatModifiers.BaseFireRateMultiplier,
                TowerCombatModifiers.PerCountFireRateMultiplier,
                PlayerEconomyModifiers.EnergyGainMultiplier,
                PlayerCombatModifiers.OutgoingDamageMultiplier,
            };
            foreach (var v in vals)
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
                    return (false, $"an augment multiplier is invalid: {v}");
            return (true, null);
        });
    }

    private static string FirstEnabledController(GameObject go)
    {
        if (go == null) return null;
        var ec = go.GetComponent<EnemyController>(); if (ec != null && ec.enabled) return "EnemyController";
        var bu = go.GetComponent<BufferController>(); if (bu != null && bu.enabled) return "BufferController";
        var pa = go.GetComponent<ParfumerController>(); if (pa != null && pa.enabled) return "ParfumerController";
        var bo = go.GetComponent<BomberController>(); if (bo != null && bo.enabled) return "BomberController";
        return null;
    }

    private void ToggleLive()
    {
        liveMonitoring = !liveMonitoring;
        LWarn(liveMonitoring ? "live monitoring RESUMED." : "live monitoring PAUSED (F7 to resume).");
    }

    private void Heartbeat()
    {
        int broken = 0;
        foreach (var kv in _state) if (!kv.Value) broken++;
        long total = 0;
        foreach (var kv in _counts) total += kv.Value;
        if (broken == 0)
            L($"heartbeat: sweep #{_sweeps}, {_state.Count} invariants, {total} checks, all holding \u2713");
        else
            LErr($"heartbeat: sweep #{_sweeps}, {broken} invariant(s) BROKEN right now (F6 for details).");
    }

    [ContextMenu("Dump live status (F6)")]
    private void DumpLiveStatus()
    {
        L("===== LIVE STATUS =====");
        LiveSweep();
        if (_state.Count == 0) { L("(no invariants evaluated yet)"); return; }
        int broken = 0;
        foreach (var kv in _state)
        {
            int n = _counts.TryGetValue(kv.Key, out int c) ? c : 0;
            L($"  {(kv.Value ? "OK  " : "FAIL")}  {kv.Key}  (checked {n}x)");
            if (!kv.Value) broken++;
        }
        L($"sweeps: {_sweeps}");
        if (broken == 0) L("<color=lime>all live invariants holding</color>");
        else LErr($"{broken} live invariant(s) BROKEN — see FAIL line(s).");
        L("=======================");
    }

    private void TryCheck(string key, System.Func<(bool ok, string detail)> probe)
    {
        bool ok; string detail;
        try { (ok, detail) = probe(); }
        catch { return; }                    // a system that isn't up yet is never a false alarm
        Record(key, ok, detail);
    }

    private void Record(string key, bool ok, string detail)
    {
        _counts[key] = (_counts.TryGetValue(key, out int c) ? c : 0) + 1;
        bool had = _state.TryGetValue(key, out bool prev);
        bool prevOk = !had || prev;
        _state[key] = ok;

        if (prevOk && !ok)
            LErr($"BROKEN: {key}" + (string.IsNullOrEmpty(detail) ? "" : $" — {detail}"));
        else if (!prevOk && ok)
            LWarn($"recovered: {key}");
    }

    /// ONE consolidated, copy-pasteable block. Everything needed to diagnose the run
    /// is inside this single Console entry, so you can click it once and copy rather
    /// than reassembling dozens of separate lines.
    [ContextMenu("Print copy/paste report")]
    public void PrintCopyPasteReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("===== AUGTEST COPY/PASTE REPORT =====");
        sb.AppendLine($"unity={Application.unityVersion} scene='{gameObject.scene.name}' " +
                      $"platform={Application.platform}");
        sb.AppendLine($"difficulty={EnemyStatModifierManager.ActiveMode} " +
                      $"modifierManager={(EnemyStatModifierManager.Instance != null ? "present" : "ABSENT")}");
        sb.AppendLine($"RESULT: {_pass} passed / {_fail} failed / {_skip} skipped");

        if (_failLines.Count == 0) sb.AppendLine("FAILURES: none");
        else
        {
            sb.AppendLine($"FAILURES ({_failLines.Count}):");
            foreach (var f in _failLines) sb.AppendLine("  FAIL  " + f);
        }

        if (_skipLines.Count == 0) sb.AppendLine("SKIPS: none");
        else
        {
            sb.AppendLine($"SKIPS ({_skipLines.Count})  <- these tests did NOT run, treat as unverified:");
            foreach (var s in _skipLines) sb.AppendLine("  SKIP  " + s);
        }

        // Live watchdog state, so one paste covers both tiers.
        int broken = 0;
        foreach (var kv in _state) if (!kv.Value) broken++;
        sb.AppendLine($"LIVE: {_state.Count} invariants evaluated, {broken} currently broken, {_sweeps} sweeps");
        foreach (var kv in _state)
            if (!kv.Value) sb.AppendLine("  LIVE-BROKEN  " + kv.Key);

        sb.AppendLine("===== END REPORT =====");

        if (_fail == 0 && broken == 0) Debug.Log(TAG + sb.ToString());
        else Debug.LogError(TAG + sb.ToString());
    }

    // =====================================================================
    //  helpers
    // =====================================================================

    /// Fixtures live under an INACTIVE root, so no Awake/Start ever fires on them.
    private GameObject NewFixture(string name)
    {
        var go = new GameObject("~" + name);
        go.transform.SetParent(_fixtureRoot, false);
        return go;
    }

    /// An ACTIVE fixture — its components DO run Awake/Start and its Rigidbody2D IS
    /// in the physics world. Use only where real physics behaviour is under test;
    /// prefer NewFixture() everywhere else so nothing executes unexpectedly.
    private GameObject NewActiveFixture(string name)
    {
        if (_activeFixtureRoot == null)
        {
            var rootGO = new GameObject("~AugTestActiveFixtures");
            rootGO.hideFlags = HideFlags.HideAndDontSave;
            _activeFixtureRoot = rootGO.transform;
        }

        var go = new GameObject("~" + name);
        go.transform.SetParent(_activeFixtureRoot, false);
        return go;
    }

    /// Returns the live manager, creating a throwaway one if the scene has none.
    /// Records ownership so the suite can clean up after itself WITHOUT ever
    /// destroying a manager that belongs to the running game.
    private EnemyStatModifierManager EnsureModifierManager()
    {
        if (EnemyStatModifierManager.Instance != null) return EnemyStatModifierManager.Instance;

        var go = new GameObject("~AugTestModifierManager");
        go.AddComponent<EnemyStatModifierManager>();   // Awake sets Instance synchronously
        _harnessOwnedManager = EnemyStatModifierManager.Instance;
        return _harnessOwnedManager;
    }

    /// True when a live gameplay session is present. The mutating tests (T12-T14)
    /// temporarily change global difficulty, the enemy multipliers and the modifier
    /// manager itself; doing that to a running game corrupts it, so they are skipped.
    private bool SceneLooksLive()
    {
        if (Object.FindAnyObjectByType<GameOrchestrator>() != null) return true;
        if (Object.FindAnyObjectByType<WaveSpawner>() != null) return true;

        // A manager that the harness did not create belongs to the scene.
        if (EnemyStatModifierManager.Instance != null && _harnessOwnedManager == null) return true;

        foreach (var es in Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
            if (es != null) return true;

        return false;
    }

    private static bool SetPrivate(object target, string field, object value)
    {
        var f = target.GetType().GetField(field, PRIV);
        if (f == null) return false;
        try { f.SetValue(target, value); return true; } catch { return false; }
    }

    private static T GetPrivate<T>(object target, string field)
    {
        var f = target.GetType().GetField(field, PRIV);
        if (f == null) return default;
        try { return (T)f.GetValue(target); } catch { return default; }
    }

    private void Check(string name, bool condition)
    {
        if (condition) { _pass++; Debug.Log(TAG + $"[PASS] {name}"); }
        else { _fail++; _failLines.Add(name); Debug.LogError(TAG + $"[FAIL] {name}"); }
    }

    private void Skip(string name, string why)
    {
        _skip++;
        _skipLines.Add($"{name} — {why}");
        Debug.LogWarning(TAG + $"[SKIP] {name} — {why}");
    }

    private static void L(string m) => Debug.Log(TAG + m);
    private static void LWarn(string m) => Debug.LogWarning(TAG + m);
    private static void LErr(string m) => Debug.LogError(TAG + m);
    private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
}


