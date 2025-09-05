using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

public class FMODEvents : MonoBehaviour
{
    [field: Header("Ambience")]
    // TODO add map ambience
    //[field: SerializeField] public EventReference ambience { get; private set; }

    [field: Header("Music")]
    [field: SerializeField] public EventReference musicAmbient { get; private set; }
    [field: SerializeField] public EventReference musicElectronic { get; private set; }
    [field: SerializeField] public EventReference musicPiano { get; private set; }

    [field: Header("Multi Shot SFX")]
    [field: SerializeField] public EventReference multiShotSound { get; private set; }

    [field: Header("Shot SFX")]
    [field: SerializeField] public EventReference shotSound { get; private set; }

    [field: Header("Footsteps SFX")]
    [field: SerializeField] public EventReference footstepsSound { get; private set; }

    [field: Header("Dash SFX")]
    [field: SerializeField] public EventReference dashSound { get; private set; }

    [field: Header("Grappling Hook SFX")]
    [field: SerializeField] public EventReference grapplingHookShoot { get; private set; }

    [field: Header("Tower Melee SFX")]
    [field: SerializeField] public EventReference towerMeleeHit { get; private set; }

    [field: Header("Tower Damage SFX")]
    [field: SerializeField] public EventReference towerDamage { get; private set; }

    [field: Header("Tower Repair SFX")]
    [field: SerializeField] public EventReference towerRepair { get; private set; }

    [field: Header("Tower Creation SFX")]
    [field: SerializeField] public EventReference towerCreation { get; private set; }

    [field: Header("Resource Collection SFX")]
    [field: SerializeField] public EventReference resourceDropCollection { get; private set; }

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