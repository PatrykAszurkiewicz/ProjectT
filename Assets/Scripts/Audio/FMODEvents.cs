using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

public class FMODEvents : MonoBehaviour
{
    [field: Header("Ambience")]
    //[field: SerializeField] public EventReference ambience { get; private set; }

    [field: Header("Music")]
    [field: SerializeField] public EventReference musicAmbient { get; private set; }
    [field: SerializeField] public EventReference musicElectronic { get; private set; }
    [field: SerializeField] public EventReference musicPiano { get; private set; }

    //[field: Header("Player SFX")]
    [field: Header("Multi Shot SFX")]
    [field: SerializeField] public EventReference multiShotSound { get; private set; }

    [field: Header("Shot SFX")]
    [field: SerializeField] public EventReference shotSound { get; private set; }

    [field: Header("Footsteps SFX")]
    [field: SerializeField] public EventReference footstepsSound { get; private set; }

    [field: Header("Dash SFX")]
    [field: SerializeField] public EventReference dashSound { get; private set; }


    public static FMODEvents instance { get; private set; }

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found more than one FMOD Events instance in the scene.");
        }
        instance = this;
    }
}