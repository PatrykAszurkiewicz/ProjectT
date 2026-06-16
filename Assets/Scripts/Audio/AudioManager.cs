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
    public bool enableDebugLogs = true;

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

    public static AudioManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found more than one Audio Manager in the scene.");
            Destroy(gameObject);
            return;
        }
        instance = this;

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
                    FMODUnity.RuntimeManager.StudioSystem.setNumListeners(2);

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

        if (FMODEvents.instance.musicAmbient.IsNull)
        {
            Debug.LogError("musicAmbient EventReference is not assigned in FMODEvents!");
            yield break;
        }

        // Wait one more frame to ensure everything is ready
        yield return new WaitForEndOfFrame();

        InitializeMusic(FMODEvents.instance.musicAmbient);

        if (musicInitialized)
        {
            SetMusicSection(MusicSection.Calm);
        }
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

    public enum MusicSection
    {
        Intro = 0,
        Calm = 1,
        Trumpet = 2,
        Pause = 3,
        Intense = 4,
        Piano = 5
    }

    public void SetMusicSection(MusicSection section)
    {
        if (enableDebugLogs) Debug.Log($"SetMusicSection called: {section}");

        if (!musicEnabled)
        {
            if (enableDebugLogs) Debug.LogWarning("Music is disabled");
            return;
        }

        if (!fmodInitialized)
        {
            if (enableDebugLogs) Debug.LogWarning("FMOD not initialized, deferring music section change");
            StartCoroutine(DeferredMusicSection(section));
            return;
        }

        if (!musicInitialized)
        {
            if (enableDebugLogs) Debug.LogWarning("Music not initialized, attempting to initialize");

            // Try to initialize music now
            if (FMODEvents.instance != null && !FMODEvents.instance.musicAmbient.IsNull)
            {
                InitializeMusic(FMODEvents.instance.musicAmbient);

                if (!musicInitialized)
                {
                    Debug.LogError("Failed to initialize music");
                    return;
                }
            }
            else
            {
                Debug.LogError("Cannot initialize music - FMODEvents or musicAmbient is null");
                return;
            }
        }

        if (!musicEventInstance.isValid())
        {
            Debug.LogError("musicEventInstance is not valid!");
            return;
        }

        try
        {
            FMOD.RESULT result = musicEventInstance.setParameterByName("MusicSection", (float)section);
            if (result == FMOD.RESULT.OK)
            {
                if (enableDebugLogs) Debug.Log($"Music section successfully set to: {section} ({(int)section})");
            }
            else
            {
                Debug.LogError($"Failed to set music parameter: {result}");
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
        if (musicEnabled && !musicInitialized && fmodInitialized)
        {
            // Initialize music if it wasn't initialized before
            if (FMODEvents.instance != null && !FMODEvents.instance.musicAmbient.IsNull)
            {
                InitializeMusic(FMODEvents.instance.musicAmbient);
                if (musicInitialized)
                {
                    SetMusicSection(MusicSection.Calm);
                }
            }
        }
        else if (!musicEnabled && musicInitialized)
        {
            // Stop music but keep it initialized for quick restart
            if (musicEventInstance.isValid())
            {
                musicEventInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            }
        }
        else if (musicEnabled && musicInitialized)
        {
            // Restart music if it was stopped
            if (musicEventInstance.isValid())
            {
                musicEventInstance.start();
            }
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

        if (!musicInitialized && FMODEvents.instance != null && !FMODEvents.instance.musicAmbient.IsNull)
        {
            InitializeMusic(FMODEvents.instance.musicAmbient);
        }
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
    }

    private void OnDestroy()
    {
        CleanUp();
    }
}