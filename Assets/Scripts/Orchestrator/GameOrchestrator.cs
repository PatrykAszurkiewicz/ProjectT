using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


// THE GAME ORCHESTRATOR 
// ║  StartRun()                                                
// ║    │                                                       
// ║    ├── Stage 1 (random biome, e.g. Grass)                  
// ║    │     ├── Wave 1  ─┐                                    
// ║    │     ├── Wave 2   │  wavesPerStage waves               
// ║    │     ├── ...      │  (from your WaveConfig)            
// ║    │     ├── Wave 8  ─┘                                    
// ║    │     └── Stage Boss                                    
// ║    │                                                       
// ║    ├── Stage 2 (random biome, e.g. Desert + Night)         
// ║    │     ├── Wave 1..8                                     
// ║    │     └── Stage Boss                                    
// ║    │                                                       
// ║    ├── Stage 3 (random biome, e.g. Snow + Fog)             
// ║    │     └── ...                                           
// ║    │                                                       
// ║    ├── Stage 4 (random biome, e.g. Wasteland)              
// ║    │     └── ...                                           
// ║    │                                                       
// ║    └── FINAL BOSS                                          
// TESTING:
// - Use the [ContextMenu] options (right-click the component in Inspector)
// - "Start Run" — begins a full run
// - "Skip To Next Stage" — jumps to the next biome stage
// - "Log Run Plan" — prints the entire run plan to Console without starting
// - Check "Auto Start Run" to begin immediately on Play

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

    [Tooltip("Safety cap (seconds, unscaled) for the pre-wave \"screen is actually visible\" gate.\n" +
             "A wave never spawns while the loading/transition cover is up; if the cover somehow\n" +
             "never clears, the wave starts anyway after this long instead of stalling the run.")]
    [Min(1f)]
    public float waveRevealGateSeconds = 20f;

    [Header("═══ SCENE REFERENCES ═══")]
    [Tooltip("Drag your BiomeManager here (or leave empty — will auto-find).")]
    public BiomeManager biomeManager;

    [Tooltip("Drag your WaveSpawner here (or leave empty — will auto-find).")]
    public WaveSpawner waveSpawner;

    [Header("═══ DEBUG ═══")]
    [Tooltip("Print detailed state transitions to Console.")]
    public bool debugLog = false;

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

    [Header(" BOSS NAME FONT ")]
    [Tooltip("Font used for the boss name flash, the boss wave counter and the final-boss " +
             "banner. Drag your Cinzel .ttf straight in here. Leave empty to keep the default " +
             "font. Forwarded to StageTransitionOverlay, which is created at runtime on this " +
             "same GameObject and so has no Inspector of its own to drag onto.")]
    public Font bossNameFont;

    [Tooltip("Tint for boss names. Pale gold by default, which suits Cinzel's engraved look.")]
    public Color bossNameColor = new Color(0.94f, 0.85f, 0.60f, 1f);

    [Tooltip("Supersampling for transition text. 1 = Unity's default, which makes a big\n" +
             "headline like 'Wave 1 Starts' look chunky. 3 rasterises glyphs at 3x and scales\n" +
             "down, which is what keeps a serif face like Cinzel clean. Raise to 4 for 4K.")]
    [Range(1f, 8f)]
    public float uiTextSharpness = 3f;

    [Tooltip("ON: draw the big centre-screen 'Wave N Starts' / boss-name flash with TextMeshPro\n" +
             "instead of legacy UI.Text. TMP renders from a signed-distance field, so the headline\n" +
             "stays razor sharp at any size. Needs a font in the slot above. This is the real fix\n" +
             "for pixelated edges - leave it on.")]
    public bool crispFlashText = true;

    [Tooltip("Thickness of the black rim behind the flash headline, in pixels. The old fixed value\n" +
             "of 3 stamps the glyph four times at whole-pixel offsets, which is itself a big part\n" +
             "of the jagged look. 1-2 is clean, 0 removes it.")]
    [Range(0f, 4f)]
    public float flashOutlineThickness = 1.5f;

    [Tooltip("ON: use the font above for EVERY transition text - the stage/biome banner, the\n" +
             "wave counter, the 'Wave N Starts' flash, the stage counter and the 'Loading'\n" +
             "caption - not just boss names. Only the typeface changes; sizes and colours stay\n" +
             "as they are, and boss names keep their own tint and size on top.")]
    public bool bossNameFontForAllText = false;

    // Auto-found references
    private StageTransitionOverlay transitionOverlay;
    private AugmentsMenu augmentsMenu;
    private AugmentsMenu[] augmentsMenus;   // Phase 6: one per player in co-op

    // Identity token for UIModalStack while an augment menu is open. A plain object
    // (not `this`) so the orchestrator can hold other stack entries independently.
    private readonly object _augmentModal = new object();

    // True only while the orchestrator is actually holding the augment modal layer.
    // Guards against popping a layer we never pushed (see RewindToCurrentWaveStart).
    private bool _augmentModalHeld;
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

    /// <summary>
    /// TRUE once this orchestrator has taken ownership of wave pacing for the scene —
    /// INCLUDING the boot / resume window BEFORE the first stage has started, while
    /// CurrentState is still Idle.
    ///
    /// WHY THIS EXISTS (bug fix): WaveSpawner decided whether it was "orchestrator-driven"
    /// by testing `CurrentState != Idle`. But the state is legitimately Idle from the
    /// moment the gameplay scene loads until the run loop actually begins — which, on the
    /// Continue path, is DeferredResume's wait for players/weapons (up to 15s), and on a
    /// fresh run is any frame where StartRun is deferred or bails. During that window the
    /// spawner fell back to STANDALONE mode, ticked its own countdown and dealt its own
    /// waves from its inspector WaveConfig — behind the black boot cover. That is why
    /// enemies (sometimes already killed by restored towers) existed before the biome had
    /// finished loading and before the player could play.
    ///
    /// Ownership is claimed in Awake, not in StartRun, so the window is closed from frame
    /// zero. It is deliberately NOT claimed when the orchestrator is configured to never
    /// start a run (autoStartRun off and no resume intent) — that setup is a genuine
    /// "standalone spawner with a dormant orchestrator" and keeps its old behaviour.
    /// </summary>
    public bool WillDriveWaves { get; private set; }

    /// <summary>
    /// The check WaveSpawner uses: is some orchestrator responsible for waves right now?
    /// True during the boot/resume window as well as during an in-flight run.
    /// </summary>
    /// <summary>
    /// TRUE while the loading / stage-transition cover is still hiding gameplay.
    /// Read-only pass-through to StageTransitionOverlay.IsCovering, so other systems
    /// (e.g. WaveSpawner's early-spawn tracer) can ask "can the player see the arena
    /// yet?" without needing a reference to the overlay.
    /// </summary>
    public bool ScreenIsCovered =>
        enableTransitions && transitionOverlay != null && transitionOverlay.IsCovering;

    public static bool WavesAreOrchestrated =>
        Instance != null && (Instance.WillDriveWaves || Instance.CurrentState != RunState.Idle);

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

    /// <summary>
    /// Write an autosave at the CURRENT position, but CLAMPED to a real, resumable wave.
    /// During the final boss CurrentStageIndex/CurrentWaveInStage point PAST the last
    /// stage (the plan holds only the stages), so saving them raw makes the continue
    /// screen read "Stage 3 Wave 3" and used to make resume reject the save. Clamping
    /// here keeps the on-disk position valid. Call this for a forced "Exit & Save"
    /// (e.g. from the controller-disconnect overlay) instead of AutoSaveWaveStart with
    /// the raw indices.
    /// </summary>
    public void ForceAutoSave()
    {
        var p = RunPersistence.Instance;
        if (p == null) return;

        // During the final boss, keep the dedicated final-boss save so a forced exit
        // (e.g. controller-disconnect overlay) still resumes straight into the final
        // boss instead of rewinding to the last wave.
        if (CurrentState == RunState.FinalBoss)
        {
            WriteFinalBossAutoSave();
            return;
        }

        int stage = CurrentStageIndex;
        int wave = CurrentWaveInStage;
        if (currentRunPlan != null && currentRunPlan.Count > 0)
        {
            stage = Mathf.Clamp(stage, 0, currentRunPlan.Count - 1);
            int lastWave = Mathf.Max(0, currentRunPlan[stage].waves.Count - 1);
            wave = Mathf.Clamp(wave, 0, lastWave);
        }
        p.AutoSaveWaveStart(stage, wave);
    }

    // Write the start-of-final-boss checkpoint. stageIndex/waveIndex are stored clamped
    // to the last real wave (so the continue screen reads a valid position), while the
    // atFinalBoss flag is what actually routes resume into the final boss.
    private void WriteFinalBossAutoSave()
    {
        var p = RunPersistence.Instance;
        if (p == null || currentRunPlan == null || currentRunPlan.Count == 0) return;

        int lastStage = currentRunPlan.Count - 1;
        int lastWave = Mathf.Max(0, currentRunPlan[lastStage].waves.Count - 1);
        p.AutoSaveWaveStart(lastStage, lastWave, atFinalBoss: true);
    }

    //  EVENTS (subscribe from UI, audio, etc.)

    /// Fired on every state change. Use for UI transitions.</summary>
    public event Action<RunState, RunState> OnStateChanged;   // (oldState, newState)

    /// Fired when a new stage begins. Use for "Stage 2: Desert" banners.</summary>
    public event Action<StageData> OnStageStarted;

    // Holds the loaded save during a crash/exit resume until the stage layout (and its
    // slots, and the rebuilt CentralCore) exist. Applied once inside RunStage, then
    // cleared. Covers towers AND the core, because both are destroyed and recreated by
    // TowerDefenseMap.GenerateMap during ApplyBiome.
    private RunSaveData _pendingTowerRestore;

    // Set true by a final-boss resume so the re-entered last stage rebuilds only its
    // arena/towers and then SKIPS its (already-completed) stage boss + post-stage
    // reward, going straight to the final boss. One-shot — RunStage reads and clears it.
    private bool _resumeSkipBossAndReward;

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
    // Handle to the current stage's heavy-prefab warm, so the reveal can wait on it.
    private Coroutine _stageWarm;
    private int enemiesAlive;
    private int wavePickCursor; // advances across stages while GenerateRunPlan builds the plan
    private List<WaveData> runWaveDeck;   // flattened (optionally shuffled) wave pool for this run
    private List<GameObject> bossSequence; // resolved per-stage boss order for this run (null = fixed mapping)
    private GameObject currentBossInstance; // specific boss GO we're waiting on (null when no boss alive)
    private Coroutine bossAddsRoutine;      // deals adds during a boss fight (null when none)

    // The coming wave, FULLY rolled before the between-wave gap: exactly which enemy
    // spawns, in which order, and from which side. The telegraph and the spawn loop both
    // read this one plan, so every lit side really receives enemies and every enemy
    // arrives from a side that was lit. (Previously only the FIRST enemy's side was
    // rolled early; every later enemy re-rolled its side at spawn time, so the arc said
    // "Left" and the second enemy walked in from the Top.)
    private readonly List<GameObject> _wavePlanPrefabs = new List<GameObject>();
    private readonly List<SpawnDirection> _wavePlanDirs = new List<SpawnDirection>();
    // Enemies still to be dealt from each side, indexed by (int)SpawnDirection. A side
    // stays lit while its count is > 0 and is allowed to fade once it reaches 0.
    private static readonly int SpawnDirectionCount = System.Enum.GetValues(typeof(SpawnDirection)).Length;
    private readonly int[] _wavePendingPerDir = new int[SpawnDirectionCount];
    private WaveData _wavePlanFor;          // which WaveData the plan above was rolled for
    private int _waveTelegraphToken;        // bumps per plan; stale telegraph loops exit
    private List<MapLayoutDefinition> usedLayouts = new List<MapLayoutDefinition>();
    private MapLayoutDefinition runWideLayout; // used when changeLayoutPerStage == false

    //  UNITY LIFECYCLE

    void Awake()
    {
        BootProfiler.Mark("[PERF] Orchestrator.Awake (scene finished loading)");
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Claim wave ownership NOW, before any other component's Start() runs, so the
        // WaveSpawner can never fall back to its standalone countdown during the boot /
        // resume window (see WillDriveWaves). Reading RunResumeIntent.Pending here is
        // non-destructive — Start() is what consumes/clears it.
        WillDriveWaves = autoStartRun || RunResumeIntent.Pending;

        // FIX: this used to call DontDestroyOnLoad when (and only when) the object
        // happened to be a scene root — so whether the orchestrator survived a scene
        // load depended on where someone had parented it in the editor. When it DID
        // persist, every scene reference it caches in Awake (biomeManager, waveSpawner,
        // transitionOverlay, the augment menus) dangled after a menu round-trip, while
        // the correctly-wired new orchestrator was destroyed by the singleton guard.
        // The orchestrator is a GAMEPLAY-SCENE object; keep it that way unconditionally.

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

            // Hand the boss-name styling to the overlay. Done here because the overlay is
            // usually AddComponent'd at runtime, so it never appears in the Inspector for
            // you to drag a font onto - the fields live on the orchestrator instead.
            if (bossNameFont != null) transitionOverlay.bossNameFont = bossNameFont;
            transitionOverlay.bossNameColor = bossNameColor;
            transitionOverlay.useFontForAllText = bossNameFontForAllText;
            transitionOverlay.textSharpness = uiTextSharpness;
            transitionOverlay.useCrispFlashText = crispFlashText;
            transitionOverlay.flashOutlineThickness = flashOutlineThickness;

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

        // Powers WavePacingMode.ReadyUp (the bottom-right "READY" button). Built at
        // runtime like the other helpers; dormant unless the run uses ReadyUp pacing.
        if (WaveReadyGate.Instance == null) gameObject.AddComponent<WaveReadyGate>();


    }

    void Start()
    {
        BootProfiler.Mark("[PERF] Orchestrator.Start");
        ValidateSetup();

        // timeScale persists across scene reloads; if the menu was opened while
        // paused (timeScale 0), the run's scaled waits would hang on black.
        Time.timeScale = 1f;

        // A throwing or corrupt resume must never strand the player: catch it and
        // fall through to a fresh run instead of leaving the boot half-initialised.
        if (RunResumeIntent.Pending)                       // came from the continue menu
        {
            bool wantResume = RunResumeIntent.Resume;
            RunResumeIntent.Clear();
            // Resume must wait until players + their Weapon children are wired up
            // (augment replay needs them), so it runs deferred in a coroutine.
            if (wantResume) { StartCoroutine(DeferredResume()); return; }
            StartRun();                                    // explicit fresh run (Abandon)
            return;
        }

        // No resume intent → fresh run. Resuming a saved run ALWAYS goes through the
        // Continue menu (which sets RunResumeIntent above). There is deliberately no
        // silent auto-resume of a stale save here — that was a black-screen footgun.
        if (autoStartRun) StartRun();
    }

    //  PUBLIC API

    // Optional player-supplied seed for the NEXT run (a menu can queue one so a run
    // can be replayed/shared). Consumed once in StartRun; null → fresh TickCount seed
    // (original behaviour). The run plan is already fully reproducible from the seed.
    private static int? s_queuedRunSeed;

    // Clear the static between Play sessions when "Enter Play Mode without domain
    // reload" is on — otherwise a seed queued in one session silently replayed the
    // same run in the next. (PlayerRegistry / Tower / RunResumeIntent already do this.)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOrchestratorStatics() => s_queuedRunSeed = null;

    public static void QueueRunSeed(int seed) => s_queuedRunSeed = seed;
    public static void QueueRunSeed(string text)   // alphanumeric → stable seed
    {
        if (string.IsNullOrWhiteSpace(text)) { s_queuedRunSeed = null; return; }
        if (int.TryParse(text.Trim(), out int n)) { s_queuedRunSeed = n; return; }
        unchecked
        {
            uint h = 2166136261u;                  // FNV-1a: same text ⇒ same run
            foreach (char c in text.Trim()) { h ^= c; h *= 16777619u; }
            s_queuedRunSeed = (int)h;
        }
    }

    /// Start a new roguelike run. Generates a random biome sequence and begins.
    [ContextMenu("▶ Start Run")]
    public void StartRun()
    {
        if (runConfig == null)
        {
            Debug.LogError("[Orchestrator] No RunConfig assigned! Create one via Create → Game → Run Config");
            // We cannot drive a run without a blueprint. RELEASE wave ownership so the
            // WaveSpawner's legacy standalone mode still works exactly as it did before
            // this fix — a misconfigured scene must not end up with no waves at all.
            WillDriveWaves = false;
            return;
        }

        // A run is definitely ours from here on (covers manual/ContextMenu StartRun calls
        // in a scene with autoStartRun off).
        WillDriveWaves = true;

        // Stop any existing run
        if (runCoroutine != null)
            StopCoroutine(runCoroutine);

        // Lock in the difficulty for this run from the current Options-menu selection.
        // From here on everything that scales (enemies + bosses) reads ActiveMode.
        var activeDifficulty = EnemyStatModifierManager.LockActiveFromSelected();

        // Use a player-queued seed if present, otherwise a fresh one (original behaviour).
        int runSeed = s_queuedRunSeed ?? System.Environment.TickCount;
        s_queuedRunSeed = null;
        UnityEngine.Random.InitState(runSeed);
        RunPersistence.Instance?.BeginRun(runSeed, runConfig != null ? runConfig.name : "", (int)activeDifficulty);


        // Clear per-player static augment state so a previous run's cooldown /
        // parry / projectile-parry upgrades don't leak into this fresh run (statics
        // survive scene reloads in a built player).
        CooldownModifier.Reset();
        ParryUpgrades.ResetAll();
        ProjectileParry.Reset();

        // Same reason, for the augment-334/338/340/346/348 multiplier holders. These
        // were the one family of augment statics with NO reset anywhere, so tower
        // damage / fire rate and the energy-gain multiplier compounded from run to run.
        AugmentRuntimeModifiers.ResetAll();


        // FIX: nothing ever cleared the wave/stage/final-boss snapshots, so a rewind
        // early in a NEW run could restore state captured during the PREVIOUS one
        // (same scene load, e.g. after a game over then Play again).
        WaveCheckpointService.Instance?.ResetForNewRun();

        // Generate the run plan
        currentRunPlan = GenerateRunPlan();
        BootProfiler.Mark("[PERF] RunPlan generated");

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

        // FIX: stopping the run coroutine mid-augment-selection abandoned the modal
        // layer it had pushed, so if the core died while a reward menu was open the
        // game stayed frozen at timeScale 0 behind the game-over screen with no menu
        // left to close. Release the layer and shut the menus.
        if (_augmentModalHeld)
        {
            if (augmentsMenus != null)
                foreach (var m in augmentsMenus)
                    if (m != null) m.ForceClose();
            if (augmentsMenu != null) augmentsMenu.ForceClose();

            UIModalStack.Pop(_augmentModal);
            _augmentModalHeld = false;
        }

        SetState(RunState.GameOver);
        OnGameOver?.Invoke();

        RunPersistence.Instance?.OnSaveConsumed();
        WaveCheckpointService.Instance?.ResetForNewRun();

        // FIX: the stage-warm coroutine was left running when the run ended.
        if (_stageWarm != null) { StopCoroutine(_stageWarm); _stageWarm = null; }
        StopBossAdds();


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

        // Show the Win screen directly — no black "VICTORY" banner. A short beat lets
        // the final kill register; the overlay is already transparent during play, so
        // nothing needs revealing before OnVictory activates the Win screen.
        yield return new WaitForSecondsRealtime(0.75f);
        SetState(RunState.Victory);
        OnVictory?.Invoke();
        RunPersistence.Instance?.OnSaveConsumed();
        Debug.Log("[Orchestrator] ═══ VICTORY! ═══");
    }

    private IEnumerator RunLoop()
    {
        // Assert black + stand the boot reveal watchdog down the INSTANT the run
        // begins. This covers the screen from frame 0 of the run (no scene-default /
        // previous-session biome can show before the first stage builds) and, crucially,
        // stops the overlay's soft failsafe from force-clearing the black mid-startup on
        // a slow machine — which is what was uncovering the wrong biome and wiping the
        // banner ("brief title"). Per-stage prewarming happens later, inside RunStage and
        // behind this black, so this stays cheap and can't trip the hard 20s ceiling the
        // way the earlier whole-run prewarm did. The first FadeIn disarms the watchdog.
        if (enableTransitions && transitionOverlay != null)
        {
            transitionOverlay.NotifyIntroStarted();
            transitionOverlay.SnapToBlack();
        }

        // ── Play through each stage ──
        for (CurrentStageIndex = 0; CurrentStageIndex < currentRunPlan.Count; CurrentStageIndex++)
        {
            // Re-lock difficulty from the current menu selection at each fresh stage, so
            // a Normal↔Nightmare change made mid-run takes effect from the NEXT stage.
            EnemyStatModifierManager.LockActiveFromSelected();

            yield return RunStage(currentRunPlan[CurrentStageIndex]);

            // Check for game over between stages
            if (CurrentState == RunState.GameOver)
                yield break;
        }

        yield return FinishRun();

    }

    //  SPRITE PREWARM (automatic — no wiring)
    // Warms the sprite folders for ONE stage's enemies + its stage boss, spread over a
    // few frames. Called from RunStage while the screen is ALREADY black (behind the
    // fade-out / snap-to-black) AND after NotifyIntroStarted() has stood the boot
    // watchdog down — so the load is hidden and neither the soft nor (in practice) the
    // hard reveal failsafe can fire. Per-stage rather than whole-run keeps each load
    // well under the overlay's hard ceiling on a cold first launch, which is what was
    // aborting the intro (brief title flash + early wave indicators) before.

    // Warms one prefab's folders: its EnemyData body frames (covers every standard
    // enemy and boss), plus any custom-art components that warm themselves through
    // ISpritePrewarm (Insect burrow, Parfumer body, …). Reads the prefab directly —
    // no instantiation, so there are no spawn side effects.

    /// Give a prefab's ISpritePrewarm components a chance to prepare themselves.
    ///
    /// ALL Resources-based sprite warming has been removed. Enemy art is now referenced
    /// directly from EnemyData/prefabs, so Unity loads it during the scene load, off the
    /// main thread — there is nothing left to warm. (For the record: the loading this
    /// system existed to hide measured 20 ms across all 21 folders.)
    ///
    /// ISpritePrewarm itself is NOT dead and must stay. Its implementors do real work
    /// that has nothing to do with Resources — BerserkVisual.PrewarmSpriteFolders()
    /// kicks off a BACKGROUND COMPUTE that generates the Berserk's frames procedurally.
    /// Drop this call and that cost moves to first spawn, as a visible hitch.
    private static IEnumerator PrewarmProviders(GameObject prefab)
    {
        if (prefab == null) yield break;

        var providers = prefab.GetComponentsInChildren<ISpritePrewarm>(true);
        if (providers.Length == 0) yield break;

        foreach (var p in providers)
        {
            p?.PrewarmSpriteFolders();
            yield return null;   // still synchronous per call; breathe between them
        }

        BootProfiler.Mark($"[PERF] prewarm providers {prefab.name} ({providers.Length})");
    }

    //  STAGE FLOW
    // Runs each stage's ISpritePrewarm providers behind the black intro, one per frame.
    //
    // This used to warm Resources sprite FOLDERS as well. That is all gone: enemy art is
    // referenced directly now, so Unity loads it during the scene load. What remains is
    // only the ISpritePrewarm opt-in, whose implementors do genuine non-Resources work
    // (BerserkVisual generates its frames on a worker thread).
    // Bounded to a few prefabs — the opposite of the old full-roster prewarm.
    private IEnumerator WarmHeavyStagePrefabs(StageData stage)
    {
        if (stage == null) yield break;

        if (stage.hasStageBoss && stage.stageBossPrefab != null)
        {
            yield return PrewarmProviders(stage.stageBossPrefab);
        }

        if (stage.waves == null) yield break;

        var warmed = new HashSet<GameObject>();
        foreach (var wave in stage.waves)
        {
            if (wave?.enemies == null) continue;

            foreach (var group in wave.enemies)
            {
                var prefab = group?.enemyPrefab;
                if (prefab == null || !warmed.Add(prefab)) continue;

                // Only prefabs that actually opt in via ISpritePrewarm have anything to
                // do now, so the old "first wave warms every type" rule is pointless —
                // a prefab without a provider would just be a no-op call and a wasted
                // frame yield. Skip it entirely.
                if (prefab.GetComponentInChildren<ISpritePrewarm>(true) != null)
                    yield return PrewarmProviders(prefab);
            }
        }

        // END OF THE WARM. The report used to be attached to PrewarmStageSprites, which
        // is DEAD CODE — declared but never called — so Summary() and SnapshotTextures()
        // never executed once across three profiling runs. Both logs ended at
        // "warmed Boss3" for exactly that reason. This is the method that actually runs.
        BootProfiler.Mark("[PERF] Stage warm END");
        BootProfiler.SnapshotTextures("after stage warm");
        BootProfiler.Summary("BOOT");
    }
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



        // Warm ONLY this stage's HEAVY prefabs (boss + ISpritePrewarm opt-ins like
        // elites) in the background, behind the black intro. Scoped to a few prefabs —
        // NOT the whole roster — so it doesn't reintroduce the boot freeze or stutter.
        // This is what stops the 1-2s freeze when a boss/elite first appears (and, with
        // Fix 1, keeps the boss zoom + audio intact).
        //StartCoroutine(WarmHeavyStagePrefabs(stage));
        _stageWarm = StartCoroutine(WarmHeavyStagePrefabs(stage));
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

                    // Wait for the render thread to actually PAINT AND PRESENT the black before
                    // the ~1-2s ApplyBiome block below. This is the crux of the intermittent flash:
                    // coroutines run on the MAIN thread and can't see the GPU, so "yield return null"
                    // only advances main-thread frames — it does NOT wait for the render thread,
                    // which at startup lags extra while it compiles shaders. If the main thread
                    // reaches ApplyBiome before the black has been painted, the last painted frame
                    // (your scene) is frozen on screen for the whole block: the ~1-in-10 flash.
                    //
                    // WaitForEndOfFrame DOES wait for the render thread to finish the frame; the
                    // following "yield return null" then guarantees that finished frame was actually
                    // presented (we've entered the next frame's Update, which only happens after the
                    // previous frame was displayed). Two such cycles make it virtually certain the
                    // black is on the display before we block. Order matters: the block must come
                    // AFTER the yield-null, never straight after WaitForEndOfFrame (that resumes just
                    // BEFORE display, so blocking there would delay the black's own present).
                    //
                    // This is a strong probabilistic guard, not a proof — the only 100% fix is to
                    // stop ApplyBiome hard-blocking the main thread (chunk the biome build across
                    // frames). See the note in chat.
                    BootProfiler.Mark("[PERF] black snapped, waiting for first paint");
                    for (int _paintWait = 0; _paintWait < 2; _paintWait++)
                    {
                        yield return new WaitForEndOfFrame();
                        yield return null;
                    }
                    BootProfiler.Mark("[PERF] first paint done (shaders compiled)");
                }

                // Swap biome while the screen is black — SYNCHRONOUSLY, before any yield.
                // Deliberately BEFORE the prewarm below: ApplyBiome runs with no yield, so
                // the scene already shows the CORRECT biome by the time the prewarm hands
                // frames back to Unity. Even if the black cover ever has a one-frame gap,
                // the previous / scene-default biome can never flash through.
                // Wrapped so a failure here can't abort the coroutine before FadeIn().
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

                // Enemy sprites are NOT bulk-prewarmed here. Doing so was a multi-second
                // synchronous Resources.LoadAll stall (freeze on black, or stutter if
                // backgrounded). Instead each enemy type loads its folder ONCE on its
                // first spawn (EnemyAnimationController.LoadSprites, cached for the run),
                // spreading the cost into small one-time hitches instead of one big freeze.

                // Let biome settle (particles, overlays, etc.)
                yield return new WaitForSeconds(0.3f);

                // Show "Stage 1: Frozen Tundra" banner over black
                yield return transitionOverlay.ShowBanner(stage, runConfig.stageCount);
                // Finish the heavy warm (boss/elite) BEFORE revealing, so its synchronous
                // loads stay behind the title instead of freezing the first seconds of play.
                if (_stageWarm != null) { yield return _stageWarm; _stageWarm = null; }
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

                // (Sprites load lazily on first spawn — see note in the transitions path.)

                yield return new WaitForSeconds(runConfig.timeBetweenStages);
            }

        }
        else
        {
            // RESUME path (Continue / crash-recovery): the intro is skipped, but the
            // biome must still be built — otherwise the stage renders with only the
            // scene's default background (no overlay, and whatever biome the scene
            // happened to default to instead of the planned one). Apply it now, then
            // reveal the screen, since Awake snapped it to black and the intro's
            // FadeIn (which normally reveals) didn't run.
            // Stand the boot watchdog down BEFORE building the biome so the failsafe
            // can't uncover a resumed stage mid-setup.
            if (enableTransitions && transitionOverlay != null)
                transitionOverlay.NotifyIntroStarted();

            try
            {
                ApplyBiome(stage);
                OnStageStarted?.Invoke(stage);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Orchestrator] Resume biome setup threw — revealing screen anyway. {e}");
            }

            if (waveSpawner != null) waveSpawner.enemySpawnCountMultiplier = stage.enemyCountMultiplier;

            // Sprites load lazily on first spawn (see note in the transitions path).

            if (enableTransitions && transitionOverlay != null)
            {
                transitionOverlay.NotifyIntroStarted(); // idempotent — keep watchdog down
                                                        //yield return null;                      // let the biome build one frame
                yield return new WaitForSecondsRealtime(0.3f);
                if (_stageWarm != null) { yield return _stageWarm; _stageWarm = null; }
                yield return transitionOverlay.FadeIn();// reveal (no banner on resume)
            }
        }

        // Deferred tower + CORE restore (crash/exit resume). Both objects are destroyed
        // and recreated by TowerDefenseMap.GenerateMap inside ApplyBiome above, so
        // neither can be restored any earlier:
        //   * slots (and therefore towers) only exist once the layout has been applied;
        //   * the CentralCore that RestoreAbsolutes wrote to during TryResumeSavedRun has
        //     already been destroyed by now, which is why every resume used to hand the
        //     player a full-health core. RestoreCore re-seeds the REBUILT one.
        // One-shot — cleared after it runs.
        if (_pendingTowerRestore != null)
        {
            // Give the layout a frame to register its slots with TowerPlacementManager.
            yield return null;
            try
            {
                RunPersistence.Instance?.RestoreCore(_pendingTowerRestore);
                RunPersistence.Instance?.RestoreTowers(_pendingTowerRestore);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Orchestrator] Tower/core restore failed. {e}");
            }
            _pendingTowerRestore = null;
        }



        // One-shot resume flag: when resuming straight into the final boss, the last
        // stage is re-entered only to rebuild its arena/towers — its boss and reward
        // were already completed before the save, so skip them this invocation.
        bool skipBossAndReward = _resumeSkipBossAndReward;
        _resumeSkipBossAndReward = false;

        // The heavy-prefab warm is awaited on the two transition paths above, but the
        // "no transitions" branch never did — it left the coroutine running and the
        // handle set, so the NEXT stage's `yield return _stageWarm` could wait on a
        // stale handle. Await it here so all three paths converge in the same state
        // before waves begin. (Do NOT StopCoroutine here: that would abort the warm
        // on the no-transitions path and reintroduce the boss/elite spawn freeze this
        // whole system exists to prevent.)
        if (_stageWarm != null) { yield return _stageWarm; _stageWarm = null; }

        //  2. WAVES: Play through each wave 
        for (CurrentWaveInStage = startWaveIndex; CurrentWaveInStage < stage.waves.Count; CurrentWaveInStage++)
        {
            // Check game over
            if (CurrentState == RunState.GameOver) yield break;

            WaveData wave = stage.waves[CurrentWaveInStage];

            // Brief countdown before wave
            SetState(RunState.WaveCountdown);
            OnWaveStarted?.Invoke(CurrentWaveInStage, stage.waves.Count);

            // FIX: WaveCheckpointService only promotes a snapshot to StageStartSnapshot
            // when waveIndex == 0. A run resumed at wave 3 (or straight into the final
            // boss) therefore never had one, and a STAGE-BOSS rewind was silently
            // refused for the rest of that stage. Treat the first wave we actually run
            // in this stage as the stage start.
            WaveCheckpointService.Instance?.CaptureSnapshot(
                CurrentStageIndex, CurrentWaveInStage,
                forceStageStart: CurrentWaveInStage == startWaveIndex);
            RunPersistence.Instance?.AutoSaveWaveStart(CurrentStageIndex, CurrentWaveInStage);
            if (debugLog)
                Debug.Log($"[Orchestrator] Wave {CurrentWaveInStage + 1}/{stage.waves.Count} starting...");

            // Update persistent wave counter at top of screen + center flash
            // The persistent top-of-screen counter updates now; the centre-screen flash
            // waits until the gap is over (below) so it announces the wave STARTING rather
            // than competing with the countdown that precedes it.
            if (transitionOverlay != null)
                transitionOverlay.SetWaveCounter(
                    $"Wave {CurrentWaveInStage + 1}/{stage.waves.Count}");

            // Roll the whole wave (who + from where), then telegraph EVERY side it will use
            // for the whole gap and while enemies are still being dealt. Both must happen
            // BEFORE the wait, or there is nothing to show.
            PrepareWavePlan(wave, stage);
            StartCoroutine(TelegraphWaveSides(_waveTelegraphToken));

            // Mode-aware pause before the wave spawns (countdown / ready-up / immediate).
            yield return WaitBeforeWave();

            // Spawn the wave
            SetState(RunState.WaveActive);

            if (transitionOverlay != null)
                transitionOverlay.FlashWaveStart($"Wave {CurrentWaveInStage + 1} Starts", 1.5f);

            PlayWaveStartCue();
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
        if (stage.hasStageBoss && stage.stageBossPrefab != null && !skipBossAndReward)
        {
            SetState(RunState.StageBoss);
            OnBossSpawned?.Invoke(stage);

            if (debugLog)
                Debug.Log($"[Orchestrator] STAGE BOSS spawning: {stage.stageBossPrefab.name}");

            // Update counter and flash with the boss's REAL name.
            // Read off the prefab (this runs before SpawnBoss), which works because the
            // name lives in serialized data - EnemyData.enemyName or the per-prefab
            // override on BaseBossStats - not in anything Awake() computes.
            if (transitionOverlay != null)
            {
                string bossName = ResolveBossName(stage.stageBossPrefab);
                transitionOverlay.SetBossCounter(bossName);
                transitionOverlay.FlashBossName(bossName, 1.5f);
            }

            SpawnBoss(stage.stageBossPrefab);
            StartBossAdds(CurrentStageIndex, isFinalBoss: false);
            yield return WaitForBossDead();
            StopBossAdds();

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
        if (!skipBossAndReward)
        {
            if (enablePostStageChoice && postStageChoiceMenu != null)
            {
                yield return ShowPostStageChoice(stage);
            }
            else if (enableAugmentSelection && augmentsMenu != null)
            {
                // Fallback to old behaviour if post-stage choice is disabled
                yield return ShowAugmentSelection($"stage {stage.stageIndex + 1} boss");
            }
        }

        //  5. STAGE COMPLETE 
        SetState(RunState.StageComplete);

        if (debugLog)
            Debug.Log($"[Orchestrator] Stage {stage.stageIndex + 1} complete!");

        yield return new WaitForSeconds(pauseAfterLastKill);
    }

    // Mode-aware pause before each wave spawns. Replaces the old fixed
    // `WaitForSeconds(timeBetweenWaves)`, so between-wave pacing is selectable per run
    // via RunConfig.wavePacingMode. Countdown reproduces the original behaviour exactly
    // (including the 1s lead-in on the first wave of a stage), so the default is a no-op.
    private IEnumerator WaitBeforeWave()
    {
        // HARD RULE: a wave never spawns while the screen is still covered. The stage
        // intro already awaits FadeIn before reaching here, so in a healthy run this
        // returns on the first check and costs nothing. It matters on the paths that do
        // NOT go through the intro — a resumed stage, a watchdog-forced reveal, a
        // transition that was interrupted — where waves could otherwise begin over black.
        yield return WaitUntilScreenRevealed();

        bool isFirstWaveOfStage = (CurrentWaveInStage == 0);
        WavePacingMode mode = runConfig != null ? runConfig.wavePacingMode : WavePacingMode.Countdown;
        var gate = WaveReadyGate.Instance;

        // The gap before THIS wave.
        //   First wave of a stage → firstWaveDelay. This used to be a hardcoded 1f in all
        //     three modes, which is exactly why Countdown and Immediate behaved
        //     identically at the start of a stage: both waited the same literal second
        //     and timeBetweenWaves never entered the picture.
        //   Every later wave  → timeBetweenWaves.
        float secs = isFirstWaveOfStage
            ? (runConfig != null ? runConfig.firstWaveDelay : 1f)
            : (runConfig != null ? runConfig.timeBetweenWaves : 0f);
        secs = Mathf.Max(0f, secs);

        switch (mode)
        {
            case WavePacingMode.Immediate:
                // Back-to-back waves: no between-wave gap at all. The stage's opening prep
                // time is still honoured — an unexplained pause is worse than a visible
                // one, so it ticks down on screen like any other wait. Set First Wave
                // Delay to 0 for a truly instant stage opening.
                if (isFirstWaveOfStage)
                    yield return CountdownOrWait(gate, secs);
                yield break;

            case WavePacingMode.ReadyUp:
                // Prep time first, THEN the READY button — otherwise a player who readies
                // instantly skips the tower-placement window the delay exists to give.
                if (isFirstWaveOfStage)
                    yield return CountdownOrWait(gate, secs);

                if (gate != null)
                {
                    yield return gate.WaitForAllReady(() => CurrentState == RunState.GameOver);
                }
                else
                {
                    // Gate somehow missing — fall back to a plain wait so a run can
                    // never stall waiting on a button that isn't there.
                    Debug.LogWarning("[Orchestrator] WaveReadyGate missing — falling back to a timed wait.");
                    if (!isFirstWaveOfStage) yield return new WaitForSeconds(secs);
                }
                yield break;

            case WavePacingMode.Countdown:
            default:
                yield return CountdownOrWait(gate, secs);
                yield break;
        }
    }

    // The timed wait shared by the pacing modes. Shows WaveReadyGate's big ticking number
    // whenever the gate exists (it is created in Awake, so in practice it always does);
    // falls back to a silent wait only if something has destroyed it.
    private IEnumerator CountdownOrWait(WaveReadyGate gate, float seconds)
    {
        if (seconds <= 0f) yield break;

        if (gate != null)
            yield return gate.WaitForCountdown(seconds);
        else
            yield return new WaitForSeconds(seconds);
    }



    // Blocks until the transition overlay has actually revealed gameplay, so enemies can
    // never walk in (or be killed by towers) while the player is still looking at the
    // loading cover.
    //
    // BOUNDED ON PURPOSE: if the cover somehow never clears, the run continues anyway
    // after the cap rather than stalling forever — a late wave is recoverable, a dead run
    // is not. Uses UNSCALED time so a paused timeScale can't extend the cap.
    private IEnumerator WaitUntilScreenRevealed()
    {
        if (!enableTransitions || transitionOverlay == null) yield break;
        if (!transitionOverlay.IsCovering) yield break;   // normal case — zero cost

        float waited = 0f;
        while (transitionOverlay.IsCovering && waited < waveRevealGateSeconds)
        {
            if (CurrentState == RunState.GameOver) yield break;
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (transitionOverlay.IsCovering)
            Debug.LogWarning($"[Orchestrator] Screen still covered after {waveRevealGateSeconds:F1}s — " +
                             "starting the wave anyway rather than stalling the run.");
        else if (debugLog)
            Debug.Log($"[Orchestrator] Held wave {CurrentWaveInStage + 1} for {waited:F2}s until the screen was revealed.");
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
            // ForceClose(), not augmentsMenu.SetActive(false): the latter disables a
            // CHILD object, so the menu never runs its close path and never pops its
            // UIModalStack layer — the run would resume permanently frozen.
            if (augmentsMenus != null)
                foreach (var m in augmentsMenus)
                    if (m != null) m.ForceClose();
            if (augmentsMenu != null) augmentsMenu.ForceClose();

            // The coroutine that pushed this layer is about to be stopped, so pop it
            // here or the freeze outlives the menu.
            // FIX: RunState.AugmentSelect is ALSO set by ShowPostStageChoice BEFORE it
            // pushes anything (the stage-clear screen reuses the state). Rewinding while
            // that screen was up popped a layer that had never been pushed, unbalancing
            // the stack. Only pop when we are actually holding it.
            if (_augmentModalHeld)
            {
                UIModalStack.Pop(_augmentModal);
                _augmentModalHeld = false;
            }
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
        StopBossAdds();
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

        // Show the Win screen directly — no black "VICTORY" banner (see FinishRun).
        yield return new WaitForSecondsRealtime(0.75f);
        SetState(RunState.Victory);
        OnVictory?.Invoke();
        RunPersistence.Instance?.OnSaveConsumed();
        Debug.Log("[Orchestrator] ═══ VICTORY! ═══");
    }

    public void JumpToWave(int stageIndex, int startWaveIndex)
    {
        if (currentRunPlan == null || stageIndex < 0 || stageIndex >= currentRunPlan.Count) return;
        if (runCoroutine != null) StopCoroutine(runCoroutine);
        StopBossAdds();
        Time.timeScale = 1f;
        enemiesAlive = 0;
        currentBossInstance = null;
        CurrentStageIndex = stageIndex;
        runCoroutine = StartCoroutine(ResumeRunLoop(stageIndex, startWaveIndex));
    }

    private IEnumerator ResumeRunLoop(int stageIndex, int startWaveIndex)
    {
        // The resumed (partial) stage keeps the difficulty it was saved with — restored
        // by AdoptLoadedRun/SetActiveMode — so we do NOT re-lock before this first call.
        yield return RunStage(currentRunPlan[stageIndex], startWaveIndex, skipIntro: true);
        if (CurrentState == RunState.GameOver) yield break;
        for (CurrentStageIndex = stageIndex + 1; CurrentStageIndex < currentRunPlan.Count; CurrentStageIndex++)
        {
            // Stages AFTER the resumed one are fresh: re-lock from the menu selection.
            EnemyStatModifierManager.LockActiveFromSelected();

            yield return RunStage(currentRunPlan[CurrentStageIndex]);
            if (CurrentState == RunState.GameOver) yield break;
        }
        yield return FinishRun();
    }

    // Resume after a controlled scene reload (the Continue menu). The augment
    // replay inside TryResumeSavedRun resolves each weapon-unlock augment against a
    // Weapon component on the chooser; in Start() the player prefab hasn't finished
    // wiring its Weapon child yet, so those replays ("Cannot apply augment N to
    // current target" for 65/93/327…) fail. Wait — bounded — until the expected
    // players are registered AND a Weapon exists, then resume.
    private IEnumerator DeferredResume()
    {
        Time.timeScale = 1f;
        if (enableTransitions && transitionOverlay != null)
            transitionOverlay.NotifyIntroStarted(); // keep the boot watchdog from revealing black mid-wait

        int wantPlayers = CoopManager.Instance != null ? CoopManager.Instance.TargetPlayerCount : 1;

        // Wait until EVERY expected player is registered, resolvable by index, and has
        // its Weapon child wired — the augment replay targets each player's Weapon, and
        // resolves the chooser via PlayerRegistry.Get(playerIndex). The old 3s/Count-only
        // wait gave up too early in co-op: if P2 (or its index/Weapon) wasn't ready, the
        // replay dropped P2's weapon-unlock augments (partial loss), and on a hard timeout
        // we fell through to StartRun() — which BeginRun→Clear+DeleteSave WIPED the run.
        // Cap is generous because the Continue menu already gated on the controllers, so
        // seating WILL complete; we must never bail early and destroy the save.
        bool resumed = false;
        float t = 0f;
        const float maxWait = 15f;
        while (t < maxWait && !resumed)
        {
            if (ResumePlayersReady(wantPlayers))
            {
                try { resumed = TryResumeSavedRun(); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Orchestrator] Resume threw — {e}");
                    break;
                }
                if (resumed) break;

                // TryResumeSavedRun returned false. If it DISCARDED the save (corrupt /
                // old version / incompatible RunConfig), the file is gone → stop and
                // fresh-start below. If the save is STILL on disk, this was only the
                // controller gate — keep waiting for the players, never fresh-start.
                if (!RunPersistence.SaveExists) break;
            }
            yield return null;
            t += Time.unscaledDeltaTime;
        }

        if (resumed) yield break;

        // Did not resume. Start a fresh run ONLY when there is genuinely no save to
        // protect. A surviving save means resume was merely deferred (players still
        // seating) — starting fresh here would BeginRun→delete it and reset the run,
        // which is exactly the bug that wiped equipped weapons. Preserve the save.
        if (!RunPersistence.SaveExists)
        {
            StartRun();
        }
        else
        {
            // A surviving save means resume was only DEFERRED (players still seating, a controller
            // dropped during the load, etc.). We must NOT start a fresh run — that would
            // BeginRun→delete the save. But we must ALSO never leave the player on the Awake black
            // cover: reveal the screen and reopen the Continue gate so they retry, save intact.
            Debug.LogError("[Orchestrator] Could not resume in time, but a SAVE EXISTS — keeping it intact " +
                           "(NOT starting a fresh run). Revealing the screen and reopening the Continue gate to retry.");
            if (enableTransitions && transitionOverlay != null)
                yield return transitionOverlay.FadeIn();   // guarantee we never sit on black
            ContinueRunMenu.Open();                          // let the player retry; save is intact
        }
    }

    // Resume readiness: all expected players registered, each resolvable by its index,
    // and each carrying a Weapon child (the augment-replay target). Stricter than a bare
    // Count check so co-op replay never applies a player's unlocks to a null/By-position
    // chooser.
    private bool ResumePlayersReady(int wantPlayers)
    {
        if (PlayerRegistry.Count < wantPlayers) return false;
        for (int i = 0; i < wantPlayers; i++)
        {
            var pr = PlayerRegistry.Instance != null ? PlayerRegistry.Instance.Get(i) : null;
            if (pr == null) return false;
            if (pr.GetComponentInChildren<Weapon>() == null) return false;
        }
        return true;
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

        // Controller gate: refuse to resume a co-op save until enough players are
        // seated. Does NOT delete the save — the player can connect a controller
        // and retry. present == 0 (registry not populated yet) is left to pass so
        // single-player timing never regresses.
        int needed = Mathf.Max(1, data.runPlayerCount > 0 ? data.runPlayerCount
                                  : (data.players != null ? data.players.Count : 1));
        int present = PlayerRegistry.Count;
        if (present > 0 && present < needed)
        {
            Debug.LogWarning($"[Orchestrator] Saved run needs {needed} players but {present} seated — not resuming (save kept).");
            return false;
        }

        try
        {
            // Restore this run's difficulty BEFORE any stage scaling / enemy spawn so a
            // resumed run scales exactly as the original did.
            EnemyStatModifierManager.SetActiveMode(data.difficulty);

            UnityEngine.Random.InitState(data.runSeed);
            currentRunPlan = GenerateRunPlan();

            // The run plan holds only the STAGES; the final boss runs AFTER them as a
            // separate phase, so a saved position past the last stage (CurrentStageIndex
            // == plan.Count during RunState.FinalBoss) is NORMAL, not corrupt. Never
            // discard/delete the save for being out of range — that wipes the whole run.
            // Only a genuinely unusable plan (the seed produced nothing) is unrecoverable.
            // For everything else, CLAMP to the last real wave and resume there: every
            // augment/weapon is still replayed, so the player keeps all progress and just
            // re-approaches the final boss.
            if (currentRunPlan == null || currentRunPlan.Count == 0)
            {
                Debug.LogWarning("[Orchestrator] No run plan could be generated from the saved seed — discarding and starting fresh.");
                p.DeleteSave();
                return false;
            }

            int lastStage = currentRunPlan.Count - 1;
            int resumeStage = Mathf.Clamp(data.stageIndex, 0, lastStage);
            int lastWaveOfStage = Mathf.Max(0, currentRunPlan[resumeStage].waves.Count - 1);
            int resumeWave = Mathf.Clamp(data.waveIndex, 0, lastWaveOfStage);
            if (resumeStage != data.stageIndex || resumeWave != data.waveIndex)
                Debug.LogWarning($"[Orchestrator] Saved position (stage {data.stageIndex + 1}, wave {data.waveIndex + 1}) is past " +
                                 $"the last stage (final-boss phase) or out of range — resuming at stage {resumeStage + 1}, wave " +
                                 $"{resumeWave + 1} with all augments/weapons restored.");

            p.AdoptLoadedRun(data);


            // Clean per-player static slate before replaying saved augments —
            // each ApplyAugment re-sets the right player's values.
            CooldownModifier.Reset();
            ParryUpgrades.ResetAll();
            ProjectileParry.Reset();


            // Replay augments — ISOLATED per item. This is the core multi-resume fix:
            // a single augment whose effect throws (or whose weapon target won't resolve
            // on this particular cycle) must NEVER abort the whole resume. Previously one
            // such throw fell to the outer catch, which DELETED the save; DeferredResume
            // then saw no save and started a FRESH run — which is exactly why a second
            // Continue came back with only the starting melee. Now each augment that
            // fails is logged and skipped, and every other augment/weapon still restores.
            int augTried = 0, augOk = 0;
            if (AugmentRegistry.Instance != null && data.augments != null)
                foreach (var a in data.augments)
                {
                    augTried++;
                    try
                    {
                        PlayerStats chooser = null;
                        var pr = PlayerRegistry.Instance != null ? PlayerRegistry.Instance.Get(a.playerIndex) : null;
                        if (pr != null) chooser = pr.Stats;
                        if (AugmentRegistry.Instance.ApplyAugment(a.id, a.rarity, chooser)) augOk++;
                    }
                    catch (System.Exception ae)
                    {
                        Debug.LogError($"[Orchestrator] Resume: augment {a.id} (P{a.playerIndex}) threw on replay — skipping it, keeping the rest. {ae.Message}");
                    }
                }

            // Re-apply in-run blueprint-drop unlocks. No augment backs these, so the
            // replay above can't rebuild them. ForceUnlock fires OnUnlocksChanged →
            // each player's WeaponRollController rebuilds its hotbar and re-equips.
            // Isolated per item for the same reason as the augment replay.
            int bpTried = 0, bpOk = 0;
            if (data.blueprintUnlocks != null && WeaponUnlockRegistry.Instance != null)
                foreach (var bu in data.blueprintUnlocks)
                {
                    if (bu == null || bu.slot < 0) continue;
                    bpTried++;
                    try { WeaponUnlockRegistry.Instance.ForceUnlock(bu.slot, bu.playerIndex); bpOk++; }
                    catch (System.Exception be)
                    {
                        Debug.LogError($"[Orchestrator] Resume: blueprint unlock slot {bu.slot} (P{bu.playerIndex}) threw — skipping it, keeping the rest. {be.Message}");
                    }
                }

            Debug.Log($"[Orchestrator] Resume replay complete: augments {augOk}/{augTried} applied, blueprint unlocks {bpOk}/{bpTried} applied.");

            // Equipped weapon/tool LAST, so an explicit mid-run swap beats whatever the
            // augment replay above re-equipped. (These save fields existed but were
            // never read back, so a manual swap silently reverted on every resume.)
            try { p.RestoreEquipment(data); }
            catch (System.Exception ee)
            {
                Debug.LogError($"[Orchestrator] Resume: restoring equipped weapon/tool failed (non-fatal) — {ee.Message}");
            }

            // Towers can't be rebuilt yet — slots don't exist until the stage
            // layout is applied inside RunStage. Defer to there.
            _pendingTowerRestore = data;

            // Player/core/economy absolutes — applied AFTER replay. Non-critical to the
            // run continuing, so a failure here must not abort the resume either.
            try { p.RestoreAbsolutes(data); }
            catch (System.Exception re)
            {
                Debug.LogError($"[Orchestrator] Resume: restoring absolute stats failed (non-fatal) — {re.Message}");
            }

            if (data.atFinalBoss)
            {
                // Resume straight into the final boss: re-enter the last stage to rebuild
                // its arena + towers, but skip its (already-beaten) boss and reward, then
                // fall through to the final boss. Starting past the last wave skips the
                // wave loop; the skip flag handles the boss + reward.
                int lastStageIdx = currentRunPlan.Count - 1;
                int pastAllWaves = currentRunPlan[lastStageIdx].waves != null
                    ? currentRunPlan[lastStageIdx].waves.Count : 0;
                _resumeSkipBossAndReward = true;
                JumpToWave(lastStageIdx, pastAllWaves);
            }
            else
            {
                JumpToWave(resumeStage, resumeWave);
            }

            // Consume the save only once the run is definitely resuming (after JumpToWave
            // has started the run loop). If anything above had thrown, the save survives
            // so the player can retry from the menu instead of losing the run.
            p.OnSaveConsumed();
            return true;
        }
        catch (System.Exception e)
        {
            // An unexpected throw here (run-plan / jump / adopt) is rare now that the
            // replays are isolated above. CRITICAL: do NOT delete the save. Deleting it
            // is what made DeferredResume start a fresh run and wipe the player's weapons
            // on the second Continue. Preserve the save so the player can return to the
            // menu and press Continue again (the controller-gate path already depends on
            // a surviving save). Log the full stack so the exact cause is visible.
            Debug.LogError($"[Orchestrator] Resume threw unexpectedly — SAVE PRESERVED for retry (NOT starting a fresh run). {e}");
            return false;
        }
    }

    // WAVE SPAWNING (uses your existing WaveSpawner)
    // Spawns a wave and waits until all enemies are dead.
    // Uses WaveSpawner's existing SpawnEnemy logic but driven by the orchestrator.
    // Rolls the coming wave completely: expands the groups (with the stage's count
    // multiplier), shuffles the order, and assigns each enemy its side. The random rolls
    // are the same ones the spawn loop used to make inline — they just happen once, up
    // front, so the telegraph can show the real answer.
    private void PrepareWavePlan(WaveData wave, StageData stage)
    {
        _wavePlanPrefabs.Clear();
        _wavePlanDirs.Clear();
        System.Array.Clear(_wavePendingPerDir, 0, _wavePendingPerDir.Length);
        _wavePlanFor = wave;
        _waveTelegraphToken++;

        if (wave == null) return;

        float countMul = stage != null ? stage.enemyCountMultiplier : 1f;
        if (wave.enemies != null)
        {
            foreach (var group in wave.enemies)
            {
                if (group == null || group.enemyPrefab == null || group.count <= 0)
                    continue;

                int modifiedCount = Mathf.Max(1, Mathf.RoundToInt(group.count * countMul));
                for (int i = 0; i < modifiedCount; i++)
                    _wavePlanPrefabs.Add(group.enemyPrefab);
            }
        }

        Shuffle(_wavePlanPrefabs);

        bool hasList = wave.spawnDirections != null && wave.spawnDirections.Count > 0;

        SpawnDirection shared = SpawnDirection.Top;
        if (hasList && wave.oneDirectionForAllEnemies)
            shared = wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];

        if (!hasList || wave.oneDirectionForAllEnemies)
        {
            for (int i = 0; i < _wavePlanPrefabs.Count; i++)
                _wavePlanDirs.Add(shared);
        }
        else
        {
            // Every listed side gets at least one enemy (when there are enough enemies), so
            // "this wave comes from 3 sides" really means 3 sides — not 3 sides on paper with
            // one of them never rolled. The rest are rolled from the AUTHORED list exactly as
            // before, so a list like [Top, Top, Left] still favours Top 2:1. The order is then
            // shuffled so the guaranteed ones aren't always first.
            var distinct = new List<SpawnDirection>();
            foreach (var d in wave.spawnDirections)
                if (!distinct.Contains(d)) distinct.Add(d);
            Shuffle(distinct);

            for (int i = 0; i < _wavePlanPrefabs.Count; i++)
            {
                SpawnDirection dir = i < distinct.Count
                    ? distinct[i]
                    : wave.spawnDirections[UnityEngine.Random.Range(0, wave.spawnDirections.Count)];
                _wavePlanDirs.Add(dir);
            }

            Shuffle(_wavePlanDirs);
        }

        foreach (var dir in _wavePlanDirs)
            _wavePendingPerDir[(int)dir]++;

        if (debugLog)
        {
            var sb = new System.Text.StringBuilder();
            for (int d = 0; d < _wavePendingPerDir.Length; d++)
                if (_wavePendingPerDir[d] > 0)
                    sb.Append($"{(SpawnDirection)d}x{_wavePendingPerDir[d]} ");
            Debug.Log($"[Orchestrator] Wave plan: {_wavePlanPrefabs.Count} enemies from {sb}");
        }
    }

    // Keeps an arc lit on EVERY side the planned wave will use — through the between-wave
    // gap and while enemies are still being dealt. A side stops being refreshed once its
    // last enemy has spawned, and the arc then fades on its own (holdDuration). The arc's
    // hold (~4s) is shorter than a countdown can be, so arcs are refreshed, not lit once.
    //
    // Self-terminating: exits when a newer plan replaces this one (token), when the run
    // leaves the countdown/active states (game over, boss, rewind), or when every planned
    // enemy has spawned. No handle or teardown needed.
    private IEnumerator TelegraphWaveSides(int token)
    {
        const float refreshInterval = 1.5f;

        while (token == _waveTelegraphToken
               && (CurrentState == RunState.WaveCountdown || CurrentState == RunState.WaveActive))
        {
            bool anyPending = false;
            for (int d = 0; d < _wavePendingPerDir.Length; d++)
            {
                if (_wavePendingPerDir[d] <= 0) continue;
                anyPending = true;

                // Never light an arc the player can't see yet (stage intro / loading cover).
                if (!ScreenIsCovered && waveSpawner != null)
                    waveSpawner.ShowWaveIndicatorPublic((SpawnDirection)d);
            }

            if (!anyPending) yield break;

            yield return new WaitForSeconds(refreshInterval);
        }
    }

    private IEnumerator SpawnAndWaitForWave(WaveData wave, StageData stage)
    {
        // Extra delay before wave (from WaveData)
        if (wave.extraDelayBeforeStart > 0)
            yield return new WaitForSeconds(wave.extraDelayBeforeStart);

        // The plan is normally rolled by RunStage before the gap. If something reaches
        // here without one (or with a plan for a different wave), roll it now so the
        // spawn and its arcs still agree.
        if (_wavePlanFor != wave)
        {
            PrepareWavePlan(wave, stage);
            StartCoroutine(TelegraphWaveSides(_waveTelegraphToken));
        }

        // Local copies: a rewind / new plan mid-deal must not mutate the list we iterate.
        int token = _waveTelegraphToken;
        var prefabs = new List<GameObject>(_wavePlanPrefabs);
        var dirs = new List<SpawnDirection>(_wavePlanDirs);
        _wavePlanFor = null;   // consumed — the same WaveData object may be reused later

        // Track enemies
        enemiesAlive += prefabs.Count;

        // Music is handled by MusicDirector, which reacts to SetState(RunState.WaveActive)
        // just above this call. Driving it from here too meant two owners for one
        // FMOD parameter.

        // Spawn each enemy with delay, from the side the plan (and the arcs) promised.
        for (int i = 0; i < prefabs.Count; i++)
        {
            waveSpawner.SpawnEnemyPublic(prefabs[i], dirs[i]);

            // One fewer pending from this side. When a side hits 0 the telegraph stops
            // refreshing it; the real spawn just refreshed it, so it fades after holdDuration.
            if (token == _waveTelegraphToken)
                _wavePendingPerDir[(int)dirs[i]] = Mathf.Max(0, _wavePendingPerDir[(int)dirs[i]] - 1);

            // Apply stage scaling to spawn delay
            float baseDelay = UnityEngine.Random.Range(wave.minSpawnDelay, wave.maxSpawnDelay);
            float scaledDelay = baseDelay * stage.spawnDelayMultiplier;
            yield return new WaitForSeconds(Mathf.Max(0.2f, scaledDelay));
        }

        // Wait until all enemies are dead
        yield return WaitForAllEnemiesDead();

        // Music: OnWaveCleared (fired by RunStage right after this returns) is what
        // MusicDirector listens to for the wave-clear sting + drop back to Calm.
    }

    private IEnumerator WaitForAllEnemiesDead()
    {
        while (true)
        {
            if (CurrentState == RunState.GameOver) yield break;

            // Cross-check the counter against the actual scene state.
            // PERF: CountLivingEnemiesInScene is a FindObjectsByType over every
            // EnemyStats (array allocation + a GetComponent per enemy). It used to run
            // EVERY frame for the whole wave, cost growing with enemy count. The exit
            // needs BOTH conditions, so only pay for the scene scan once the cheap
            // counter already says zero. Exit behaviour is identical.
            if (enemiesAlive <= 0 && CountLivingEnemiesInScene(excludeBoss: false) <= 0)
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

    //  BOSS-PHASE ADDS
    //
    //  Deals procedural enemy batches from RunConfig's Enemy Pool for as long as the boss
    //  is alive, then stops on its own. Nothing else about a boss fight changes:
    //  WaitForBossDead still waits for the boss AND an empty arena, so whatever adds are
    //  still standing when the boss drops simply have to be cleared before the stage ends.
    //
    //  This runs OUTSIDE runCoroutine, which is parked inside WaitForBossDead for the
    //  whole fight — so every teardown path (game over, rewind) stops it explicitly.

    private void StartBossAdds(int stageIndex, bool isFinalBoss)
    {
        StopBossAdds();

        if (runConfig == null || waveSpawner == null) return;
        if (!(isFinalBoss ? runConfig.bossAddsDuringFinalBoss : runConfig.bossAddsDuringStageBoss))
            return;

        var pool = BuildEligibleEnemyPool(stageIndex, out float totalWeight);
        if (pool.Count == 0)
        {
            Debug.LogWarning($"[Orchestrator] Boss adds are ON but no Enemy Pool entry is eligible at " +
                             $"stage {stageIndex + 1}. Check the prefabs, weights and Min Stage Index.");
            return;
        }

        bossAddsRoutine = StartCoroutine(BossAddsLoop(pool, totalWeight, stageIndex));
    }

    private void StopBossAdds()
    {
        if (bossAddsRoutine != null) { StopCoroutine(bossAddsRoutine); bossAddsRoutine = null; }
    }

    private IEnumerator BossAddsLoop(List<EnemyPoolEntry> pool, float totalWeight, int stageIndex)
    {
        // Reuse the per-stage curves normal waves already ride, so adds scale in step
        // with the rest of the run instead of needing a second set of dials.
        StageData stage = (currentRunPlan != null && stageIndex >= 0 && stageIndex < currentRunPlan.Count)
            ? currentRunPlan[stageIndex]
            : null;
        float countMul = stage != null ? stage.enemyCountMultiplier : 1f;
        float delayMul = stage != null ? stage.spawnDelayMultiplier : 1f;

        yield return new WaitForSeconds(runConfig.bossAddsFirstDelay);

        while (CurrentState != RunState.GameOver && BossIsAlive())
        {
            // Same hard rule the wave path follows: nothing spawns behind the cover.
            if (!ScreenIsCovered)
            {
                int batch = Mathf.Max(1, Mathf.RoundToInt(runConfig.bossAddsPerBatch * countMul));
                SpawnDirection dir = (SpawnDirection)UnityEngine.Random.Range(0, 4);

                for (int i = 0; i < batch; i++)
                {
                    // The ceiling HOLDS the batch rather than shrinking it, so the pressure
                    // still arrives — just paced by how fast the players are clearing.
                    while (runConfig.bossAddsMaxAlive > 0
                           && CountLivingEnemiesInScene(excludeBoss: true) >= runConfig.bossAddsMaxAlive)
                    {
                        if (CurrentState == RunState.GameOver || !BossIsAlive()) yield break;
                        yield return new WaitForSeconds(0.25f);   // polling, not per-frame
                    }

                    if (CurrentState == RunState.GameOver || !BossIsAlive()) yield break;

                    waveSpawner.SpawnEnemyPublic(WeightedPick(pool, totalWeight), dir);

                    // WaveEnemy.OnDisable decrements this for every wave-marked enemy, adds
                    // included, so skipping the increment would desync the counter.
                    enemiesAlive++;

                    float d = UnityEngine.Random.Range(runConfig.proceduralMinSpawnDelay,
                                                       runConfig.proceduralMaxSpawnDelay) * delayMul;
                    yield return new WaitForSeconds(Mathf.Max(0.2f, d));
                }
            }

            yield return new WaitForSeconds(Mathf.Max(0.5f, UnityEngine.Random.Range(
                runConfig.bossAddsInterval.x, runConfig.bossAddsInterval.y)));
        }
    }

    // Is the boss we're waiting on still up? Uses the same "WaveEnemy got disabled"
    // signal WaitForBossDead does, so both agree on the moment of death.
    private bool BossIsAlive()
    {
        if (currentBossInstance == null || !currentBossInstance.activeInHierarchy) return false;
        var we = currentBossInstance.GetComponent<WaveEnemy>();
        return we == null || we.enabled;
    }

    //  BOSS SPAWNING
    /// Shows the post-stage choice menu: Heal All + bonus energy  OR  Augment + small energy.
    private IEnumerator ShowPostStageChoice(StageData stage)
    {
        SetState(RunState.AugmentSelect); // reuse existing state — gameplay is paused either way

        // Reward-screen appear SFX (covers both the prefab and legacy menus below).
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.rewardScreen.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.rewardScreen, Vector3.zero);
        }

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

            // Per-player application: heal that player's own health.
            // FIX: the energy bonus used to be granted INSIDE this loop, but the wallet
            // (EnergyManager) is SHARED — so a 2-player co-op stage paid out twice the
            // advertised reward, scaling with player count. Health is per-player and
            // stays in the loop; the shared bonus is now granted once below, at the
            // value of the most generous choice anyone made.
            int sharedBonus = 0;
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == StageClearScreenMenu.Choice.Heal)
                {
                    if (debugLog) Debug.Log($"[Orchestrator] P{i} picked RESTORE");
                    HealPlayerOnly(i);
                    sharedBonus = Mathf.Max(sharedBonus, scaledHealBonus);
                }
                else if (choices[i] == StageClearScreenMenu.Choice.Augment)
                {
                    if (debugLog) Debug.Log($"[Orchestrator] P{i} picked EMPOWER");
                    sharedBonus = Mathf.Max(sharedBonus, scaledAugmentBonus);
                }
            }
            GiveEnergyBonus(sharedBonus);

            // Augment menus for the players who chose Empower.
            if (augmentPlayers.Count > 0 && enableAugmentSelection)
            {
                // FIX: ShowAugmentSelectionForPlayers matches menus by boundPlayerIndex.
                // A co-op scene that only has the DEFAULT menu (boundPlayerIndex == -1)
                // matched nothing, so both players silently lost the augment they had
                // just traded their heal for. Fall back to the shared menu in that case.
                bool haveBoundMenus = false;
                for (int i = 0; i < augmentPlayers.Count; i++)
                    if (FindAugmentMenuForPlayer(augmentPlayers[i]) != null) { haveBoundMenus = true; break; }

                if (PlayerRegistry.Count > 1 && haveBoundMenus)
                    yield return ShowAugmentSelectionForPlayers(augmentPlayers);
                else if (augmentsMenu != null)
                    yield return ShowAugmentSelection($"stage {stage.stageIndex + 1} post-stage choice");
                else
                    Debug.LogWarning("[Orchestrator] Empower was chosen but no AugmentsMenu exists — reward skipped.");
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

        // Players.
        // FIX (co-op): this healed FindFirstObjectByType<PlayerStats>() — ONE arbitrary
        // player. Reached through the legacy PostStageChoiceMenu fallback, it meant P2
        // got nothing from a "Heal All" reward they had just paid a stage for.
        int healed = 0;
        var reg = PlayerRegistry.Instance;
        if (reg != null && PlayerRegistry.Count > 0)
        {
            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                var stats = all[i] != null ? all[i].Stats : null;
                if (stats == null) continue;
                float miss = stats.maxHealth - stats.currentHealth;
                if (miss > 0f) stats.Heal(miss);
                healed++;
            }
        }
        else
        {
            var player = FindFirstObjectByType<PlayerStats>();
            if (player != null)
            {
                float miss = player.maxHealth - player.currentHealth;
                if (miss > 0f) player.Heal(miss);
                healed = 1;
            }
        }

        if (debugLog)
            Debug.Log($"[Orchestrator] Healed: core={(core != null)}, towers={(towers?.Length ?? 0)}, players={healed}");
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

        // The orchestrator owns the freeze via UIModalStack. It must NOT snapshot
        // Time.timeScale: when this fires while the pause menu is already up the
        // snapshot is 0, and restoring it after the player un-pauses re-freezes the
        // game with no menu on screen and no way to un-freeze. The stack recomputes
        // the correct state from whatever is actually open.
        UIModalStack.Push(_augmentModal);
        _augmentModalHeld = true;

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

        // Wait until every opened menu has closed. Each AugmentsMenu now pops only its
        // OWN stack layer on close, so the first player to confirm can no longer
        // un-freeze the game while the second is still choosing — this layer holds it.
        bool anyOpen = true;
        while (anyOpen)
        {
            anyOpen = false;
            foreach (var m in opened)
                if (m != null && m.augmentsMenu != null && m.augmentsMenu.activeSelf) { anyOpen = true; break; }
            if (anyOpen) yield return new WaitForSecondsRealtime(0.1f);
        }

        // Hold suppression until every gamepad trigger is released (see the note
        // in ShowAugmentSelection) so a trigger held to confirm can't resume with
        // the attack dead until re-pulled. No-ops without a held pad trigger.
        // Reassert() is insurance against any OTHER screen that still writes
        // Time.timeScale directly (WeaponBlueprintMenu / LoreArchiveMenu).
        UIModalStack.Reassert();
        yield return MenuInputGuard.WaitForGamepadTriggersReleased();
        UIModalStack.Pop(_augmentModal);
        _augmentModalHeld = false;

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

        // The orchestrator owns the freeze and opens every player's menu, waiting for ALL.
        // Always take ownership — not only in co-op. In single player the menu used to
        // manage the clock alone, so opening it on top of the pause menu (the wave ends
        // on the same frame you press Esc) left the two fighting over Time.timeScale.
        UIModalStack.Push(_augmentModal);
        _augmentModalHeld = true;

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

        // Hold suppression until every gamepad trigger is released. A trigger still
        // held from confirming the menu had its press-edge swallowed while suppressed;
        // resuming now would leave that attack dead until re-pulled.
        // WaitForGamepadTriggersReleased is frame-based + unscaled, so it advances
        // while still frozen at timeScale 0 and no-ops with no pad/trigger held.
        UIModalStack.Reassert();
        yield return MenuInputGuard.WaitForGamepadTriggersReleased();
        UIModalStack.Pop(_augmentModal);
        _augmentModalHeld = false;

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

    // ─────────────────────────────────────────────────────────────────────
    //  BOSS NAME RESOLUTION
    //  Reads a human-readable name off a boss PREFAB, before it is instantiated.
    //    1. BaseBossStats.bossDisplayName  <- for bosses with no EnemyData asset
    //    2. EnemyData.enemyName            <- for bosses that have one
    //    3. the prefab's GameObject name, tidied ("Boss5_Prefab" -> "Boss5 Prefab")
    //  Works for every boss - Boss1, Boss3, the final boss, anything added later - with
    //  zero per-boss wiring here.
    private static string ResolveBossName(GameObject bossPrefab, string fallback = "Boss")
    {
        if (bossPrefab == null) return fallback;

        // BaseBossStats.DisplayName already walks all three tiers, so one call covers
        // named-by-field bosses, EnemyData bosses and unnamed ones alike.
        var boss = bossPrefab.GetComponentInChildren<BaseBossStats>(true);
        if (boss != null) return boss.DisplayName;

        // Not a BaseBossStats boss (a beefed-up regular enemy used as one, say).
        var stats = bossPrefab.GetComponentInChildren<EnemyStats>(true);
        if (stats != null && stats.enemyData != null
            && !string.IsNullOrWhiteSpace(stats.enemyData.enemyName))
            return stats.enemyData.enemyName.Trim();

        return bossPrefab.name.Replace("(Clone)", "").Replace('_', ' ').Trim();
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

        // ── Boss intro cinematic ──────────────────────────────────────────
        // The moment the boss exists, focus the camera on it: single player pans +
        // zooms onto the boss; co-op momentarily leaves the split for one full-screen
        // zoom, then returns. No-op if no BossZoomController is present in the scene,
        // so this is safe/opt-in — drop one BossZoomController into GameScene to enable.
        if (currentBossInstance != null && BossZoomController.Instance != null)
            BossZoomController.Instance.PlayIntro(currentBossInstance);
    }

    // How long the boss-dead wait may spin with the boss at 0 HP before we assume its
    // death teardown failed and force it. Boss death routines hand off to
    // EnemyDeathVFX and never call Destroy(gameObject) themselves, so a VFX that
    // throws or never completes used to hang WaitForBossDead — and therefore the whole
    // run — forever. Generous, so a long legitimate disintegration can never trip it.
    private const float BossDeathTimeoutSeconds = 30f;

    // Waits until the specific boss instance is dead AND no other enemies are alive.
    private IEnumerator WaitForBossDead()
    {
        // Only starts counting once the boss has actually reached 0 HP, so a long
        // fight is never affected — this measures the TEARDOWN, not the fight.
        float deadButNotGoneTimer = 0f;

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

            // Cross-check the scene for ANY other living enemy.
            // PERF: short-circuit - the scene scan (FindObjectsByType) only matters once
            // the boss is gone, so it no longer runs every frame of the boss fight.
            if (!bossStillAlive && CountLivingEnemiesInScene(excludeBoss: true) == 0)
            {
                currentBossInstance = null;
                yield break;
            }

            // Fail-safe. If the boss object is still around but its health is gone, its
            // death routine ran and the teardown stalled. Force the destroy so the run
            // can continue instead of locking. This cannot fire during a live fight:
            // it requires currentHealth <= 0.
            if (bossStillAlive && currentBossInstance != null)
            {
                var bossStats = currentBossInstance.GetComponent<EnemyStats>();
                if (bossStats != null && bossStats.currentHealth <= 0f)
                {
                    // FIX: was Time.deltaTime, which is 0 while paused — the failsafe
                    // could never fire if the player paused during a stalled teardown.
                    deadButNotGoneTimer += Time.unscaledDeltaTime;
                    if (deadButNotGoneTimer >= BossDeathTimeoutSeconds)
                    {
                        Debug.LogError(
                            "[Orchestrator] Boss reached 0 HP but its death teardown never " +
                            $"completed after {BossDeathTimeoutSeconds}s. Forcing destroy so the " +
                            "run can continue. Check the boss death VFX / EnemyDeathVFX.");
                        Destroy(currentBossInstance);
                        currentBossInstance = null;
                        yield break;
                    }
                }
                else
                {
                    deadButNotGoneTimer = 0f;
                }
            }
            else
            {
                deadButNotGoneTimer = 0f;
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

            // Warm the final boss's sprites while the screen is black. Async, so the
            // overlay keeps rendering and its reveal watchdog cannot trip.
            if (runConfig.finalBossPrefab != null)
                yield return PrewarmProviders(runConfig.finalBossPrefab);

            yield return transitionOverlay.ShowBossMessage(
                ResolveBossName(runConfig.finalBossPrefab, "Final Boss"), "FINAL BOSS", 2f);
            yield return transitionOverlay.FadeIn(0.7f);
        }

        // Update counter for final boss - its real name, not the generic label.
        if (transitionOverlay != null)
            transitionOverlay.SetBossCounter(ResolveBossName(runConfig.finalBossPrefab, "Final Boss"));

        OnBossSpawned?.Invoke(null);

        yield return new WaitForSecondsRealtime(1f);

        SpawnBoss(runConfig.finalBossPrefab);

        // The final boss sits outside the stage loop, so CurrentStageIndex has already
        // run past the end of the plan. Scale its adds off the LAST stage instead.
        StartBossAdds(Mathf.Max(0, (currentRunPlan != null ? currentRunPlan.Count : 1) - 1),
                      isFinalBoss: true);

        // Snapshot the start of the final boss fight so the clock can rewind to
        // exactly here (re-running only the final boss, not the last stage).
        WaveCheckpointService.Instance?.CaptureFinalBossSnapshot();

        // Cross-session save: persist progress at the START of the final boss so a
        // crash/exit during the fight resumes straight into the final boss (the last
        // stage's boss and post-stage reward already completed, not re-offered),
        // rather than rewinding to the last wave of the last stage.
        WriteFinalBossAutoSave();

        yield return WaitForBossDead();
        StopBossAdds();

        // Final boss down. Fire the SAME event the stage bosses use, with -1 as the
        // stage index to mean "this was the final boss" — mirroring the existing
        // OnBossSpawned(null) convention that RunProgressBar already relies on.
        // Without this there is no event at all for the final kill: OnVictory doesn't
        // land until ~2.75s later (2s here + 0.75s in FinishRun), which is far too
        // late for music or a stinger.
        OnBossKilled?.Invoke(-1);

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
        BootProfiler.Mark("[PERF] ApplyBiome START (biome+map build)");
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
        BootProfiler.Mark("[PERF] ApplyBiome END / Prewarm BEGIN");
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

            // Roll weather, gated by each biome's allowed weather (single source of
            // truth = BiomeWeatherDefaults). A biome only gets rain/snow if it supports
            // it AND the chance roll passes. Snow biome always snows; Marsh can rain.
            //
            // These are hoisted out of the object initializer (they used to be inline)
            // so balloonsEnabled can depend on nightMode. The ORDER and the
            // short-circuiting of the Random.value calls are preserved exactly, so a
            // given run seed still produces the identical run plan.
            bool rollNight = UnityEngine.Random.value < runConfig.nightModeChance;
            bool rollFog = UnityEngine.Random.value < runConfig.fogChance;
            bool rollRain = BiomeWeatherDefaults.ForBiome(biome).rainEnabled
                            && UnityEngine.Random.value < runConfig.rainChance;
            bool rollSnow = BiomeWeatherDefaults.ForBiome(biome).snowEnabled
                            && (biome == BiomeType.Snow
                                || UnityEngine.Random.value < runConfig.snowChance);

            // FIX: balloons were rolled independently of nightMode. RunConfig's own
            // tooltip says lantern balloons are only visible at night, so a daylight
            // stage still got [BALLOONS] in StageData.ToString() and in the stage
            // banner, and the roll was wasted. Gate it on night.
            bool rollBalloons = (UnityEngine.Random.value < runConfig.nightBalloonChance)
                                && rollNight;

            var stage = new StageData
            {
                stageIndex = i,
                biome = biome,
                layout = layout,

                nightMode = rollNight,
                fogEnabled = rollFog,
                rainEnabled = rollRain,
                snowEnabled = rollSnow,
                balloonsEnabled = rollBalloons,

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
                // Refill if we need more stages than available biomes.
                pool = new List<BiomeType>(runConfig.availableBiomes);

                // FIX: a plain refill could deal the biome we JUST used again, so with
                // allowRepeatBiomes = false you could still get Desert -> Desert across
                // the refill boundary — which reads as the setting being broken.
                if (!runConfig.allowRepeatBiomes && sequence.Count > 0 && pool.Count > 1)
                    pool.Remove(sequence[sequence.Count - 1]);
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
        var eligible = BuildEligibleEnemyPool(stageIndex, out float totalWeight);

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
                // How many sides this wave attacks from follows RunConfig.directionsPerStage
                // (e.g. Stage 1: 1-2, Stage 2: 1-3, Stage 3: 2-3). Rolled here, inside the
                // seeded plan generation, so a resumed run gets the same sides back.
                spawnDirections = RollWaveDirections(stageIndex),
                oneDirectionForAllEnemies = false,
                minSpawnDelay = runConfig.proceduralMinSpawnDelay,
                maxSpawnDelay = runConfig.proceduralMaxSpawnDelay,
                enemies = groups
            });
        }

        return result;
    }

    // Picks which sides one procedural wave attacks from, using the stage's rule in
    // RunConfig.directionsPerStage. Element 0 = Stage 1; stages past the end of the list
    // reuse the last element. An empty list keeps the old behaviour (all four sides) and
    // makes NO random calls, so runs without a rule roll exactly the same plan as before.
    private List<SpawnDirection> RollWaveDirections(int stageIndex)
    {
        var all = new List<SpawnDirection>
        {
            SpawnDirection.Top, SpawnDirection.Bottom,
            SpawnDirection.Left, SpawnDirection.Right
        };

        var rules = runConfig != null ? runConfig.directionsPerStage : null;
        if (rules == null || rules.Count == 0) return all;

        WaveDirectionRule rule = rules[Mathf.Clamp(stageIndex, 0, rules.Count - 1)];
        if (rule == null) return all;

        // Tolerate min/max entered the wrong way round, and keep both inside 1..4.
        int lo = Mathf.Clamp(Mathf.Min(rule.minDirections, rule.maxDirections), 1, all.Count);
        int hi = Mathf.Clamp(Mathf.Max(rule.minDirections, rule.maxDirections), 1, all.Count);
        int count = UnityEngine.Random.Range(lo, hi + 1);   // int Range: max is exclusive

        Shuffle(all);
        all.RemoveRange(count, all.Count - count);
        return all;
    }

    // Enemy Pool entries that may appear at this stage (0-based), plus their summed
    // weight for WeightedPick. Lifted out of GenerateProceduralWaves unchanged so
    // procedural waves and boss-phase adds draw from exactly the same filter.
    private List<EnemyPoolEntry> BuildEligibleEnemyPool(int stageIndex, out float totalWeight)
    {
        var eligible = new List<EnemyPoolEntry>();
        totalWeight = 0f;

        if (runConfig.enemyPool != null)
            foreach (var e in runConfig.enemyPool)
                if (e != null && e.enemyPrefab != null && e.weight > 0f && stageIndex >= e.minStageIndex)
                {
                    eligible.Add(e);
                    totalWeight += e.weight;
                }

        return eligible;
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

    // One-shot "WaveStart" cue the instant enemies go live. The event is 3D (it has a
    // Spatializer), so it is played at the player's position — the nearest thing to a
    // "centered" point for a whole-screen cue — falling back to the camera, then origin.
    // In co-op it plays once, near player 1; that reads fine for a non-diegetic cue.
    private void PlayWaveStartCue()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return;
        if (FMODEvents.instance.waveStart.IsNull) return;

        Vector3 pos;
        var pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pos = pm.transform.position;
        else if (Camera.main != null) pos = Camera.main.transform.position;
        else pos = Vector3.zero;

        AudioManager.instance.PlayOneShot(FMODEvents.instance.waveStart, pos);
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

        // FIX: this was an `else if` off the spawner check, so the most useful warning
        // in the file was suppressed in exactly the case where setup was most broken.
        // Also skipped when procedural waves are on — the pool is genuinely unused then.
        if (runConfig != null && !runConfig.useProceduralWaves
            && (runConfig.waveConfigPool == null || runConfig.waveConfigPool.Count == 0))
            Debug.LogWarning("[Orchestrator] ⚠ RunConfig has no WaveConfigs in pool. " +
                             "Drag your existing WaveConfig.asset into Run Config → Wave Config Pool.");

        if (runConfig != null && runConfig.useProceduralWaves
            && (runConfig.enemyPool == null || runConfig.enemyPool.Count == 0))
            Debug.LogError("[Orchestrator] ❌ Use Procedural Waves is ON but Enemy Pool is empty — " +
                           "every stage will generate ZERO waves and jump straight to the boss.");
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


// Implemented by components whose sprite art lives on the component itself (not on
// EnemyData) — the Insect burrow animator, the Parfumer body, etc. Lets
// GameOrchestrator warm those folders up front with no manual wiring: it finds every
// implementer on each enemy/boss prefab and asks it to warm its own folders.
//
// IMPORTANT: PrewarmSpriteFolders() is called on the PREFAB (no instance is created),
// so implementations must ONLY read their serialized fields and call the static
// folder caches (EnemyAnimationController.LoadFolderCached / ParfumerController.Prewarm).
// No scene work, no coroutines, no AddComponent — nothing that needs a live instance.
public interface ISpritePrewarm
{
    void PrewarmSpriteFolders();
}


