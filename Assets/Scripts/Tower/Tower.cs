using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Tower : MonoBehaviour, IEnergyConsumer, IDamageable
{

    [Header("Grappling Hook")]
    private bool isGrapplingTarget = false;

    public void SetGrapplingTarget(bool isTarget)
    {
        isGrapplingTarget = isTarget;
    }

    [Header("Energy Generator Settings")]
    public float generatorSelfConsumption = 0.1f;
    public bool isEnergyGenerator = false; // Should be set to true for Generator towers
    public float energyGenerationRate = 1f; // Energy units per second
    public float generationRange = 4f; // Range to show generation effect
    public float generationInterval = 0.25f; // How often to generate Energy (in seconds)
    public bool showGenerationEffects = true; // Visual effects for generation
    public Color generationEffectColor = new Color(0.3f, 0.7f, 1f, 0.5f); // Light blue with transparency
    private float lastGenerationTime;
    private float generationCarry;   // fractional energy carried between generation ticks
    private GameObject auraObject;
    private SpriteRenderer auraRenderer;

    [Header("Heal Tower Settings")]
    [Tooltip("Set TRUE for Healing towers (auto-set when TowerType == Heal). A heal " +
             "tower restores nearby players' HEALTH instead of attacking enemies.")]
    public bool isHealTower = false;
    [Tooltip("Radius (world units) within which players are healed.")]
    public float healRange = 4f;
    [Tooltip("Health restored per second to EACH player inside healRange.")]
    public float healPerSecond = 5f;
    [Tooltip("How often (seconds) the heal pulse fires. Smaller = smoother.")]
    public float healInterval = 0.25f;
    [Tooltip("Fraction of the health actually restored that the tower pays from its " +
             "OWN energy (like the generator's self-consumption). 0 = free healing.")]
    public float healSelfConsumption = 0.1f;
    [Tooltip("Show the pulsing heal aura ring.")]
    public bool showHealEffects = true;
    [Tooltip("Colour of the heal aura.")]
    public Color healEffectColor = new Color(0.3f, 1f, 0.4f, 0.5f);
    private float lastHealTime;

    // Augments
    [Header("Special Effects")]
    public float freezeChance = 0f;        // Chance to freeze enemies
    public float healthRegenRate = 0f;     // Energy regeneration per second
    public float energyCostMultiplier = 1f; // Multiplier for energy costs (1.0 = normal, 0.7 = 30% less energy cost)

    // Auxiliary method for Augments
    private void UpdateTowerRegeneration()
    {
        if (healthRegenRate > 0f && currentEnergy < maxEnergy)
        {
            SupplyEnergy(healthRegenRate * Time.deltaTime);
        }
    }

    [Header("Collision Settings")]
    public SpriteCollisionConfig collisionConfig = new SpriteCollisionConfig()
    {
        enableCollision = true,
        isTrigger = false,
        colliderType = SpriteCollisionConfig.ColliderType.Box,
        paddingPercent = 0.05f // 5% padding for towers
    };
    private Collider2D spriteCollider;

    [System.Serializable]
    public class TentacleConfig
    {
        public float length = 1.2f;
        public float width = 0.3f;
        public int segments = 8;
        public float swayAmount = 0.1f;
        public float swaySpeed = 2f;
        public Color color = new Color(0.337f, 0.176f, 0.259f, 0.8f);
        public Color tipColor = new Color(0.8f, 0.3f, 0.3f, 1f);
        public Vector2 attachmentOffset = new Vector2(0f, -0.3f);
    }

    [System.Serializable]
    public class MeleeConfig
    {
        public float damageMultiplier = 1.5f;
        public float attackDuration = 0.3f;
        public float swipeArcDegrees = 60f;
        public float swipeSpeed = 8f;
        public AnimationCurve swipeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    // NOTE: new values MUST be appended at the END. Unity serializes enums by their
    // integer index, so inserting in the middle would silently re-map every tower
    // already saved in a prefab/scene. 'Heal' is the Healing Tower type.
    public enum TowerType { Basic, Artillery, Laser, Ice, Poison, Generator, Hammer, Heal }


    [Header("Hammer Tower (AOE) Settings")]
    public bool isHammerTower = false;
    public float hammerAOERadius = 2f;
    public float hammerAttackInterval = 1.5f;
    [Tooltip("Body collider size for the Hammer tower (LOCAL units; world size = this x tower scale). " +
             "This is what blocks the player/enemies. Small & explicit because the sprite frame is mostly empty margin.")]
    public Vector2 hammerBodyColliderSize = new Vector2(3f, 2f);
    [Tooltip("Body collider offset (LOCAL units). Nudge Y down so it sits on the base, not the crystals.")]
    public Vector2 hammerBodyColliderOffset = new Vector2(0f, -2f);
    private float lastHammerAttackTime;
    private HammerTowerAnimator hammerAnimator;
    private bool hammerAnimatorChecked;
    public GameObject hammerImpactEffectPrefab;
    [Header("Hammer Particle Effects")]
    public bool enableDustParticles = true;
    public Color dustColor = new Color(0.6f, 0.5f, 0.4f, 0.8f); // Brownish dust
    public int dustParticleCount = 50;
    public float dustLifetime = 1.5f;
    public float dustSpeed = 3f;
    public float dustSize = 0.15f;



    [Header("Laser Tower Settings")]
    public bool isLaserTower = false;
    public float laserWidth = 0.5f;
    public float laserMaxLength = 12f;
    public float laserDamagePerSecond = 15f;
    public Color laserColor = Color.red;
    private LineRenderer laserRenderer;
    private GameObject laserObject;
    private bool isLaserActive = false;


    private LineRenderer laserGlowRenderer; // Secondary glow layer
    private ParticleSystem laserStartParticles;
    private ParticleSystem laserImpactParticles;
    private float laserFlickerTimer = 0f;
    private float laserScrollOffset = 0f;
    private Material laserBeamMaterial;
    private Material laserGlowMaterial;

    // ── Upgraded laser VFX (added) ──────────────────────────────────────────
    [Header("Laser VFX (added)")]
    [Tooltip("Beam hue. The core burns toward white; glow/aura keep this color.")]
    [ColorUsage(true, true)] public Color laserBeamColor = new Color(0.72f, 0.12f, 1f, 1f);
    [Tooltip("Master brightness of the additive beam. Raise if you have bloom.")]
    [Range(0.25f, 4f)] public float laserIntensity = 1.6f;
    [Tooltip("Muzzle offset above the tower origin (world units).")]
    public float laserMuzzleOffset = 0.4f;
    [Tooltip("Spin-up time before the beam reaches full power.")]
    public float laserChargeTime = 0.28f;
    [Tooltip("Fade-out time when the beam switches off.")]
    public float laserPowerDownTime = 0.13f;
    [Tooltip("Sorting order for the beam. Must beat the grass (~1600 on Default). 32000 is safe.")]
    public int laserSortingOrder = 32000;

    private LineRenderer laserAuraRenderer;      // widest soft halo
    private SpriteRenderer laserMuzzleGlow;      // flare at the muzzle
    private SpriteRenderer laserImpactGlow;      // hot spot at the hit point
    private Material laserAuraMaterial;
    private Material laserSpriteMaterial;        // shared additive for glows/rings/particles
    private Texture2D laserBeamTex, laserDotTex, laserRingTex;
    private Sprite laserDotSprite, laserRingSprite;

    private float laserEnvelope = 0f;            // 0..1(+) master beam strength
    private bool laserIgnitedThisRun = false;
    private float laserNoiseSeed;
    private Vector3 laserStartCached, laserEndCached;
    private bool laserHasBeam = false;          // a valid endpoint was set at least once

    private struct LaserRing { public SpriteRenderer sr; public float age, life, maxScale; public Color col; public bool active; }
    private readonly List<LaserRing> laserRings = new List<LaserRing>();

    [Tooltip("Idle 'charged capacitor' glow at the muzzle: spins up after placing,\n" +
             "holds while the tower waits, is spent into the beam when it fires, then\n" +
             "spins up again. Purely cosmetic - turn off to get the old look back.")]
    public bool enableLaserChargeVisual = true;

    // ── Public laser state, for cosmetic-only companions (LaserChargeVisual) ──
    // Read-only on purpose: nothing outside this class may drive the beam.

    /// World-space point the beam leaves the tower from. Exactly the maths
    /// UpdateLaserBeam() uses, so anything hanging off the muzzle stays glued to the
    /// prefab-calibrated laserMuzzleOffset.
    public Vector3 LaserMuzzleWorldPosition => transform.position + Vector3.up * laserMuzzleOffset;

    /// True while the tower is lasing a target (from target acquisition through
    /// power-down request, i.e. the whole time the beam owns the muzzle).
    public bool IsLaserFiring => isLaserTower && isLaserActive && laserHasBeam;

    /// 0..1(+) master beam strength, as driven by TickLaserFX.
    public float LaserEnvelope => laserEnvelope;

    public Color LaserBeamColor => laserBeamColor;
    public float LaserIntensity => laserIntensity;
    public int LaserSortingOrder => laserSortingOrder;
    public string LaserSortingLayerName =>
        laserRenderer != null ? laserRenderer.sortingLayerName : "Default";


    [Header("Tower Properties")]
    public string towerName = "Basic Tower";
    public float damage = 10f;
    private float _range = 5f;
    public float range
    {
        get => _range;
        set
        {
            _range = value;
            // Update projectile detection range when range changes
            if (!isEnergyGenerator)
            {
                ProjectileRange = _range;
                if (rangeCollider != null)
                    rangeCollider.radius = _range;
            }
        }
    }
    public float fireRate = 1f;
    public int cost = 100;
    public TowerType towerType = TowerType.Basic;

    [Header("Visual Settings")]
    //public string spriteResourcePath = "Sprites/spritesheet_transparent2";
    public string spriteResourcePath = "Sprites/Towers/tower_melee_sprite";
    public int spriteIndex = 0;
    public float spriteScale = 0.5f;
    public bool enableAnimation = true;
    public int animationFrameCount = 43;
    public float animationSpeed = 0.25f;

    [Tooltip("Play a simple ALWAYS-ON looping frame animation from spriteResourcePath,\n" +
             "driven by a self-contained coroutine (independent of Utilities.AnimateSprite).\n" +
             "Use for towers whose body is just a looping idle - e.g. the Laser Tower's\n" +
             "00.png..48.png. Frames used = animationFrameCount, starting at spriteIndex.\n" +
             "Leave FALSE on every existing tower so they keep their current animation path.")]
    public bool loopIdleAnimation = false;

    [Tooltip("Playback speed (frames per second) for loopIdleAnimation.")]
    public float idleAnimationFps = 12f;

    [Tooltip("Set TRUE for prefabs that bring their OWN visuals — an Animator-driven\n" +
             "prefab, or a child SpriteRenderer with its own animation. When true the\n" +
             "Tower script will NOT load a sprite from spriteResourcePath, will NOT run\n" +
             "the built-in sprite-sheet animation, will NOT override the prefab's\n" +
             "transform scale, and will NOT play the placement decay animation —\n" +
             "leaving all rendering to the prefab. Use this for the animated Generator\n" +
             "and Healing tower prefabs.")]
    public bool usePrefabVisuals = false;

    [Header("Combat Settings")]
    public LayerMask targetLayer = -1;
    public GameObject projectilePrefab;
    private float baseDamageForEnergyCost; // Base damage before augments, used for energy cost


    [Header("Upgrade Settings")]
    public bool canUpgrade = true;
    public Tower upgradeTowerPrefab;
    public int upgradeLevel = 1;
    public int maxUpgradeLevel = 3;

    [Tooltip("In-place stat upgrade: PRIMARY-OUTPUT increase PER upgrade level " +
             "(0.20 = +20% each upgrade). Applies to whatever this tower's main job is — " +
             "attack damage for combat towers, healing rate for Heal towers, energy " +
             "generation rate for Generators. Derived live from upgradeLevel, so it " +
             "persists through saves/rewind for free and never clobbers augment values.")]
    public float upgradeDamageBonusPerLevel = 0.20f;

    [Tooltip("In-place stat upgrade: max-health (energy) increase PER upgrade level " +
             "(0.20 = +20% each upgrade). Re-derived from upgradeLevel on SetUpgradeLevel " +
             "so restoring the saved level reproduces the boosted health automatically.")]
    public float upgradeHealthBonusPerLevel = 0.20f;

    // Relative max-health multiplier currently folded into maxEnergy by the in-place
    // upgrade. Tracked so RefreshUpgradeHealthScaling() is idempotent and stacks
    // cleanly with augment-driven maxEnergy changes.
    [System.NonSerialized] private float _appliedHealthMult = 1f;

    [Header("Tentacle & Melee")]
    public bool useTentacleTurret = true;
    public TentacleConfig tentacleConfig = new TentacleConfig();
    public MeleeConfig meleeConfig = new MeleeConfig();

    [Header("Rotation & Energy")]
    public float rotationSpeed = 180f;
    public bool smoothRotation = true;
    private float _maxEnergy = 100f;
    public float maxEnergy
    {
        get => _maxEnergy;
        set
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
            {
                Debug.LogWarning($"Tower '{towerName}': Invalid maxEnergy value: {value}, ignoring");
                return;
            }

            // Scale current energy proportionally to maintain health percentage
            float healthPercentage = _maxEnergy > 0 ? currentEnergy / _maxEnergy : 1f;
            _maxEnergy = value;
            currentEnergy = _maxEnergy * healthPercentage;

            UpdateVisuals();
            OnEnergyChanged?.Invoke(currentEnergy);
        }
    }
    public float currentEnergy = 100f;




    public bool requiresEnergyToFunction = true;
    public bool showEnergyBar = true;

    [Tooltip("Vertical offset (world units) for the energy bar above the tower's pivot. " +
             "Set to a value > 0 to override the per-TowerType default. " +
             "Leave at 0 to use the default for this tower's TowerType.")]
    public float energyBarOffsetOverride = 0f;

    [Header("Damage Settings")]
    public float armorReduction = 0f;
    public bool immuneToEnemyDamage = false;
    public float damageFlashDuration = 0.1f;
    public Color damageFlashColor = new Color(2f, 2f, 2f, 1f); // Additive bright flash

    // Properties
    public bool CanUpgrade => canUpgrade && upgradeLevel < maxUpgradeLevel && upgradeTowerPrefab != null;
    //public bool CanFire => Time.time >= lastFireTime + (1f / fireRate);
    public bool CanFire => Time.time >= lastFireTime +
    (1f / Mathf.Max(0.0001f, fireRate * TowerCombatModifiers.FireRateMultiplier));
    public float ProjectileRange { get; private set; }
    public Transform FirePoint => firePoint;

    // Events
    [System.NonSerialized]

    public System.Action<float> OnEnergyChanged;
    [System.NonSerialized]

    public System.Action OnEnergyDepleted, OnEnergyRestored;
    [System.NonSerialized]

    public System.Action<float, GameObject> OnDamageTaken;
    [System.NonSerialized]

    public System.Action<GameObject> OnTowerDestroyed;

    // Components
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D rangeCollider;
    private TowerSlot parentSlot;
    private EnergyBar energyBar;

    // Targeting and Combat
    [System.NonSerialized]

    private List<GameObject> enemiesInRange = new List<GameObject>();
    private GameObject currentTarget;
    private float lastFireTime;
    private float targetAngle, currentAngle;

    // Tentacle System
    private LineRenderer tentacleRenderer;
    private GameObject tentacleContainer;
    private Vector3[] tentaclePoints;
    private Transform firePoint;
    private float swayTimer, fireAnimTimer, meleeAnimTimer, swipeTimer;
    private bool isFiring, isMeleeAttacking, isSwipingMelee;

    // State
    private bool isDisabledByDamage;
    private bool isDestroyed = false;
    private Coroutine damageFlashCoroutine;

    // Generator ambience: a per-tower FMOD loop that plays only while a Generator
    // tower is alive and operational. Owned per-tower (created through RuntimeManager,
    // not AudioManager.CreateInstance) so it is released cleanly instead of piling up
    // in the shared instance list as towers are built and sold.
    //
    // Continuity: if the FMOD event has a loop region we simply hold the single
    // instance forever. If it is a one-shot (non-looping) clip we double-buffer —
    // starting the next instance a moment before the current one ends so the two
    // overlap and hide any tail/gap. isOneshot (queried at creation) picks the path,
    // so this stays correct whether or not you loop the event in FMOD.
    private FMOD.Studio.EventInstance generatorAmbience;
    private bool generatorAmbienceCreated;
    private bool generatorAmbienceOneshot;    // true = non-looping clip -> crossfade
    private int generatorAmbienceLengthMs;    // full event length in ms (0 = unknown)

    // Heal-tower halo + laser-tower beam are simple held loops rather than the
    // generator's double-buffered crossfade: both events loop cleanly in FMOD, so one
    // held instance each is enough. SpatialLoopSfx (see AudioManager.cs) owns the
    // create/guard/stop/release lifecycle. Towers don't move, so position is set on start.
    private readonly SpatialLoopSfx healHaloSfx = new SpatialLoopSfx("Heal tower halo");
    private readonly SpatialLoopSfx laserAttackSfx = new SpatialLoopSfx("Laser tower beam");

    // How long before a one-shot clip's end to start the next overlapping instance.
    private const float GeneratorAmbienceOverlapSeconds = 0.6f;

    void Awake()
    {
        LoadConfig();
        InitializeComponents();
        baseDamageForEnergyCost = damage; // Store base damage before augments are applied

    }

    // Live towers on the map. Folded in here (instead of a separate registry
    // script) because towers are few and persistent — this only exists to save
    // enemies from calling FindGameObjectsWithTag("Tower") + GetComponent every
    // 0.5s. Consumers (EnemyController.UpdateTarget) still skip null /
    // !activeInHierarchy / IsDestroyed() towers exactly as the old tag scan did.
    // Note: this keys on the Tower COMPONENT, so a hypothetical object tagged
    // "Tower" with no Tower component (none should exist) would no longer be
    // targeted — the only behavioural difference from the old scan.
    public static readonly List<Tower> ActiveTowers = new List<Tower>();

    // Clear the statics between Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveTowers.Clear();
        SpriteFrameCache.Clear();   // the Sprites it holds don't survive the session
    }

    // Register when active, unregister when disabled or destroyed.
    void OnEnable()
    {
        if (!ActiveTowers.Contains(this)) ActiveTowers.Add(this);
    }

    void OnDisable()
    {
        ActiveTowers.Remove(this);
    }

    void Start()
    {
        SetupTower();
        EnergyManager.Instance?.RegisterEnergyConsumer(this);

        // Registration above resets maxEnergy/currentEnergy to the global tower base,
        // so re-derive any in-place UPGRADE health bonus from this tower's upgrade
        // level (which may have just been set by a rewind / save-resume restore).
        // Damage is derived live in GetEffectiveDamage(), so it needs nothing here.
        _appliedHealthMult = 1f;
        RefreshUpgradeHealthScaling();

        // Apply any augments that were applied before this tower was created
        ApplyGlobalAugments();
        // ADD THIS ENTIRE SECTION
        if (isLaserTower)
        {
            // Fix: Find the TRIGGER collider (that's our range collider)
            var allColliders = GetComponents<CircleCollider2D>();
            foreach (var col in allColliders)
            {
                if (col.isTrigger)
                {
                    rangeCollider = col;
                    break;
                }
            }

            laserMaxLength = 12f;
            range = 8f;
            ProjectileRange = 8f;
            rangeCollider.radius = 8f;
            //Debug.Log($"Laser setup: collider radius={rangeCollider.radius}, ProjectileRange={ProjectileRange}");

            // Beam widths + sorting are now driven by the upgraded VFX
            // (InitializeLaser / TickLaserFX). Nothing to override here — the old
            // sortingOrder = 100 was BELOW the grass (~1600) and hid the beam.
        }


    }

    // Method to apply all previously applied augments to this tower
    public void ApplyGlobalAugments()
    {
        if (AugmentRegistry.Instance == null)
        {
            Debug.LogWarning($"Tower '{towerName}': AugmentRegistry is null, cannot apply augments");
            return;
        }

        var appliedAugments = AugmentRegistry.Instance.GetAppliedAugments();
        //Debug.Log($"Tower '{towerName}': Found {appliedAugments.Count} applied augments to check");

        foreach (int augmentId in appliedAugments)
        {
            var augmentData = AugmentRegistry.Instance.GetAugmentData(augmentId);
            if (augmentData != null && augmentData.AffectsTower)
            {
                //Debug.Log($"Tower '{towerName}': Applying tower augment '{augmentData.Name}' (ID: {augmentId})");

                var effect = AugmentRegistry.Instance.GetEffect(augmentId);
                if (effect != null)
                {
                    var target = new AugmentTarget(null as PlayerStats) { Tower = this };
                    if (effect.CanApplyTo(target))
                    {
                        try
                        {
                            float oldRate = energyGenerationRate;
                            effect.Apply(target);
                            //Debug.Log($"Tower '{towerName}': Applied augment '{augmentData.Name}' - Generation rate: {oldRate} -> {energyGenerationRate}");
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"Tower '{towerName}': Failed to apply augment '{augmentData.Name}': {e.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Tower '{towerName}': Cannot apply augment '{augmentData.Name}' - CanApplyTo returned false");
                    }
                }
                else
                {
                    Debug.LogError($"Tower '{towerName}': No effect found for augment ID {augmentId}");
                }
            }
            else if (augmentData != null)
            {
                //Debug.Log($"Tower '{towerName}': Skipping non-tower augment '{augmentData.Name}' (AffectsTower: {augmentData.AffectsTower})");
            }
        }
    }

    void Update()
    {
        // Runs before the state guards below so the loop stops cleanly the instant the
        // generator is depleted, disabled by damage, or destroyed.
        TickGeneratorAmbience();
        TickHealHalo();       // heal-tower ambience loop
        TickLaserAttackSfx(); // laser-tower beam loop

        if (isDestroyed) return;

        try
        {
            if (IsEnergyDepleted() || isDisabledByDamage) return;

            UpdateTowerRegeneration();

            if (isEnergyGenerator || towerType == TowerType.Generator)
            {
                UpdateEnergyGeneration();
            }
            else if (isHealTower || towerType == TowerType.Heal)
            {
                UpdateHealTower();
            }
            else if (isHammerTower)
            {
                UpdateHammerTower();
            }
            else if (isLaserTower)
            {
                UpdateTargeting();
                UpdateLaserTower();
                TickLaserFX(Time.deltaTime);
            }
            else
            {
                UpdateTargeting();
                UpdateTentacles();
                TryFire();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Tower '{towerName}': Error in Update: {e.Message}");
            if (float.IsNaN(currentEnergy) || float.IsNaN(maxEnergy))
            {
                Debug.LogError($"Tower '{towerName}': Detected corrupted energy values, disabling tower");
                DisableTower();
                isDestroyed = true;
            }
        }
    }



    // GENERATOR AMBIENCE (gapless)
    // Keeps a continuous loop while this Generator tower is operational and tears it
    // down the moment it is not. For a looping FMOD event we just hold one instance;
    // for a one-shot clip we crossfade overlapping instances so there is no gap.
    private void TickGeneratorAmbience()
    {
        if (!IsGenerator()) return;

        if (IsOperational())
        {
            // First start, or restart after a disable tore the instance down.
            if (!generatorAmbienceCreated)
            {
                StartGeneratorAmbienceInstance();
                return;
            }

            FMOD.Studio.PLAYBACK_STATE state;
            generatorAmbience.getPlaybackState(out state);

            if (!generatorAmbienceOneshot)
            {
                // Event loops in FMOD: it never ends on its own. Only the paranoid
                // restart if it somehow stopped — no overlap, no per-cycle churn.
                if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
                    generatorAmbience.start();
                return;
            }

            // One-shot clip: keep it seamless.
            if (state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
            {
                // Slipped past the overlap window and stopped — free and restart so it
                // can never sit silent (self-heals a rare miss).
                generatorAmbience.release();
                StartGeneratorAmbienceInstance();
                return;
            }

            if (generatorAmbienceLengthMs > 0)
            {
                // Crossfade: a moment before the current instance ends, start a fresh
                // one (they overlap) and release the old, which plays out its tail
                // then frees itself.
                int overlapMs = Mathf.RoundToInt(GeneratorAmbienceOverlapSeconds * 1000f);
                int pos;
                if (generatorAmbience.getTimelinePosition(out pos) == FMOD.RESULT.OK
                    && pos >= generatorAmbienceLengthMs - overlapMs)
                {
                    var old = generatorAmbience;
                    if (StartGeneratorAmbienceInstance())
                        old.release();
                }
            }
        }
        else if (generatorAmbienceCreated)
        {
            // Not operational: fade out and free (frees once the fade completes).
            generatorAmbience.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            generatorAmbience.release();
            generatorAmbienceCreated = false;
        }
    }

    // Creates, positions, and starts a fresh ambience instance, caching its length and
    // one-shot flag so TickGeneratorAmbience knows how to keep it continuous. Returns
    // false if the audio system isn't ready yet.
    private bool StartGeneratorAmbienceInstance()
    {
        if (AudioManager.instance == null || FMODEvents.instance == null) return false;
        if (FMODEvents.instance.generatorTowerAmbience.IsNull) return false;

        var inst = RuntimeManager.CreateInstance(FMODEvents.instance.generatorTowerAmbience);
        // Generators do not move, so the 3D position can be set once.
        inst.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
        inst.start();

        generatorAmbienceLengthMs = 0;
        generatorAmbienceOneshot = true; // safe default: treat as non-looping
        FMOD.Studio.EventDescription desc;
        if (inst.getDescription(out desc) == FMOD.RESULT.OK)
        {
            desc.getLength(out generatorAmbienceLengthMs);
            desc.isOneshot(out generatorAmbienceOneshot);
        }

        generatorAmbience = inst;
        generatorAmbienceCreated = true;
        return true;
    }

    private void StopGeneratorAmbience(bool release)
    {
        if (!generatorAmbienceCreated) return;

        if (release)
        {
            generatorAmbience.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            generatorAmbience.release();
            generatorAmbienceCreated = false;
        }
        else
        {
            generatorAmbience.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        }
    }

    // Continuous halo ambience while a Heal tower is alive and operational. Mirrors
    // the generator's operational gate, so a depleted/disabled/destroyed heal tower
    // fades quiet. One held looping instance — no crossfade needed (the event loops).
    private void TickHealHalo()
    {
        var fe = FMODEvents.instance;
        bool want = fe != null
                    && (isHealTower || towerType == TowerType.Heal)
                    && IsOperational();

        if (want && !healHaloSfx.IsActive)
            healHaloSfx.Play(fe.healingTowerHalo, transform.position);
        else if (!want && healHaloSfx.IsActive)
            healHaloSfx.Stop(immediate: false); // fade out
    }

    // Beam sound that lives exactly as long as the laser tower is emitting. IsLaserFiring
    // is the tower's own "beam is up" flag (active + has an endpoint), so the sound
    // starts with the visible beam and stops when it drops — no charge-up gap, matching
    // the RedEye fix. Held loop, stopped with a fade on power-down.
    private void TickLaserAttackSfx()
    {
        var fe = FMODEvents.instance;
        bool want = fe != null && IsLaserFiring;

        if (want && !laserAttackSfx.IsActive)
            laserAttackSfx.Play(fe.laserTowerAttack, LaserMuzzleWorldPosition);
        else if (!want && laserAttackSfx.IsActive)
            laserAttackSfx.Stop(immediate: false);

        // Muzzle is fixed on the tower, but keep it pinned in case the tower is ever moved.
        if (laserAttackSfx.IsActive) laserAttackSfx.SetPosition(LaserMuzzleWorldPosition);
    }

    #region Hammer Tower System
    void UpdateHammerTower()
    {
        if (!isHammerTower) return;

        // Lazily resolve the optional animator once.
        if (!hammerAnimatorChecked)
        {
            hammerAnimator = GetComponent<HammerTowerAnimator>();
            hammerAnimatorChecked = true;
        }

        // Mid-swing: let the animation run. Damage/sound fire from the impact callback.
        if (hammerAnimator != null && hammerAnimator.IsAttacking) return;

        // Nothing to hit? Don't swing.
        if (!AnyEnemyInHammerRange()) return;

        // Cooldown between swings.
        if (Time.time < lastHammerAttackTime + hammerAttackInterval) return;

        // Don't start a swing we can't finish.
        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed) return;

        lastHammerAttackTime = Time.time;

        if (hammerAnimator != null)
            hammerAnimator.PlayAttack(onImpact: PerformHammerImpact); // damage fires on the impact frame
        else
            PerformHammerImpact();                                    // no animator -> instant (legacy)
    }

    bool AnyEnemyInHammerRange()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, hammerAOERadius, targetLayer);
        foreach (Collider2D hit in hits)
            if (IsEnemy(hit.gameObject)) return true;
        return false;
    }

    // Called at the exact impact frame (or immediately, if there is no animator).
    // Re-scans the AOE so the slam hits whoever is actually under the hammer when it lands.
    void PerformHammerImpact()
    {
        if (isDisabledByDamage || isDestroyed) return;

        // Ground-slam feedback always plays on impact (a whiff still hits the ground).
        PlayHammerImpactEffect();
        AudioManager.instance?.PlayOneShot(FMODEvents.instance.towerMeleeHit, transform.position);

        // Dedicated hammer-slam cue, fired on the same impact frame as the hit above.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.hammerTowerAttack.IsNull)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.hammerTowerAttack, transform.position);

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, hammerAOERadius, targetLayer);
        List<GameObject> targets = new List<GameObject>();
        foreach (Collider2D hit in hits)
            if (IsEnemy(hit.gameObject)) targets.Add(hit.gameObject);

        if (targets.Count == 0) return;

        float baseCost = baseDamageForEnergyCost * 0.1f;
        float energyCost = baseCost * energyCostMultiplier;
        var generatorBoost = GetComponent<GeneratorProximityBoost>();
        if (generatorBoost != null)
            energyCost *= generatorBoost.GetEnergyEfficiencyMultiplier();

        if (currentEnergy < energyCost) return;
        ConsumeEnergy(energyCost);

        float effectiveDamage = GetEffectiveDamage();
        foreach (GameObject target in targets)
        {
            if (target == null) continue;
            var stats = target.GetComponent<EnemyStats>();
            if (stats != null)
            {
                TowerKillAttribution.MarkTowerHit(target);
                stats.TakeDamage(effectiveDamage);
                CombatStats.ReportTowerDamageDealt(effectiveDamage);
                ApplyFreezeEffect(target);
            }
        }
    }

    void PlayHammerImpactEffect()
    {
        if (hammerImpactEffectPrefab != null)
        {
            Instantiate(hammerImpactEffectPrefab, transform.position, Quaternion.identity);
        }
        // Create dust particle effect
        if (enableDustParticles)
        {
            CreateDustParticleEffect();
        }
        // Create shockwave visual effect
        StartCoroutine(HammerShockwaveEffect());
    }

    void CreateDustParticleEffect()
    {
        GameObject dustEffect = new GameObject("HammerDustEffect");
        dustEffect.transform.position = transform.position;
        ParticleSystem ps = dustEffect.AddComponent<ParticleSystem>();
        ps.Stop();
        ps.Clear();
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.5f); // Variation
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f); // Speed variation
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
        main.startColor = new Color(0.65f, 0.55f, 0.45f, 0.9f);
        main.gravityModifier = 0f;
        main.maxParticles = 50;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        emission.burstCount = 1;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, 40));
        // Radial emission 
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;
        shape.radiusThickness = 0.3f;
        shape.arc = 360f;
        // Simulate particles spreading out then slowing down
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
        // Deceleration curve 
        AnimationCurve decelCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f);
        velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve(1f, decelCurve);
        // Fade out
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.alphaKeys = new GradientAlphaKey[] {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(0.7f, 0.4f),
            new GradientAlphaKey(0f, 1f)
        };
        colorOverLifetime.color = gradient;
        // Particles expand as dust cloud spreads
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = 50;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
        Destroy(dustEffect, 2f);
    }

    System.Collections.IEnumerator HammerShockwaveEffect()
    {
        GameObject shockwave = new GameObject("HammerShockwave");
        shockwave.transform.position = transform.position;
        shockwave.transform.SetParent(transform);

        LineRenderer lr = shockwave.AddComponent<LineRenderer>();
        Material lineMat = new Material(Shader.Find("Sprites/Default"));
        lineMat.color = new Color(1f, 0.5f, 0f, 0.8f); // Orange color
        lr.material = lineMat;
        lr.startWidth = 0.2f;
        lr.endWidth = 0.2f;
        lr.useWorldSpace = false;
        lr.sortingOrder = 25;

        // Create circle
        int segments = 32;
        lr.positionCount = segments + 1;

        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float currentRadius = Mathf.Lerp(0.5f, hammerAOERadius, t);
            float alpha = 1f - t;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * 2f * Mathf.PI;
                Vector3 pos = new Vector3(
                    Mathf.Cos(angle) * currentRadius,
                    Mathf.Sin(angle) * currentRadius,
                    0f
                );
                lr.SetPosition(i, pos);
            }

            Color color = lineMat.color;
            color.a = alpha * 0.8f;
            lineMat.color = color;

            yield return null;
        }

        Destroy(shockwave);
    }
    #endregion

    #region Laser Tower System

    // Builds the upgraded, self-contained laser VFX: three additive beam layers
    // (aura + glow + white-hot core), muzzle/impact glow sprites, converging
    // charge-up motes and impact sparks. Textures + materials are generated at
    // runtime, so there is nothing to import. Everything renders at a high
    // sortingOrder so the grass (~1600 on Default) can never hide it.
    void InitializeLaser()
    {
        if (!isLaserTower) return;

        laserNoiseSeed = UnityEngine.Random.value * 1000f;
        laserStartCached = laserEndCached = transform.position;

        // Textures/sprites are generated ONCE per session and shared by every laser
        // tower (LaserVfxAssets). They used to be rebuilt per placement: ~37k pixels
        // of per-pixel CPU work, two Sprite.Creates and three GPU uploads every time a
        // tower was dropped - and they were never released, so each placement leaked
        // them too. Same pixels, same look, paid for once.
        laserBeamTex = LaserVfxAssets.BeamTexture;
        laserDotTex = LaserVfxAssets.DotTexture;
        laserRingTex = LaserVfxAssets.RingTexture;
        laserDotSprite = LaserVfxAssets.DotSprite;
        laserRingSprite = LaserVfxAssets.RingSprite;

        // Materials stay per-tower - each beam scrolls its own texture offset - but
        // they're cheap, and Cleanup() now destroys them instead of leaking them.
        Shader add = LaserVfxAssets.AdditiveShader;
        laserBeamMaterial = new Material(add) { mainTexture = laserBeamTex };
        laserGlowMaterial = new Material(add) { mainTexture = laserBeamTex };
        laserAuraMaterial = new Material(add) { mainTexture = laserBeamTex };
        laserSpriteMaterial = new Material(add) { mainTexture = laserDotTex };
        LaserVfxAssets.Tint(laserBeamMaterial, Color.white); LaserVfxAssets.Tint(laserGlowMaterial, Color.white);
        LaserVfxAssets.Tint(laserAuraMaterial, Color.white); LaserVfxAssets.Tint(laserSpriteMaterial, Color.white);

        laserObject = new GameObject("Laser");
        laserObject.transform.SetParent(transform);
        laserObject.transform.localPosition = Vector3.zero;

        // Three beam layers (back-to-front): wide aura, soft glow, bright core.
        laserAuraRenderer = LaserMakeLine("LaserAura", laserAuraMaterial, laserSortingOrder);
        laserGlowRenderer = LaserMakeLine("LaserGlow", laserGlowMaterial, laserSortingOrder + 1);
        laserRenderer = LaserMakeLine("LaserCore", laserBeamMaterial, laserSortingOrder + 2);

        laserMuzzleGlow = LaserMakeGlowSprite("LaserMuzzle", laserSortingOrder + 3);
        laserImpactGlow = LaserMakeGlowSprite("LaserImpact", laserSortingOrder + 3);

        // Charge-up motes converging INTO the muzzle (reuses laserStartParticles).
        var inflowGO = new GameObject("LaserInflow");
        inflowGO.transform.SetParent(laserObject.transform);
        inflowGO.transform.localPosition = Vector3.zero;
        laserStartParticles = inflowGO.AddComponent<ParticleSystem>();
        {
            var m = laserStartParticles.main;
            m.loop = true; m.playOnAwake = false; m.startLifetime = 0.4f; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            m.startColor = Color.Lerp(laserBeamColor, Color.white, 0.4f);
            m.simulationSpace = ParticleSystemSimulationSpace.Local; m.maxParticles = 60;
            var em = laserStartParticles.emission; em.enabled = true; em.rateOverTime = 90f;
            var sh = laserStartParticles.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 0.7f; sh.radiusThickness = 0f;                 // spawn on the ring edge
            var vel = laserStartParticles.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local; vel.radial = new ParticleSystem.MinMaxCurve(-2.2f);
            var col = laserStartParticles.colorOverLifetime; col.enabled = true;
            col.color = LaserFadeGradient(laserBeamColor, Color.white);
            var psr = laserStartParticles.GetComponent<ParticleSystemRenderer>();
            psr.material = laserSpriteMaterial;
            psr.sortingLayerName = laserRenderer.sortingLayerName;
            psr.sortingOrder = laserSortingOrder + 1;
            laserStartParticles.Stop();
        }

        // Impact sparks (reuses laserImpactParticles).
        var sparkGO = new GameObject("LaserImpactParticles");
        sparkGO.transform.SetParent(laserObject.transform);
        sparkGO.transform.localPosition = Vector3.zero;
        laserImpactParticles = sparkGO.AddComponent<ParticleSystem>();
        {
            var m = laserImpactParticles.main;
            m.loop = false; m.playOnAwake = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            m.gravityModifier = 1.3f; m.startColor = Color.Lerp(laserBeamColor, Color.white, 0.5f);
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 128;
            var em = laserImpactParticles.emission; em.enabled = false;   // Emit() manually
            var sh = laserImpactParticles.shape; sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
            var col = laserImpactParticles.colorOverLifetime; col.enabled = true;
            col.color = LaserFadeGradient(Color.white, laserBeamColor);
            var psr = laserImpactParticles.GetComponent<ParticleSystemRenderer>();
            psr.material = laserSpriteMaterial;
            psr.sortingLayerName = laserRenderer.sortingLayerName;
            psr.sortingOrder = laserSortingOrder + 4;
            laserImpactParticles.Stop();
        }

        // Idle 'charged capacitor' glow at the muzzle. Added here (rather than on the
        // prefab) so there's nothing to wire up, and last so it can read the sorting
        // layer off the beam renderers above. Cosmetic only - it reads the public
        // laser state and never touches damage, energy or targeting.
        if (enableLaserChargeVisual && GetComponent<LaserChargeVisual>() == null)
            gameObject.AddComponent<LaserChargeVisual>();
    }

    // Per-frame VFX driver. Runs every frame for laser towers (see Update). Reads
    // isLaserActive + the cached muzzle/hit points (set by UpdateLaserBeam) and
    // plays the whole charge → ignite → beam → power-down cycle. Pure visuals.
    void TickLaserFX(float dt)
    {
        if (!isLaserTower || laserRenderer == null) return;

        float target = (isLaserActive && laserHasBeam) ? 1f : 0f;
        float rate = target > laserEnvelope
            ? dt / Mathf.Max(0.01f, laserChargeTime)
            : dt / Mathf.Max(0.01f, laserPowerDownTime);

        // Over-bright ignition flash + shock rings on the rising edge.
        if (target > 0f && !laserIgnitedThisRun && laserEnvelope >= 0.28f)
        {
            laserIgnitedThisRun = true;
            laserEnvelope = 1.15f;
            SpawnLaserRing(laserStartCached, 0.15f, 1.0f, 0.28f);
            SpawnLaserRing(laserEndCached, 0.10f, 1.3f, 0.30f);
            if (laserImpactParticles != null) laserImpactParticles.Emit(14);
        }
        if (target <= 0f && laserEnvelope <= 0.05f) laserIgnitedThisRun = false;

        laserEnvelope = Mathf.MoveTowards(laserEnvelope, target, rate);

        bool visible = laserEnvelope > 0.002f;
        laserRenderer.enabled = visible;
        if (laserGlowRenderer) laserGlowRenderer.enabled = visible;
        if (laserAuraRenderer) laserAuraRenderer.enabled = visible;
        if (laserMuzzleGlow) laserMuzzleGlow.enabled = visible;
        if (laserImpactGlow) laserImpactGlow.enabled = visible && laserEnvelope > 0.15f;

        // Charge motes play only while spinning up.
        if (laserStartParticles != null)
        {
            laserStartParticles.transform.position = laserStartCached;
            bool charging = target > 0f && laserEnvelope < 0.9f;
            if (charging && !laserStartParticles.isPlaying) laserStartParticles.Play();
            else if (!charging && laserStartParticles.isPlaying) laserStartParticles.Stop();
        }

        UpdateLaserRings(dt);

        if (!visible)
        {
            if (laserImpactParticles != null && laserImpactParticles.isPlaying) laserImpactParticles.Stop();
            return;
        }

        float t = Time.time;
        float e = Mathf.Clamp01(laserEnvelope);
        float over = Mathf.Max(0f, laserEnvelope - 1f);

        Vector3 start = laserStartCached;
        Vector3 endJit = laserEndCached + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.015f * e);
        Vector3 dir = endJit - start; float len = dir.magnitude;
        if (len < 1e-4f) { dir = Vector3.up; len = 1e-4f; }
        Vector3 fwd = dir / len;
        Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);

        float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(laserEnvelope * 1.6f)); // grows out during charge
        Vector3 hit = start + fwd * (len * reach);

        const int pts = 16;
        BuildLaserPolyline(laserAuraRenderer, pts, start, fwd, perp, len, reach, t, e, 0.6f);
        BuildLaserPolyline(laserGlowRenderer, pts, start, fwd, perp, len, reach, t, e, 1f);
        BuildLaserPolyline(laserRenderer, pts, start, fwd, perp, len, reach, t, e, 1f);

        float jit = 1f + (Mathf.PerlinNoise(t * 15f, laserNoiseSeed) - 0.5f) * 2f * 0.16f;
        float snap = 0.55f + 0.45f * e;
        laserRenderer.widthMultiplier = 0.06f * jit * snap * (1f + over * 1.6f);
        if (laserGlowRenderer) laserGlowRenderer.widthMultiplier = 0.20f * (0.85f + 0.15f * jit) * snap * (1f + over);
        if (laserAuraRenderer) laserAuraRenderer.widthMultiplier = 0.50f * (0.9f + 0.1f * jit) * (0.5f + 0.5f * e) * (1f + over * 0.5f);

        Color core = Color.Lerp(laserBeamColor, Color.white, 0.7f + 0.3f * e) * laserIntensity * (0.4f + 0.6f * e) * (1f + over); core.a = e;
        Color glow = laserBeamColor * laserIntensity * (0.35f + 0.65f * e); glow.a = e * 0.85f;
        Color aura = laserBeamColor * laserIntensity * (0.25f + 0.4f * e); aura.a = e * 0.4f;
        SetLaserLineColor(laserRenderer, core, 0.06f);
        if (laserGlowRenderer) SetLaserLineColor(laserGlowRenderer, glow, 0.08f);
        if (laserAuraRenderer) SetLaserLineColor(laserAuraRenderer, aura, 0.15f);

        // Energy pattern scrolls along the core.
        var off = laserRenderer.material.mainTextureOffset;
        off.x -= 7f * dt * 0.15f;
        laserRenderer.material.mainTextureOffset = off;
        laserRenderer.material.mainTextureScale = new Vector2(Mathf.Max(1f, len * 0.8f), 1f);

        if (laserMuzzleGlow)
        {
            laserMuzzleGlow.transform.position = start;
            float mp = 0.9f + 0.22f * Mathf.Sin(t * 34f);
            laserMuzzleGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.18f, 0.6f, e) * mp * (1f + over);
            laserMuzzleGlow.transform.rotation = Quaternion.Euler(0, 0, t * 90f);
            laserMuzzleGlow.color = LaserFade(Color.Lerp(laserBeamColor, Color.white, 0.6f) * laserIntensity, Mathf.Clamp01(e * 1.2f));
        }
        if (laserImpactGlow)
        {
            laserImpactGlow.transform.position = hit;
            float ip = 0.85f + 0.3f * Mathf.PerlinNoise(t * 22f, 5.1f);
            laserImpactGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.55f, e) * ip * (1f + over);
            laserImpactGlow.transform.rotation = Quaternion.Euler(0, 0, -t * 120f);
            laserImpactGlow.color = LaserFade(Color.Lerp(laserBeamColor, Color.white, 0.4f) * laserIntensity, e);
        }

        if (laserImpactParticles != null)
        {
            laserImpactParticles.transform.position = hit;
            if (laserEnvelope > 0.6f)
            {
                if (!laserImpactParticles.isPlaying) laserImpactParticles.Play();
                if (UnityEngine.Random.value < 0.15f) laserImpactParticles.Emit(1);
            }
        }
    }

    void BuildLaserPolyline(LineRenderer lr, int pts, Vector3 start, Vector3 fwd, Vector3 perp,
                            float len, float reach, float t, float e, float waverScale)
    {
        if (lr == null) return;
        lr.positionCount = pts;
        for (int i = 0; i < pts; i++)
        {
            float f = i / (float)(pts - 1);
            Vector3 p = start + fwd * (len * f * reach);
            if (i != 0 && i != pts - 1)
            {
                float shape = Mathf.Sin(f * Mathf.PI);
                float n = Mathf.PerlinNoise(laserNoiseSeed + f * 3.5f, t * 10f) - 0.5f;
                p += perp * (n * 2f * 0.03f * waverScale * shape * e);
            }
            lr.SetPosition(i, p);
        }
    }

    static Color LaserFade(Color c, float a) { c.a = a; return c; }

    static void SetLaserLineColor(LineRenderer lr, Color c, float edge)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(c.a, edge),
                new GradientAlphaKey(c.a, 1f - edge),
                new GradientAlphaKey(c.a * 0.5f, 1f)
            });
        lr.colorGradient = g;
    }

    void SpawnLaserRing(Vector3 pos, float startScale, float maxScale, float life)
    {
        int idx = -1;
        for (int i = 0; i < laserRings.Count; i++) if (!laserRings[i].active) { idx = i; break; }
        if (idx == -1)
        {
            var go = new GameObject("LaserRing");
            go.transform.SetParent(laserObject != null ? laserObject.transform : transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = laserRingSprite; sr.material = laserSpriteMaterial;
            sr.sortingLayerName = laserRenderer.sortingLayerName;
            sr.sortingOrder = laserSortingOrder + 2;
            laserRings.Add(new LaserRing { sr = sr });
            idx = laserRings.Count - 1;
        }
        var r = laserRings[idx];
        r.sr.transform.position = pos;
        r.sr.transform.localScale = Vector3.one * startScale;
        r.age = 0f; r.life = life; r.maxScale = maxScale;
        r.col = Color.Lerp(laserBeamColor, Color.white, 0.5f) * laserIntensity;
        r.active = true; r.sr.enabled = true;
        laserRings[idx] = r;
    }

    void UpdateLaserRings(float dt)
    {
        for (int i = 0; i < laserRings.Count; i++)
        {
            var r = laserRings[i];
            if (!r.active) continue;
            r.age += dt;
            float k = r.age / r.life;
            if (k >= 1f) { r.active = false; if (r.sr) r.sr.enabled = false; laserRings[i] = r; continue; }
            if (r.sr)
            {
                r.sr.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, r.maxScale, Mathf.SmoothStep(0f, 1f, k));
                var c = r.col; c.a = 1f - k; r.sr.color = c;
            }
            laserRings[i] = r;
        }
    }

    LineRenderer LaserMakeLine(string goName, Material mat, int order)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(laserObject.transform);
        go.transform.localPosition = Vector3.zero;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.material = mat;
        lr.numCapVertices = 8; lr.numCornerVertices = 4;
        lr.textureMode = LineTextureMode.Tile; lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.sortingOrder = order; lr.positionCount = 2; lr.enabled = false;
        return lr;
    }

    SpriteRenderer LaserMakeGlowSprite(string goName, int order)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(laserObject.transform);
        go.transform.localPosition = Vector3.zero;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = laserDotSprite; sr.material = laserSpriteMaterial;
        sr.sortingOrder = order; sr.enabled = false;
        return sr;
    }

    // LaserTint / LaserFindAdditiveShader / LaserMakeDot / LaserMakeRing /
    // LaserMakeBeam moved verbatim to LaserVfxAssets, which builds them once per
    // session and shares them across every laser tower (and LaserChargeVisual)
    // instead of regenerating them on every placement.
    static Gradient LaserFadeGradient(Color from, Color to)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        return g;
    }


    void UpdateLaserTower()
    {
        if (!isLaserTower || laserRenderer == null)
        {
            if (Time.frameCount % 120 == 0)
            {
                Debug.LogWarning($"UpdateLaserTower early exit: isLaserTower={isLaserTower}, laserRenderer null={laserRenderer == null}");
            }
            return;
        }

        // Find target
        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            DisableLaser();
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);

        if (Time.frameCount % 60 == 0)
        {
            /*
            Debug.Log($"Laser: target={currentTarget.name}, distance={distanceToTarget:F2}, " +
                      $"ProjectileRange={ProjectileRange}, isActive={isLaserActive}");
                      */
        }

        if (distanceToTarget > ProjectileRange)
        {
            //Debug.Log($"Laser: Target too far! {distanceToTarget:F2} > {ProjectileRange}");
            DisableLaser();
            return;
        }

        // Activate laser
        if (!isLaserActive)
        {
            ActivateLaser();
        }

        // Update laser position and damage
        UpdateLaserBeam();
    }

    void ActivateLaser()
    {
        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed)
        {
            Debug.LogWarning("Cannot activate laser: depleted/disabled/destroyed");
            return;
        }
        isLaserActive = true;
        // Renderers/particles are driven by TickLaserFX (charge-up begins now).
    }


    void DisableLaser()
    {
        // Just request "off". TickLaserFX plays the power-down fade and turns the
        // renderers/particles off once the beam has fully faded.
        isLaserActive = false;
    }

    void UpdateLaserBeam()
    {
        if (!isLaserActive || currentTarget == null) return;
        if (laserRenderer == null)
        {
            Debug.LogError("laserRenderer is NULL");
            return;
        }

        // Energy consumption (unchanged)
        float energyCost = (damage * 0.1f * Time.deltaTime) * energyCostMultiplier;
        if (currentEnergy < energyCost)
        {
            DisableLaser();
            return;
        }
        ConsumeEnergy(energyCost);

        // Cache the muzzle + hit point; TickLaserFX draws the actual beam from these.
        laserStartCached = transform.position + Vector3.up * laserMuzzleOffset;
        laserEndCached = currentTarget.transform.position;
        laserHasBeam = true;

        // Apply damage. Laser deals continuous damage; the in-place upgrade boosts it
        // (+20% per level) via UpgradePowerMultiplier, matching projectile/melee towers.
        float damageThisFrame = damage * Time.deltaTime * TowerCombatModifiers.DamageMultiplier * UpgradePowerMultiplier;
        var targetStats = currentTarget.GetComponent<EnemyStats>();
        if (targetStats != null)
        {
            TowerKillAttribution.MarkTowerHit(targetStats.gameObject);
            targetStats.TakeDamage(damageThisFrame);
            CombatStats.ReportTowerDamageDealt(damageThisFrame);
        }
    }
    #endregion



    #region Energy Generation System
    void UpdateEnergyGeneration()
    {
        // Validate generation rate before using it
        if (float.IsNaN(energyGenerationRate) || float.IsInfinity(energyGenerationRate))
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid energyGenerationRate: {energyGenerationRate}, resetting to 1.0");
            energyGenerationRate = 1f;
        }

        if (float.IsNaN(generationInterval) || float.IsInfinity(generationInterval) || generationInterval <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid generationInterval: {generationInterval}, resetting to 0.25");
            generationInterval = 0.25f;
        }

        // Generate energy at specified intervals
        if (Time.time >= lastGenerationTime + generationInterval)
        {
            GenerateEnergy();
            lastGenerationTime = Time.time;
        }

        // Update visual effects
        if (showGenerationEffects)
        {
            UpdateGenerationEffects();
        }
    }
    void GenerateEnergy()
    {
        if (EnergyManager.Instance == null) return;

        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed) return;

        // Validate input values
        if (float.IsNaN(energyGenerationRate) || float.IsInfinity(energyGenerationRate))
        {
            Debug.LogWarning($"Tower '{towerName}': Cannot generate energy - invalid generation rate");
            return;
        }

        if (float.IsNaN(generationInterval) || float.IsInfinity(generationInterval) || generationInterval <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Cannot generate energy - invalid generation interval");
            return;
        }

        // Calculate energy to generate based on rate and interval. The in-place
        // upgrade boosts generation rate (+20% per level), derived live so it persists.
        float energyToGenerate = energyGenerationRate * generationInterval * UpgradePowerMultiplier;

        // Validate the calculated energy
        if (float.IsNaN(energyToGenerate) || float.IsInfinity(energyToGenerate))
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid energyToGenerate calculated: {energyToGenerate}, skipping generation");
            return;
        }

        //int energyAmount = Mathf.RoundToInt(energyToGenerate);
        // Accumulate fractional energy across ticks so small rates aren't silently
        // lost to per-tick integer rounding (e.g. rate 1 × interval 0.25 = 0.25,
        // which RoundToInt would floor to 0 every tick → no energy ever).
        generationCarry += energyToGenerate * EnergyManager.Instance.globalResourceMultiplier;
        int energyAmount = Mathf.FloorToInt(generationCarry);
        generationCarry -= energyAmount;

        // Give energy to the player
        if (energyAmount > 0)
            EnergyManager.Instance.GivePlayerEnergy(energyAmount);

        // Augment 345 — Overload Aura: deal AoE damage (= energy generated) and
        // pulse the stasis visual. No-op unless the augment is active.
        GeneratorAoeDamage.Apply(this, energyAmount);

        // Energy cost for energy generation
        //float selfEnergyCost = energyToGenerate * 0.1f; // 10% of generated energy
        float selfEnergyCost = energyToGenerate * generatorSelfConsumption;

        // Validate self energy cost
        if (float.IsNaN(selfEnergyCost) || float.IsInfinity(selfEnergyCost))
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid selfEnergyCost: {selfEnergyCost}, using fallback cost");
            selfEnergyCost = energyAmount * 0.1f;
        }

        ConsumeEnergy(selfEnergyCost);

        // Visual feedback
        if (showGenerationEffects)
        {
            CreateGenerationPulse();
        }
    }


    void UpdateGenerationEffects()
    {
        if (auraRenderer == null) return;
        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed)
        {
            auraRenderer.color = Color.clear;
            return;
        }
        // Create pulsating effect
        float time = Time.time * 0.5f; // Pulse speed
        float pulse = Mathf.Sin(time) * 0.5f + 0.5f;

        // Validate pulse value
        if (float.IsNaN(pulse) || float.IsInfinity(pulse))
        {
            pulse = 0.5f;
        }

        // Pulsate the aura color
        Color auraColor = generationEffectColor;
        auraColor.a = generationEffectColor.a * (0.3f + pulse * 0.7f); // Pulse opacity
        auraRenderer.color = auraColor;

        // Pulsate the aura size slightly
        float baseScale = spriteScale * 2.5f;
        float scaleMultiplier = 1f + (pulse * 0.2f); // Pulse between 100% and 120% size

        // Validate scale values
        if (float.IsNaN(baseScale) || float.IsInfinity(baseScale))
        {
            baseScale = 1.25f; // Fallback value
        }

        if (float.IsNaN(scaleMultiplier) || float.IsInfinity(scaleMultiplier))
        {
            scaleMultiplier = 1f;
        }

        Vector3 newScale = Vector3.one * baseScale * scaleMultiplier;

        // Final validation of the scale vector
        if (float.IsNaN(newScale.x) || float.IsNaN(newScale.y) || float.IsNaN(newScale.z) ||
            float.IsInfinity(newScale.x) || float.IsInfinity(newScale.y) || float.IsInfinity(newScale.z))
        {
            newScale = Vector3.one * 1.25f; // Safe fallback
        }

        auraObject.transform.localScale = newScale;
        auraObject.transform.Rotate(0, 0, 30f * Time.deltaTime);
    }

    void CreateGenerationPulse()
    {
        StartCoroutine(GenerationPulseEffect());
        StartCoroutine(AuraBurstEffect());
    }

    System.Collections.IEnumerator AuraBurstEffect()
    {
        if (auraRenderer == null) yield break;

        float duration = 0.5f;
        float elapsed = 0f;
        float startScale = spriteScale * 2.5f;
        float burstScale = startScale * 1.5f; // Burst to 150% size

        // Validate scale values
        if (float.IsNaN(startScale) || float.IsInfinity(startScale))
        {
            startScale = 1.25f;
            burstScale = 1.875f;
        }

        Color startColor = generationEffectColor;
        Color burstColor = new Color(startColor.r, startColor.g, startColor.b, startColor.a * 2f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Scale burst
            float scaleCurve = Mathf.Sin(t * Mathf.PI);
            float currentScale = Mathf.Lerp(startScale, burstScale, scaleCurve);

            // Validate current scale
            if (float.IsNaN(currentScale) || float.IsInfinity(currentScale))
            {
                currentScale = startScale;
            }

            Vector3 scaleVector = Vector3.one * currentScale;
            if (float.IsNaN(scaleVector.x) || float.IsNaN(scaleVector.y) || float.IsNaN(scaleVector.z))
            {
                scaleVector = Vector3.one * startScale;
            }

            auraObject.transform.localScale = scaleVector;

            // Color burst
            Color currentColor = Color.Lerp(startColor, burstColor, scaleCurve);
            auraRenderer.color = currentColor;

            yield return null;
        }

        // Reset to normal
        auraObject.transform.localScale = Vector3.one * startScale;
        auraRenderer.color = startColor;
    }

    System.Collections.IEnumerator GenerationPulseEffect()
    {
        // Prefabs that animate themselves own their renderer — tinting it here
        // fights the Animator and produces a frantic flicker.
        if (usePrefabVisuals) yield break;
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;
        Color pulseColor = Color.Lerp(originalColor, generationEffectColor, 0.5f);

        // Pulse effect
        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin((elapsed / duration) * Mathf.PI);

            // Validate t value
            if (float.IsNaN(t) || float.IsInfinity(t))
            {
                t = 0.5f;
            }

            spriteRenderer.color = Color.Lerp(originalColor, pulseColor, t);
            yield return null;
        }

        spriteRenderer.color = originalColor;
    }
    #endregion



    #region Heal Tower System
    void UpdateHealTower()
    {
        // Validate tunables (mirrors the generator's defensive checks).
        if (float.IsNaN(healPerSecond) || float.IsInfinity(healPerSecond) || healPerSecond < 0f)
            healPerSecond = 0f;
        if (float.IsNaN(healInterval) || float.IsInfinity(healInterval) || healInterval <= 0f)
            healInterval = 0.25f;

        if (Time.time >= lastHealTime + healInterval)
        {
            // In-place upgrade boosts healing rate (+20% per level), derived live.
            HealNearbyPlayers(healPerSecond * healInterval * UpgradePowerMultiplier);
            lastHealTime = Time.time;
        }

        if (showHealEffects) UpdateHealEffects();
    }

    // Heal every player within healRange by up to healAmount HP. Co-op aware:
    // iterates the PlayerRegistry, falling back to the lone tagged player in
    // single player. Pays self-consumption proportional to HP actually restored.
    void HealNearbyPlayers(float healAmount)
    {
        if (healAmount <= 0f) return;
        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed) return;

        float rangeSqr = healRange * healRange;
        float totalHealed = 0f;

        var reg = PlayerRegistry.Instance;
        if (reg != null && PlayerRegistry.Count > 0)
        {
            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                var pr = all[i];
                if (pr == null || pr.Stats == null) continue;
                if (((Vector2)(pr.transform.position - transform.position)).sqrMagnitude > rangeSqr) continue;
                totalHealed += HealOnePlayer(pr.Stats, healAmount);
            }
        }
        else
        {
            var p = FindFirstObjectByType<PlayerStats>();
            if (p != null &&
                ((Vector2)(p.transform.position - transform.position)).sqrMagnitude <= rangeSqr)
                totalHealed += HealOnePlayer(p, healAmount);
        }

        if (totalHealed > 0f && healSelfConsumption > 0f)
            ConsumeEnergy(totalHealed * healSelfConsumption);
    }

    // Heals a single player up to their max. Returns the amount actually restored
    // (so the caller can charge self-consumption only for real healing). Leaves
    // fully-healed and downed (0 HP) players alone — reviving is a separate system.
    float HealOnePlayer(PlayerStats stats, float healAmount)
    {
        if (stats == null) return 0f;
        float missing = stats.maxHealth - stats.currentHealth;
        if (missing <= 0.01f) return 0f;          // already full
        if (stats.currentHealth <= 0f) return 0f; // downed — let the revive system handle it
        float applied = Mathf.Min(missing, healAmount);
        stats.Heal(applied);
        return applied;
    }

    void InitializeHealEffects()
    {
        // Reuse the shared aura object/renderer (a tower is never both a generator
        // and a heal tower), tinted with the heal colour.
        auraObject = new GameObject("HealAura");
        auraObject.transform.SetParent(transform);
        auraObject.transform.localPosition = new Vector3(0.1f, 0.1f, 0f);

        auraRenderer = auraObject.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = CreateCircleSprite();
        auraRenderer.color = healEffectColor;
        if (spriteRenderer != null)
            auraRenderer.sortingOrder = spriteRenderer.sortingOrder + 1; // in front of the tower

        float auraScale = spriteScale * 4f;
        if (float.IsNaN(auraScale) || float.IsInfinity(auraScale) || auraScale <= 0f)
            auraScale = 2f;
        auraObject.transform.localScale = Vector3.one * auraScale;
    }

    void UpdateHealEffects()
    {
        if (auraRenderer == null) return;
        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed)
        {
            auraRenderer.color = Color.clear;
            return;
        }

        float pulse = Mathf.Sin(Time.time * 0.5f) * 0.5f + 0.5f;
        if (float.IsNaN(pulse) || float.IsInfinity(pulse)) pulse = 0.5f;

        Color c = healEffectColor;
        c.a = healEffectColor.a * (0.3f + pulse * 0.7f);
        auraRenderer.color = c;
    }
    #endregion

    void OnDestroy() => Cleanup();

    #region Initialization
    void LoadConfig()
    {
        try
        {
            var configFile = Resources.Load<TextAsset>("Towers/tower_config");
            if (configFile == null) return;

            string json = configFile.text;
            int startIndex = json.IndexOf($"\"{towerType}\"");
            if (startIndex == -1) return;

            int braceStart = json.IndexOf('{', startIndex);
            int braceEnd = json.IndexOf('}', braceStart);
            if (braceStart == -1 || braceEnd == -1) return;

            string section = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            float health = ExtractFloat(section, "health");
            if (health > 0) { maxEnergy = currentEnergy = health; }

            float dmg = ExtractFloat(section, "damage");
            if (dmg > 0) damage = dmg;

            float rate = ExtractFloat(section, "fireRate");
            if (rate > 0) fireRate = rate;

            float rng = ExtractFloat(section, "range");
            if (rng > 0) range = rng;
        }
        catch (System.Exception e) { Debug.LogError($"Config error: {e.Message}"); }
    }

    float ExtractFloat(string json, string key)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, $"\"{key}\":\\s*([0-9.]+)");
            return match.Success ? float.Parse(match.Groups[1].Value) : 0f;
        }
        catch { return 0f; }
    }

    void InitializeComponents()
    {
        gameObject.tag = "Tower";

        // Initialize laser for laser towers
        if (isLaserTower)
        {
            InitializeLaser();
        }

        // Auto-detect if this is a generator based on tower type
        if (towerType == TowerType.Generator)
        {
            isEnergyGenerator = true;
            useTentacleTurret = false; // Generators don't need combat tentacles
        }

        // Auto-detect Heal tower based on tower type
        if (towerType == TowerType.Heal)
        {
            isHealTower = true;
            useTentacleTurret = false; // Heal towers don't attack
        }

        // Ensure SpriteRenderer exists and is properly initialized. Prefabs that
        // bring their own visuals may keep the renderer on a CHILD (e.g. under an
        // Animator), so look there too and never add a stray empty one to the root.
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null && usePrefabVisuals)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = 20;
            spriteRenderer.sortingLayerName = "Default";
        }

        // Y-Sort: dynamically sort against grass based on Y position.
        // Negative offset pushes the sort point toward the tower's base,
        // preventing grass near the tower center from protruding in front.
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.5f;
        }

        // Validate spriteScale before using it
        if (float.IsNaN(spriteScale) || float.IsInfinity(spriteScale) || spriteScale <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid spriteScale: {spriteScale}, resetting to 0.5");
            spriteScale = 0.5f;
        }

        // Prefabs with their own visuals keep their authored scale.
        if (!usePrefabVisuals)
            transform.localScale = Vector3.one * spriteScale;

        // Setup collider
        rangeCollider = GetComponent<CircleCollider2D>();
        if (rangeCollider == null)
        {
            rangeCollider = gameObject.AddComponent<CircleCollider2D>();
        }
        rangeCollider.isTrigger = true;

        // Only initialize tentacles for combat towers
        if (useTentacleTurret && !isEnergyGenerator)
        {
            InitializeTentacles();
        }

        // Initialize generation effects for generators
        if (isEnergyGenerator && showGenerationEffects)
        {
            InitializeGenerationEffects();
        }

        // Initialize the heal aura for heal towers
        if (isHealTower && showHealEffects)
        {
            InitializeHealEffects();
        }
    }

    void InitializeGenerationEffects()
    {
        // Create a circular aura effect around the tower
        auraObject = new GameObject("GenerationAura");
        auraObject.transform.SetParent(transform);
        //auraObject.transform.localPosition = Vector3.zero;
        // Shifting the pulsating aura effect to right and top to align with Tower Generator sprite
        // TODO adjust when we have new Generator sprites
        auraObject.transform.localPosition = new Vector3(0.1f, 0.1f, 0f);

        auraRenderer = auraObject.AddComponent<SpriteRenderer>();
        auraRenderer.sprite = CreateCircleSprite();
        auraRenderer.color = generationEffectColor;
        auraRenderer.sortingOrder = spriteRenderer.sortingOrder + 1; // IN FRONT of the tower

        float auraScale = spriteScale * 4f;

        // Validate aura scale
        if (float.IsNaN(auraScale) || float.IsInfinity(auraScale) || auraScale <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid auraScale: {auraScale}, using fallback");
            auraScale = 2f;
        }

        auraObject.transform.localScale = Vector3.one * auraScale;
    }

    Sprite CreateCircleSprite()
    {
        int size = 128;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float outerRadius = size * 0.4f;
        float innerRadius = size * 0.25f; // Create a ring effect

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 pos = new Vector2(x, y);
                float distance = Vector2.Distance(pos, center);

                if (distance <= outerRadius && distance >= innerRadius)
                {
                    // Create a smooth falloff
                    float alpha = 1f - Mathf.Abs(distance - (innerRadius + outerRadius) * 0.5f) / ((outerRadius - innerRadius) * 0.5f);
                    alpha = Mathf.Clamp01(alpha);
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    colors[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    void InitializeTentacles()
    {
        tentacleContainer = new GameObject("TentacleContainer");
        tentacleContainer.transform.SetParent(transform);
        tentacleContainer.transform.localPosition = tentacleConfig.attachmentOffset;

        tentacleRenderer = tentacleContainer.AddComponent<LineRenderer>();
        tentacleRenderer.material = new Material(Shader.Find("Sprites/Default")) { color = tentacleConfig.color };
        tentacleRenderer.startWidth = tentacleConfig.width;
        tentacleRenderer.endWidth = tentacleConfig.width * 0.3f;
        tentacleRenderer.positionCount = tentacleConfig.segments;
        tentacleRenderer.useWorldSpace = false;
        tentacleRenderer.sortingOrder = 999; // Will be overridden each frame in UpdateTentacles()

        var firePointObj = new GameObject("FirePoint");
        firePointObj.transform.SetParent(tentacleContainer.transform);
        firePointObj.transform.localPosition = Vector3.right * tentacleConfig.length;
        firePoint = firePointObj.transform;

        tentaclePoints = new Vector3[tentacleConfig.segments];
        for (int i = 0; i < tentacleConfig.segments; i++)
        {
            float t = (float)i / (tentacleConfig.segments - 1);
            tentaclePoints[i] = Vector3.right * (tentacleConfig.length * t);
        }
    }

    void SetupTower()
    {
        parentSlot = GetComponentInParent<TowerSlot>();

        // Ensure rangeCollider exists (it should be created in InitializeComponents)
        if (rangeCollider == null)
        {
            rangeCollider = GetComponent<CircleCollider2D>();
            if (rangeCollider == null)
            {
                Debug.LogWarning($"Tower '{towerName}': rangeCollider not found, creating one");
                rangeCollider = gameObject.AddComponent<CircleCollider2D>();
                rangeCollider.isTrigger = true;
            }
        }

        if (isEnergyGenerator)
        {
            // Generators use generation range instead of projectile range
            ProjectileRange = generationRange;
            rangeCollider.radius = generationRange;
        }
        else if (isHealTower)
        {
            // Heal towers use their heal radius
            ProjectileRange = healRange;
            rangeCollider.radius = healRange;
        }
        else if (isHammerTower)
        {
            // Hammer towers use AOE radius for detection
            ProjectileRange = hammerAOERadius;
            rangeCollider.radius = hammerAOERadius;
        }
        else if (isLaserTower)
        {
            // Laser towers use their max length
            ProjectileRange = laserMaxLength;
            rangeCollider.radius = laserMaxLength;
        }
        else
        {
            // Standard combat towers
            float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;
            ProjectileRange = Mathf.Max(range * 2f, tentacleReach * 3.5f, 6f);
            rangeCollider.radius = ProjectileRange + 0.5f;
        }

        LoadSprite();
        SetupSpriteCollision();
        SetupEnergyBar();
    }
    void SetupTower2()
    {
        parentSlot = GetComponentInParent<TowerSlot>();

        if (isEnergyGenerator)
        {
            // Generators use generation range instead of projectile range
            ProjectileRange = generationRange;
            rangeCollider.radius = generationRange;
        }
        else
        {
            float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;
            ProjectileRange = Mathf.Max(range * 2f, tentacleReach * 3.5f, 6f);
            rangeCollider.radius = ProjectileRange + 0.5f;
        }

        LoadSprite();
        SetupSpriteCollision();
        SetupEnergyBar();
    }

    public bool IsGenerator() => isEnergyGenerator || towerType == TowerType.Generator;
    public float GetGenerationRate() => energyGenerationRate;
    public void SetGenerationRate(float rate)
    {
        if (float.IsNaN(rate) || float.IsInfinity(rate) || rate < 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid generation rate: {rate}, ignoring");
            return;
        }
        energyGenerationRate = rate;
    }

    void SetupSpriteCollision()
    {
        // Respect "Enable Collision = false": do not auto-generate a body collider.
        // Lets you hand-place a collider that hugs the base instead of the full frame.
        if (collisionConfig == null || !collisionConfig.enableCollision)
            return;

        // Hammer tower: its sprite frame is huge and mostly transparent, so the
        // sprite-fit collider becomes a giant invisible wall. Build a small, explicit
        // body collider instead (tune hammerBodyColliderSize/Offset in the Inspector).
        if (isHammerTower)
        {
            var body = GetComponent<BoxCollider2D>();
            if (body == null) body = gameObject.AddComponent<BoxCollider2D>();
            body.isTrigger = false;
            body.size = hammerBodyColliderSize;
            body.offset = hammerBodyColliderOffset;
            spriteCollider = body;
            return;
        }

        // When the visible sprite lives on a CHILD (e.g. usePrefabVisuals prefabs
        // whose SpriteRenderer isn't on the root, like the Heal tower), the shared
        // SpriteCollisionManager can't size a collider — it reads the root's
        // SpriteRenderer, which is absent. Build a body collider on the root from the
        // child renderer's bounds so the player can't walk through the tower.
        if (spriteRenderer != null && spriteRenderer.gameObject != gameObject)
        {
            SetupBodyColliderFromChildRenderer();
            return;
        }

        if (spriteRenderer?.sprite != null)
        {
            spriteCollider = SpriteCollisionManager.SetupCollision(gameObject, collisionConfig);
        }
        else
        {
            // Delay setup if sprite is not ready
            SpriteCollisionManager.SetupCollisionDelayed(this, collisionConfig);
        }
    }

    // Adds/sizes a BoxCollider2D on the tower ROOT from the (child) sprite renderer's
    // world bounds. Used for prefabs whose renderer sits on a child object.
    void SetupBodyColliderFromChildRenderer()
    {
        if (collisionConfig == null || !collisionConfig.enableCollision) return;
        if (spriteRenderer == null) return;

        var box = gameObject.GetComponent<BoxCollider2D>();
        if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
        box.isTrigger = collisionConfig.isTrigger;

        Bounds b = spriteRenderer.bounds;                 // world-space AABB of the child sprite
        Vector3 ls = transform.lossyScale;
        float sx = Mathf.Abs(ls.x) < 0.0001f ? 1f : Mathf.Abs(ls.x);
        float sy = Mathf.Abs(ls.y) < 0.0001f ? 1f : Mathf.Abs(ls.y);

        float keep = 1f - Mathf.Clamp01(collisionConfig.paddingPercent);
        float w = (b.size.x / sx) * keep;
        float h = (b.size.y / sy) * keep;

        // Fallback if the sprite bounds aren't ready (e.g. Animator hasn't applied a frame yet).
        if (w < 0.05f || h < 0.05f || float.IsNaN(w) || float.IsNaN(h)) { w = 1f; h = 1f; }

        box.size = new Vector2(w, h);
        Vector3 localCenter = transform.InverseTransformPoint(b.center);
        box.offset = new Vector2(localCenter.x, localCenter.y);

        spriteCollider = box;
    }

    // ── Sprite frame cache ──────────────────────────────────────────────────
    //
    // Nested because LoadSprite() below is its only consumer, and this project has
    // enough scripts already. Same shape as HammerTowerAnimator's FrameCache, which
    // exists for the same reason — that one just never got applied to the towers.
    //
    // WHY: LoadSprite() used to call Resources.LoadAll<Sprite>() straight from
    // Start(), i.e. on the frame the tower is instantiated. Resources.LoadAll is a
    // BLOCKING call: the first Laser Tower placement of a session read and
    // decompressed all 49 idle frames on the main thread before the frame could
    // finish. That is the 1-2s freeze at placement — reproducible, not bad luck.
    //
    // WHAT: load each folder once per session, and warm the slow ones in the
    // background at startup so the cost lands during the loading screen instead of
    // on a placement. Ordering semantics are unchanged: same Resources.LoadAll, same
    // stable OrderBy on the leading integer of each sprite name.
    private static class SpriteFrameCache
    {
        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();

        public static void Clear() => Cache.Clear();

        /// Frames for 'resourcePath', ordered by the leading integer in each sprite
        /// name. Loose numeric frames (00.png ... 48.png) play in sequence; sliced
        /// spritesheets ("tower_melee_sprite_3") have no leading digit, fall back to
        /// int.MaxValue and — OrderBy being a STABLE sort — keep their slice order.
        /// Returns a fresh array each call, so no caller can disturb the shared copy.
        public static Sprite[] GetFrames(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return System.Array.Empty<Sprite>();

            if (Cache.TryGetValue(resourcePath, out var cached) && IsValid(cached))
                return (Sprite[])cached.Clone();

            var loaded = Resources.LoadAll<Sprite>(resourcePath) ?? System.Array.Empty<Sprite>();
            if (loaded.Length > 1)
                loaded = loaded.OrderBy(s => ParseLeadingFrameInt(s.name, int.MaxValue)).ToArray();

            // A failed load is cached as an empty array so a broken path doesn't hit
            // the filesystem on every placement; IsValid() rejects it on read, so a
            // path that becomes valid later is still picked up.
            Cache[resourcePath] = loaded;
            return (Sprite[])loaded.Clone();
        }

        public static bool IsWarm(string resourcePath) =>
            !string.IsNullOrEmpty(resourcePath) &&
            Cache.TryGetValue(resourcePath, out var c) && IsValid(c);

        /// Make a folder resident without blocking. Resources has no async LoadAll, so
        /// pull the frames one at a time ("00", "01", ...) — each LoadAsync does its
        /// decompression on a worker thread, spreading the cost over many frames. The
        /// closing GetFrames() then runs the real LoadAll for exact ordering, which is
        /// ~free once every texture it wants is already in memory.
        public static IEnumerator PreloadAsync(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath) || IsWarm(resourcePath)) yield break;

            for (int i = 0; i < 512; i++)   // bound: never spin on a pathological folder
            {
                var req = Resources.LoadAsync<Sprite>($"{resourcePath}/{i:00}");
                yield return req;
                if (req.asset == null) break;          // walked past the last frame
            }
            GetFrames(resourcePath);
        }

        // Parse the leading run of digits in a sprite name ("07_glow" -> 7).
        private static int ParseLeadingFrameInt(string name, int fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            int i = 0;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            return i > 0 && int.TryParse(name.Substring(0, i), out int v) ? v : fallback;
        }

        // Cached sprites only stay usable while they're alive — a scene change plus
        // UnloadUnusedAssets can pull them out from under us.
        private static bool IsValid(Sprite[] frames) =>
            frames != null && frames.Length > 0 && frames[0] != null;
    }

    /// Folders preloaded in the background at startup. Add a tower's
    /// spriteResourcePath here if its first placement hitches.
    private static readonly string[] WarmupSpriteFolders =
    {
        "Sprites/Buildings/Towers/LaserTower",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void WarmTowerSprites()
    {
        if (WarmupSpriteFolders == null || WarmupSpriteFolders.Length == 0) return;

        // Statics can't run coroutines, so spawn a throwaway hidden runner —
        // same trick HammerTowerAnimator.AutoWarmCache() uses.
        var go = new GameObject("~TowerSpriteWarmup") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<Warmer>().Begin(WarmupSpriteFolders);
    }

    private class Warmer : MonoBehaviour
    {
        public void Begin(string[] folders) => StartCoroutine(Run(folders));

        private IEnumerator Run(string[] folders)
        {
            foreach (var f in folders) yield return SpriteFrameCache.PreloadAsync(f);
            Destroy(gameObject);
        }
    }

    void LoadSprite()
    {
        // Prefabs that animate themselves (Animator or a child sprite animation)
        // own their rendering — don't overwrite their sprite or start the built-in
        // sprite-sheet animation coroutine on top of them.
        if (usePrefabVisuals) return;

        // Frames come from a process-wide cache instead of a direct, BLOCKING
        // Resources.LoadAll on the placement frame. That LoadAll is what froze the
        // game for ~1-2s the first time a Laser Tower was placed: it read and
        // decompressed all 49 idle frames on the main thread. SpriteFrameCache warms
        // that folder asynchronously at startup and reuses the result afterwards.
        //
        // Ordering is unchanged - the cache runs the same Resources.LoadAll and the
        // same stable OrderBy on the leading integer of each sprite name, so loose
        // numeric frames (00.png ... 48.png) still play in sequence and sliced
        // spritesheets still keep their slice order.
        var sprites = SpriteFrameCache.GetFrames(spriteResourcePath);

        // Always-on looping idle (e.g. Laser Tower): handle this FIRST and
        // independently of the spriteIndex gate below, and ALWAYS log the load
        // result. (Previously this lived inside 'if (sprites.Length > spriteIndex)',
        // so a failed load of 0 sprites skipped everything — including the warning —
        // silently. That is exactly what "no animation, no warning" looks like.)
        if (loopIdleAnimation)
        {
            int loaded = sprites?.Length ?? 0;
            if (loaded > 0)
                Debug.Log($"[IdleAnim] '{towerName}': loaded {loaded} frame(s) from " +
                          $"Resources/'{spriteResourcePath}' (first='{sprites[0].name}', last='{sprites[loaded - 1].name}').");
            else
                Debug.LogError($"[IdleAnim] '{towerName}': loaded 0 frames from Resources/'{spriteResourcePath}'. " +
                    "Resources.LoadAll<Sprite> found nothing. Verify the frames are at " +
                    "Assets/Resources/" + spriteResourcePath + "/ and each PNG's Texture Type = Sprite (2D and UI).");

            if (loaded >= 2 && spriteRenderer != null)
            {
                int idx = Mathf.Clamp(spriteIndex, 0, loaded - 1);
                spriteRenderer.sprite = sprites[idx];
                StartCoroutine(LoopIdleFrames(sprites));
            }
            return;   // idle towers never use the Utilities.AnimateSprite path
        }

        if (sprites?.Length > spriteIndex)
        {
            spriteRenderer.sprite = sprites[spriteIndex];
            // Only animate if: animation is enabled, there are multiple sprites available
            // Animation frame count is greater than 1 and we have enough sprites for the animation frame count
            bool hasEnoughFrames = sprites.Length >= animationFrameCount;
            bool shouldAnimate = enableAnimation && sprites.Length > 1 && animationFrameCount > 1 && hasEnoughFrames;
            if (shouldAnimate)
            {
                StartCoroutine(Utilities.AnimateSprite(spriteRenderer, sprites, enableAnimation, animationFrameCount, spriteIndex, animationSpeed));
            }
        }
    }

    // Self-contained, always-looping frame animation for 'idle-only' towers.
    // Independent of Utilities.AnimateSprite: fixed FPS, loops forever, and stops
    // automatically when the tower is destroyed (Unity kills its coroutines).
    private System.Collections.IEnumerator LoopIdleFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length < 2 || spriteRenderer == null) yield break;

        int start = Mathf.Clamp(spriteIndex, 0, frames.Length - 1);
        int count = animationFrameCount > 1
            ? Mathf.Min(animationFrameCount, frames.Length - start)
            : frames.Length - start;
        if (count < 2) { start = 0; count = frames.Length; }

        float fps = idleAnimationFps > 0.01f ? idleAnimationFps : 12f;
        var wait = new WaitForSeconds(1f / fps);

        Debug.Log($"[IdleAnim] '{towerName}': looping {count} frame(s) from index {start} at {fps} fps.");

        int i = 0;
        while (true)
        {
            if (spriteRenderer == null) yield break;
            spriteRenderer.sprite = frames[start + i];
            i = (i + 1) % count;
            yield return wait;
        }
    }

    void SetupEnergyBar()
    {
        if (!showEnergyBar) return;

        energyBar = gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = true;
        energyBar.energyBarHeight = 0.1f;
        energyBar.energyBarWidth = 1f;
        // Per-prefab override takes precedence; otherwise fall back to a per-TowerType default.
        // Tune individual values in GetDefaultEnergyBarOffset() below, or just set
        // energyBarOffsetOverride on a specific prefab in the inspector.
        // Per-prefab override always wins.
        // Prefab-visual towers aren't scaled down by spriteScale, so the hardcoded
        // per-type offsets (tuned for the ~0.25-scaled sprite towers) float the bar
        // far too high. For those, derive the offset from the real sprite bounds so
        // it sits just above the visible sprite at any scale or hierarchy.
        if (energyBarOffsetOverride > 0f)
        {
            energyBar.energyBarOffset = energyBarOffsetOverride;
        }
        else if (usePrefabVisuals && spriteRenderer != null && spriteRenderer.sprite != null)
        {
            energyBar.energyBarOffset = ComputeEnergyBarOffsetFromBounds();
        }
        else
        {
            energyBar.energyBarOffset = GetDefaultEnergyBarOffset(towerType);
        }
        energyBar.showEnergyText = true;

        if (EnergyManager.Instance != null)
        {
            energyBar.SetColors(EnergyManager.Instance.normalColor, EnergyManager.Instance.lowEnergyColor,
                              EnergyManager.Instance.criticalEnergyColor, EnergyManager.Instance.depletedEnergyColor);
        }

        energyBar.Initialize(this, spriteRenderer);
    }

    /// <summary>
    /// Per-TowerType default vertical offset for the energy bar.
    /// Tune these to match each tower's sprite height. Used when
    /// energyBarOffsetOverride is 0 (the default).
    /// </summary>
    static float GetDefaultEnergyBarOffset(TowerType type)
    {
        switch (type)
        {
            case TowerType.Laser: return 1.5f;
            case TowerType.Basic: return 4.4f;// 2.4f;
            case TowerType.Artillery: return 4.4f;
            case TowerType.Ice: return 4.4f;
            case TowerType.Poison: return 4.4f;
            case TowerType.Generator: return 4.6f;
            case TowerType.Hammer: return 4.6f;
            case TowerType.Heal: return 4.6f;
            default: return 4.4f;
        }
    }

    // Computes a bar offset (in the root's LOCAL Y units — the same space the
    // GetDefaultEnergyBarOffset values use) that sits just above the visible sprite.
    // Works whether the SpriteRenderer is on the root or a child, and at any scale,
    // because it measures the renderer's real world bounds and divides out the root
    // scale that EnergyBar will re-apply.
    float ComputeEnergyBarOffsetFromBounds()
    {
        float scaleY = Mathf.Abs(transform.lossyScale.y);
        if (scaleY < 0.0001f) scaleY = 1f;

        // World height from the tower origin to the top of the sprite, plus a margin.
        float worldTop = spriteRenderer.bounds.max.y - transform.position.y + 0.25f;
        if (float.IsNaN(worldTop) || worldTop < 0.1f) worldTop = scaleY; // sane fallback

        return worldTop / scaleY;
    }
    #endregion



    #region Targeting & Combat
    void UpdateTargeting()
    {
        // Manual reverse loop instead of RemoveAll(lambda): the lambda captured
        // `this`, allocating a delegate every frame per tower. Same elements removed,
        // zero per-frame allocation.
        for (int i = enemiesInRange.Count - 1; i >= 0; i--)
        {
            var e = enemiesInRange[i];
            if (e == null || !IsValidTarget(e))
                enemiesInRange.RemoveAt(i);
        }

        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            currentTarget = GetClosestTarget();
        }

        // Update target angle and smooth rotation for tentacle aiming
        if (currentTarget != null)
        {
            Vector2 direction = (currentTarget.transform.position - transform.position).normalized;
            targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            targetAngle = Mathf.Repeat(targetAngle + 180f, 360f) - 180f;

            // Validate angles to prevent NaN
            if (float.IsNaN(targetAngle) || float.IsInfinity(targetAngle))
            {
                targetAngle = 0f;
            }

            if (smoothRotation && !isSwipingMelee)
            {
                float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
                float rotationStep = rotationSpeed * Time.deltaTime;

                // Validate rotation values
                if (float.IsNaN(angleDifference) || float.IsInfinity(angleDifference))
                {
                    angleDifference = 0f;
                }

                if (float.IsNaN(rotationStep) || float.IsInfinity(rotationStep))
                {
                    rotationStep = 1f; // Fallback rotation step
                }

                currentAngle = Mathf.Abs(angleDifference) <= rotationStep ?
                    targetAngle : currentAngle + Mathf.Sign(angleDifference) * rotationStep;

                currentAngle = Mathf.Repeat(currentAngle + 180f, 360f) - 180f;

                // Validate current angle
                if (float.IsNaN(currentAngle) || float.IsInfinity(currentAngle))
                {
                    currentAngle = 0f;
                }

                // Apply rotation to tentacle container for better aiming
                if (tentacleContainer != null)
                    tentacleContainer.transform.rotation = Quaternion.AngleAxis(currentAngle, Vector3.forward);
            }
        }
    }

    GameObject GetClosestTarget()
    {
        GameObject closest = null;
        float closestDist = float.MaxValue;

        foreach (var enemy in enemiesInRange)
        {
            float dist = Vector2.Distance(transform.position, enemy.transform.position);
            if (dist < closestDist) { closestDist = dist; closest = enemy; }
        }
        return closest;
    }

    bool IsValidTarget(GameObject target)
    {
        if (target == null || !IsEnemy(target)) return false;
        float dist = Vector2.Distance(transform.position, target.transform.position);
        return dist <= ProjectileRange && ((1 << target.layer) & targetLayer) != 0;
    }

    // Cached once. NameToLayer is a string lookup; calling it per target per frame
    // inside the targeting loop was pure waste.
    private static int _enemyLayer = -1;

    bool IsEnemy(GameObject target)
    {
        if (target == null) return false;

        if (_enemyLayer < 0) _enemyLayer = LayerMask.NameToLayer("Enemy");

        // Fast positive path: anything on the Enemy layer is an enemy. Players and
        // towers live on their own layers (never the Enemy layer), so this cannot
        // misclassify them — and it skips the GetComponent/CompareTag probes below
        // for the overwhelmingly common case (the reason this method was hot).
        if (_enemyLayer >= 0 && target.layer == _enemyLayer) return true;

        // Fallback for enemies NOT on the Enemy layer (tag-only / component-only).
        // Identical to the original logic, so nothing that used to be excluded
        // (players, towers) can slip through here.
        if (target.GetComponent<PlayerMovement>() || target.CompareTag("Player") || target.GetComponent<Tower>()) return false;
        try { if (target.CompareTag("Enemy")) return true; } catch { }
        return target.GetComponent<EnemyStats>() != null;
    }

    void TryFire()
    {
        if (currentTarget != null && CanFire) FireAtTarget(currentTarget);
    }
    private float GetEffectiveDamage()
    {
        float baseDamage = damage;

        // Check for synergy boost from adjacent towers
        var synergyBoost = GetComponent<TowerSynergyBoost>();
        if (synergyBoost != null)
        {
            baseDamage *= synergyBoost.GetDamageMultiplier();
        }

        // Check for symbiosis boost from player proximity
        var symbiosisBoost = GetComponent<SymbiosisBoost>();
        if (symbiosisBoost != null)
        {
            baseDamage *= symbiosisBoost.GetDamageMultiplier();
        }

        // Augments 338 / 346 — global tower damage multiplier (1.0 when unused).
        baseDamage *= TowerCombatModifiers.DamageMultiplier;

        // In-place upgrade bonus (+X% per upgrade level). Derived live from
        // upgradeLevel so it survives saves/rewind via the saved level alone, and
        // never overwrites augment-set damage (it multiplies on top, like synergy).
        baseDamage *= UpgradeDamageMultiplier;

        return baseDamage;
    }

    public void FireAtTarget(GameObject target)
    {
        if (target == null || IsEnergyDepleted() || isDisabledByDamage || isDestroyed) return;

        // Use base damage (before augments) for energy cost calculation
        float baseCost = baseDamageForEnergyCost * 0.05f;
        float energyCost = baseCost * energyCostMultiplier;

        // Apply generator proximity efficiency boost
        var generatorBoost = GetComponent<GeneratorProximityBoost>();
        if (generatorBoost != null)
        {
            float efficiencyMultiplier = generatorBoost.GetEnergyEfficiencyMultiplier();
            energyCost *= efficiencyMultiplier;
            //Debug.Log($"[TOWER] {towerName} energy cost reduced by generator proximity: {baseCost * energyCostMultiplier:F2} -> {energyCost:F2}");
        }

        if (float.IsNaN(energyCost) || float.IsInfinity(energyCost))
        {
            energyCost = baseCost;
        }

        if (currentEnergy < energyCost) return;

        ConsumeEnergy(energyCost);
        lastFireTime = Time.time;

        float dist = Vector2.Distance(transform.position, target.transform.position);
        float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;

        // Get effective damage for proximity boost
        float effectiveBaseDamage = GetEffectiveDamage();

        if (dist <= tentacleReach + 0.4f)
        {
            // Melee Attack - Apply melee multiplier to symbiosis-boosted damage
            isMeleeAttacking = true;
            isSwipingMelee = true;
            meleeAnimTimer = 0f;
            swipeTimer = 0f;
            var stats = target.GetComponent<EnemyStats>();

            float meleeDamage = effectiveBaseDamage * meleeConfig.damageMultiplier;

            if (float.IsNaN(meleeDamage) || float.IsInfinity(meleeDamage))
            {
                meleeDamage = effectiveBaseDamage;
            }

            //Debug.Log($"[TOWER] {towerName} MELEE attack: {effectiveBaseDamage} * {meleeConfig.damageMultiplier} = {meleeDamage} damage to {target.name}");
            TowerKillAttribution.MarkTowerHit(target);
            stats?.TakeDamage(meleeDamage);
            CombatStats.ReportTowerDamageDealt(meleeDamage);
            ApplyFreezeEffect(target);
            AudioManager.instance?.PlayOneShot(FMODEvents.instance.towerMeleeHit, FirePoint?.position ?? transform.position);
        }
        else if (dist <= ProjectileRange)
        {
            // Projectile Attack - Use symbiosis-boosted damage directly
            isFiring = true;
            fireAnimTimer = 0f;

            if (projectilePrefab != null)
            {
                AudioManager.instance?.PlayOneShot(FMODEvents.instance.multiShotSound, FirePoint.position);
                Vector3 spawn = FirePoint?.position ?? transform.position;
                Vector3 dir = (target.transform.position - spawn).normalized;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                if (float.IsNaN(angle) || float.IsInfinity(angle))
                {
                    angle = 0f;
                }

                // Pooled spawn (was Instantiate). PrefabPool.Get recycles a retired
                // projectile of this prefab, or makes one on first use. Projectile
                // fully resets its own per-shot state in Initialize(), so a recycled
                // instance is indistinguishable from a fresh one.
                var proj = PrefabPool.Get(projectilePrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));
                var projectileComponent = proj.GetComponent<Projectile>();
                if (projectileComponent != null)
                {
                    //Debug.Log($"[TOWER] {towerName} PROJECTILE attack dealing {effectiveBaseDamage} damage to {target.name}");
                    projectileComponent.Initialize(target, effectiveBaseDamage, range);

                    if (freezeChance > 0f)
                    {
                        projectileComponent.SetFreezeChance(freezeChance);
                    }
                }
            }
            else
            {
                // Direct damage without projectile
                var enemyStats = target.GetComponent<EnemyStats>();
                if (enemyStats != null)
                {
                    //Debug.Log($"[TOWER] {towerName} DIRECT attack dealing {effectiveBaseDamage} damage to {target.name}");
                    TowerKillAttribution.MarkTowerHit(target);
                    enemyStats.TakeDamage(effectiveBaseDamage);
                    CombatStats.ReportTowerDamageDealt(effectiveBaseDamage);
                    ApplyFreezeEffect(target);
                }
            }
        }
    }



    private void ApplyFreezeEffect(GameObject target)
    {
        //Debug.Log($"ApplyFreezeEffect called on {target.name} with freezeChance={freezeChance}");

        if (freezeChance <= 0f) return;

        if (Random.Range(0f, 1f) <= freezeChance)
        {
            //Debug.Log($"Freeze chance succeeded! Looking for EnemyController on {target.name}");

            var enemyController = target.GetComponent<EnemyController>();
            if (enemyController != null)
            {
                enemyController.ApplyFreeze(2f);
                //Debug.Log($"Tower '{towerName}' froze enemy {target.name}!");
            }
            else
            {
                //Debug.LogError($"No EnemyController found on {target.name}!");
            }
        }
    }

    #endregion

    #region Tentacle System
    void UpdateTentacles()
    {
        if (!useTentacleTurret || tentacleRenderer == null) return;

        // Keep tentacle just behind the tower's own sprite (which is Y-sorted dynamically)
        if (spriteRenderer != null)
            tentacleRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;

        swayTimer += Time.deltaTime * tentacleConfig.swaySpeed;

        // Validate sway timer
        if (float.IsNaN(swayTimer) || float.IsInfinity(swayTimer))
        {
            swayTimer = 0f;
        }

        // Update animation timers
        if (isFiring)
        {
            fireAnimTimer += Time.deltaTime * 3f;
            if (fireAnimTimer >= 1f) { isFiring = false; fireAnimTimer = 0f; }
        }

        if (isMeleeAttacking)
        {
            meleeAnimTimer += Time.deltaTime;
            if (meleeAnimTimer >= meleeConfig.attackDuration) { isMeleeAttacking = false; meleeAnimTimer = 0f; }
        }

        if (isSwipingMelee)
        {
            swipeTimer += Time.deltaTime * meleeConfig.swipeSpeed;
            if (swipeTimer >= 1f) { isSwipingMelee = false; swipeTimer = 0f; }
        }

        // Update tentacle shape
        for (int i = 0; i < tentacleConfig.segments; i++)
        {
            float t = (float)i / (tentacleConfig.segments - 1);
            Vector3 pos = Vector3.right * (tentacleConfig.length * t);

            // Apply sway animation
            float swayValue = Mathf.Sin(swayTimer + t * Mathf.PI) * tentacleConfig.swayAmount * t;

            // Validate sway value
            if (float.IsNaN(swayValue) || float.IsInfinity(swayValue))
            {
                swayValue = 0f;
            }

            pos.y += swayValue;

            // Apply firing animation
            if (isFiring)
            {
                float fireAnim = Mathf.Sin(fireAnimTimer * Mathf.PI);

                if (float.IsNaN(fireAnim) || float.IsInfinity(fireAnim))
                {
                    fireAnim = 0f;
                }

                pos.x += fireAnim * 0.3f * t;
                pos.y *= (1f - fireAnim * 0.5f);
            }

            // Apply melee attack animation
            if (isMeleeAttacking)
            {
                float meleeAnim = Mathf.Sin((meleeAnimTimer / meleeConfig.attackDuration) * Mathf.PI);

                if (float.IsNaN(meleeAnim) || float.IsInfinity(meleeAnim))
                {
                    meleeAnim = 0f;
                }

                pos.x += meleeAnim * 0.5f * t;
                float whip = Mathf.Sin(meleeAnim * Mathf.PI * 2f) * 0.3f * t;

                if (float.IsNaN(whip) || float.IsInfinity(whip))
                {
                    whip = 0f;
                }

                pos.y += whip;
            }

            // Apply swipe animation
            if (isSwipingMelee)
            {
                float swipeProgress = meleeConfig.swipeCurve.Evaluate(swipeTimer);

                if (float.IsNaN(swipeProgress) || float.IsInfinity(swipeProgress))
                {
                    swipeProgress = 0f;
                }

                float swipeAngle = Mathf.Lerp(-meleeConfig.swipeArcDegrees / 2f, meleeConfig.swipeArcDegrees / 2f, swipeProgress);
                float swipeAngleRad = swipeAngle * Mathf.Deg2Rad;
                float swipeExtension = Mathf.Sin(swipeProgress * Mathf.PI) * 0.4f;

                // Validate swipe values
                if (float.IsNaN(swipeAngle) || float.IsInfinity(swipeAngle))
                {
                    swipeAngle = 0f;
                    swipeAngleRad = 0f;
                }

                if (float.IsNaN(swipeExtension) || float.IsInfinity(swipeExtension))
                {
                    swipeExtension = 0f;
                }

                pos.x += swipeExtension * t;

                float radius = pos.magnitude;
                float currentAngleRad = Mathf.Atan2(pos.y, pos.x);
                float newAngleRad = currentAngleRad + (swipeAngleRad * 1.5f * t);

                // Validate angle calculations
                if (float.IsNaN(newAngleRad) || float.IsInfinity(newAngleRad))
                {
                    newAngleRad = currentAngleRad;
                }

                pos.x = Mathf.Cos(newAngleRad) * radius;
                pos.y = Mathf.Sin(newAngleRad) * radius;

                float whipEffect = Mathf.Sin(swipeProgress * Mathf.PI * 2f) * 0.2f * t;

                if (float.IsNaN(whipEffect) || float.IsInfinity(whipEffect))
                {
                    whipEffect = 0f;
                }

                pos.y += whipEffect;
            }

            // Apply target tracking, allowing tracking during firing, but not during melee swipes
            if (currentTarget != null && !isSwipingMelee)
            {
                Vector3 targetDir = transform.InverseTransformDirection((currentTarget.transform.position - transform.position).normalized);

                // Validate target direction
                if (float.IsNaN(targetDir.x) || float.IsNaN(targetDir.y) || float.IsNaN(targetDir.z) ||
                    float.IsInfinity(targetDir.x) || float.IsInfinity(targetDir.y) || float.IsInfinity(targetDir.z))
                {
                    targetDir = Vector3.right; // Default direction
                }

                pos += targetDir * (t * 0.2f);
            }

            // Validation of tentacle position
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z) ||
                float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
            {
                pos = Vector3.right * (tentacleConfig.length * t); // Reset to base position
            }

            tentaclePoints[i] = pos;
        }

        tentacleRenderer.SetPositions(tentaclePoints);

        // Update fire point position
        if (firePoint != null && tentaclePoints.Length > 0)
        {
            Vector3 tip = tentaclePoints[tentaclePoints.Length - 1];
            firePoint.position = tentacleContainer.transform.TransformPoint(tip);
        }

        // Update visual effects
        var gradient = new Gradient();
        Color baseColor = tentacleConfig.color;
        Color tipColor = (isFiring || isMeleeAttacking || isSwipingMelee) ?
                        (isSwipingMelee ? Color.Lerp(tentacleConfig.tipColor, Color.white, 0.3f) : tentacleConfig.tipColor) :
                        baseColor;

        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(baseColor, 0), new GradientColorKey(tipColor, 1) },
            new GradientAlphaKey[] { new GradientAlphaKey(baseColor.a, 0), new GradientAlphaKey(1, 1) }
        );
        tentacleRenderer.colorGradient = gradient;
    }
    #endregion

    #region IEnergyConsumer Implementation
    public void ConsumeEnergy(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            Debug.LogWarning($"Tower '{towerName}': Trying to consume invalid energy amount: {amount}, ignoring");
            return;
        }

        if (isDestroyed)
        {
            Debug.LogWarning($"Tower '{towerName}': Trying to consume energy on destroyed tower, ignoring");
            return;
        }

        float prev = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);

        // Validate result
        if (float.IsNaN(currentEnergy) || float.IsInfinity(currentEnergy))
        {
            Debug.LogWarning($"Tower '{towerName}': Energy became invalid after consumption, resetting to 0");
            currentEnergy = 0f;
        }

        if (currentEnergy != prev)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisuals();
            if (currentEnergy <= 0f && prev > 0f) OnEnergyDepleted?.Invoke();
        }
    }

    public void SupplyEnergy(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            Debug.LogWarning($"Tower '{towerName}': Trying to supply invalid energy amount: {amount}, ignoring");
            return;
        }

        if (isDestroyed)
        {
            Debug.LogWarning($"Tower '{towerName}': Trying to supply energy to destroyed tower, ignoring");
            return;
        }

        float prev = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);

        // Validate result
        if (float.IsNaN(currentEnergy) || float.IsInfinity(currentEnergy))
        {
            Debug.LogWarning($"Tower '{towerName}': Energy became invalid after supply, clamping to valid range");
            currentEnergy = Mathf.Clamp(prev, 0f, maxEnergy);
        }

        if (currentEnergy != prev)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisuals();
            if (prev <= 0f && currentEnergy > 0f) OnEnergyRestored?.Invoke();
            if (isDisabledByDamage && currentEnergy > 0f) EnableTower();
        }
    }

    public void SetEnergy(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            Debug.LogWarning($"Tower '{towerName}': Trying to set invalid energy amount: {amount}, ignoring");
            return;
        }

        float prev = currentEnergy;
        currentEnergy = Mathf.Clamp(amount, 0f, maxEnergy);
        if (currentEnergy != prev) { OnEnergyChanged?.Invoke(currentEnergy); UpdateVisuals(); }
    }

    public void SetMaxEnergy(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Trying to set invalid max energy: {amount}, ignoring");
            return;
        }

        maxEnergy = amount;
        currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        UpdateVisuals();
    }

    public float GetEnergy() => currentEnergy;
    public float GetMaxEnergy() => maxEnergy;
    public float GetEnergyPercentage()
    {
        // Added validation to prevent NaN
        if (float.IsNaN(currentEnergy) || float.IsInfinity(currentEnergy))
        {
            Debug.LogWarning($"Tower '{towerName}': currentEnergy is invalid: {currentEnergy}, resetting to 0");
            currentEnergy = 0f;
        }

        if (float.IsNaN(maxEnergy) || float.IsInfinity(maxEnergy) || maxEnergy <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': maxEnergy is invalid: {maxEnergy}, resetting to default");
            maxEnergy = 100f;
            currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        }

        return maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    }
    public Vector3 GetPosition() => transform.position;
    public bool IsEnergyDepleted() => EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetTowerDeadThreshold();
    public bool IsEnergyLow() => EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetTowerCriticalThreshold();

    void UpdateVisuals()
    {
        // Skip visual updates if this tower is a grappling target
        if (isGrapplingTarget) return;

        if (spriteRenderer != null && EnergyManager.Instance != null)
            EnergyManager.Instance.UpdateConsumerVisuals(this, spriteRenderer);

        if (useTentacleTurret && tentacleRenderer?.material != null)
        {
            Color baseColor = tentacleConfig.color;
            if (IsEnergyDepleted()) baseColor = Color.Lerp(baseColor, EnergyManager.Instance.depletedEnergyColor, 0.7f);
            else if (IsEnergyLow()) baseColor = Color.Lerp(baseColor, EnergyManager.Instance.criticalEnergyColor, 0.5f);
            tentacleRenderer.material.color = baseColor;
        }
    }
    #endregion

    #region IDamageable Implementation
    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (immuneToEnemyDamage || isDisabledByDamage || isDestroyed) return false;

        if (float.IsNaN(damageAmount) || float.IsInfinity(damageAmount))
        {
            Debug.LogWarning($"Tower '{towerName}': Received invalid damage amount: {damageAmount}, ignoring");
            return false;
        }

        float actualDamage = damageAmount * (1f - armorReduction);

        // Validate actual damage
        if (float.IsNaN(actualDamage) || float.IsInfinity(actualDamage))
        {
            Debug.LogWarning($"Tower '{towerName}': Calculated invalid actual damage: {actualDamage}, using original amount");
            actualDamage = damageAmount;
        }

        ConsumeEnergy(actualDamage);

        // Combat telemetry: damage taken by towers (post-armor, all sources).
        CombatStats.ReportTowerDamageTaken(actualDamage);

        // Play Tower damage sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDamage, transform.position);
        }

        StartDamageFlash();
        OnDamageTaken?.Invoke(actualDamage, damageSource);

        if (IsEnergyDepleted())
        {
            if (AudioManager.instance != null && FMODEvents.instance != null)
            {
                AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, transform.position);
            }
            // Give refund when tower is destroyed by enemies
            if (EnergyManager.Instance != null)
            {
                int refundValue = EnergyManager.Instance.GetTowerSellValue();
                EnergyManager.Instance.GivePlayerEnergy(refundValue);
            }

            // Augment 344 — Phoenix Protocol: remember this tower so it can be
            // rebuilt next stage. No-op unless the augment is active.
            //TowerRevivalManager.RecordDestroyed(this, parentSlot);

            DisableTower();
            OnTowerDestroyed?.Invoke(damageSource);
            return true;
        }
        return false;
    }

    public bool CanTakeDamage() => !immuneToEnemyDamage && !isDisabledByDamage && !isDestroyed;
    public float GetCurrentHealth() => currentEnergy;
    public float GetMaxHealth() => maxEnergy;
    public float GetHealthPercentage() => GetEnergyPercentage();
    public bool IsDestroyed() => isDisabledByDamage || isDestroyed || IsEnergyDepleted();

    public void DisableTower()
    {
        if (isDisabledByDamage) return;
        isDisabledByDamage = true;
        if (spriteRenderer != null) { var c = spriteRenderer.color; c.a = 0.5f; spriteRenderer.color = c; }
    }

    public void EnableTower()
    {
        if (!isDisabledByDamage) return;
        isDisabledByDamage = false;
        if (spriteRenderer != null) { var c = spriteRenderer.color; c.a = 1f; spriteRenderer.color = c; }
    }

    void StartDamageFlash()
    {
        if (damageFlashCoroutine != null) StopCoroutine(damageFlashCoroutine);
        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        // Double blink with additive bright color for better visibility
        for (int i = 0; i < 2; i++)
        {
            spriteRenderer.color = damageFlashColor; // Bright additive color
            yield return new WaitForSeconds(damageFlashDuration);
            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(0.05f);
        }

        damageFlashCoroutine = null;
    }
    #endregion

    #region Collision & Public Interface
    void OnTriggerEnter2D(Collider2D other)
    {
        /*Debug.Log($"[TOWER] OnTriggerEnter2D: {other.gameObject.name}, " +
                  $"Layer: {LayerMask.LayerToName(other.gameObject.layer)}, " +
                  $"IsEnemy: {IsEnemy(other.gameObject)}, " +
                  $"LayerMatch: {((1 << other.gameObject.layer) & targetLayer) != 0}");*/
        if (IsEnemy(other.gameObject) && ((1 << other.gameObject.layer) & targetLayer) != 0)
        {
            enemiesInRange.Add(other.gameObject);
            //Debug.Log($"[TOWER] ✓ ADDED enemy to range! Total: {enemiesInRange.Count}");
        }
        else
        {
            //Debug.Log($"[TOWER] ✗ NOT added (failed checks)");
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (enemiesInRange.Remove(other.gameObject) && currentTarget == other.gameObject)
            currentTarget = null;
    }

    public bool IsTargetInMeleeRange(GameObject target) => target != null && Vector2.Distance(transform.position, target.transform.position) <= tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude + 0.4f;
    public bool IsTargetInProjectileRange(GameObject target) => target != null && Vector2.Distance(transform.position, target.transform.position) <= ProjectileRange;
    public bool IsOperational() => !IsEnergyDepleted() && !isDisabledByDamage && !isDestroyed;

    public void UpgradeTower()
    {
        if (!CanUpgrade || parentSlot == null) return;
        parentSlot.RemoveTower();
        var upgraded = Instantiate(upgradeTowerPrefab.gameObject, parentSlot.transform.position, Quaternion.identity);
        upgraded.transform.SetParent(parentSlot.transform, false);
        upgraded.transform.localPosition = Vector3.zero;
        parentSlot.currentTower = upgraded;
        parentSlot.isOccupied = true;
        upgraded.GetComponent<Tower>()?.SetUpgradeLevel(upgradeLevel + 1);
    }

    public void SellTower() => parentSlot?.RemoveTower();

    // Accessors
    public int GetBuildCost() => EnergyManager.Instance?.GetTowerBuildCost() ?? cost;
    public bool CanAfford() => EnergyManager.Instance?.CanAffordTower() ?? true;
    public int GetSellValue() => EnergyManager.Instance?.GetTowerSellValue() ?? Mathf.RoundToInt(cost * 0.5f);
    public void SetDamage(float newDamage)
    {
        if (float.IsNaN(newDamage) || float.IsInfinity(newDamage) || newDamage < 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid damage value: {newDamage}, ignoring");
            return;
        }
        damage = newDamage;
    }
    public void SetRange(float newRange)
    {
        if (float.IsNaN(newRange) || float.IsInfinity(newRange) || newRange <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid range value: {newRange}, ignoring");
            return;
        }

        range = newRange; // This triggers the property setter
    }
    public void SetFireRate(float newFireRate)
    {
        if (float.IsNaN(newFireRate) || float.IsInfinity(newFireRate) || newFireRate <= 0f)
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid fire rate: {newFireRate}, ignoring");
            return;
        }
        fireRate = newFireRate;
    }
    public void SetArmor(float newArmor)
    {
        if (float.IsNaN(newArmor) || float.IsInfinity(newArmor))
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid armor value: {newArmor}, ignoring");
            return;
        }
        armorReduction = Mathf.Clamp01(newArmor);
    }
    // In-place stat upgrade API 

    // Default per-level bonuses, used when the serialized field is 0/unset. Prefabs
    // saved BEFORE these fields were added deserialize them as 0 (Unity uses the type
    // default, not the C# initializer, for newly-added fields), which would make the
    // upgrade multiplier 1.0 → no effect. Falling back to 0.20 keeps upgrades working
    // on existing prefabs while still honouring any explicit non-zero value.
    private const float DefaultUpgradeBonusPerLevel = 0.20f;
    private float EffectiveDamageBonusPerLevel =>
        upgradeDamageBonusPerLevel > 0.0001f ? upgradeDamageBonusPerLevel : DefaultUpgradeBonusPerLevel;
    private float EffectiveHealthBonusPerLevel =>
        upgradeHealthBonusPerLevel > 0.0001f ? upgradeHealthBonusPerLevel : DefaultUpgradeBonusPerLevel;

    /// Live attack multiplier from the in-place upgrade (1.0 at level 1).
    public float UpgradeDamageMultiplier =>
        Mathf.Pow(1f + EffectiveDamageBonusPerLevel, Mathf.Max(0, upgradeLevel - 1));

    /// Same multiplier, named for non-combat towers (heal rate, generation rate, etc.).
    public float UpgradePowerMultiplier => UpgradeDamageMultiplier;

    public int GetUpgradeLevel() => upgradeLevel;
    public int GetMaxUpgradeLevel() => maxUpgradeLevel;
    public bool IsAtMaxUpgrade => upgradeLevel >= maxUpgradeLevel;

    /// True when this tower can still take an in-place stat upgrade.
    public bool CanStatUpgrade => canUpgrade && upgradeLevel < maxUpgradeLevel && !isDestroyed;

    /// Set the upgrade level and re-derive the max-health bonus from it. Called on
    /// fresh upgrades AND on restore (rewind / save-resume), so a saved upgradeLevel
    /// reproduces the boosted health automatically. Damage is derived live in
    /// GetEffectiveDamage(), so it needs no extra work here.
    public void SetUpgradeLevel(int level)
    {
        upgradeLevel = Mathf.Max(1, level);
        RefreshUpgradeHealthScaling();
    }

    /// Apply ONE in-place upgrade: +X% attack (live, via UpgradeDamageMultiplier) and
    /// +Y% max health. No-op at max level or once destroyed. Returns true if upgraded.
    public bool ApplyUpgrade()
    {
        if (isDestroyed || upgradeLevel >= maxUpgradeLevel) return false;
        upgradeLevel++;
        RefreshUpgradeHealthScaling();

        // Console confirmation that the upgrade took effect (output multiplier + the
        // resulting generation/heal rate where relevant). Helps verify non-combat towers.
        string extra =
            IsGenerator() ? $", generation {energyGenerationRate * UpgradePowerMultiplier:F2}/s (base {energyGenerationRate:F2})"
          : (isHealTower || towerType == TowerType.Heal) ? $", heal {healPerSecond * UpgradePowerMultiplier:F2}/s (base {healPerSecond:F2})"
          : $", damage x{UpgradePowerMultiplier:F2}";
        Debug.Log($"[Upgrade] '{towerName}' -> level {upgradeLevel}: output x{UpgradePowerMultiplier:F2}, maxHP {maxEnergy:F0}{extra}");
        return true;
    }

    // Re-derive the max-health (maxEnergy) upgrade bonus from upgradeLevel, idempotently.
    // Applied as a RELATIVE multiplier on top of whatever maxEnergy currently is, so it
    // stacks cleanly with augment-driven maxEnergy changes and reproduces exactly when a
    // saved upgradeLevel is restored. The maxEnergy setter preserves the current health
    // percentage, so a full tower stays full and a damaged one keeps its ratio.
    private void RefreshUpgradeHealthScaling()
    {
        float want = Mathf.Pow(1f + EffectiveHealthBonusPerLevel, Mathf.Max(0, upgradeLevel - 1));
        if (_appliedHealthMult <= 0f) _appliedHealthMult = 1f;
        if (Mathf.Approximately(want, _appliedHealthMult)) return;

        float factor = want / _appliedHealthMult;
        maxEnergy = maxEnergy * factor;   // setter scales currentEnergy proportionally + updates visuals
        _appliedHealthMult = want;
    }
    public float GetDamage()
    {
        return damage;
    }
    public float GetRange() => range;
    public float GetFireRate() => fireRate;
    public float GetArmor() => armorReduction;
    public int GetCost() => cost;
    public TowerType GetTowerType() => towerType;
    public float LastFireTime => lastFireTime;
    #endregion

    void Cleanup()
    {
        // Stop and release the generator ambience loop, if any.
        StopGeneratorAmbience(release: true);

        // Hard-stop the heal-halo and laser-beam loops so a sold/destroyed tower
        // can't leave a sound droning on. Idempotent if they were never started.
        healHaloSfx.Stop(immediate: true);
        laserAttackSfx.Stop(immediate: true);

        // Cleanup laser
        if (laserObject != null) DestroyImmediate(laserObject);
        if (laserStartParticles != null) DestroyImmediate(laserStartParticles.gameObject);
        if (laserImpactParticles != null) DestroyImmediate(laserImpactParticles.gameObject);

        // The four additive materials are created per tower, so they have to be
        // released per tower - Unity does not collect runtime materials on its own,
        // and selling/rebuilding towers used to leak a set every time. The TEXTURES
        // and SPRITES are deliberately NOT touched: they belong to LaserVfxAssets and
        // are shared by every other laser tower.
        LaserVfxAssets.SafeDestroy(laserBeamMaterial);
        LaserVfxAssets.SafeDestroy(laserGlowMaterial);
        LaserVfxAssets.SafeDestroy(laserAuraMaterial);
        LaserVfxAssets.SafeDestroy(laserSpriteMaterial);
        laserBeamMaterial = laserGlowMaterial = laserAuraMaterial = laserSpriteMaterial = null;


        // Mark as destroyed to prevent further operations
        isDestroyed = true;

        // Idempotent with OnDisable's removal; covers an object destroyed while
        // already inactive (where OnDisable won't fire again).
        ActiveTowers.Remove(this);

        EnergyManager.Instance?.UnregisterEnergyConsumer(this);
        if (tentacleContainer != null) DestroyImmediate(tentacleContainer);
        if (auraObject != null) DestroyImmediate(auraObject);
        if (energyBar != null) Destroy(energyBar);
        if (damageFlashCoroutine != null) StopCoroutine(damageFlashCoroutine);
    }


#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (isEnergyGenerator)
        {
            // Generation range for generators
            UnityEditor.Handles.color = generationEffectColor;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, generationRange);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                $"Generator: {energyGenerationRate * UpgradePowerMultiplier:F2}/sec (base {energyGenerationRate:F1}, L{upgradeLevel})");
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.7f,
                $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");
        }
        else if (isHammerTower)
        {
            // Hammer AOE range
            UnityEditor.Handles.color = new Color(1f, 0.5f, 0f, 0.3f);
            UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.forward, hammerAOERadius);
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, hammerAOERadius);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f,
                $"Hammer AOE: {hammerAOERadius}m");
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f,
                $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");
        }
        else if (isLaserTower)
        {
            // Laser range
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, ProjectileRange);
            if (currentTarget != null && isLaserActive)
            {
                UnityEditor.Handles.DrawLine(transform.position, currentTarget.transform.position);
            }
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f,
                $"Laser: {(isLaserActive ? "ACTIVE" : "INACTIVE")}");
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f,
                $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");
        }
        else
        {
            // Original combat tower gizmos
            UnityEditor.Handles.color = Color.blue;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, range);
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, ProjectileRange);

            // NULL CHECK ADDED HERE
            if (rangeCollider != null)
            {
                UnityEditor.Handles.color = Color.green;
                UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, rangeCollider.radius);
            }

            if (currentTarget != null)
            {
                float dist = Vector2.Distance(transform.position, currentTarget.transform.position);
                UnityEditor.Handles.color = dist <= ProjectileRange ? Color.red : Color.gray;
                UnityEditor.Handles.DrawLine(transform.position, currentTarget.transform.position);
                UnityEditor.Handles.Label((transform.position + currentTarget.transform.position) / 2f, $"Dist: {dist:F1}");
            }

            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, $"Status: {(isDisabledByDamage ? "DISABLED" : "ACTIVE")}");
        }
    }
#endif
}

public interface IDamageable
{
    bool TakeDamage(float damageAmount, GameObject damageSource = null);
    bool CanTakeDamage();
    float GetCurrentHealth();
    float GetMaxHealth();
    float GetHealthPercentage();
    bool IsDestroyed();
}


