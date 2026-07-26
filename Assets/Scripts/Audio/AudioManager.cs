using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

[DefaultExecutionOrder(-100)] // Ensure AudioManager initializes before other scripts
public class AudioManager : MonoBehaviour
{
    [Header("Volume")]
    [Range(0, 1)]
    public float masterVolume = 1;
    [Range(0, 1)]
    public float musicVolume = 1;
    [Range(0, 1)]
    public float ambienceVolume = 1;
    [Range(0, 1)]
    public float SFXVolume = 1;

    [Header("Music Settings")]
    public bool musicEnabled = true; // Enable by default
    private bool previousMusicEnabled = true;

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

    // ── Random gameplay music track pool ─────────────────────────────────────
    // A run's background music is ONE FMOD event chosen at random from the pool
    // below (built from the four gameplay music EventReferences on FMODEvents). A
    // fresh track is rolled each time a run begins — AudioManager.UpdateMusicContext()
    // does this the moment a GameOrchestrator appears — and the Options menu's
    // "Switch Track" button rolls a different one on demand. The main menu plays a
    // separate dedicated track instead (see menu music, below).
    //
    // This layers on TOP of the existing MusicSection system: whichever track is
    // loaded still receives section changes. Tracks that don't author a
    // "MusicSection" parameter are handled gracefully (see _sectionParamMissing).
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

    // ── Dedicated menu music (MusicMenu) ─────────────────────────────────────
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
        // ---- Singleton ----------------------------------------------------
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

        // ---- Persistence --------------------------------------------------
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

    private IEnumerator InitializeFMOD()
    {
        if (enableDebugLogs) Debug.Log("Initializing FMOD...");

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
                    fmodInitialized = true;

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

        if (!fmodInitialized)
        {
            Debug.LogError("Failed to initialize FMOD after 5 seconds!");
            yield break;
        }

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
        //
        // This does NOT fix an event set to "Stream" in FMOD Studio — a stream always
        // opens on start and carries its own latency. If a sound is still late after
        // this, uncheck "Stream" on its audio asset in FMOD Studio.
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
        // This is called when values change in the Inspector during runtime
        if (Application.isPlaying && previousMusicEnabled != musicEnabled)
        {
            HandleMusicToggle();
            previousMusicEnabled = musicEnabled;
        }
    }
#endif

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
            // Prefer the LABEL, not the raw float. In FMOD Studio "MusicSection" is a
            // labeled parameter whose labels are spelled exactly like the MusicSection
            // enum values (Menu, StageIntro, Calm, Intense, Boss, ...). Setting by label
            // means "Boss" always finds the Boss region even if the labels get reordered
            // in Studio — with a raw float, reordering silently sends you to the wrong
            // music. The two must stay in lockstep: enum name == Studio label.
            FMOD.RESULT result =
                musicEventInstance.setParameterByNameWithLabel("MusicSection", section.ToString());

            if (result == FMOD.RESULT.OK)
            {
                if (enableDebugLogs)
                    Debug.Log($"Music section successfully set to: {section} ({(int)section})");
            }
            else if (result == FMOD.RESULT.ERR_EVENT_NOTFOUND)
            {
                // The label doesn't exist on the parameter yet. Two common causes:
                //   • that section hasn't been authored in Studio (still building it), or
                //   • "MusicSection" is a plain numeric parameter, not a labeled one.
                // Fall back to the numeric value so a half-authored bank still plays
                // *something*, but warn loudly so it gets fixed — a missing label is a
                // real setup bug, not a runtime hiccup to swallow silently.
                Debug.LogWarning(
                    $"[AudioManager] MusicSection has no label '{section}'. Falling back to " +
                    $"numeric {(int)section}. Add a label named exactly '{section}' to the " +
                    "'MusicSection' parameter in FMOD Studio (labels must match the " +
                    "MusicSection enum), then rebuild banks.");

                FMOD.RESULT fallback =
                    musicEventInstance.setParameterByName("MusicSection", (float)section);
                if (fallback == FMOD.RESULT.ERR_EVENT_NOTFOUND)
                {
                    // Neither the label nor a numeric "MusicSection" exists on this
                    // event: it's a simple (non-adaptive) track. Warn ONCE and stop
                    // driving sections into it for as long as it stays loaded.
                    _sectionParamMissing = true;
                    Debug.LogWarning(
                        $"[AudioManager] Track '{CurrentMusicTrackName}' has no 'MusicSection' " +
                        "parameter — playing it as a plain loop and ignoring section changes. " +
                        "(Author a 'MusicSection' parameter on it if you want adaptive regions.)");
                }
                else if (fallback != FMOD.RESULT.OK)
                    Debug.LogError($"Failed to set music parameter (numeric fallback): {fallback}");
                else if (enableDebugLogs)
                    Debug.Log($"Music section set via numeric fallback: {section} ({(int)section})");
            }
            else
            {
                // Any other result is a genuine failure (invalid handle, parameter name
                // typo'd in code, event not loaded, ...). Surface it as an error — this
                // is the "raise an error like before" behaviour, now scoped to real faults.
                Debug.LogError($"Failed to set music parameter '{section}': {result}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception setting music section: {e.Message}");
        }
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

    // How many tracks were actually assigned in FMODEvents.
    public int MusicTrackCount { get { EnsureTrackPool(); return _musicTracks.Count; } }

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

    // Build the pool from FMODEvents' four gameplay music references (once).
    // Unassigned references are skipped, so it works with however many are wired.
    private void EnsureTrackPool()
    {
        if (_musicTracks.Count > 0) return;
        var fe = FMODEvents.instance;
        if (fe == null) return;

        AddTrack("Ambient", fe.musicAmbient);
        AddTrack("Calm", fe.musicCalm);
        AddTrack("Electronic", fe.musicElectronic);
        AddTrack("Piano", fe.musicPiano);

        if (_currentTrackIndex < 0 && _musicTracks.Count > 0)
            _currentTrackIndex = UnityEngine.Random.Range(0, _musicTracks.Count);
    }

    private void AddTrack(string name, EventReference reference)
    {
        if (!reference.IsNull)
            _musicTracks.Add(new MusicTrack { name = name, reference = reference });
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

