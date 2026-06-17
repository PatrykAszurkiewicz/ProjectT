using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// THE GAME ORCHESTRATOR — runs an entire roguelike session.
/// ║  StartRun()                                                ║
/// ║    │                                                       ║
/// ║    ├── Stage 1 (random biome, e.g. Grass)                  ║
/// ║    │     ├── Wave 1  ─┐                                    ║
/// ║    │     ├── Wave 2   │  wavesPerStage waves               ║
/// ║    │     ├── ...      │  (from your WaveConfig)            ║
/// ║    │     ├── Wave 8  ─┘                                    ║
/// ║    │     └── Stage Boss                                    ║
/// ║    │                                                       ║
/// ║    ├── Stage 2 (random biome, e.g. Desert + Night)         ║
/// ║    │     ├── Wave 1..8                                     ║
/// ║    │     └── Stage Boss                                    ║
/// ║    │                                                       ║
/// ║    ├── Stage 3 (random biome, e.g. Snow + Fog)             ║
/// ║    │     └── ...                                           ║
/// ║    │                                                       ║
/// ║    ├── Stage 4 (random biome, e.g. Wasteland)              ║
/// ║    │     └── ...                                           ║
/// ║    │                                                       ║
/// ║    └── FINAL BOSS                                          ║

/// TESTING:
/// - Use the [ContextMenu] options (right-click the component in Inspector)
/// - "Start Run" — begins a full run
/// - "Skip To Next Stage" — jumps to the next biome stage
/// - "Log Run Plan" — prints the entire run plan to Console without starting
/// - Check "Auto Start Run" to begin immediately on Play

public class GameOrchestrator : MonoBehaviour
{
    //  SINGLETON

    public static GameOrchestrator Instance { get; private set; }

    //  INSPECTOR FIELDS

    [Header("═══ CONFIGURATION ═══")]
    [Tooltip("The run blueprint. Create via: Create → Game → Run Config")]
    public RunConfig runConfig;

    [Tooltip("Start a run automatically when the scene loads? Great for testing.")]
    public bool autoStartRun = true;

    [Tooltip("If true, a saved run on disk is resumed automatically on launch. " +
             "Leave OFF to always start fresh (recommended until a 'Continue' button is wired). " +
             "A stale save left from testing is the usual cause of a black-screen boot.")]
    public bool autoResumeSavedRun = false;

    [Header("═══ SCENE REFERENCES ═══")]
    [Tooltip("Drag your BiomeManager here (or leave empty — will auto-find).")]
    public BiomeManager biomeManager;

    [Tooltip("Drag your WaveSpawner here (or leave empty — will auto-find).")]
    public WaveSpawner waveSpawner;

    [Header("═══ DEBUG ═══")]
    [Tooltip("Print detailed state transitions to Console.")]
    public bool debugLog = true;

    [Header("═══ TRANSITIONS ═══")]
    [Tooltip("Enable smooth fade-to-black transitions between stages.")]
    public bool enableTransitions = true;

    [Header("═══ AUGMENTS ═══")]
    [Tooltip("Show augment selection after each stage boss kill. If no AugmentsMenu found, skipped.")]
    public bool enableAugmentSelection = true;

    [Header("═══ POST-STAGE CHOICE ═══")]
    [Tooltip("After each stage, show a menu: Heal everything + bonus energy  OR  Pick augment + small energy.")]
    public bool enablePostStageChoice = true;

    [Tooltip("Energy given to the player if they pick HEAL.")]
    public int healChoiceEnergyBonus = 300;

    [Tooltip("Energy given to the player if they pick AUGMENT.")]
    public int augmentChoiceEnergyBonus = 100;

    [Header("═══ PACING ═══")]
    [Tooltip("Seconds to pause after the last enemy of a wave/boss dies before any menu " +
             "or stage transition appears. Lets the kill land before UI interrupts.")]
    [Min(0f)]
    public float pauseAfterLastKill = 1f;

    // Auto-found references
    private StageTransitionOverlay transitionOverlay;
    private AugmentsMenu augmentsMenu;
    private AugmentsMenu[] augmentsMenus;   // Phase 6: one per player in co-op
    private PostStageChoiceMenu postStageChoiceMenu;
    private StageClearScreenMenu stageClearMenu;   // Phase 8: prefab-based reward screen (single + co-op split)

    //  RUN STATE (read these from other scripts)

    public enum RunState
    {
        Idle,           // waiting to start
        StageIntro,     // showing biome transition
        WaveCountdown,  // brief pause before next wave
        WaveActive,     // enemies are alive
        StageBoss,      // boss fight at end of stage
        AugmentSelect,  // player choosing an augment
        StageComplete,  // stage cleared, about to advance
        FinalBoss,      // final boss after all stages
        Victory,        // player won!
        GameOver        // core destroyed
    }

    /// Current state of the run. Read from UI scripts to show banners etc.</summary>
    public RunState CurrentState { get; private set; } = RunState.Idle;

    /// Which stage we're on (0-based).</summary>
    public int CurrentStageIndex { get; private set; }

    /// Which wave within the current stage (0-based).</summary>
    public int CurrentWaveInStage { get; private set; }

    /// Total stages in this run.</summary>
    public int TotalStages => currentRunPlan?.Count ?? 0;

    /// Total waves in current stage.</summary>
    public int TotalWavesInCurrentStage => GetCurrentStage()?.waves?.Count ?? 0;

    /// The biome sequence for this run (for UI map screens etc).</summary>
    public List<StageData> RunPlan => currentRunPlan;

    //  EVENTS (subscribe from UI, audio, etc.)

    /// Fired on every state change. Use for UI transitions.</summary>
    public event Action<RunState, RunState> OnStateChanged;   // (oldState, newState)

    /// Fired when a new stage begins. Use for "Stage 2: Desert" banners.</summary>
    public event Action<StageData> OnStageStarted;

    // Holds saved tower data during a crash/exit resume until the stage layout
    // (and its slots) exist; applied once inside RunStage, then cleared.
    private RunSaveData _pendingTowerRestore;

    /// Fired when a wave begins. Use for "Wave 5/8" indicators.</summary>
    public event Action<int, int> OnWaveStarted;              // (waveIndex, totalWaves)

    /// Fired when all enemies in a wave are dead.</summary>
    public event Action<int> OnWaveCleared;                   // (waveIndex)

    /// Fired when a stage boss appears.</summary>
    public event Action<StageData> OnBossSpawned;

    /// Fired when stage boss is killed.</summary>
    public event Action<int> OnBossKilled;                    // (stageIndex)

    /// Fired when the entire run is won.</summary>
    public event Action OnVictory;

    /// Fired on game over.</summary>
    public event Action OnGameOver;

    //  PRIVATE STATE

    private List<StageData> currentRunPlan;
    private Coroutine runCoroutine;
    private int enemiesAlive;
    private int wavePickCursor; // advances across stages while GenerateRunPlan builds the plan
    private List<WaveData> runWaveDeck;   // flattened (optionally shuffled) wave pool for this run
    private List<GameObject> bossSequence; // resolved per-stage boss order for this run (null = fixed mapping)
    private GameObject currentBossInstance; // specific boss GO we're waiting on (null when no boss alive)
    private List<MapLayoutDefinition> usedLayouts = new List<MapLayoutDefinition>();
    private MapLayoutDefinition runWideLayout; // used when changeLayoutPerStage == false

    //  UNITY LIFECYCLE

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad only works on ROOT GameObjects. This object is nested,
        // so calling it unconditionally only logged a warning and did nothing. Call
        // it solely when we ARE root — this silences the warning while preserving the
        // original runtime behaviour (the orchestrator did NOT persist across scene
        // loads). If you ever need true cross-scene persistence, move this object to
        // the scene root in the editor AND re-resolve the Awake scene references on
        // load, since they would otherwise dangle after a scene change.
        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);

        // Auto-find scene references if not assigned
        if (biomeManager == null)
            biomeManager = FindFirstObjectByType<BiomeManager>();
        if (waveSpawner == null)
            waveSpawner = FindFirstObjectByType<WaveSpawner>();

        // Create transition overlay
        if (enableTransitions)
        {
            transitionOverlay = GetComponent<StageTransitionOverlay>();
            if (transitionOverlay == null)
                transitionOverlay = gameObject.AddComponent<StageTransitionOverlay>();

            // Initialize AND force opaque black NOW, in Awake, before the first frame
            // renders. The stage-intro coroutine runs as a nested coroutine a frame or
            // two later, so relying on it (or on the startBlack inspector value) leaves
            // a one-frame blink of the scene before the banner. Asserting black here
            // guarantees frame 0 is already covered.
            transitionOverlay.EnsureInitialized();
            transitionOverlay.SnapToBlack();
        }

        // Find augment menu (searches inactive objects too)
        augmentsMenu = FindFirstObjectByType<AugmentsMenu>(FindObjectsInactive.Include);
        augmentsMenus = FindObjectsByType<AugmentsMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // Create post-stage choice menu (runtime-built, no prefab needed)
        if (enablePostStageChoice)
        {
            postStageChoiceMenu = GetComponent<PostStageChoiceMenu>();
            if (postStageChoiceMenu == null)
                postStageChoiceMenu = gameObject.AddComponent<PostStageChoiceMenu>();

            // Phase 8: prefab-based reward screen. When its prefab is present it supersedes the
            // procedural menu (single-player full-screen, co-op split). If the prefab is missing,
            // IsAvailable stays false and we use the legacy menu — single player never regresses.
            stageClearMenu = GetComponent<StageClearScreenMenu>();
            if (stageClearMenu == null)
                stageClearMenu = gameObject.AddComponent<StageClearScreenMenu>();
        }

        if (WaveCheckpointService.Instance == null) gameObject.AddComponent<WaveCheckpointService>();
        if (RunPersistence.Instance == null) gameObject.AddComponent<RunPersistence>();


    }

    void Start()
    {
        ValidateSetup();

        // A throwing or corrupt resume must never strand the player: catch it and
        // fall through to a fresh run instead of leaving the boot half-initialised.
        bool resumed = false;
        if (autoResumeSavedRun)
        {
            try { resumed = TryResumeSavedRun(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[Orchestrator] Saved-run resume failed — starting fresh. {e}");
                resumed = false;
            }
        }
        if (!resumed && autoStartRun) StartRun();

    }

    //  PUBLIC API

    /// Start a new roguelike run. Generates a random biome sequence and begins.
    [ContextMenu("▶ Start Run")]
    public void StartRun()
    {
        if (runConfig == null)
        {
            Debug.LogError("[Orchestrator] No RunConfig assigned! Create one via Create → Game → Run Config");
            return;
        }

        // Stop any existing run
        if (runCoroutine != null)
            StopCoroutine(runCoroutine);

        int runSeed = System.Environment.TickCount;
        UnityEngine.Random.InitState(runSeed);
        RunPersistence.Instance?.BeginRun(runSeed, runConfig != null ? runConfig.name : "");


        // Clear per-player static augment state so a previous run's cooldown /
        // parry / projectile-parry upgrades don't leak into this fresh run (statics
        // survive scene reloads in a built player).
        CooldownModifier.Reset();
        ParryUpgrades.ResetAll();
        ProjectileParry.Reset();


        // Generate the run plan
        currentRunPlan = GenerateRunPlan();

        if (debugLog)
        {
            Debug.Log("[Orchestrator] ═══ NEW RUN ═══");
            foreach (var stage in currentRunPlan)
                Debug.Log($"  {stage}");
        }

        // Start the run loop
        CurrentStageIndex = 0;
        runCoroutine = StartCoroutine(RunLoop());
    }

    // Call this when an enemy dies. The orchestrator tracks alive counts.
    // Hook this up from your enemy death logic.
    public void OnEnemyDeath()
    {
        enemiesAlive = Mathf.Max(0, enemiesAlive - 1);

        if (debugLog && enemiesAlive <= 3)
            Debug.Log($"[Orchestrator] Enemy died. Remaining: {enemiesAlive}");
    }

    // Call this when the central core is destroyed (game over).
    public void TriggerGameOver()
    {
        if (CurrentState == RunState.GameOver || CurrentState == RunState.Victory)
            return;

        if (runCoroutine != null)
            StopCoroutine(runCoroutine);

        SetState(RunState.GameOver);
        OnGameOver?.Invoke();

        RunPersistence.Instance?.OnSaveConsumed();


        Debug.Log("[Orchestrator] ══ GAME OVER ══");
    }

    // Get the current stage data (or null if no run active).</summary>
    public StageData GetCurrentStage()
    {
        if (currentRunPlan == null || CurrentStageIndex >= currentRunPlan.Count)
            return null;
        return currentRunPlan[CurrentStageIndex];
    }

    //  DEBUG / TESTING

    [ContextMenu("⏭ Skip To Next Stage")]
    public void DebugSkipStage()
    {
        // Kill all enemies instantly
        enemiesAlive = 0;
        Debug.Log("[Orchestrator] DEBUG: Skipping to next stage...");
    }

    [ContextMenu("📋 Log Run Plan (no start)")]
    public void DebugLogRunPlan()
    {
        if (runConfig == null)
        {
            Debug.LogError("[Orchestrator] No RunConfig assigned!");
            return;
        }

        var plan = GenerateRunPlan();
        Debug.Log("═══ RUN PLAN (preview) ═══");
        foreach (var stage in plan)
            Debug.Log($"  {stage}");
        Debug.Log($"  {(runConfig.hasFinalBoss ? "→ FINAL BOSS" : "→ Victory")}");
    }

    [ContextMenu("☠ Trigger Game Over")]
    public void DebugGameOver()
    {
        TriggerGameOver();
    }

    //  THE MAIN RUN LOOP

    private IEnumerator FinishRun()
    {
        if (runConfig.hasFinalBoss && runConfig.finalBossPrefab != null)
        {
            yield return RunFinalBoss();
            if (CurrentState == RunState.GameOver) yield break;
        }
        Time.timeScale = 1f;
        if (enableTransitions && transitionOverlay != null)
        {
            yield return new WaitForSecondsRealtime(1f);
            yield return transitionOverlay.FadeOut(0.8f);
            yield return transitionOverlay.ShowMessage("VICTORY", "", 3f);
        }
        SetState(RunState.Victory);
        OnVictory?.Invoke();
        RunPersistence.Instance?.OnSaveConsumed();
        Debug.Log("[Orchestrator] ═══ VICTORY! ═══");
    }

    private IEnumerator RunLoop()
    {
        // ── Play through each stage ──
        for (CurrentStageIndex = 0; CurrentStageIndex < currentRunPlan.Count; CurrentStageIndex++)
        {
            yield return RunStage(currentRunPlan[CurrentStageIndex]);

            // Check for game over between stages
            if (CurrentState == RunState.GameOver)
                yield break;
        }

        yield return FinishRun();

    }

    //  STAGE FLOW

    private IEnumerator RunStage(StageData stage, int startWaveIndex = 0, bool skipIntro = false)
    {
        if (debugLog)
            Debug.Log($"[Orchestrator] ─── {stage} ───");

        // Push this stage's enemy HP/damage scaling into the global modifier manager
        // BEFORE any enemy or boss spawns this stage, so each one reads it at spawn
        // (health) / on attack (damage). It composes multiplicatively with augment
        // multipliers and never overwrites them. Set once per stage entry here so it
        // also covers the no-transition and resume (skipIntro) paths below.
        EnemyStatModifierManager.SetStageScaling(
            stage.enemyHealthMultiplier,
            stage.enemyDamageMultiplier,
            runConfig != null && runConfig.scaleBossesWithStage);


        if (!skipIntro)
        {

            //  1. STAGE INTRO: Fade out → swap biome → show banner → fade in
            SetState(RunState.StageIntro);

            if (enableTransitions && transitionOverlay != null)
            {
                // Tell the overlay the intro is running so its boot watchdog stands down
                // (a slow biome build must not trip it and reveal the game early).
                transitionOverlay.NotifyIntroStarted();

                if (stage.stageIndex > 0)
                {
                    // Not the first stage — fade to black first
                    yield return transitionOverlay.FadeOut();
                }
                else
                {
                    // First stage: GUARANTEE the screen is fully black before we build
                    // the biome, instead of assuming startBlack stuck. Without this, if
                    // startBlack is off the freshly-built biome flashes for ~a second
                    // before the banner.
                    transitionOverlay.SnapToBlack();
                }

                // Swap biome while screen is black. Wrapped so a failure here can't
                // abort the coroutine before FadeIn() and strand the player on black.
                try
                {
                    ApplyBiome(stage);
                    OnStageStarted?.Invoke(stage);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Orchestrator] Stage intro setup threw — revealing screen anyway. {e}");
                }

                // Configure the WaveSpawner's scaling for this stage
                if (waveSpawner != null)
                    waveSpawner.enemySpawnCountMultiplier = stage.enemyCountMultiplier;

                // Let biome settle (particles, overlays, etc.)
                yield return new WaitForSeconds(0.3f);

                // Show "Stage 1: Frozen Tundra" banner over black
                yield return transitionOverlay.ShowBanner(stage, runConfig.stageCount);

                // Fade back in
                yield return transitionOverlay.FadeIn();
            }
            else
            {
                // No transitions — instant swap (original behavior). Wrapped so a
                // failure here can't abort the stage coroutine.
                try
                {
                    ApplyBiome(stage);
                    OnStageStarted?.Invoke(stage);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Orchestrator] Stage intro setup threw. {e}");
                }

                if (waveSpawner != null)
                    waveSpawner.enemySpawnCountMultiplier = stage.enemyCountMultiplier;

                yield return new WaitForSeconds(runConfig.timeBetweenStages);
            }

        }
        else
        {
            if (waveSpawner != null) waveSpawner.enemySpawnCountMultiplier = stage.enemyCountMultiplier;
        }

        // Deferred tower restore (crash/exit resume): slots only exist once the
        // stage layout has been applied above, so we rebuild saved towers HERE,
        // before the first wave. One-shot — cleared after it runs.
        if (_pendingTowerRestore != null)
        {
            // Give the layout a frame to register its slots with TowerPlacementManager.
            yield return null;
            try
            {
                RunPersistence.Instance?.RestoreTowers(_pendingTowerRestore);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Orchestrator] Tower restore failed. {e}");
            }
            _pendingTowerRestore = null;
        }



        //  2. WAVES: Play through each wave 
        for (CurrentWaveInStage = startWaveIndex; CurrentWaveInStage < stage.waves.Count; CurrentWaveInStage++)
        {
            // Check game over
            if (CurrentState == RunState.GameOver) yield break;

            WaveData wave = stage.waves[CurrentWaveInStage];

            // Brief countdown before wave
            SetState(RunState.WaveCountdown);
            OnWaveStarted?.Invoke(CurrentWaveInStage, stage.waves.Count);
            WaveCheckpointService.Instance?.CaptureSnapshot(CurrentStageIndex, CurrentWaveInStage);
            RunPersistence.Instance?.AutoSaveWaveStart(CurrentStageIndex, CurrentWaveInStage);
            if (debugLog)
                Debug.Log($"[Orchestrator] Wave {CurrentWaveInStage + 1}/{stage.waves.Count} starting...");

            // Update persistent wave counter at top of screen + center flash
            if (transitionOverlay != null)
            {
                transitionOverlay.SetWaveCounter(
                    $"Wave {CurrentWaveInStage + 1}/{stage.waves.Count}");
                transitionOverlay.FlashWaveStart($"Wave {CurrentWaveInStage + 1} Starts", 1.5f);
            }

            float preWaveDelay = (CurrentWaveInStage == 0) ? 1f : runConfig.timeBetweenWaves;
            yield return new WaitForSeconds(preWaveDelay);

            // Spawn the wave
            SetState(RunState.WaveActive);
            yield return SpawnAndWaitForWave(wave, stage);

            // Wave cleared
            OnWaveCleared?.Invoke(CurrentWaveInStage);

            if (debugLog)
                Debug.Log($"[Orchestrator] Wave {CurrentWaveInStage + 1}/{stage.waves.Count} cleared!");

            // Breathing room so the last kill lands before any UI pops in.
            if (pauseAfterLastKill > 0f)
                yield return new WaitForSeconds(pauseAfterLastKill);

            // Augment selection after every Nth wave (if configured)
            if (enableAugmentSelection && augmentsMenu != null && runConfig.augmentEveryNWaves > 0)
            {
                int waveNum = CurrentWaveInStage + 1; // 1-based
                if (waveNum % runConfig.augmentEveryNWaves == 0)
                {
                    yield return ShowAugmentSelection($"wave {waveNum}");
                }
            }
        }

        //  3. STAGE BOSS 
        if (stage.hasStageBoss && stage.stageBossPrefab != null)
        {
            SetState(RunState.StageBoss);
            OnBossSpawned?.Invoke(stage);

            if (debugLog)
                Debug.Log($"[Orchestrator] STAGE BOSS spawning: {stage.stageBossPrefab.name}");

            // Update counter and flash
            if (transitionOverlay != null)
            {
                transitionOverlay.SetWaveCounter("BOSS");
                transitionOverlay.FlashWaveStart("BOSS", 1.5f);
            }

            SpawnBoss(stage.stageBossPrefab);
            yield return WaitForBossDead();

            if (CurrentState == RunState.GameOver) yield break;

            OnBossKilled?.Invoke(CurrentStageIndex);
            if (debugLog)
                Debug.Log($"[Orchestrator] Stage {stage.stageIndex + 1} BOSS defeated!");

            // Clear the "BOSS" counter now that the fight is over
            if (transitionOverlay != null)
                transitionOverlay.SetWaveCounter("");

            // Let boss death VFX play out. Slightly longer than regular kills — it's a boss.
            yield return new WaitForSeconds(pauseAfterLastKill + 0.5f);
        }

        // 4. POST-STAGE CHOICE: Heal+Energy  OR  Augment+Energy 
        if (enablePostStageChoice && postStageChoiceMenu != null)
        {
            yield return ShowPostStageChoice(stage);
        }
        else if (enableAugmentSelection && augmentsMenu != null)
        {
            // Fallback to old behaviour if post-stage choice is disabled
            yield return ShowAugmentSelection($"stage {stage.stageIndex + 1} boss");
        }

        //  5. STAGE COMPLETE 
        SetState(RunState.StageComplete);

        if (debugLog)
            Debug.Log($"[Orchestrator] Stage {stage.stageIndex + 1} complete!");

        yield return new WaitForSeconds(pauseAfterLastKill);
    }



    public bool RewindToCurrentWaveStart()
    {
        var cp = WaveCheckpointService.Instance;
        if (cp == null)
        {
            Debug.LogWarning("[CLOCK] No WaveCheckpointService in scene — cannot rewind.");
            return false;
        }

        bool inFinalBoss = CurrentState == RunState.FinalBoss;
        bool inStageBoss = CurrentState == RunState.StageBoss;
        bool inWave = CurrentState == RunState.WaveActive
                        || CurrentState == RunState.WaveCountdown
                        || CurrentState == RunState.AugmentSelect;

        if (!inFinalBoss && !inStageBoss && !inWave)
        {
            Debug.LogWarning($"[CLOCK] Rewind blocked — run state is {CurrentState}; need a wave or boss fight.");
            return false;
        }

        // If we're rewinding out of the open augment menu, close it so it doesn't
        // linger and so WaitForAugmentMenuClosed's poll exits. JumpToWave / the
        // final-boss restart restore Time.timeScale (the menu sets it to 0).
        if (CurrentState == RunState.AugmentSelect)
        {
            // Close EVERY player's menu so the wait-loop can exit on a rewind.
            if (augmentsMenus != null)
                foreach (var m in augmentsMenus)
                    if (m != null && m.augmentsMenu != null) m.augmentsMenu.SetActive(false);
                    else if (augmentsMenu != null && augmentsMenu.augmentsMenu != null)
                        augmentsMenu.augmentsMenu.SetActive(false);
        }

        // FINAL BOSS: rewind to the start of the final boss fight only (the final
        // boss is not part of a stage, so we re-run just it — not the last stage).
        if (inFinalBoss)
        {
            if (cp.FinalBossSnapshot == null)
            {
                Debug.LogWarning("[CLOCK] Rewind blocked — no final-boss snapshot available yet.");
                return false;
            }
            Debug.Log("[CLOCK] Rewinding to the start of the FINAL BOSS fight.");
            cp.RestoreSnapshot(cp.FinalBossSnapshot);
            RestartFinalBoss();
            return true;
        }

        // STAGE BOSS: rewind to the start of the stage. WAVE: rewind to the wave start.
        RunSnapshot snap = inStageBoss ? cp.StageStartSnapshot : cp.CurrentSnapshot;
        if (snap == null)
        {
            Debug.LogWarning($"[CLOCK] Rewind blocked — no {(inStageBoss ? "stage-start" : "wave")} snapshot available yet.");
            return false;
        }

        //Debug.Log($"[CLOCK] Rewinding to stage {snap.stageIndex} wave {snap.waveIndex} " +
        //          $"(from state {CurrentState}{(inStageBoss ? ", boss → stage start" : "")}).");

        cp.RestoreSnapshot(snap);
        JumpToWave(snap.stageIndex, snap.waveIndex);
        return true;
    }

    // Stop the run loop and re-run ONLY the final boss (used by a final-boss rewind).
    // After the final boss is beaten again, the run finishes to Victory as normal.
    private void RestartFinalBoss()
    {
        if (runCoroutine != null) StopCoroutine(runCoroutine);
        Time.timeScale = 1f;
        enemiesAlive = 0;
        currentBossInstance = null;
        runCoroutine = StartCoroutine(ResumeFinalBoss());
    }

    private IEnumerator ResumeFinalBoss()
    {
        yield return RunFinalBoss();
        if (CurrentState == RunState.GameOver) yield break;

        // Mirror FinishRun's victory tail (RunFinalBoss only runs the fight itself).
        Time.timeScale = 1f;
        if (enableTransitions && transitionOverlay != null)
        {
            yield return new WaitForSecondsRealtime(1f);
            yield return transitionOverlay.FadeOut(0.8f);
            yield return transitionOverlay.ShowMessage("VICTORY", "", 3f);
        }
        SetState(RunState.Victory);
        OnVictory?.Invoke();
        RunPersistence.Instance?.OnSaveConsumed();
        Debug.Log("[Orchestrator] ═══ VICTORY! ═══");
    }

    public void JumpToWave(int stageIndex, int startWaveIndex)
    {
        if (currentRunPlan == null || stageIndex < 0 || stageIndex >= currentRunPlan.Count) return;
        if (runCoroutine != null) StopCoroutine(runCoroutine);
        Time.timeScale = 1f;
        enemiesAlive = 0;
        currentBossInstance = null;
        CurrentStageIndex = stageIndex;
        runCoroutine = StartCoroutine(ResumeRunLoop(stageIndex, startWaveIndex));
    }

    private IEnumerator ResumeRunLoop(int stageIndex, int startWaveIndex)
    {
        yield return RunStage(currentRunPlan[stageIndex], startWaveIndex, skipIntro: true);
        if (CurrentState == RunState.GameOver) yield break;
        for (CurrentStageIndex = stageIndex + 1; CurrentStageIndex < currentRunPlan.Count; CurrentStageIndex++)
        {
            yield return RunStage(currentRunPlan[CurrentStageIndex]);
            if (CurrentState == RunState.GameOver) yield break;
        }
        yield return FinishRun();
    }

    public bool TryResumeSavedRun()
    {
        var p = RunPersistence.Instance;
        if (p == null || !p.HasSave) return false;
        if (!p.TryLoad(out var data)) return false;
        if (data.saveVersion < 2)
        {
            Debug.LogWarning("[Orchestrator] Saved run is from an older version — discarding it and starting fresh.");
            p.DeleteSave();
            return false;
        }
        try
        {
            UnityEngine.Random.InitState(data.runSeed);
            currentRunPlan = GenerateRunPlan();

            // Validate the save against the freshly generated plan. A plan that no
            // longer contains the saved stage/wave (RunConfig changed, corrupt file)
            // must NOT resume — that path skips the intro and leaves a black screen.
            if (currentRunPlan == null
                || data.stageIndex < 0 || data.stageIndex >= currentRunPlan.Count
                || data.waveIndex < 0 || data.waveIndex >= currentRunPlan[data.stageIndex].waves.Count)
            {
                Debug.LogWarning("[Orchestrator] Saved run is incompatible with the current RunConfig — discarding it and starting fresh.");
                p.DeleteSave();
                return false;
            }

            p.AdoptLoadedRun(data);


            // Clean per-player static slate before replaying saved augments —
            // each ApplyAugment re-sets the right player's values.
            CooldownModifier.Reset();
            ParryUpgrades.ResetAll();
            ProjectileParry.Reset();


            if (AugmentRegistry.Instance != null)
                foreach (var a in data.augments)
                {
                    PlayerStats chooser = null;
                    var pr = PlayerRegistry.Instance != null ? PlayerRegistry.Instance.Get(a.playerIndex) : null;
                    if (pr != null) chooser = pr.Stats;
                    AugmentRegistry.Instance.ApplyAugment(a.id, a.rarity, chooser);
                }

            // Towers can't be rebuilt yet — slots don't exist until the stage
            // layout is applied inside RunStage. Defer to there.
            _pendingTowerRestore = data;
            p.RestoreAbsolutes(data);  // player/core/economy — applied AFTER replay

            p.OnSaveConsumed();
            JumpToWave(data.stageIndex, data.waveIndex);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Orchestrator] Resume failed ({e.Message}) — discarding save and starting fresh.");
            p.DeleteSave();
            return false;
        }
    }

    // WAVE SPAWNING (uses your existing WaveSpawner)
    // Spawns a wave and waits until all enemies are dead.
    // Uses WaveSpawner's existing SpawnEnemy logic but driven by the orchestrator.
    private IEnumerator SpawnAndWaitForWave(WaveData wave, StageData stage)
    {
        // Extra delay before wave (from WaveData)
        if (wave.extraDelayBeforeStart > 0)
            yield return new WaitForSeconds(wave.extraDelayBeforeStart);

        // Show wave direction indicators
        waveSpawner.ShowWaveIndicatorsPublic(wave.spawnDirections);

        // Build the list of enemies to spawn
        List<GameObject> enemyPrefabsToSpawn = new List<GameObject>();
        if (wave.enemies != null)
        {
            foreach (var group in wave.enemies)
            {
                if (group == null || group.enemyPrefab == null || group.count <= 0)
                    continue;

                int modifiedCount = Mathf.Max(1,
                    Mathf.RoundToInt(group.count * stage.enemyCountMultiplier));

                for (int i = 0; i < modifiedCount; i++)
                    enemyPrefabsToSpawn.Add(group.enemyPrefab);
            }
        }

        // Shuffle
        Shuffle(enemyPrefabsToSpawn);

        // Track enemies
        enemiesAlive += enemyPrefabsToSpawn.Count;

        // Switch music to intense
        if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
        {
            AudioManager.instance.EnsureMusicReady();
            AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Intense);
        }

        // Pick a single direction if oneDirectionForAllEnemies
        SpawnDirection chosenDir = SpawnDirection.Top;
        if (wave.oneDirectionForAllEnemies && wave.spawnDirections != null && wave.spawnDirections.Count > 0)
            chosenDir = wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

        // Spawn each enemy with delay
        foreach (var prefab in enemyPrefabsToSpawn)
        {
            SpawnDirection dir;
            if (wave.spawnDirections == null || wave.spawnDirections.Count == 0)
                dir = chosenDir;
            else
                dir = wave.oneDirectionForAllEnemies
                    ? chosenDir
                    : wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

            waveSpawner.SpawnEnemyPublic(prefab, dir);

            // Apply stage scaling to spawn delay
            float baseDelay = UnityEngine.Random.Range(wave.minSpawnDelay, wave.maxSpawnDelay);
            float scaledDelay = baseDelay * stage.spawnDelayMultiplier;
            yield return new WaitForSeconds(Mathf.Max(0.2f, scaledDelay));
        }

        // Wait until all enemies are dead
        yield return WaitForAllEnemiesDead();

        // Switch music to calm
        if (AudioManager.instance != null && AudioManager.instance.musicEnabled)
            AudioManager.instance.SetMusicSection(AudioManager.MusicSection.Calm);
    }

    private IEnumerator WaitForAllEnemiesDead()
    {
        while (true)
        {
            if (CurrentState == RunState.GameOver) yield break;

            // Cross-check the counter against the actual scene state
            int aliveInScene = CountLivingEnemiesInScene(excludeBoss: false);

            if (enemiesAlive <= 0 && aliveInScene <= 0)
                yield break;

            yield return null;
        }
    }

    // Counts EnemyStats components in the scene that are still alive
    // (component enabled + GameObject active). Used as a scene-state fallback that
    // is independent of the orchestrator's internal counters and works for ANY
    // enemy regardless of spawn path. Optionally excludes the boss instance.
    private int CountLivingEnemiesInScene(bool excludeBoss)
    {
        int alive = 0;
        var allEnemies = UnityEngine.Object.FindObjectsByType<EnemyStats>(
            FindObjectsSortMode.None);
        foreach (var es in allEnemies)
        {
            if (es == null) continue;
            if (!es.enabled) continue;
            if (!es.gameObject.activeInHierarchy) continue;
            if (excludeBoss && currentBossInstance != null
                && es.gameObject == currentBossInstance) continue;
            // Skip ambient non-combat enemies (gremlins) — see comment above.
            if (es.GetComponent<GremlinController>() != null) continue;
            alive++;
        }
        return alive;
    }

    //  BOSS SPAWNING
    /// Shows the post-stage choice menu: Heal All + bonus energy  OR  Augment + small energy.
    private IEnumerator ShowPostStageChoice(StageData stage)
    {
        SetState(RunState.AugmentSelect); // reuse existing state — gameplay is paused either way

        if (debugLog)
            Debug.Log($"[Orchestrator] Post-stage choice menu: stage {stage.stageIndex + 1}");

        // Scale post-stage energy rewards with stage number
        int scaledHealBonus = StageEnergyScaling.HealChoiceEnergy(runConfig, stage.stageIndex, healChoiceEnergyBonus);
        int scaledAugmentBonus = StageEnergyScaling.AugmentChoiceEnergy(runConfig, stage.stageIndex, augmentChoiceEnergyBonus);

        if (debugLog)
            Debug.Log($"[Orchestrator] Post-stage rewards (stage {stage.stageIndex + 1}): " +
                      $"Heal={scaledHealBonus} Augment={scaledAugmentBonus}");

        // PHASE 8: prefer the prefab-based reward screen when its prefab is present.
        // Single player -> one full-screen instance. Co-op -> one half-size instance per player,
        // each choosing independently; we then apply each player's reward to that player.
        if (stageClearMenu != null && stageClearMenu.IsAvailable)
        {
            int playerCount = Mathf.Max(1, PlayerRegistry.Count);
            StageClearScreenMenu.Choice[] choices = null;
            yield return StartCoroutine(
                stageClearMenu.ShowChoices(
                    scaledHealBonus,
                    scaledAugmentBonus,
                    c => choices = c
                )
            );

            if (choices == null || choices.Length == 0)
            {
                // Defensive: treat as everyone healed so the run can't stall.
                choices = new StageClearScreenMenu.Choice[playerCount];
                for (int i = 0; i < choices.Length; i++) choices[i] = StageClearScreenMenu.Choice.Heal;
            }

            // Collect who picked what.
            bool anyHeal = false;
            var augmentPlayers = new System.Collections.Generic.List<int>();
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == StageClearScreenMenu.Choice.Heal) anyHeal = true;
                else if (choices[i] == StageClearScreenMenu.Choice.Augment) augmentPlayers.Add(i);
            }

            // Shared world heal happens once if ANYONE chose restore (core + towers are shared).
            if (anyHeal) HealSharedCoreAndTowers();

            // Per-player application: heal that player's own health + grant the energy bonus.
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == StageClearScreenMenu.Choice.Heal)
                {
                    if (debugLog) Debug.Log($"[Orchestrator] P{i} picked RESTORE + energy bonus");
                    HealPlayerOnly(i);
                    GiveEnergyBonus(scaledHealBonus);
                }
                else if (choices[i] == StageClearScreenMenu.Choice.Augment)
                {
                    if (debugLog) Debug.Log($"[Orchestrator] P{i} picked EMPOWER + small energy");
                    GiveEnergyBonus(scaledAugmentBonus);
                }
            }

            // Augment menus for the players who chose Empower.
            if (augmentPlayers.Count > 0 && enableAugmentSelection)
            {
                if (PlayerRegistry.Count > 1)
                    yield return ShowAugmentSelectionForPlayers(augmentPlayers);
                else if (augmentsMenu != null)
                    yield return ShowAugmentSelection($"stage {stage.stageIndex + 1} post-stage choice");
            }

            yield break;
        }

        // LEGACY fallback (prefab missing): original single procedural menu.
        PostStageChoiceMenu.Choice chosen = PostStageChoiceMenu.Choice.None;
        yield return StartCoroutine(
            postStageChoiceMenu.ShowChoice(
                scaledHealBonus,
                scaledAugmentBonus,
                c => chosen = c
            )
        );

        if (chosen == PostStageChoiceMenu.Choice.Heal)
        {
            if (debugLog) Debug.Log("[Orchestrator] Player picked HEAL ALL + energy bonus");
            HealEverything();
            GiveEnergyBonus(scaledHealBonus);
        }
        else if (chosen == PostStageChoiceMenu.Choice.Augment)
        {
            if (debugLog) Debug.Log("[Orchestrator] Player picked AUGMENT + small energy");
            GiveEnergyBonus(scaledAugmentBonus);

            // Only show augment popup if it's actually configured
            if (enableAugmentSelection && augmentsMenu != null)
            {
                yield return ShowAugmentSelection($"stage {stage.stageIndex + 1} post-stage choice");
            }
        }
    }

    // Fully heal the Core, all Towers, and the Player.
    private void HealEverything()
    {
        // Core — refill to max energy (which is effectively its HP)
        var core = FindFirstObjectByType<CentralCore>();
        if (core != null)
        {
            float missing = core.maxEnergy - core.currentEnergy;
            if (missing > 0f) core.SupplyEnergy(missing);
        }

        // Towers — refill each tower's energy
        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        if (towers != null)
        {
            foreach (var t in towers)
            {
                if (t == null) continue;
                float missing = t.maxEnergy - t.currentEnergy;
                if (missing > 0f) t.SupplyEnergy(missing);
            }
        }

        // Player
        var player = FindFirstObjectByType<PlayerStats>();
        if (player != null)
        {
            float missing = player.maxHealth - player.currentHealth;
            if (missing > 0f) player.Heal(missing);
        }

        if (debugLog)
            Debug.Log($"[Orchestrator] Healed: core={(core != null)}, towers={(towers?.Length ?? 0)}, player={(player != null)}");
    }

    // Heal the SHARED world objects only — core + all towers. Called once per post-stage choice
    // if any player chose Restore (these are shared in co-op, so healing them per-player would be
    // redundant). Mirrors the core/tower half of HealEverything.
    private void HealSharedCoreAndTowers()
    {
        var core = FindFirstObjectByType<CentralCore>();
        if (core != null)
        {
            float missing = core.maxEnergy - core.currentEnergy;
            if (missing > 0f) core.SupplyEnergy(missing);
        }

        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        if (towers != null)
        {
            foreach (var t in towers)
            {
                if (t == null) continue;
                float missing = t.maxEnergy - t.currentEnergy;
                if (missing > 0f) t.SupplyEnergy(missing);
            }
        }

        if (debugLog)
            Debug.Log($"[Orchestrator] Healed shared core+towers (towers={(towers?.Length ?? 0)}).");
    }

    // Heal one specific player's health. In co-op the player is resolved from the registry; in
    // single player (registry empty) it falls back to the lone PlayerStats — identical to the
    // player half of HealEverything.
    private void HealPlayerOnly(int playerIndex)
    {
        PlayerStats player = null;
        if (PlayerRegistry.Count > 0)
        {
            var pref = PlayerRegistry.Instance.Get(playerIndex);
            player = pref != null ? pref.Stats : null;
        }
        else
        {
            player = FindFirstObjectByType<PlayerStats>();
        }

        if (player != null)
        {
            float missing = player.maxHealth - player.currentHealth;
            if (missing > 0f) player.Heal(missing);
            if (debugLog) Debug.Log($"[Orchestrator] Healed P{playerIndex} health.");
        }
    }

    // Co-op: open the augment menu for ONLY the players who chose Empower, and wait until those
    // specific menus close. The orchestrator owns the freeze (as in ShowAugmentSelection) so the
    // two menus don't fight over Time.timeScale.
    private IEnumerator ShowAugmentSelectionForPlayers(System.Collections.Generic.List<int> playerIndices)
    {
        SetState(RunState.AugmentSelect);
        if (playerIndices == null || playerIndices.Count == 0) yield break;

        float prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Cursor.visible = true;
        PlayerAttack.SetAllSuppressed(true);

        // Resolve each chosen player's bound menu and open it.
        var opened = new System.Collections.Generic.List<AugmentsMenu>();
        foreach (int idx in playerIndices)
        {
            var menu = FindAugmentMenuForPlayer(idx);
            if (menu == null)
            {
                if (debugLog) Debug.LogWarning($"[Orchestrator] No AugmentsMenu bound to player {idx}; skipping its augment.");
                continue;
            }
            menu.ResetRerolls();
            menu.ActivateAugments();
            opened.Add(menu);
        }

        // Wait until every opened menu has closed.
        bool anyOpen = true;
        while (anyOpen)
        {
            anyOpen = false;
            foreach (var m in opened)
                if (m != null && m.augmentsMenu != null && m.augmentsMenu.activeSelf) { anyOpen = true; break; }
            if (anyOpen) yield return new WaitForSecondsRealtime(0.1f);
        }

        Time.timeScale = prevTimeScale;
        Cursor.visible = false;
        PlayerAttack.SetAllSuppressed(false);

        if (debugLog) Debug.Log("[Orchestrator] Per-player augment selection complete.");
    }

    // Find the AugmentsMenu whose boundPlayerIndex matches (Phase 6 per-player menus).
    private AugmentsMenu FindAugmentMenuForPlayer(int playerIndex)
    {
        if (augmentsMenus != null)
            foreach (var m in augmentsMenus)
                if (m != null && m.boundPlayerIndex == playerIndex) return m;
        return null;
    }

    /// Give the player bonus energy through the EnergyManager.
    private void GiveEnergyBonus(int amount)
    {
        if (amount <= 0) return;
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.GivePlayerEnergy(amount);
            if (debugLog) Debug.Log($"[Orchestrator] Gave player +{amount} energy");
        }
        else if (debugLog)
        {
            Debug.LogWarning("[Orchestrator] EnergyManager.Instance is null — energy bonus skipped");
        }
    }

    /// Shows the augment selection popup and waits for the player to pick.
    /// Used after waves (every Nth) and after stage bosses.
    private IEnumerator ShowAugmentSelection(string reason)
    {
        SetState(RunState.AugmentSelect);

        if (debugLog)
            Debug.Log($"[Orchestrator] Augment selection after {reason}...");

        // Co-op: the orchestrator owns the freeze so two menus don't fight over
        // Time.timeScale, and it opens every player's menu and waits for ALL.
        bool coop = PlayerRegistry.Count > 1;
        float prevTimeScale = Time.timeScale;
        if (coop)
        {
            Time.timeScale = 0f;
            Cursor.visible = true;
            PlayerAttack.SetAllSuppressed(true);
        }

        var menus = (augmentsMenus != null && augmentsMenus.Length > 0)
            ? augmentsMenus
            : (augmentsMenu != null ? new[] { augmentsMenu } : new AugmentsMenu[0]);

        foreach (var m in menus)
        {
            if (m == null) continue;
            m.ResetRerolls();
            m.ActivateAugments();
        }

        yield return WaitForAugmentMenuClosed();

        if (coop)
        {
            Time.timeScale = prevTimeScale;
            Cursor.visible = false;
            PlayerAttack.SetAllSuppressed(false);
        }

        if (debugLog)
            Debug.Log($"[Orchestrator] Augment selected, continuing...");
    }

    // True while ANY player's augment menu is still open.
    private bool AnyAugmentMenuOpen()
    {
        if (augmentsMenus != null)
            foreach (var m in augmentsMenus)
                if (m != null && m.augmentsMenu != null && m.augmentsMenu.activeSelf)
                    return true;
        return augmentsMenu != null && augmentsMenu.augmentsMenu != null
               && augmentsMenu.augmentsMenu.activeSelf;
    }

    // Waits until the AugmentsMenu is closed (player made a selection).
    // Uses WaitForSecondsRealtime because Time.timeScale is 0 while the menu is open.
    private IEnumerator WaitForAugmentMenuClosed()
    {
        // The augment menu sets Time.timeScale = 0 when open.
        // Normal yield return null won't advance when timeScale is 0.
        // So we poll using realtime waits.
        while (AnyAugmentMenuOpen())
        {
            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    private void SpawnBoss(GameObject bossPrefab)
    {
        // Increment orchestrator's enemiesAlive.
        // WaveSpawner.SpawnEnemy increments the SPAWNER's own enemiesAlive (separate variable).
        // WaveEnemy.OnDisable() decrements the ORCHESTRATOR's enemiesAlive when the boss dies.
        enemiesAlive++;

        SpawnDirection dir = (SpawnDirection)UnityEngine.Random.Range(0, 4);

        // Record how many WaveEnemy markers exist BEFORE the boss spawns, so we can
        // identify the new GameObject the spawner creates and track IT specifically.
        var beforeSpawn = new HashSet<WaveEnemy>(
            UnityEngine.Object.FindObjectsByType<WaveEnemy>(FindObjectsSortMode.None));

        waveSpawner.SpawnEnemyPublic(bossPrefab, dir);

        // Find the WaveEnemy that wasn't in the scene before the spawn — that's our boss.
        currentBossInstance = null;
        foreach (var we in UnityEngine.Object.FindObjectsByType<WaveEnemy>(FindObjectsSortMode.None))
        {
            if (!beforeSpawn.Contains(we))
            {
                currentBossInstance = we.gameObject;
                break;
            }
        }

        if (currentBossInstance == null && debugLog)
            Debug.LogWarning("[Orchestrator] SpawnBoss: could not locate the spawned boss GameObject. " +
                             "Falling back to counter-only wait — may exit early if other enemies linger.");
    }

    // Waits until the specific boss instance is dead AND no other enemies are alive.
    private IEnumerator WaitForBossDead()
    {
        while (true)
        {
            if (CurrentState == RunState.GameOver) yield break;

            // Is the boss itself still alive?
            bool bossStillAlive = currentBossInstance != null
                && currentBossInstance.activeInHierarchy;
            if (bossStillAlive)
            {
                var we = currentBossInstance.GetComponent<WaveEnemy>();
                // OnDisable disables the MonoBehaviour — when that happens, we treat the boss as dead.
                bossStillAlive = (we != null && we.enabled);
            }

            // Cross-check the scene for ANY other living enemy
            int otherEnemiesAlive = CountLivingEnemiesInScene(excludeBoss: true);
            if (!bossStillAlive && otherEnemiesAlive == 0)
            {
                currentBossInstance = null;
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator RunFinalBoss()
    {
        SetState(RunState.FinalBoss);

        // Defensive: a previous menu may have left Time.timeScale at 0. Force gameplay
        // timescale back to 1 here so all the WaitForSeconds calls below actually advance.
        Time.timeScale = 1f;

        if (debugLog)
            Debug.Log($"[Orchestrator] ═══ FINAL BOSS ═══ " +
                      $"({(runConfig.finalBossPrefab != null ? runConfig.finalBossPrefab.name : "none")})");

        // Dramatic intro
        if (enableTransitions && transitionOverlay != null)
        {
            yield return transitionOverlay.FadeOut(0.4f);
            yield return transitionOverlay.ShowMessage("FINAL BOSS", "", 2f);
            yield return transitionOverlay.FadeIn(0.7f);
        }

        // Update counter for final boss
        if (transitionOverlay != null)
            transitionOverlay.SetWaveCounter("FINAL BOSS");

        OnBossSpawned?.Invoke(null);

        yield return new WaitForSecondsRealtime(1f);

        SpawnBoss(runConfig.finalBossPrefab);

        // Snapshot the start of the final boss fight so the clock can rewind to
        // exactly here (re-running only the final boss, not the last stage).
        WaveCheckpointService.Instance?.CaptureFinalBossSnapshot();

        yield return WaitForBossDead();

        if (debugLog)
            Debug.Log("[Orchestrator] FINAL BOSS defeated!");

        // Clear counter
        if (transitionOverlay != null)
            transitionOverlay.SetWaveCounter("");

        // Delay before victory so death VFX can play out
        yield return new WaitForSecondsRealtime(2f);
    }

    private void ApplyBiome(StageData stage)
    {
        if (biomeManager == null)
        {
            Debug.LogWarning("[Orchestrator] No BiomeManager found — skipping biome switch.");
            return;
        }

        // Night must be set BEFORE the biome builds. enableNightMode is read while
        // the biome generates (ApplyNightOverlay) and is NOT overridden by any biome
        // default, so setting it first makes night apply in a single pass — fixing the
        // night overlay that used to flash onto a non-night stage for one frame.
        biomeManager.SetNightMode(stage.nightMode);

        // Switch the biome (background, overlays, obstacles, etc.).
        biomeManager.SetBiome(stage.biome);

        // Fog / rain / snow / balloons are applied AFTER SetBiome on purpose: with
        // applyBiomeFogDefaults / applyBiomeWeatherDefaults ON (both default ON),
        // ApplyBiome() overwrites these with the biome's own defaults, so the
        // per-stage rolls must be set afterwards to win. Update() applies them next
        // frame (a brief, harmless fog/weather settle — far less visible than the
        // night blackout flash this leaves fixed).
        biomeManager.SetFog(stage.fogEnabled);
        biomeManager.enableRain = stage.rainEnabled;
        biomeManager.enableSnow = stage.snowEnabled;
        biomeManager.enableNightBalloons = stage.balloonsEnabled;

        // Apply map layout (null = use TowerDefenseMap's own default rings)
        var map = UnityEngine.Object.FindFirstObjectByType<TowerDefenseMap>();
        if (map != null)
        {
            map.ApplyLayout(stage.layout);
        }

        if (debugLog)
            Debug.Log($"[Orchestrator] Biome applied: {stage.biome}" +
                      $"{(stage.layout != null ? $" +Layout:{stage.layout.layoutName}" : "")}" +
                      $"{(stage.nightMode ? " +Night" : "")}" +
                      $"{(stage.balloonsEnabled ? " +Balloons" : "")}" +
                      $"{(stage.fogEnabled ? " +Fog" : "")}" +
                      $"{(stage.rainEnabled ? " +Rain" : "")}" +
                      $"{(stage.snowEnabled ? " +Snow" : "")}");
    }

    //  RUN PLAN GENERATION
    // Generates the full run plan: picks biomes, rolls modifiers, selects waves from your WaveConfig pool.
    private List<StageData> GenerateRunPlan()
    {
        var plan = new List<StageData>();
        var biomeSequence = PickBiomeSequence();

        // Reset the run-wide wave cursor so waves are consumed in order from the
        // start of the pool every time a plan is generated.
        wavePickCursor = 0;

        // Build the wave deck (flatten pool, shuffle if randomizeWaves) and the boss
        // order (shuffled draw if randomizeBosses) once per run, using the seeded RNG.
        BuildWaveDeck();
        bossSequence = runConfig.randomizeBosses ? PickBossSequence() : null;

        // Pick a single run-wide layout if changeLayoutPerStage is false
        usedLayouts.Clear();
        runWideLayout = null;
        var library = runConfig.mapLayoutLibrary;
        if (library != null && !library.changeLayoutPerStage)
        {
            runWideLayout = library.PickRandom(null);
            if (runWideLayout != null)
                Debug.Log($"[Orchestrator] Run-wide layout: {runWideLayout.layoutName}");
        }

        for (int i = 0; i < runConfig.stageCount; i++)
        {
            BiomeType biome = biomeSequence[i];

            // Pick a layout for this stage
            MapLayoutDefinition layout = runWideLayout; // may be null
            if (library != null && library.changeLayoutPerStage)
            {
                layout = library.PickRandom(usedLayouts);
                if (layout != null) usedLayouts.Add(layout);
            }

            var stage = new StageData
            {
                stageIndex = i,
                biome = biome,
                layout = layout,

                // Roll weather (with sensible biome-specific overrides)
                nightMode = UnityEngine.Random.value < runConfig.nightModeChance,
                fogEnabled = UnityEngine.Random.value < runConfig.fogChance,
                rainEnabled = UnityEngine.Random.value < runConfig.rainChance
                              && biome != BiomeType.Desert,
                snowEnabled = UnityEngine.Random.value < runConfig.snowChance
                              || biome == BiomeType.Snow,
                balloonsEnabled = UnityEngine.Random.value < runConfig.nightBalloonChance,

                // Difficulty scaling
                enemyCountMultiplier = Mathf.Pow(runConfig.enemyCountScalePerStage, i),
                spawnDelayMultiplier = Mathf.Pow(runConfig.spawnDelayScalePerStage, i),
                enemyHealthMultiplier = Mathf.Pow(runConfig.enemyHealthScalePerStage, i),
                enemyDamageMultiplier = Mathf.Pow(runConfig.enemyDamageScalePerStage, i),

                waves = runConfig.useProceduralWaves
                    ? GenerateProceduralWaves(i)
                    : PickWavesForStage(i),
            };

            // Resolve which boss this stage spawns:
            //   randomized draw (bossSequence) → per-stage list/fallback (GetStageBoss) → none.
            stage.stageBossPrefab = (bossSequence != null && i < bossSequence.Count && bossSequence[i] != null)
                ? bossSequence[i]
                : runConfig.GetStageBoss(i);
            stage.hasStageBoss = (stage.stageBossPrefab != null);

            plan.Add(stage);
        }

        return plan;
    }

    // Picks a random non-repeating sequence of biomes from the pool.
    private List<BiomeType> PickBiomeSequence()
    {
        var pool = new List<BiomeType>(runConfig.availableBiomes);
        var sequence = new List<BiomeType>();

        for (int i = 0; i < runConfig.stageCount; i++)
        {
            if (pool.Count == 0)
            {
                // Refill if we need more stages than available biomes
                pool = new List<BiomeType>(runConfig.availableBiomes);
            }

            int pick = UnityEngine.Random.Range(0, pool.Count);
            sequence.Add(pool[pick]);

            if (!runConfig.allowRepeatBiomes)
                pool.RemoveAt(pick);
        }

        return sequence;
    }

    // Flattens the WaveConfig pool into one deck for the run, shuffled if randomizeWaves.
    // Built once per run in GenerateRunPlan so the shuffle order is stable for a seed.
    private void BuildWaveDeck()
    {
        runWaveDeck = new List<WaveData>();
        if (runConfig.waveConfigPool != null)
            foreach (var config in runConfig.waveConfigPool)
                if (config != null && config.waves != null)
                    runWaveDeck.AddRange(config.waves);

        if (runConfig.randomizeWaves)
            Shuffle(runWaveDeck);
    }

    // Deals the next `wavesPerStage` waves from the run deck using a running cursor.
    //   - randomizeWaves OFF → pool order (Stage 1 = Wave 0, Stage 2 = Wave 1, …)
    //   - randomizeWaves ON  → the deck was shuffled, so a different (seeded) order each run
    // Wraps around if the run needs more waves than the deck holds.
    private List<WaveData> PickWavesForStage(int stageIndex)
    {
        var result = new List<WaveData>();
        int needed = runConfig.wavesPerStage;

        if (runWaveDeck == null || runWaveDeck.Count == 0)
        {
            Debug.LogWarning("[Orchestrator] Wave deck is empty — no WaveConfigs/ waves in the pool.");
            return result;
        }

        for (int i = 0; i < needed; i++)
        {
            int index = wavePickCursor % runWaveDeck.Count;
            result.Add(runWaveDeck[index]);
            wavePickCursor++;
        }

        return result;
    }

    // Builds waves for a stage by randomly sampling the Enemy Pool (procedural mode).
    // Enemies are emitted at BASE counts; per-stage scaling is applied later in the
    // spawn path (enemyCountMultiplier), so this stays stage-agnostic except for the
    // minStageIndex gate that controls which enemies are eligible.
    private List<WaveData> GenerateProceduralWaves(int stageIndex)
    {
        var result = new List<WaveData>();

        // Collect enemies eligible for this stage.
        var eligible = new List<EnemyPoolEntry>();
        float totalWeight = 0f;
        if (runConfig.enemyPool != null)
            foreach (var e in runConfig.enemyPool)
                if (e != null && e.enemyPrefab != null && e.weight > 0f && stageIndex >= e.minStageIndex)
                {
                    eligible.Add(e);
                    totalWeight += e.weight;
                }

        if (eligible.Count == 0)
        {
            Debug.LogWarning($"[Orchestrator] Procedural waves ON but no eligible enemies for stage " +
                             $"{stageIndex + 1}. Check Enemy Pool (prefab / weight / minStageIndex).");
            return result;
        }

        int waves = Mathf.Max(1, runConfig.wavesPerStage);
        for (int w = 0; w < waves; w++)
        {
            // Sample baseEnemiesPerWave picks, tally duplicates into grouped counts.
            var tally = new Dictionary<GameObject, int>();
            for (int n = 0; n < runConfig.baseEnemiesPerWave; n++)
            {
                var prefab = WeightedPick(eligible, totalWeight);
                tally.TryGetValue(prefab, out int c);
                tally[prefab] = c + 1;
            }

            var groups = new List<EnemyGroup>();
            foreach (var kv in tally)
                groups.Add(new EnemyGroup { enemyPrefab = kv.Key, count = kv.Value });

            result.Add(new WaveData
            {
                waveNumber = w,
                extraDelayBeforeStart = 0f,
                spawnDirections = new List<SpawnDirection>
                {
                    SpawnDirection.Top, SpawnDirection.Bottom,
                    SpawnDirection.Left, SpawnDirection.Right
                },
                oneDirectionForAllEnemies = false,
                minSpawnDelay = runConfig.proceduralMinSpawnDelay,
                maxSpawnDelay = runConfig.proceduralMaxSpawnDelay,
                enemies = groups
            });
        }

        return result;
    }

    // Weighted random pick from the eligible enemy entries.
    private GameObject WeightedPick(List<EnemyPoolEntry> eligible, float totalWeight)
    {
        float r = UnityEngine.Random.value * totalWeight;
        foreach (var e in eligible)
        {
            r -= e.weight;
            if (r <= 0f) return e.enemyPrefab;
        }
        return eligible[eligible.Count - 1].enemyPrefab; // float-rounding fallback
    }

    // Produces a per-stage boss order by drawing from stageBossPrefabs without repeats
    // (reshuffling a fresh bag when the pool is smaller than the stage count). Returns an
    // empty list if the pool is empty, so the caller falls back to the fixed mapping.
    private List<GameObject> PickBossSequence()
    {
        var sequence = new List<GameObject>();

        var pool = new List<GameObject>();
        if (runConfig.stageBossPrefabs != null)
            foreach (var b in runConfig.stageBossPrefabs)
                if (b != null) pool.Add(b);

        if (pool.Count == 0) return sequence; // caller uses GetStageBoss() fallback

        var bag = new List<GameObject>();
        for (int i = 0; i < runConfig.stageCount; i++)
        {
            if (bag.Count == 0) { bag.AddRange(pool); Shuffle(bag); }
            sequence.Add(bag[0]);
            bag.RemoveAt(0);
        }

        return sequence;
    }

    //  STATE MANAGEMENT
    private void SetState(RunState newState)
    {
        if (CurrentState == newState) return;

        RunState old = CurrentState;
        CurrentState = newState;

        if (debugLog)
            Debug.Log($"[Orchestrator] State: {old} → {newState}");

        OnStateChanged?.Invoke(old, newState);
    }

    //  VALIDATION
    private void ValidateSetup()
    {
        if (runConfig == null)
            Debug.LogError("[Orchestrator] ❌ No RunConfig assigned! Create via Create → Game → Run Config");
        if (biomeManager == null)
            Debug.LogWarning("[Orchestrator] ⚠ No BiomeManager found in scene. Biome switching will be skipped.");
        if (waveSpawner == null)
            Debug.LogError("[Orchestrator] ❌ No WaveSpawner found in scene! Enemy spawning won't work.");
        else if (runConfig != null && (runConfig.waveConfigPool == null || runConfig.waveConfigPool.Count == 0))
            Debug.LogWarning("[Orchestrator] ⚠ RunConfig has no WaveConfigs in pool. " +
                             "Drag your existing WaveConfig.asset into Run Config → Wave Config Pool.");
    }

    //  UTILITY
    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int k = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[k]) = (list[k], list[i]);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}

