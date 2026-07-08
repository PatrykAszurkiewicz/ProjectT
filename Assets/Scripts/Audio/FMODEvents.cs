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
    [field: SerializeField] public EventReference musicCalm { get; private set; }

    [field: Header("Multi Shot SFX")]
    [field: SerializeField] public EventReference multiShotSound { get; private set; }

    [field: Header("Shot SFX")]
    [field: SerializeField] public EventReference shotSound { get; private set; }

    [field: Header("Melee SFX")]
    [field: SerializeField] public EventReference meleeSwing { get; private set; }
    [field: SerializeField] public EventReference meleeHit { get; private set; }

    [field: Header("Footsteps SFX")]
    [field: SerializeField] public EventReference footstepsSound { get; private set; }

    [field: Header("Dash SFX")]
    [field: SerializeField] public EventReference dashSound { get; private set; }

    [field: Header("Grappling Hook SFX")]
    [field: SerializeField] public EventReference grapplingHookShoot { get; private set; }

    [field: Header("Bomb Launcher SFX")]
    [field: SerializeField] public EventReference proximityMine { get; private set; }
    [field: SerializeField] public EventReference proximityMineExplode { get; private set; }

    [field: Header("Flamethrower SFX")]
    [field: SerializeField] public EventReference flamethrower { get; private set; }

    [field: Header("Turret SFX")]
    [field: SerializeField] public EventReference turretRotate { get; private set; }
    [field: SerializeField] public EventReference turretShot { get; private set; }
    [field: SerializeField] public EventReference turretSetup { get; private set; }

    [field: Header("Trap Tool SFX")]
    [field: SerializeField] public EventReference trapSetup { get; private set; }
    [field: SerializeField] public EventReference trapCapture { get; private set; }

    [field: Header("Decoy Tool SFX")]
    [field: SerializeField] public EventReference decoySetup { get; private set; }

    [field: Header("Boomerang SFX")]
    [field: SerializeField] public EventReference boomerangShot { get; private set; }
    [field: SerializeField] public EventReference boomerangHit { get; private set; }

    [field: Header("Ranged Weapon SFX")]
    [field: SerializeField] public EventReference rangedShot { get; private set; }
    [field: SerializeField] public EventReference rangedHit { get; private set; }

    [field: Header("Clock Tool SFX")]
    [field: SerializeField] public EventReference rewindActivate { get; private set; }

    [field: Header("Hammer SFX")]
    [field: SerializeField] public EventReference hammerHit { get; private set; }

    [field: Header("Projectile Parry SFX")]
    [field: SerializeField] public EventReference projectileParry { get; private set; }

    [field: Header("Tower Melee SFX")]
    [field: SerializeField] public EventReference towerMeleeHit { get; private set; }

    [field: Header("Tower Damage SFX")]
    [field: SerializeField] public EventReference towerDamage { get; private set; }

    [field: Header("Tower Repair SFX")]
    [field: SerializeField] public EventReference towerRepair { get; private set; }

    [field: Header("Tower Creation SFX")]
    [field: SerializeField] public EventReference towerCreation { get; private set; }

    [field: Header("Tower Death SFX")]
    [field: SerializeField] public EventReference towerDeath { get; private set; }

    [field: Header("Resource Collection SFX")]
    [field: SerializeField] public EventReference resourceDropCollection { get; private set; }

    [field: Header("Central Core Death SFX")]
    [field: SerializeField] public EventReference centralCoreDeath { get; private set; }

    [field: Header("Gremlin SFX")]
    [field: SerializeField] public EventReference gremlinAppearance { get; private set; }
    [field: SerializeField] public EventReference gremlinDeath { get; private set; }

    [field: Header("Enemy SFX")]
    [field: SerializeField] public EventReference enemyAttack { get; private set; }

    [field: Header("Scarecrow SFX")]
    [field: SerializeField] public EventReference scarecrowScream { get; private set; }

    [field: Header("Shield SFX")]
    [field: SerializeField] public EventReference shieldBlock { get; private set; }
    [field: SerializeField] public EventReference shieldParry { get; private set; }

    [field: Header("Boss SFX")]
    [field: SerializeField] public EventReference bossLaserShot { get; private set; }
    [field: SerializeField] public EventReference bossGroundHit { get; private set; }

    [field: Header("Augment SFX")]
    [field: SerializeField] public EventReference augmentReroll { get; private set; }
    [field: SerializeField] public EventReference click { get; private set; }
    [field: SerializeField] public EventReference augmentScreen { get; private set; }

    [field: Header("Menu SFX")]
    [field: SerializeField] public EventReference menuClick { get; private set; }

    [field: Header("Mortar SFX")]
    [field: SerializeField] public EventReference mortarShot { get; private set; }
    [field: SerializeField] public EventReference mortarExplosion { get; private set; }

    [field: Header("Smoke Screen SFX")]
    [field: SerializeField] public EventReference smokeShot { get; private set; }

    [field: Header("Proximity Mine SFX")]
    [field: SerializeField] public EventReference mineExplosion { get; private set; }

    [field: Header("Enemy Attack SFX")]
    [field: SerializeField] public EventReference pitcherAttack { get; private set; }
    [field: SerializeField] public EventReference pitcherHit { get; private set; }
    [field: SerializeField] public EventReference eyeAttack { get; private set; }
    [field: SerializeField] public EventReference bomberExplosion { get; private set; }

    [field: Header("Boss 2 SFX")]
    [field: SerializeField] public EventReference boss2Explosion { get; private set; }

    [field: Header("Tool Setup SFX")]
    [field: SerializeField] public EventReference mineSetup { get; private set; }
    [field: SerializeField] public EventReference torchSetup { get; private set; }
    [field: SerializeField] public EventReference cloakActivate { get; private set; }

    [field: Header("Lore Chest SFX")]
    [field: SerializeField] public EventReference openChest { get; private set; }

    public static FMODEvents instance { get; private set; }

    private void Awake()
    {
        // Single, persistent instance. A duplicate (e.g. a per-scene copy left in
        // a scene while the bootstrap already spawned the persistent one) destroys
        // itself so the persistent instance stays the single source of truth.
        if (instance != null)
        {
            Debug.LogError("Found more than one FMOD Events instance in the scene.");
            Destroy(gameObject);
            return;
        }
        instance = this;

        // Survive scene loads so menu/SFX audio works in every scene. Only root
        // objects can be marked; if this lives under an "AudioSystem" root, the
        // bootstrap marks that root instead.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }
}

