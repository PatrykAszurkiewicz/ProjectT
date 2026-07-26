using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;

[DefaultExecutionOrder(-200)] // Initialize before AudioManager (-100), which reads this
public class FMODEvents : MonoBehaviour
{
    [field: Header("Ambience")]
    // TODO add map ambience
    //[field: SerializeField] public EventReference ambience { get; private set; }

    [field: Header("Music")]
    // Dedicated main-menu track. MusicDirector routes the "Menu" section to this
    // event and cross-fades to a random gameplay track (below) when a run starts.
    [field: SerializeField] public EventReference musicMenu { get; private set; }
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

    // ── Music stingers (played by MusicDirector) ──────────────────────────
    // Non-diegetic one-shots fired on the punctuation moments. Route these to
    // bus:/Music in FMOD Studio, NOT bus:/SFX, so the music volume slider owns them.
    // Every one is optional: MusicDirector null-checks each (IsNull) and simply
    // plays nothing if it's unassigned, so you can wire them in one at a time.
    [field: Header("Music Stingers")]
    [field: SerializeField] public EventReference stingerWaveCleared { get; private set; }
    [field: SerializeField] public EventReference stingerBossDefeated { get; private set; }
    [field: SerializeField] public EventReference stingerFinalBossDefeated { get; private set; }
    [field: SerializeField] public EventReference stingerVictory { get; private set; }
    [field: SerializeField] public EventReference stingerGameOver { get; private set; }

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

    [field: Header("Splitter SFX")]
    // Lunge attack; falls back to enemyAttack if left unassigned (see SplitterController).
    [field: SerializeField] public EventReference splitterAttack { get; private set; }
    // Membrane tearing apart into children when the Splitter dies.
    [field: SerializeField] public EventReference splitterSplit { get; private set; }

    [field: Header("Vortex SFX")]
    // Fired each time the vortex expels a new enemy from its disk.
    [field: SerializeField] public EventReference vortexSpawn { get; private set; }

    [field: Header("Parfumer SFX")]
    // Fired each time the Parfumer drops a poison cloud.
    [field: SerializeField] public EventReference parfumerSmoke { get; private set; }

    [field: Header("Buffer SFX")]
    // Fired each time the Buffer drops a fog patch.
    [field: SerializeField] public EventReference bufferSmoke { get; private set; }

    [field: Header("Smoke Screen Tool SFX")]
    // Burst when a thrown smoke canister lands and the cloud appears.
    [field: SerializeField] public EventReference smokeExplosion { get; private set; }

    [field: Header("Revenant Necronomicon SFX")]
    // Played when the player activates the Revenant book tool.
    [field: SerializeField] public EventReference revenantActivation { get; private set; }

    [field: Header("Tower Slot SFX")]
    // One shot for placing, upgrading, or removing a tower in a slot.
    [field: SerializeField] public EventReference towerPlacement { get; private set; }
    // Continuous loop while a Generator tower is alive and operational.
    [field: SerializeField] public EventReference generatorTowerAmbience { get; private set; }

    [field: Header("Reward Screen SFX")]
    // Played when the post-stage reward screen appears.
    [field: SerializeField] public EventReference rewardScreen { get; private set; }

    [field: Header("Boss 2 SFX")]
    [field: SerializeField] public EventReference boss2Explosion { get; private set; }

    [field: Header("Tool Setup SFX")]
    [field: SerializeField] public EventReference mineSetup { get; private set; }
    [field: SerializeField] public EventReference torchSetup { get; private set; }
    [field: SerializeField] public EventReference cloakActivate { get; private set; }

    [field: Header("Lore Chest SFX")]
    [field: SerializeField] public EventReference openChest { get; private set; }

    // ── Sustained / spatialised SFX ───────────────────────────────────────
    // All six of these are 3D events with a Spatializer, played through
    // SpatialLoopSfx (a held EventInstance whose position is refreshed each
    // frame), EXCEPT decoyAmbience which is a repeating one-shot. Every one is
    // optional: the owning script null-checks it (IsNull) and stays silent if it
    // is unassigned, so they can be wired in one at a time.
    //
    // The four marked LOOPING need a loop region in FMOD Studio — they are
    // started when something begins and stopped when it ends, so a plain
    // one-shot would run out before the stop arrives.

    [field: Header("Decoy Tool Ambience")]
    // Repeating one-shot. Pulses every DecoyDevice.ambienceInterval seconds
    // (4 by default) for as long as the decoy is armed. Does NOT need to loop.
    [field: SerializeField] public EventReference decoyAmbience { get; private set; }

    [field: Header("Red Eye SFX")]
    // FMOD event "Laser". LOOPING. Plays only while the RedEye's beam is
    // actually emitting — the charge-up window is silent on this event.
    [field: SerializeField] public EventReference redEyeLaser { get; private set; }

    [field: Header("Bomber Warning SFX")]
    // FMOD event "BombWarning". LOOPING. Starts when the Bomber arms its fuse
    // and stops the instant it detonates (or disarms).
    [field: SerializeField] public EventReference bombWarning { get; private set; }

    [field: Header("Boss 2 Warning / Summon SFX")]
    // FMOD event "Boss2ExplosionWarning". LOOPING. Runs for the meteor telegraph
    // window, pinned to the marked ground spot, and stops as the blast lands.
    [field: SerializeField] public EventReference boss2ExplosionWarning { get; private set; }

    // FMOD event "Boss2Spawn". One-shot per summoned minion, at that minion's
    // own spawn point. Does NOT need to loop.
    [field: SerializeField] public EventReference boss2Spawn { get; private set; }

    [field: Header("Boss Intro SFX")]
    // FMOD event "BossZoomSound". LOOPING (or a long sting). Plays for the
    // duration of the boss-intro camera zoom, positioned on the boss.
    [field: SerializeField] public EventReference bossZoomSound { get; private set; }

    // ── Wave / world / tower SFX (all 3D, Spatializer attached) ───────────
    // Same convention as the block above: optional (IsNull-checked at each call
    // site) and, where marked LOOPING, needing a loop region in FMOD Studio.

    [field: Header("Wave SFX")]
    // FMOD event "WaveStart". One-shot cue the instant a wave goes live. It has a
    // Spatializer, so it is played at the player's position (the closest thing to a
    // "centered" point for a whole-screen event) rather than at the world origin.
    [field: SerializeField] public EventReference waveStart { get; private set; }

    [field: Header("Lore Chest Appearance SFX")]
    // FMOD event "ChestAppearance". One-shot at the chest's spawn point. Replaces the
    // gremlinAppearance sound the chest spawner was firing by mistake.
    [field: SerializeField] public EventReference chestAppearance { get; private set; }

    [field: Header("Healing Tower SFX")]
    // FMOD event "HealingTowerHalo". LOOPING. Continuous halo ambience held for as
    // long as a Heal tower is alive and operational, positioned on the tower.
    [field: SerializeField] public EventReference healingTowerHalo { get; private set; }

    [field: Header("Laser Tower SFX")]
    // FMOD event "LaserTowerAttack". LOOPING. Plays only while the Laser tower's beam
    // is up (charge + emit), stopped when the beam drops. Positioned at the muzzle.
    [field: SerializeField] public EventReference laserTowerAttack { get; private set; }

    [field: Header("Gremlin Appearance SFX (v2)")]
    // FMOD event "GremlinAppearance2". One-shot when a gremlin spawns. Used INSTEAD of
    // the older gremlinAppearance on the spawn path (see GremlinSpawner) to avoid two
    // appearance sounds firing on the same frame.
    [field: SerializeField] public EventReference gremlinAppearance2 { get; private set; }

    [field: Header("Hammer Tower SFX")]
    // FMOD event "HammerTowerAttack". One-shot on the ground-slam impact frame (fired
    // from the Hammer animator's onImpact callback), at the tower's position.
    [field: SerializeField] public EventReference hammerTowerAttack { get; private set; }

    public static FMODEvents instance { get; private set; }

    private void Awake()
    {
        // ---- Singleton ----------------------------------------------------
        // Duplicates appear when a scene still carries its own audio object while
        // AudioBootstrap has already spawned the persistent one. Destroy only the
        // COMPONENT: this may share a GameObject with other things that must live.
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                $"[FMODEvents] Duplicate found on '{gameObject.name}' in scene " +
                $"'{gameObject.scene.name}'. Destroying it. Remove the audio object from " +
                "this scene - AudioBootstrap provides a persistent one in every scene.");
            Destroy(this);
            return;
        }
        instance = this;

        // ---- Persistence --------------------------------------------------
        // DontDestroyOnLoad only works on root objects. Detach from any parent
        // ("Managers", "AudioSystem", ...) so this survives scene loads whether it
        // is a child of AudioManager or a sibling next to it.
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}

