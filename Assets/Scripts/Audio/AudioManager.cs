using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

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
    public bool musicEnabled = false; // Set to false by default
    private bool previousMusicEnabled = false;

    private Bus masterBus;
    private Bus musicBus;
    private Bus ambienceBus;
    private Bus sfxBus;

    private List<EventInstance> eventInstances;
    private List<StudioEventEmitter> eventEmitters;

    private EventInstance ambienceEventInstance;
    private EventInstance musicEventInstance;
    private bool musicInitialized = false;

    public static AudioManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found more than one Audio Manager in the scene.");
        }
        instance = this;

        eventInstances = new List<EventInstance>();
        eventEmitters = new List<StudioEventEmitter>();

        //masterBus = RuntimeManager.GetBus("bus:/");
        //musicBus = RuntimeManager.GetBus("bus:/Music");
        //ambienceBus = RuntimeManager.GetBus("bus:/Ambience");
        //sfxBus = RuntimeManager.GetBus("bus:/SFX");
    }

    private void Start()
    {
        //InitializeAmbience(FMODEvents.instance.ambience);

        previousMusicEnabled = musicEnabled; // Initialize the tracking variable

        if (musicEnabled)
        {
            InitializeMusic(FMODEvents.instance.musicAmbient);
            AudioManager.instance.SetMusicSection(MusicSection.Calm);
        }
    }

    private void Update()
    {
        masterBus.setVolume(masterVolume);
        musicBus.setVolume(musicVolume);
        ambienceBus.setVolume(ambienceVolume);
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
        ambienceEventInstance = CreateInstance(ambienceEventReference);
        ambienceEventInstance.start();
    }

    private void InitializeMusic(EventReference musicEventReference)
    {
        musicEventInstance = CreateInstance(musicEventReference);
        musicEventInstance.start();
        musicInitialized = true;
    }

    public void SetMusicSection(int sectionIndex)
    {
        if (!musicEnabled || !musicInitialized) return;
        musicEventInstance.setParameterByName("MusicSection", sectionIndex);
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
        if (!musicEnabled || !musicInitialized) return;
        musicEventInstance.setParameterByName("MusicSection", (float)section);
    }

    public void ToggleMusic()
    {
        musicEnabled = !musicEnabled;
        HandleMusicToggle();
        previousMusicEnabled = musicEnabled;
    }

    private void HandleMusicToggle()
    {
        if (musicEnabled && !musicInitialized)
        {
            // Initialize music if it wasn't initialized before
            if (FMODEvents.instance != null && FMODEvents.instance.musicAmbient.IsNull == false)
            {
                InitializeMusic(FMODEvents.instance.musicAmbient);
                SetMusicSection(MusicSection.Calm);
            }
        }
        else if (!musicEnabled && musicInitialized)
        {
            // Stop music but keep it initialized for quick restart
            musicEventInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        }
        else if (musicEnabled && musicInitialized)
        {
            // Restart music if it was stopped
            musicEventInstance.start();
        }
    }

    public void SetAmbienceParameter(string parameterName, float parameterValue)
    {
        ambienceEventInstance.setParameterByName(parameterName, parameterValue);
    }

    //public void SetMusicArea(MusicArea area)
    //{
    //    musicEventInstance.setParameterByName("area", (float)area);
    //}

    public void PlayOneShot(EventReference sound, Vector3 worldPos)
    {
        RuntimeManager.PlayOneShot(sound, worldPos);
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

    private void CleanUp()
    {
        // Stop and release any created instances
        foreach (EventInstance eventInstance in eventInstances)
        {
            eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            eventInstance.release();
        }
        // Stop all of the event emitters
        foreach (StudioEventEmitter emitter in eventEmitters)
        {
            emitter.Stop();
        }
    }

    private void OnDestroy()
    {
        CleanUp();
    }
}