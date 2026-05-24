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
    private PostStageChoiceMenu postStageChoiceMenu;

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

    /// <summary>Current state of the run. Read from UI scripts to show banners etc.</summary>
    public RunState CurrentState { get; private set; } = RunState.Idle;

    /// <summary>Which stage we're on (0-based).</summary>
    public int CurrentStageIndex { get; private set; }

    /// <summary>Which wave within the current stage (0-based).</summary>
    public int CurrentWaveInStage { get; private set; }

    /// <summary>Total stages in this run.</summary>
    public int TotalStages => currentRunPlan?.Count ?? 0;

    /// <summary>Total waves in current stage.</summary>
    public int TotalWavesInCurrentStage => GetCurrentStage()?.waves?.Count ?? 0;

    /// <summary>The biome sequence for this run (for UI map screens etc).</summary>
    public List<StageData> RunPlan => currentRunPlan;

    //  EVENTS (subscribe from UI, audio, etc.)

    /// <summary>Fired on every state change. Use for UI transitions.</summary>
    public event Action<RunState, RunState> OnStateChanged;   // (oldState, newState)

    /// <summary>Fired when a new stage begins. Use for "Stage 2: Desert" banners.</summary>
    public event Action<StageData> OnStageStarted;

    /// <summary>Fired when a wave begins. Use for "Wave 5/8" indicators.</summary>
    public event Action<int, int> OnWaveStarted;              // (waveIndex, totalWaves)

    /// <summary>Fired when all enemies in a wave are dead.</summary>
    public event Action<int> OnWaveCleared;                   // (waveIndex)

    /// <summary>Fired when a stage boss appears.</summary>
    public event Action<StageData> OnBossSpawned;

    /// <summary>Fired when stage boss is killed.</summary>
    public event Action<int> OnBossKilled;                    // (stageIndex)

    /// <summary>Fired when the entire run is won.</summary>
    public event Action OnVictory;

    /// <summary>Fired on game over.</summary>
    public event Action OnGameOver;

    //  PRIVATE STATE

    private List<StageData> currentRunPlan;
    private Coroutine runCoroutine;
    private int enemiesAlive;
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

            // Initialize immediately so the black screen covers the initial biome load
            transitionOverlay.EnsureInitialized();
        }

        // Find augment menu (searches inactive objects too)
        augmentsMenu = FindFirstObjectByType<AugmentsMenu>(FindObjectsInactive.Include);

        // Create post-stage choice menu (runtime-built, no prefab needed)
        if (enablePostStageChoice)
        {
            postStageChoiceMenu = GetComponent<PostStageChoiceMenu>();
            if (postStageChoiceMenu == null)
                postStageChoiceMenu = gameObject.AddComponent<PostStageChoiceMenu>();
        }
    }

    void Start()
    {
        ValidateSetup();

        if (autoStartRun)
        {
            StartRun();
        }
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

    /// Call this when an enemy dies. The orchestrator tracks alive counts.
    /// Hook this up from your enemy death logic.
    public void OnEnemyDeath()
    {
        enemiesAlive = Mathf.Max(0, enemiesAlive - 1);

        if (debugLog && enemiesAlive <= 3)
            Debug.Log($"[Orchestrator] Enemy died. Remaining: {enemiesAlive}");
    }

    /// Call this when the central core is destroyed (game over).
    public void TriggerGameOver()
    {
        if (CurrentState == RunState.GameOver || CurrentState == RunState.Victory)
            return;

        if (runCoroutine != null)
            StopCoroutine(runCoroutine);

        SetState(RunState.GameOver);
        OnGameOver?.Invoke();

        Debug.Log("[Orchestrator] ══ GAME OVER ══");
    }

    /// <summary>Get the current stage data (or null if no run active).</summary>
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

        // ── Final Boss ──
        if (runConfig.hasFinalBoss && runConfig.finalBossPrefab != null)
        {
            yield return RunFinalBoss();
            if (CurrentState == RunState.GameOver)
                yield break;
        }

        // ── Victory! ──
        // Defensive: force gameplay timescale back to 1 so the visual sequence below can't hang.
        Time.timeScale = 1f;

        if (enableTransitions && transitionOverlay != null)
        {
            yield return new WaitForSecondsRealtime(1f);
            yield return transitionOverlay.FadeOut(0.8f);
            yield return transitionOverlay.ShowMessage("VICTORY", "", 3f);
            // Leave screen faded — your menu/restart UI takes over from here
        }

        SetState(RunState.Victory);
        OnVictory?.Invoke();
        Debug.Log("[Orchestrator] ═══ VICTORY! ═══");
    }

    //  STAGE FLOW

    private IEnumerator RunStage(StageData stage)
    {
        if (debugLog)
            Debug.Log($"[Orchestrator] ─── {stage} ───");

        //  1. STAGE INTRO: Fade out → swap biome → show banner → fade in
        SetState(RunState.StageIntro);

        if (enableTransitions && transitionOverlay != null)
        {
            if (stage.stageIndex > 0)
            {
                // Not the first stage — fade to black first
                yield return transitionOverlay.FadeOut();
            }
            // First stage: screen is already black (startBlack=true), no fade-out needed

            // Swap biome while screen is black
            ApplyBiome(stage);
            OnStageStarted?.Invoke(stage);

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
            // No transitions — instant swap (original behavior)
            ApplyBiome(stage);
            OnStageStarted?.Invoke(stage);

            if (waveSpawner != null)
                waveSpawner.enemySpawnCountMultiplier = stage.enemyCountMultiplier;

            yield return new WaitForSeconds(runConfig.timeBetweenStages);
        }

        //  2. WAVES: Play through each wave 
        for (CurrentWaveInStage = 0; CurrentWaveInStage < stage.waves.Count; CurrentWaveInStage++)
        {
            // Check game over
            if (CurrentState == RunState.GameOver) yield break;

            WaveData wave = stage.waves[CurrentWaveInStage];

            // Brief countdown before wave
            SetState(RunState.WaveCountdown);
            OnWaveStarted?.Invoke(CurrentWaveInStage, stage.waves.Count);

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
        if (stage.hasStageBoss && runConfig.stageBossPrefab != null)
        {
            SetState(RunState.StageBoss);
            OnBossSpawned?.Invoke(stage);

            if (debugLog)
                Debug.Log($"[Orchestrator] STAGE BOSS spawning!");

            // Update counter and flash
            if (transitionOverlay != null)
            {
                transitionOverlay.SetWaveCounter("BOSS");
                transitionOverlay.FlashWaveStart("BOSS", 1.5f);
            }

            SpawnBoss(runConfig.stageBossPrefab);
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

    //  WAVE SPAWNING (uses your existing WaveSpawner)
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

            // Cross-check the counter against the actual scene state. We scan for
            // EnemyStats (not WaveEnemy) because that's the component on EVERY
            // enemy — including ones spawned by other enemies (e.g. boss minions,
            // splitter add-ons) which never go through WaveSpawner.SpawnEnemy and
            // therefore never get a WaveEnemy marker. Without this, a "wave clear"
            // could be declared while add-on enemies are still alive.
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
    //
    // GREMLIN EXCLUSION: Gremlins spawn ambiently throughout the run, completely
    // independent of wave/boss combat. They MUST NOT count toward "is the wave
    // cleared" or "is the boss room clear" — if they did, the orchestrator would
    // softlock the moment a gremlin was on screen (which is most of the time).
    // They're identified by the GremlinController component, which is unique to them.
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

        augmentsMenu.ResetRerolls();
        augmentsMenu.ActivateAugments();

        yield return WaitForAugmentMenuClosed();

        if (debugLog)
            Debug.Log($"[Orchestrator] Augment selected, continuing...");
    }

    // Waits until the AugmentsMenu is closed (player made a selection).
    // Uses WaitForSecondsRealtime because Time.timeScale is 0 while the menu is open.
    private IEnumerator WaitForAugmentMenuClosed()
    {
        // The augment menu sets Time.timeScale = 0 when open.
        // Normal yield return null won't advance when timeScale is 0.
        // So we poll using realtime waits.
        while (augmentsMenu != null && augmentsMenu.augmentsMenu != null
               && augmentsMenu.augmentsMenu.activeSelf)
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

            // Cross-check the scene for ANY other living enemy. We scan EnemyStats
            // (not WaveEnemy) because boss minions / add-ons spawned outside the
            // WaveSpawner.SpawnEnemy path never receive a WaveEnemy marker, but they
            // DO have EnemyStats. Scanning only WaveEnemy would miss them and the
            // reward menu would pop the moment the boss died — even while minions
            // were still on screen. This is the root cause of the post-stage menu
            // appearing prematurely after a boss kill.
            int otherEnemiesAlive = CountLivingEnemiesInScene(excludeBoss: true);

            // Note: we deliberately no longer require `enemiesAlive <= 0`. That counter
            // tracks orchestrator-spawned enemies only; boss-spawned minions are missing
            // from it (births not counted, deaths still decrement it — it clamps to 0).
            // The scene scan above is the source of truth.
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
            Debug.Log("[Orchestrator] ═══ FINAL BOSS ═══");

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

        // Set the biome (this switches background, overlays, obstacles, etc.)
        biomeManager.SetBiome(stage.biome);

        // Apply weather modifiers
        biomeManager.SetNightMode(stage.nightMode);
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

                waves = PickWavesForStage(i),
                hasStageBoss = (runConfig.stageBossPrefab != null),
            };

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

    // Picks waves for a stage from the WaveConfig pool.
    // - If ONE WaveConfig with 3 waves and need 8: it cycles through them
    // - If MULTIPLE WaveConfigs: it picks waves from random configs
    private List<WaveData> PickWavesForStage(int stageIndex)
    {
        var result = new List<WaveData>();
        int needed = runConfig.wavesPerStage;

        if (runConfig.waveConfigPool == null || runConfig.waveConfigPool.Count == 0)
        {
            Debug.LogWarning("[Orchestrator] No WaveConfigs in pool! Using empty waves.");
            return result;
        }

        // Collect ALL waves from ALL configs into one big pool
        var allWaves = new List<WaveData>();
        foreach (var config in runConfig.waveConfigPool)
        {
            if (config != null && config.waves != null)
                allWaves.AddRange(config.waves);
        }

        if (allWaves.Count == 0)
        {
            Debug.LogWarning("[Orchestrator] WaveConfig pool has no waves defined!");
            return result;
        }

        // Pick waves (cycle if we don't have enough unique ones)
        for (int i = 0; i < needed; i++)
        {
            int index = i % allWaves.Count;
            result.Add(allWaves[index]);
        }

        return result;
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
