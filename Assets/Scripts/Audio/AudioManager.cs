using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

[DefaultExecutionOrder(-100)] // Ensure AudioManager initializes before other scripts
public class AudioManager : MonoBehaviour
{
    [Header("Volume")]
    // The four volumes are stored in the serialized fields below (the "m_" prefix is
    // hidden by Unity, so the Inspector still shows "Master Volume", "Music Volume"...)
    // and exposed to other scripts through the properties further down. Every write
    // from code goes through a property setter, which can log WHO changed it — the
    // console stack trace under each "[AudioManager] ... volume" line names the script.
    // FormerlySerializedAs keeps the values already saved in prefabs and scenes.
    [Range(0, 1)]
    [UnityEngine.Serialization.FormerlySerializedAs("masterVolume")]
    [SerializeField] private float m_MasterVolume = 1;
    [Range(0, 1)]
    [UnityEngine.Serialization.FormerlySerializedAs("musicVolume")]
    [SerializeField] private float m_MusicVolume = 1;
    [Range(0, 1)]
    [UnityEngine.Serialization.FormerlySerializedAs("ambienceVolume")]
    [SerializeField] private float m_AmbienceVolume = 1;
    [Range(0, 1)]
    [UnityEngine.Serialization.FormerlySerializedAs("SFXVolume")]
    [SerializeField] private float m_SFXVolume = 1;

    [Tooltip("Log every time a script changes a volume, with the caller in the stack " +
             "trace. Turn off once you've found what was overriding the Inspector values.")]
    public bool logVolumeChanges = true;

    public float masterVolume { get => m_MasterVolume; set => SetVolume(ref m_MasterVolume, value, "Master"); }
    public float musicVolume { get => m_MusicVolume; set => SetVolume(ref m_MusicVolume, value, "Music"); }
    public float ambienceVolume { get => m_AmbienceVolume; set => SetVolume(ref m_AmbienceVolume, value, "Ambience"); }
    public float SFXVolume { get => m_SFXVolume; set => SetVolume(ref m_SFXVolume, value, "SFX"); }

    private void SetVolume(ref float field, float value, string label)
    {
        if (Mathf.Approximately(field, value)) return;
        if (logVolumeChanges)
            Debug.Log($"[AudioManager] {label} volume changed by code: {field:0.00} -> {value:0.00} " +
                      "(see the stack trace below for the script that did it)", this);
        field = value;
    }

    [Header("Music Settings")]
    public bool musicEnabled = true; // Enable by default
    private bool previousMusicEnabled = true;

    //  Which gameplay tracks are eligible for random selection 
    // One tick-box per candidate track. Untick a track to KEEP it out of the random
    // rotation, tick it to allow it. The list auto-fills with every candidate track
    // (see CandidateTrackNames), so new tracks show up here automatically and default
    // to included. This only controls the random GAMEPLAY pool — the dedicated menu
    // track (MusicMenu) is unaffected.
    [System.Serializable]
    public struct MusicTrackOption
    {
        public string name;               // matches a name in CandidateTrackNames
        public bool includeInRandomPool;  // untick to exclude from random play
    }

    [Header("Random Music Pool")]
    [Tooltip("Gameplay tracks eligible for random selection each run. Untick a track to " +
             "exclude it from random play; tick to include it. Auto-fills with all " +
             "candidate tracks — leave a new one ticked to have it join the rotation.")]
    public List<MusicTrackOption> randomTrackPool = new List<MusicTrackOption>();

    [Header("Debug")]
    public bool enableDebugLogs = false;

    private Bus masterBus;
    private Bus musicBus;
    private Bus ambienceBus;
    private Bus sfxBus;

    private List<EventInstance> eventInstances;
    private List<StudioEventEmitter> eventEmitters;

    private EventInstance ambienceEventInstance;
    private EventInstance musicEventInstance;
    private bool musicInitialized = false;
    private bool fmodInitialized = false;

    //  Random gameplay music track pool 
    // A run's background music is ONE FMOD event chosen at random from the pool
    // below (built from the four gameplay music EventReferences on FMODEvents). A
    // fresh track is rolled each time a run begins — AudioManager.UpdateMusicContext()
    // does this the moment a GameOrchestrator appears — and the Options menu's
    // "Switch Track" button rolls a different one on demand. The main menu plays a
    // separate dedicated track instead (see menu music, below).

    private struct MusicTrack { public string name; public EventReference reference; }
    private readonly List<MusicTrack> _musicTracks = new List<MusicTrack>();
    private int _currentTrackIndex = -1;
    // Which track the live gameplay instance was built from (-1 = none). When this
    // differs from _currentTrackIndex, PlayGameplayMusic rebuilds the bed.
    private int _loadedTrackIndex = -1;
    // Latches true once we learn the loaded event has no "MusicSection" parameter,
    // so simple (non-adaptive) tracks don't spam an error on every state change.
    // Cleared whenever the track changes.
    private bool _sectionParamMissing = false;
    // Sections we've already warned about for the CURRENT track (so a track with no
    // Boss region warns once, not on every boss fight). Cleared when the track changes.
    private readonly HashSet<MusicSection> _warnedMissingSections = new HashSet<MusicSection>();

    // Canonical list of gameplay tracks that CAN enter the random pool, in a fixed
    // order. This is the single place to register a track for random play. To add one:
    //    add its EventReference to FMODEvents,
    //    add its display name here,
    //    map that name to the reference in ReferenceForTrackName() below.
    // It then appears automatically as a tick-box under "Random Music Pool" (included
    // by default), and an unassigned reference is skipped so partial wiring is fine.
    private static readonly string[] CandidateTrackNames =
    {
        "Ambient", "Calm", "Electronic", "Piano",   // original four
        "Guitar", "Clavi", "Orchestral", "Starting" // newly added 
    };

    //  Dedicated menu music (MusicMenu) 
    // The main menu plays its own FMOD event, separate from the gameplay tracks, so
    // the two can cross-fade when a run starts / ends. MusicDirector routes the
    // "Menu" section here and every other section to the gameplay bed.
    private EventInstance menuMusicInstance;
    private bool menuMusicInitialized = false;
    private bool _menuMusicActive = false;   // is the menu bed the currently-audible one?
    private bool _warnedMenuMusicMissing = false;
    [Tooltip("Seconds to cross-fade between the menu track and the gameplay track.")]
    public float musicCrossfadeSeconds = 1.5f;
    [Tooltip("Fallback FMOD event path for the menu track, used if FMODEvents.musicMenu " +
             "is empty on the running instance. Leave as-is unless you renamed the event.")]
    public string menuMusicEventPath = "event:/Music/MusicMenu";
    private Coroutine _menuFade;
    private Coroutine _gameplayFade;

    // Set on a copy that loses the singleton race. Such a copy must never run
    // CleanUp() in OnDestroy, or it would tear down the real instance's FMOD state.
    private bool isDuplicate = false;

    public static AudioManager instance { get; private set; }

    private void Awake()
    {
        //  Singleton 
        // A duplicate can appear when a scene still carries its own audio object
        // while AudioBootstrap has already spawned the persistent one. Destroy the
        // COMPONENT, not the GameObject: this script may live on a shared
        // "Managers" object whose other components must survive.
        if (instance != null && instance != this)
        {
            isDuplicate = true;
            Debug.LogWarning(
                $"[AudioManager] Duplicate found on '{gameObject.name}' in scene " +
                $"'{gameObject.scene.name}'. Destroying it. Remove the audio object from " +
                "this scene - AudioBootstrap provides a persistent one in every scene.");
            Destroy(this);
            return;
        }
        instance = this;

        if (logVolumeChanges)
            Debug.Log($"[AudioManager] Live AudioManager is on '{gameObject.name}' " +
                      $"(scene '{gameObject.scene.name}'). Starting volumes: master {m_MasterVolume:0.00}, " +
                      $"music {m_MusicVolume:0.00}, ambience {m_AmbienceVolume:0.00}, SFX {m_SFXVolume:0.00}", this);

        // Make sure every candidate track has a tick-box entry even when this came from
        // a prefab instantiated at runtime (OnValidate doesn't run in that path).
        SeedRandomTrackPool();

        //  Persistence 
        // DontDestroyOnLoad only works on root objects. If this sits under a
        // "Managers" (or similar) parent, detach it first, keeping world position.
        // FMODEvents does the same for itself, so it survives whether it is a
        // child of this object or a sibling under the same parent.
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);

        eventInstances = new List<EventInstance>();
        eventEmitters = new List<StudioEventEmitter>();
        StartCoroutine(InitializeFMOD());
    }

    private void Start()
    {
        // Apply the player's saved volumes (or the Options menu defaults) as soon as
        // this AudioManager exists, whatever order the startup hooks ran in.
        if (!isDuplicate) OptionsMenu.ApplySavedSettings();
    }

    private IEnumerator InitializeFMOD()
    {
        if (enableDebugLogs) Debug.Log("Initializing FMOD...");

        // Buses valid ≠ FMOD ready. Bus validity only proves the MASTER bank loaded
        // (the buses live there); the banks that hold the EVENTS can still be streaming
        // from disk in a build. We therefore keep bus-readiness in a LOCAL and only set
        // the public fmodInitialized flag AFTER every bank + its sample data is resident
        // (see the bank-wait below). That single flag gates PlayOneShot and the sample
        // preload alike, so this closes the intermittent "wave start / boss zoom
        // sometimes don't play in the build" race.
        bool busesValid = false;

        // Wait for FMOD to be ready
        int attempts = 0;
        while (attempts < 100) // Max 5 seconds
        {
            try
            {
                masterBus = RuntimeManager.GetBus("bus:/");
                musicBus = RuntimeManager.GetBus("bus:/Music");
                ambienceBus = RuntimeManager.GetBus("bus:/Ambience");
                sfxBus = RuntimeManager.GetBus("bus:/SFX");

                if (masterBus.isValid() && musicBus.isValid() && ambienceBus.isValid() && sfxBus.isValid())
                {
                    busesValid = true;   // master bank is up; event banks may still be loading

                    // Tell FMOD to split spatial audio tracking for 2 players
                    //FMODUnity.RuntimeManager.StudioSystem.setNumListeners(2);

                    if (enableDebugLogs) Debug.Log("FMOD buses initialized successfully");
                    break;
                }
            }
            catch (System.Exception e)
            {
                if (enableDebugLogs) Debug.LogWarning($"FMOD not ready yet (attempt {attempts}): {e.Message}");
            }

            attempts++;
            yield return new WaitForSeconds(0.05f);
        }

        if (!busesValid)
        {
            Debug.LogError("Failed to initialize FMOD after 5 seconds!");
            yield break;
        }

        // Buses being valid only means the MASTER bank is loaded — the buses live there.
        // The banks that hold your EVENTS (SFX / Music) can still be streaming in from
        // disk in a build. If we allow PlayOneShot / PreloadSampleData now, any event
        // whose bank isn't resident yet fails silently (EventNotFound) — THAT is the
        // intermittent "wave start / boss zoom sometimes don't play in the build". The
        // editor never races because its banks are already warm. So wait for every bank
        // (and any streaming sample data) before we call ourselves initialized.
        //
        // Version note: HaveAllBanksLoaded / AnySampleDataLoading() exist in FMOD for
        // Unity 2.02+. On older versions, swap these for RuntimeManager.HasBankLoaded(
        // "<YourBankName>") per bank, or load banks explicitly with LoadBank(). The gate
        // is only meaningful if your FMOD settings actually request banks (the usual
        // "Load All Bank Data") — which yours do, since the events do eventually play.
        float bankWait = 0f;
        while (!RuntimeManager.HaveAllBanksLoaded && bankWait < 10f)
        {
            bankWait += Time.unscaledDeltaTime;
            yield return null;
        }
        while (RuntimeManager.AnySampleDataLoading() && bankWait < 10f)
        {
            bankWait += Time.unscaledDeltaTime;
            yield return null;
        }

        // Only NOW is it safe to play events / load their sample data. Everything gated
        // on fmodInitialized (PlayOneShot, PlaySFX, PreloadSampleData, music init) is
        // held back until here, which is what fixes the intermittent dropouts.
        fmodInitialized = true;

        // Initialize music if enabled
        previousMusicEnabled = musicEnabled;
        if (musicEnabled)
        {
            yield return StartCoroutine(InitializeMusicCoroutine());
        }

        // Preload sample data for gameplay SFX that must start in tight sync with a
        // visual (laser beams, hammer slam, warnings). Without this, the FIRST start()
        // of an event whose samples aren't resident pays a load cost — heard as a
        // ~0.5s delay before the sound — and because a held loop releases its instance
        // when it stops, the samples can be freed between uses and the cost recurs on
        // EVERY trigger, not just the first. Loading and KEEPING the sample data makes
        // every start() instant. (This is why the RedEye beam was fine but the laser
        // tower and hammer were late: same play path, different resident-sample state.)

        yield return StartCoroutine(PreloadTightSyncSampleData());
    }

    // Waits for FMODEvents, then loads sample data for the tight-sync gameplay events.
    private IEnumerator PreloadTightSyncSampleData()
    {
        float t = 0f;
        while (FMODEvents.instance == null && t < 5f) { t += 0.1f; yield return new WaitForSeconds(0.1f); }
        var fe = FMODEvents.instance;
        if (fe == null) yield break;

        SpatialLoopSfx.PreloadSampleData(fe.laserTowerAttack);
        SpatialLoopSfx.PreloadSampleData(fe.hammerTowerAttack);
        SpatialLoopSfx.PreloadSampleData(fe.healingTowerHalo);
        SpatialLoopSfx.PreloadSampleData(fe.redEyeLaser);
        SpatialLoopSfx.PreloadSampleData(fe.bombWarning);
        SpatialLoopSfx.PreloadSampleData(fe.boss2ExplosionWarning);
        SpatialLoopSfx.PreloadSampleData(fe.bossZoomSound);
    }

    private IEnumerator InitializeMusicCoroutine()
    {
        // Wait for FMODEvents to be initialized
        float timeout = 5f;
        float elapsed = 0f;

        while (FMODEvents.instance == null && elapsed < timeout)
        {
            if (enableDebugLogs) Debug.Log("Waiting for FMODEvents to initialize...");
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (FMODEvents.instance == null)
        {
            Debug.LogError("FMODEvents.instance is still null after waiting! Make sure FMODEvents is in the scene and has the FMODEvents script attached.");
            yield break;
        }

        if (!HasMusicTrack() && (FMODEvents.instance == null || FMODEvents.instance.musicMenu.IsNull))
        {
            Debug.LogError("No music EventReferences assigned in FMODEvents " +
                           "(musicMenu / musicAmbient / musicCalm / musicElectronic / musicPiano)!");
            yield break;
        }

        // Wait one more frame to ensure everything is ready
        yield return new WaitForEndOfFrame();

        // Music start-up is now owned by UpdateMusicContext() (polled in Update): it
        // brings up the menu bed or a gameplay track based on whether a run exists.
        // Nothing to start here — this coroutine just guarantees FMOD + FMODEvents are
        // ready before that poll does anything.
        if (enableDebugLogs) Debug.Log("[AudioManager] Music system ready (context poll drives playback).");
    }

    private void Update()
    {
        if (!fmodInitialized) return;

        // Update bus volumes
        if (masterBus.isValid())
            masterBus.setVolume(masterVolume);
        if (musicBus.isValid())
            musicBus.setVolume(musicVolume);
        if (ambienceBus.isValid())
            ambienceBus.setVolume(ambienceVolume);
        if (sfxBus.isValid())
            sfxBus.setVolume(SFXVolume);

        // Check for music enabled changes during runtime
        if (previousMusicEnabled != musicEnabled)
        {
            HandleMusicToggle();
            previousMusicEnabled = musicEnabled;
        }

        // AUTHORITATIVE menu-vs-gameplay decision, polled every frame. This does NOT
        // depend on MusicDirector being alive or on any bootstrap ordering — the mere
        // presence of a GameOrchestrator is the signal for "a run is happening".
        UpdateMusicContext();
    }

    // Owns which BED plays: the dedicated menu track when no run is in progress, a
    // random gameplay track while a run exists. MusicDirector (if present) layers the
    // adaptive MusicSection on top of the gameplay bed; it no longer decides the bed.
    private bool _runActivePrev = false;
    private void UpdateMusicContext()
    {
        if (!fmodInitialized || !musicEnabled) return;

        bool runActive = GameOrchestrator.Instance != null;

        if (runActive != _runActivePrev)
        {
            _runActivePrev = runActive;
            if (runActive)
            {
                // Menu → run: roll a fresh random track and cross-fade the menu out.
                SelectRandomTrackForNewRun();
                PlayGameplayMusic();
                Debug.Log($"[AudioManager] Music context → RUN (track: {CurrentMusicTrackName})");
            }
            else
            {
                // Run → menu: cross-fade back to the dedicated menu track.
                PlayMenuMusic();
                Debug.Log("[AudioManager] Music context → MENU");
            }
        }
        else if (!runActive && !_menuMusicActive)
        {
            // Steady-state safety net: we're in a menu but the menu bed isn't up yet
            // (e.g. just booted straight into the main menu). Keep trying until it
            // takes (handles the music bank loading a frame or two late).
            PlayMenuMusic();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keep the "Random Music Pool" list populated and in canonical order so the
        // tick-boxes are visible/editable without entering Play mode.
        SeedRandomTrackPool();

        // This is called when values change in the Inspector during runtime
        if (Application.isPlaying && previousMusicEnabled != musicEnabled)
        {
            HandleMusicToggle();
            previousMusicEnabled = musicEnabled;
        }
    }

    // Editor helper: re-enable every track and drop stale entries. Right-click the
    // AudioManager component header → "Reset Random Music Pool (enable all)".
    [ContextMenu("Reset Random Music Pool (enable all)")]
    private void ResetRandomTrackPool()
    {
        randomTrackPool.Clear();
        SeedRandomTrackPool();
    }
#endif

    // Appends any candidate track missing from randomTrackPool (as ENABLED), in
    // canonical order, without touching the user's existing tick choices. Cheap and
    // idempotent; safe to call from Awake and OnValidate.
    private void SeedRandomTrackPool()
    {
        for (int i = 0; i < CandidateTrackNames.Length; i++)
        {
            string name = CandidateTrackNames[i];
            bool present = false;
            for (int j = 0; j < randomTrackPool.Count; j++)
                if (randomTrackPool[j].name == name) { present = true; break; }
            if (!present)
                randomTrackPool.Add(new MusicTrackOption { name = name, includeInRandomPool = true });
        }
    }

    private void InitializeAmbience(EventReference ambienceEventReference)
    {
        if (!fmodInitialized)
        {
            Debug.LogWarning("Cannot initialize ambience - FMOD not ready");
            return;
        }

        ambienceEventInstance = CreateInstance(ambienceEventReference);
        ambienceEventInstance.start();
        if (enableDebugLogs) Debug.Log("Ambience initialized");
    }

    private void InitializeMusic(EventReference musicEventReference)
    {
        if (!fmodInitialized)
        {
            Debug.LogWarning("Cannot initialize music - FMOD not ready");
            return;
        }

        if (enableDebugLogs) Debug.Log("Attempting to initialize music...");

        if (musicEventReference.IsNull)
        {
            Debug.LogError("Music EventReference is null!");
            return;
        }

        try
        {
            musicEventInstance = CreateInstance(musicEventReference);

            FMOD.RESULT result = musicEventInstance.start();
            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError($"Failed to start music instance: {result}");
                return;
            }

            musicInitialized = true;

            // Verify the instance is in a good state
            FMOD.Studio.PLAYBACK_STATE state;
            musicEventInstance.getPlaybackState(out state);
            if (enableDebugLogs) Debug.Log($"Music initialized successfully. Playback state: {state}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception initializing music: {e.Message}");
        }
    }

    public void SetMusicSection(int sectionIndex)
    {
        SetMusicSection((MusicSection)sectionIndex);
    }

    // Values 0-5 are the ORIGINAL sections and must keep their numbers: they map
    // 1:1 onto the existing FMOD "MusicSection" parameter labels. New sections are
    // appended so no authored FMOD data shifts.
    public enum MusicSection
    {
        Intro = 0,
        Calm = 1,
        Trumpet = 2,
        Pause = 3,
        Intense = 4,
        Piano = 5,

        // ── Added for MusicDirector ──
        Menu = 6,        // main menu / lobby / any scene with no run in progress
        StageIntro = 7,  // biome transition + "Stage 2: Desert" banner
        Boss = 8,        // stage boss fight
        FinalBoss = 9,   // final boss fight
        Reward = 10,     // augment / post-stage choice screen
        Victory = 11,    // run won
        GameOver = 12    // core destroyed
    }

    public void SetMusicSection(MusicSection section)
    {
        if (enableDebugLogs) Debug.Log($"SetMusicSection called: {section}");

        if (!musicEnabled)
        {
            if (enableDebugLogs) Debug.LogWarning("Music is disabled");
            return;
        }

        // Sections only apply to the gameplay bed. While the dedicated menu track is
        // playing, IGNORE section changes entirely — this is what stops a stray caller
        // (an old WaveSpawner/menu script calling SetMusicSection) from spinning up a
        // gameplay track over the menu music.
        if (_menuMusicActive) return;

        if (!fmodInitialized)
        {
            if (enableDebugLogs) Debug.LogWarning("FMOD not initialized, deferring music section change");
            StartCoroutine(DeferredMusicSection(section));
            return;
        }

        // Do NOT lazily start music here anymore. The gameplay bed is created/owned by
        // PlayGameplayMusic (driven by the context poll). If it isn't up yet, there's
        // simply no section to set — bail rather than starting a stray instance.
        if (!musicInitialized || !musicEventInstance.isValid())
        {
            if (enableDebugLogs) Debug.LogWarning("Gameplay bed not up; ignoring section change");
            return;
        }

        // Some of the random tracks may be plain loops with no "MusicSection"
        // parameter authored. Once we've discovered that for the current track,
        // stop trying to set it — otherwise every state change logs an error.
        // The flag is reset whenever the track changes (PlayGameplayMusic rebuilds).
        if (_sectionParamMissing) return;

        try
        {
            // Set by LABEL, never by raw number. In FMOD Studio "MusicSection" is a
            // labeled parameter whose labels are spelled exactly like the MusicSection
            // enum values (Menu, StageIntro, Calm, Intense, Boss, ...).
            //
            // A track doesn't have to author every section. If a label is missing we
            // walk a FALLBACK CHAIN of labels (see FallbackChain below), e.g. a track
            // with no "Boss" plays its "Intense" section during the boss fight.
            //
            // We deliberately do NOT fall back to setParameterByName(float): on a
            // labeled parameter the number is the label's POSITION in Studio's list,
            // not the enum value, and FMOD clamps out-of-range values to the LAST label.
            // So Boss (8) on a track with six labels silently played whatever label
            // happened to be last — that was the old behaviour.
            // A track whose "MusicSection" is a plain NUMERIC parameter (no labels) has
            // no names to fall back through, so it keeps the original behaviour: the
            // enum value is sent as the number.
            if (!SectionParameterIsLabeled(out bool hasParameter))
            {
                if (!hasParameter)
                {
                    _sectionParamMissing = true;
                    Debug.LogWarning(
                        $"[AudioManager] Track '{CurrentMusicTrackName}' has no 'MusicSection' " +
                        "parameter — playing it as a plain loop and ignoring section changes. " +
                        "(Author a 'MusicSection' parameter on it if you want adaptive regions.)");
                    return;
                }

                FMOD.RESULT numeric = musicEventInstance.setParameterByName("MusicSection", (float)section);
                if (numeric != FMOD.RESULT.OK)
                    Debug.LogError($"Failed to set numeric music section '{section}': {numeric}");
                else if (enableDebugLogs)
                    Debug.Log($"Music section set (numeric parameter): {section} ({(int)section})");
                return;
            }

            MusicSection[] chain = FallbackChain(section);
            for (int i = 0; i < chain.Length; i++)
            {
                FMOD.RESULT result =
                    musicEventInstance.setParameterByNameWithLabel("MusicSection", chain[i].ToString());

                if (result == FMOD.RESULT.OK)
                {
                    if (i > 0 && _warnedMissingSections.Add(section))
                        Debug.LogWarning(
                            $"[AudioManager] Track '{CurrentMusicTrackName}' has no '{section}' " +
                            $"section — playing '{chain[i]}' instead. Add a label named exactly " +
                            $"'{section}' to its 'MusicSection' parameter in FMOD Studio if you " +
                            "want a dedicated section.");
                    if (enableDebugLogs)
                        Debug.Log($"Music section set: {chain[i]}" +
                                  (i > 0 ? $" (fallback for {section})" : ""));
                    return;
                }

                if (result != FMOD.RESULT.ERR_EVENT_NOTFOUND)
                {
                    // A genuine failure (invalid handle, event not loaded, ...), not a
                    // missing label — surface it.
                    Debug.LogError($"Failed to set music section '{chain[i]}': {result}");
                    return;
                }
                // ERR_EVENT_NOTFOUND → this label (or the whole parameter) is missing;
                // try the next one in the chain.
            }

            // None of the labels in the chain exist on this track.
            if (_warnedMissingSections.Add(section))
            {
                Debug.LogWarning(
                    $"[AudioManager] Track '{CurrentMusicTrackName}' has none of the labels " +
                    $"[{string.Join(", ", chain)}] on 'MusicSection' — keeping the current " +
                    "section. Check the label spelling in FMOD Studio.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception setting music section: {e.Message}");
        }
    }

    // Which labels to try, in order, for each section. The first label that exists on
    // the current track wins. Edit these to change what a partially-authored track
    // plays. Every chain ends in a section every adaptive track is expected to have.
    private static MusicSection[] FallbackChain(MusicSection s)
    {
        switch (s)
        {
            case MusicSection.Boss: return new[] { MusicSection.Boss, MusicSection.Intense, MusicSection.Calm };
            case MusicSection.FinalBoss: return new[] { MusicSection.FinalBoss, MusicSection.Boss, MusicSection.Intense, MusicSection.Calm };
            case MusicSection.Intense: return new[] { MusicSection.Intense, MusicSection.Calm };
            case MusicSection.StageIntro: return new[] { MusicSection.StageIntro, MusicSection.Intro, MusicSection.Calm };
            case MusicSection.Reward: return new[] { MusicSection.Reward, MusicSection.Calm };
            case MusicSection.Pause: return new[] { MusicSection.Pause, MusicSection.Calm };
            case MusicSection.Victory: return new[] { MusicSection.Victory, MusicSection.Calm };
            case MusicSection.GameOver: return new[] { MusicSection.GameOver, MusicSection.Calm };
            case MusicSection.Calm: return new[] { MusicSection.Calm, MusicSection.Intro };
            default: return new[] { s, MusicSection.Calm };
        }
    }

    // Inspects the loaded gameplay event's "MusicSection" parameter.
    // hasParameter = false → the event has no such parameter (plain loop).
    // Returns true only for a LABELED parameter (the normal adaptive setup).
    private bool SectionParameterIsLabeled(out bool hasParameter)
    {
        hasParameter = false;
        if (!musicEventInstance.isValid()) return false;
        if (musicEventInstance.getDescription(out FMOD.Studio.EventDescription desc) != FMOD.RESULT.OK)
            return false;
        if (desc.getParameterDescriptionByName("MusicSection",
                out FMOD.Studio.PARAMETER_DESCRIPTION param) != FMOD.RESULT.OK)
            return false;

        hasParameter = true;
        return (param.flags & FMOD.Studio.PARAMETER_FLAGS.LABELED) != 0;
    }

    private IEnumerator DeferredMusicSection(MusicSection section)
    {
        if (enableDebugLogs) Debug.Log($"Deferring music section change to: {section}");

        // Wait for FMOD to be ready
        float timeout = 5f;
        float elapsed = 0f;

        while (elapsed < timeout && (!fmodInitialized || !musicBus.isValid()))
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (!fmodInitialized || !musicBus.isValid())
        {
            Debug.LogError("Timed out waiting for FMOD to initialize");
            yield break;
        }

        // Try to set the music section now
        SetMusicSection(section);
    }

    public void ToggleMusic()
    {
        musicEnabled = !musicEnabled;
        HandleMusicToggle();
        previousMusicEnabled = musicEnabled;
    }

    private void HandleMusicToggle()
    {
        if (!fmodInitialized) return;

        if (musicEnabled)
        {
            // Bring up the correct bed for the current context, and re-assert the
            // section if we're in a run.
            bool runActive = GameOrchestrator.Instance != null;
            _runActivePrev = runActive;
            if (runActive)
            {
                PlayGameplayMusic();
                if (MusicDirector.Instance != null) MusicDirector.Instance.Reapply();
            }
            else
            {
                PlayMenuMusic();
            }
        }
        else
        {
            // Silence BOTH beds — either one could be the audible one.
            StopMenuMusic();
            StopGameplayMusic();
        }
    }

    public void SetAmbienceParameter(string parameterName, float parameterValue)
    {
        if (ambienceEventInstance.isValid())
        {
            ambienceEventInstance.setParameterByName(parameterName, parameterValue);
        }
    }

    public void PlaySFX(EventReference sound, Vector3 worldPos = default(Vector3))
    {
        if (fmodInitialized)
        {
            RuntimeManager.PlayOneShot(sound, worldPos);
        }
    }

    public void PlayOneShot(EventReference sound, Vector3 worldPos)
    {
        if (fmodInitialized)
        {
            RuntimeManager.PlayOneShot(sound, worldPos);
        }
    }

    public EventInstance CreateInstance(EventReference eventReference)
    {
        EventInstance eventInstance = RuntimeManager.CreateInstance(eventReference);
        eventInstances.Add(eventInstance);
        return eventInstance;
    }

    // Create the menu-music instance from whatever is available: the assigned
    // EventReference first, else the path string fallback. Returns false only if
    // neither works (e.g. the event isn't in the loaded banks).
    private bool _menuCreateErrorLogged = false;
    private bool TryCreateMenuInstance(out EventInstance inst)
    {
        inst = default;
        var fe = FMODEvents.instance;

        if (fe != null && !fe.musicMenu.IsNull)
        {
            try { inst = CreateInstance(fe.musicMenu); _menuCreateErrorLogged = false; return true; }
            catch (System.Exception e) { if (!_menuCreateErrorLogged) { _menuCreateErrorLogged = true; Debug.LogError($"[AudioManager] Menu music (reference) failed: {e.Message}"); } }
        }

        if (!string.IsNullOrEmpty(menuMusicEventPath))
        {
            try
            {
                inst = RuntimeManager.CreateInstance(menuMusicEventPath);
                if (eventInstances != null) eventInstances.Add(inst);
                _menuCreateErrorLogged = false;
                return true;
            }
            catch (System.Exception e)
            {
                if (!_menuCreateErrorLogged)
                {
                    _menuCreateErrorLogged = true;
                    Debug.LogError($"[AudioManager] Menu music (path '{menuMusicEventPath}') failed: {e.Message}. " +
                                   "Is the event in a loaded bank? Rebuild FMOD banks in FMOD → Build.");
                }
            }
        }
        return false;
    }

    public StudioEventEmitter InitializeEventEmitter(EventReference eventReference, GameObject emitterGameObject)
    {
        StudioEventEmitter emitter = emitterGameObject.GetComponent<StudioEventEmitter>();
        emitter.EventReference = eventReference;
        eventEmitters.Add(emitter);
        return emitter;
    }

    // Public method for other scripts to force music initialization if needed
    public void EnsureMusicReady()
    {
        if (!musicEnabled) return;

        if (!fmodInitialized)
        {
            Debug.LogWarning("FMOD not initialized when EnsureMusicReady called");
            return;
        }

        if (!musicInitialized && HasMusicTrack())
        {
            InitializeMusic(ActiveMusicTrack());
        }
    }

    //  MUSIC BEDS: MENU vs GAMEPLAY  ────────────────────────────────────────
    //  MusicDirector calls PlayMenuMusic() for the Menu section and
    //  PlayGameplayMusic() for every other section. The two beds cross-fade.

    // Bring up the dedicated menu track and fade the gameplay bed out.
    public void PlayMenuMusic()
    {
        if (!musicEnabled || !fmodInitialized) return;
        var fe = FMODEvents.instance;

        bool haveRef = fe != null && !fe.musicMenu.IsNull;
        bool havePath = !string.IsNullOrEmpty(menuMusicEventPath);

        if (!haveRef && !havePath)
        {
            // Nothing to play the menu with at all — fall back to a gameplay track.
            if (!_warnedMenuMusicMissing)
            {
                _warnedMenuMusicMissing = true;
                Debug.LogWarning("[AudioManager] No menu music available (musicMenu unassigned " +
                                 "and menuMusicEventPath empty). Falling back to a gameplay track.");
            }
            PlayGameplayMusic();
            return;
        }

        // Diagnostic (once): the assigned reference on the LIVE instance is empty, so
        // we're using the path fallback. This is the usual AudioBootstrap gotcha — the
        // 'Music Menu' field was set on a scene/GameScene copy of FMODEvents, not on the
        // Resources/Audio/AudioSystem prefab that actually spawns at runtime.
        if (!haveRef && !_warnedMenuMusicMissing)
        {
            _warnedMenuMusicMissing = true;
            string where = fe != null ? $"'{fe.gameObject.name}' (scene '{fe.gameObject.scene.name}')" : "<no FMODEvents.instance>";
            Debug.LogWarning($"[AudioManager] FMODEvents.musicMenu is EMPTY on the live instance {where}. " +
                             $"Playing the menu track via path fallback '{menuMusicEventPath}' instead. " +
                             "To silence this, assign 'Music Menu' on the FMODEvents of Assets/Resources/Audio/AudioSystem.");
        }
        if (haveRef) _warnedMenuMusicMissing = false;

        _menuMusicActive = true;

        // Fade the gameplay bed down (kept initialised for a quick return to a run).
        FadeGameplay(0f, stopAtEnd: true);

        if (!menuMusicInitialized || !menuMusicInstance.isValid())
        {
            if (!TryCreateMenuInstance(out menuMusicInstance))
            {
                // Couldn't create by ref OR path (event missing from banks?). Don't get
                // stuck asserting menu every frame — leave menu inactive and let the
                // gameplay bed cover it.
                _menuMusicActive = false;
                PlayGameplayMusic();
                return;
            }
            menuMusicInstance.setVolume(0f);
            menuMusicInstance.start();
            menuMusicInitialized = true;
            Debug.Log($"[AudioManager] Menu music started ({(haveRef ? "reference" : "path fallback")}).");
        }
        else
        {
            FMOD.Studio.PLAYBACK_STATE st;
            menuMusicInstance.getPlaybackState(out st);
            if (st == FMOD.Studio.PLAYBACK_STATE.STOPPED || st == FMOD.Studio.PLAYBACK_STATE.STOPPING)
            {
                menuMusicInstance.setVolume(0f);
                menuMusicInstance.start();
            }
        }
        FadeMenu(1f, stopAtEnd: false);

        if (enableDebugLogs) Debug.Log("[AudioManager] Menu music.");
    }

    // Fade the menu track out and make sure the gameplay bed is playing the currently
    // selected track (rebuilding it if the random selection changed).
    public void PlayGameplayMusic()
    {
        if (!musicEnabled || !fmodInitialized) return;

        _menuMusicActive = false;
        FadeMenu(0f, stopAtEnd: true);

        EnsureTrackPool();

        bool needNew = !musicInitialized || !musicEventInstance.isValid()
                       || _loadedTrackIndex != _currentTrackIndex;
        if (needNew)
        {
            ReleaseMusicInstance();
            musicInitialized = false;
            _sectionParamMissing = false;      // the new event may author MusicSection
            _warnedMissingSections.Clear();
            InitializeMusic(ActiveMusicTrack());
            _loadedTrackIndex = _currentTrackIndex;
            if (musicInitialized && musicEventInstance.isValid())
                musicEventInstance.setVolume(0f);   // ramped up by FadeGameplay below
            if (enableDebugLogs && musicInitialized)
                Debug.Log($"[AudioManager] Gameplay music → {CurrentMusicTrackName}");
        }
        else
        {
            // Same track, but it may have been stopped for the menu — resume it.
            FMOD.Studio.PLAYBACK_STATE st;
            musicEventInstance.getPlaybackState(out st);
            if (st == FMOD.Studio.PLAYBACK_STATE.STOPPED || st == FMOD.Studio.PLAYBACK_STATE.STOPPING)
            {
                musicEventInstance.setVolume(0f);
                musicEventInstance.start();
            }
        }
        FadeGameplay(1f, stopAtEnd: false);
    }

    private void StopMenuMusic()
    {
        _menuMusicActive = false;
        FadeMenu(0f, stopAtEnd: true);
    }

    private void StopGameplayMusic()
    {
        FadeGameplay(0f, stopAtEnd: true);
    }

    // ── Volume cross-fades (instance volume, independent of the music bus) ────
    private void FadeMenu(float to, bool stopAtEnd)
    {
        if (_menuFade != null) StopCoroutine(_menuFade);
        if (!menuMusicInstance.isValid()) return;
        _menuFade = StartCoroutine(FadeInstance(menuMusicInstance, to, musicCrossfadeSeconds, stopAtEnd));
    }

    private void FadeGameplay(float to, bool stopAtEnd)
    {
        if (_gameplayFade != null) StopCoroutine(_gameplayFade);
        if (!musicEventInstance.isValid()) return;
        _gameplayFade = StartCoroutine(FadeInstance(musicEventInstance, to, musicCrossfadeSeconds, stopAtEnd));
    }

    // Ramp an instance's volume to `to` over `dur`, using realtime so it still fades
    // while a menu has frozen the game (timeScale 0). Optionally stop at the end.
    //
    // Smoothness details that matter here:
    //  • SmoothStep instead of linear — a linear volume ramp is perceived as a fast
    //    jump (hearing is roughly logarithmic); SmoothStep eases in AND out, so the
    //    incoming track swells in gently rather than snapping to audible.
    //  • The first real frame is SKIPPED and per-frame time is CLAMPED — a scene load
    //    (e.g. entering the game on Solo/Coop) produces one enormous unscaledDeltaTime
    //    that would otherwise lurch the volume most of the way in a single frame.
    private IEnumerator FadeInstance(EventInstance inst, float to, float dur, bool stopAtEnd)
    {
        if (!inst.isValid()) yield break;

        float from;
        inst.getVolume(out from);

        if (dur <= 0f)
        {
            inst.setVolume(to);
            if (stopAtEnd) inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            yield break;
        }

        // Swallow the frame the fade starts on: its delta can include a scene-load
        // hitch. The incoming track is already sitting at volume 0, so this one-frame
        // hold is inaudible and keeps the ramp from jumping.
        yield return null;

        float t = 0f;
        while (t < dur)
        {
            // Clamp so a single long frame can't snap the volume forward.
            t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (!inst.isValid()) yield break;
            float p = Mathf.Clamp01(t / dur);
            inst.setVolume(Mathf.SmoothStep(from, to, p));   // eased, not linear
            yield return null;
        }

        if (inst.isValid())
        {
            inst.setVolume(to);
            if (stopAtEnd) inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        }
    }

    //  RANDOM MUSIC TRACKS  ────────────────────────────────────────────────

    // Friendly name of the track currently selected (for UI / logs).
    public string CurrentMusicTrackName =>
        (_currentTrackIndex >= 0 && _currentTrackIndex < _musicTracks.Count)
            ? _musicTracks[_currentTrackIndex].name : "—";

    // How many tracks are currently ELIGIBLE (assigned AND ticked-on) for random play.
    public int MusicTrackCount { get { EnsureTrackPool(); return _musicTracks.Count; } }

    // ── Include / exclude tracks at runtime (e.g. from an options toggle) ─────
    // The inspector tick-boxes under "Random Music Pool" are the easy, no-code way to
    // do this. These do the same thing from script. `name` must match one in
    // CandidateTrackNames ("Ambient", "Calm", "Electronic", "Piano", "Guitar",
    // "Clavi", "Orchestral", "Starting").

    public bool IsTrackInRandomPool(string name)
    {
        for (int i = 0; i < randomTrackPool.Count; i++)
            if (randomTrackPool[i].name == name) return randomTrackPool[i].includeInRandomPool;
        return true; // never-seen candidate defaults to included
    }

    // Turn a track on/off in the random pool and rebuild the eligible list. The change
    // takes effect on the next random roll (new run, or Options → "Switch Track"). If
    // you exclude the track that's playing right now, it keeps playing until then. The
    // currently-selected track is preserved by name if it's still eligible, so a live
    // run isn't yanked onto a different track.
    public void SetTrackInRandomPool(string name, bool include)
    {
        int slot = -1;
        for (int i = 0; i < randomTrackPool.Count; i++)
            if (randomTrackPool[i].name == name) { slot = i; break; }

        if (slot >= 0)
        {
            var opt = randomTrackPool[slot];
            opt.includeInRandomPool = include;
            randomTrackPool[slot] = opt;
        }
        else
        {
            randomTrackPool.Add(new MusicTrackOption { name = name, includeInRandomPool = include });
        }

        // Rebuild the eligible pool, keeping the current selection if still eligible.
        string selectedName = CurrentMusicTrackName;
        _musicTracks.Clear();
        _currentTrackIndex = -1;
        EnsureTrackPool();
        if (selectedName != "—")
        {
            for (int i = 0; i < _musicTracks.Count; i++)
                if (_musicTracks[i].name == selectedName)
                {
                    _currentTrackIndex = i;
                    // The live bed still holds this track; keep loaded/current in sync so
                    // PlayGameplayMusic doesn't needlessly re-fade the same track.
                    if (_loadedTrackIndex >= 0) _loadedTrackIndex = i;
                    break;
                }
        }
    }

    // Roll a random gameplay track for a new run. Called by MusicDirector when a fresh
    // GameOrchestrator appears (the player started/resumed a run). This only SELECTS
    // the track; PlayGameplayMusic brings it up when the run's first section applies,
    // so it never fights the menu→gameplay cross-fade.
    public void SelectRandomTrackForNewRun()
    {
        EnsureTrackPool();
        if (_musicTracks.Count == 0) return;
        _currentTrackIndex = UnityEngine.Random.Range(0, _musicTracks.Count);
    }

    // Switch to a DIFFERENT random track (Options → "Switch Track"). Applies live if a
    // run is in progress; in the main menu it just picks the track for the next run.
    public void SwitchToRandomMusicTrack()
    {
        EnsureTrackPool();
        if (_musicTracks.Count == 0) return;

        int idx = _currentTrackIndex;
        if (_musicTracks.Count == 1) idx = 0;
        else
            do { idx = UnityEngine.Random.Range(0, _musicTracks.Count); }
            while (idx == _currentTrackIndex);
        _currentTrackIndex = idx;

        // Only swap audibly if the gameplay bed is the active one. In the menu the
        // dedicated menu track keeps playing and this takes effect on the next run.
        if (!_menuMusicActive && musicEnabled)
        {
            // PlayGameplayMusic rebuilds the bed on the new track (index changed);
            // MusicDirector then re-pushes the current section onto it.
            PlayGameplayMusic();
            if (MusicDirector.Instance != null) MusicDirector.Instance.Reapply();
        }
    }

    // Build the pool from FMODEvents' gameplay music references (once). A candidate is
    // added only if BOTH its EventReference is assigned AND its "Random Music Pool"
    // tick-box is on. Unassigned references are skipped, so it works with however many
    // are wired.
    private void EnsureTrackPool()
    {
        if (_musicTracks.Count > 0) return;
        var fe = FMODEvents.instance;
        if (fe == null) return;

        for (int i = 0; i < CandidateTrackNames.Length; i++)
        {
            string name = CandidateTrackNames[i];
            EventReference reference = ReferenceForTrackName(fe, name);
            if (reference.IsNull) continue;       // not wired in FMODEvents (yet)
            if (!IsTrackEnabled(name)) continue;  // excluded via the inspector tick-box
            _musicTracks.Add(new MusicTrack { name = name, reference = reference });
        }

        if (_currentTrackIndex < 0 && _musicTracks.Count > 0)
            _currentTrackIndex = UnityEngine.Random.Range(0, _musicTracks.Count);
    }

    // Maps a candidate name to its FMODEvents reference. Keep in lockstep with
    // CandidateTrackNames — a name with no case here yields a null reference and is
    // simply skipped.
    private static EventReference ReferenceForTrackName(FMODEvents fe, string name)
    {
        switch (name)
        {
            case "Ambient": return fe.musicAmbient;
            case "Calm": return fe.musicCalm;
            case "Electronic": return fe.musicElectronic;
            case "Piano": return fe.musicPiano;
            case "Guitar": return fe.musicGuitar;
            case "Clavi": return fe.musicClavi;
            case "Orchestral": return fe.musicOrchestral;
            case "Starting": return fe.musicStarting;
            default: return default;
        }
    }

    // Looks up a track's include/exclude tick-box. A candidate that isn't listed yet is
    // auto-added as ENABLED, so newly registered tracks default to "in the pool" and
    // appear in the inspector list the first time the pool is built.
    private bool IsTrackEnabled(string name)
    {
        for (int i = 0; i < randomTrackPool.Count; i++)
            if (randomTrackPool[i].name == name) return randomTrackPool[i].includeInRandomPool;

        randomTrackPool.Add(new MusicTrackOption { name = name, includeInRandomPool = true });
        return true;
    }

    // True if at least one gameplay music track is available to play.
    private bool HasMusicTrack()
    {
        EnsureTrackPool();
        if (_currentTrackIndex >= 0 && _currentTrackIndex < _musicTracks.Count) return true;
        // Fallback so nothing regresses if the pool somehow stayed empty.
        return FMODEvents.instance != null && !FMODEvents.instance.musicAmbient.IsNull;
    }

    // The EventReference of the currently selected gameplay track (falls back to the
    // original hardcoded musicAmbient if the pool is unexpectedly empty).
    private EventReference ActiveMusicTrack()
    {
        EnsureTrackPool();
        if (_currentTrackIndex >= 0 && _currentTrackIndex < _musicTracks.Count)
            return _musicTracks[_currentTrackIndex].reference;
        return FMODEvents.instance != null ? FMODEvents.instance.musicAmbient : default;
    }

    // Stop + release the current gameplay music instance and drop it from the tracked
    // list so CleanUp() never double-releases it.
    private void ReleaseMusicInstance()
    {
        if (_gameplayFade != null) { StopCoroutine(_gameplayFade); _gameplayFade = null; }
        if (musicEventInstance.isValid())
        {
            musicEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            musicEventInstance.release();
            if (eventInstances != null) eventInstances.Remove(musicEventInstance);
        }
        _loadedTrackIndex = -1;
    }

    // Public properties for debugging
    public bool IsFMODInitialized => fmodInitialized;
    public bool IsMusicInitialized => musicInitialized;

    private void CleanUp()
    {
        // Stop and release any created instances
        if (eventInstances != null)
        {
            foreach (EventInstance eventInstance in eventInstances)
            {
                if (eventInstance.isValid())
                {
                    eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    eventInstance.release();
                }
            }
            eventInstances.Clear();
        }

        // Stop all of the event emitters
        if (eventEmitters != null)
        {
            foreach (StudioEventEmitter emitter in eventEmitters)
            {
                if (emitter != null)
                {
                    emitter.Stop();
                }
            }
            eventEmitters.Clear();
        }

        // Clean up specific instances
        if (ambienceEventInstance.isValid())
        {
            ambienceEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            ambienceEventInstance.release();
        }

        if (musicEventInstance.isValid())
        {
            musicEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            musicEventInstance.release();
        }

        if (menuMusicInstance.isValid())
        {
            menuMusicInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            menuMusicInstance.release();
        }
    }

    private void OnDestroy()
    {
        // A duplicate never owned any FMOD state - leaving CleanUp() to it would
        // silence the real instance.
        if (isDuplicate) return;

        if (instance == this) instance = null;
        CleanUp();
    }
}

// SUSTAINED 3D SFX HELPER
//
// Lives in this file rather than its own because it is a plain class, not a
// MonoBehaviour — only MonoBehaviour/ScriptableObject types have to match their
// filename in Unity — and this is where the rest of the audio plumbing already is.
//
// One held FMOD EventInstance with a start / follow / stop lifecycle, for sounds
// that must play FOR AS LONG AS something is happening rather than being fired and
// forgotten: the RedEye's laser, the Bomber's fuse warning, Boss2's explosion
// telegraph, the boss-intro zoom.
//
// Everything else in this project uses AudioManager.PlayOneShot, which is
// fire-and-forget and cannot be stopped. Held instances CAN leak, and can act on a
// recycled handle — the failure mode Boss1.cs documents at length, where a double
// release lets one boss's cleanup silence a completely different boss (FMOD
// recycles handle pointers, so a stale struct copy ends up aimed at whatever
// instance was created into that slot next). Rather than repeat that lifecycle by
// hand in four more scripts, it lives here once:
//
//   - _active is cleared BEFORE any stop/release, so a re-entrant or double call
//     can never release the same handle twice.
//   - clearHandle() zeroes the struct after release, so a stale copy is inert.
//   - isValid() guards every FMOD call.
//   - Play() releases any previous instance first, so re-triggering never leaks.
//   - 3D attributes are POSITION ONLY, refreshed by the caller via SetPosition().
//     That is exactly what AudioManager.PlayOneShot(evt, pos) does for every other
//     sound in the game, so these spatialise identically to the sounds already
//     tuned in FMOD Studio.
//
// Deliberately NOT using RuntimeManager.AttachInstanceToGameObject: attaching hands
// FMOD the full transform + Rigidbody2D, which adds orientation (the event's
// panner/cone would rotate with the enemy, so volume would depend on facing) and
// velocity (Doppler off the Rigidbody2D as it walks). Neither is wanted here, and
// position-only sidesteps the deprecated Transform overload entirely.
//
// EVENT AUTHORING: for a sound that should sustain until Stop() is called, the FMOD
// event needs a loop region. If it is a plain one-shot it simply plays once and ends
// on its own — Stop() is still safe, it just may have nothing left to stop.
//
// NOT registered with AudioManager.CreateInstance() on purpose: that list is only
// drained on AudioManager teardown, so a per-enemy loop would grow it unboundedly
// and risk a double release when this helper releases the same handle itself.
public sealed class SpatialLoopSfx
{
    private EventInstance _inst;
    private bool _active;

    // Used only in warnings, so a problem points at the script that owns the sound.
    private readonly string _owner;

    /// True while this helper holds a started instance.
    public bool IsActive => _active;

    public SpatialLoopSfx(string owner = null)
    {
        _owner = string.IsNullOrEmpty(owner) ? "SpatialLoopSfx" : owner;
    }

    /// Start the event at `worldPos`. Safe to call when already playing (the
    /// previous instance is released first) and safe to call with an unassigned
    /// EventReference (does nothing). Returns true if a sound actually started.
    public bool Play(EventReference eventRef, Vector3 worldPos)
    {
        // Unassigned in FMODEvents — this is the "wire the events in one at a time"
        // case, and it must stay silent rather than throwing.
        if (eventRef.IsNull) return false;

        // FMOD may not be up yet (AudioManager runs an init coroutine at boot).
        var am = AudioManager.instance;
        if (am == null || !am.IsFMODInitialized) return false;

        Stop(immediate: true);   // never stack two instances from one owner

        try
        {
            _inst = RuntimeManager.CreateInstance(eventRef);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[{_owner}] CreateInstance failed: {e.Message}");
            _inst.clearHandle();
            return false;
        }

        if (!_inst.isValid())
        {
            _inst.clearHandle();
            return false;
        }

        // Set the position BEFORE start() so the first audible frame is already in
        // the right place instead of snapping there afterwards.
        _inst.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(worldPos));

        FMOD.RESULT r = _inst.start();
        if (r != FMOD.RESULT.OK)
        {
            // ERR_STUDIO_MAX_INSTANCES here means the event's Max Instances cap in
            // FMOD Studio is full — which, from the player's side, looks exactly
            // like "the sound just wasn't there".
            Debug.LogWarning($"[{_owner}] start() failed with {r}. " +
                             "Check Max Instances / stealing on this event in FMOD Studio.");
            _inst.release();
            _inst.clearHandle();
            return false;
        }

        _active = true;
        return true;
    }

    /// Keep the sound sitting on a moving source. Cheap — call it every frame.
    public void SetPosition(Vector3 worldPos)
    {
        if (!_active || !_inst.isValid()) return;
        _inst.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(worldPos));
    }

    /// Instance-level volume multiplier (0..1), on top of the event's own mix and the
    /// Spatializer's distance attenuation. Used to fade/swell a held sound from code —
    /// e.g. the RedEye beam winding up across its charge. No-op if nothing is playing.
    public void SetVolume(float volume01)
    {
        if (!_active || !_inst.isValid()) return;
        _inst.setVolume(volume01);
    }

    /// Instance-level pitch multiplier (1 = authored pitch). <1 lowers, >1 raises.
    /// Sweeping this from code gives a "power-up"/riser without authoring a parameter.
    public void SetPitch(float pitch)
    {
        if (!_active || !_inst.isValid()) return;
        _inst.setPitch(pitch);
    }

    /// immediate == true  -> hard cut. Use when the thing the sound describes has
    ///                       been replaced by something louder (an explosion), or
    ///                       when the owner is being destroyed.
    /// immediate == false -> ALLOWFADEOUT, so the event's own release/AHDSR tail
    ///                       plays out instead of being chopped mid-sample.
    public void Stop(bool immediate = false)
    {
        if (!_active) return;

        // Clear FIRST, before any FMOD call, so nothing can re-enter and release twice.
        _active = false;

        if (_inst.isValid())
        {
            _inst.stop(immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _inst.release();
        }

        // Zero the struct so this owner can never touch the (now recycled) handle.
        _inst.clearHandle();
    }

    // Events whose sample data we've already asked FMOD to load, so we only do it once.
    private static readonly HashSet<string> _sampleLoaded = new HashSet<string>();

    /// Loads — and keeps — an event's sample data in memory so the first, and every,
    /// start() is instant instead of paying a load cost the moment it is first needed.
    /// A held loop releases its instance when it stops, which can free the samples
    /// between uses; keeping them resident stops that cost recurring on every trigger.
    /// Safe to call repeatedly (each event loads once), and a no-op for an unassigned
    /// event or before FMOD is up. Does NOT affect events marked "Stream" in FMOD
    /// Studio — those always stream on start and must be un-streamed there for tight sync.
    public static void PreloadSampleData(EventReference eventRef)
    {
        if (eventRef.IsNull) return;
        var am = AudioManager.instance;
        if (am == null || !am.IsFMODInitialized) return;

        string key = eventRef.Guid.ToString();
        if (_sampleLoaded.Contains(key)) return;

        try
        {
            var desc = RuntimeManager.GetEventDescription(eventRef);
            if (desc.isValid())
            {
                desc.loadSampleData(); // async load; finishes well before first fire
                _sampleLoaded.Add(key);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SpatialLoopSfx] PreloadSampleData failed: {e.Message}");
        }
    }
}


