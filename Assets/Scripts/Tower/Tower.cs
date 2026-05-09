using UnityEngine;
using System.Collections;
using System.Collections.Generic;
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
    private GameObject auraObject;
    private SpriteRenderer auraRenderer;

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

    public enum TowerType { Basic, Artillery, Laser, Ice, Poison, Generator, Hammer }


    [Header("Hammer Tower (AOE) Settings")]
    public bool isHammerTower = false;
    public float hammerAOERadius = 2f;
    public float hammerAttackInterval = 1.5f;
    private float lastHammerAttackTime;
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

    [Header("Combat Settings")]
    public LayerMask targetLayer = -1;
    public GameObject projectilePrefab;
    private float baseDamageForEnergyCost; // Base damage before augments, used for energy cost


    [Header("Upgrade Settings")]
    public bool canUpgrade = true;
    public Tower upgradeTowerPrefab;
    public int upgradeLevel = 1;
    public int maxUpgradeLevel = 3;

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
    public bool CanFire => Time.time >= lastFireTime + (1f / fireRate);
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

    void Awake()
    {
        LoadConfig();
        InitializeComponents();
        baseDamageForEnergyCost = damage; // Store base damage before augments are applied

    }

    void Start()
    {
        SetupTower();
        EnergyManager.Instance?.RegisterEnergyConsumer(this);

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

            if (laserRenderer != null)
            {
                laserRenderer.startWidth = 0.12f;
                laserRenderer.endWidth = 0.07f;
                laserRenderer.sortingOrder = 100;
                //Debug.Log($"Laser visual: width={laserRenderer.startWidth}, sorting={laserRenderer.sortingOrder}");
            }
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
        if (isDestroyed) return;

        try
        {
            if (IsEnergyDepleted() || isDisabledByDamage) return;

            UpdateTowerRegeneration();

            if (isEnergyGenerator || towerType == TowerType.Generator)
            {
                UpdateEnergyGeneration();
            }
            else if (isHammerTower)
            {
                UpdateHammerTower();
            }
            else if (isLaserTower)
            {
                UpdateTargeting();
                UpdateLaserTower();
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



    #region Hammer Tower System
    void UpdateHammerTower()
    {
        if (!isHammerTower) return;

        // Find enemies in AOE range
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, hammerAOERadius, targetLayer);
        List<GameObject> validTargets = new List<GameObject>();

        foreach (Collider2D hit in hits)
        {
            if (IsEnemy(hit.gameObject))
            {
                validTargets.Add(hit.gameObject);
            }
        }

        // Attack at intervals
        if (validTargets.Count > 0 && Time.time >= lastHammerAttackTime + hammerAttackInterval)
        {
            PerformHammerAttack(validTargets);
            lastHammerAttackTime = Time.time;
        }
    }

    void PerformHammerAttack(List<GameObject> targets)
    {
        if (IsEnergyDepleted() || isDisabledByDamage || isDestroyed) return;

        // Energy cost for AOE attack (higher than single target)
        float baseCost = baseDamageForEnergyCost * 0.1f; // More expensive than normal attack
        float energyCost = baseCost * energyCostMultiplier;

        // Apply generator proximity efficiency boost
        var generatorBoost = GetComponent<GeneratorProximityBoost>();
        if (generatorBoost != null)
        {
            energyCost *= generatorBoost.GetEnergyEfficiencyMultiplier();
        }

        if (currentEnergy < energyCost) return;

        ConsumeEnergy(energyCost);

        float effectiveDamage = GetEffectiveDamage();

        // Damage all enemies in range
        foreach (GameObject target in targets)
        {
            if (target == null) continue;

            var stats = target.GetComponent<EnemyStats>();
            if (stats != null)
            {
                stats.TakeDamage(effectiveDamage);
                ApplyFreezeEffect(target);
            }
        }

        // Visual and audio feedback
        PlayHammerImpactEffect();
        AudioManager.instance?.PlayOneShot(FMODEvents.instance.towerMeleeHit, transform.position);

        //Debug.Log($"Hammer Tower '{towerName}' hit {targets.Count} enemies for {effectiveDamage} damage each!");
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

    void InitializeLaser()
    {
        if (!isLaserTower) return;

        laserObject = new GameObject("Laser");
        laserObject.transform.SetParent(transform);
        laserObject.transform.localPosition = Vector3.zero;

        // Main laser beam
        laserRenderer = laserObject.AddComponent<LineRenderer>();
        laserBeamMaterial = new Material(Shader.Find("Sprites/Default"));
        laserBeamMaterial.color = Color.white;
        laserRenderer.material = laserBeamMaterial;
        laserRenderer.startWidth = 0.12f;
        laserRenderer.endWidth = 0.07f;
        laserRenderer.positionCount = 2;
        laserRenderer.useWorldSpace = true;
        laserRenderer.sortingOrder = 200;
        laserRenderer.startColor = new Color(0.7f, 0f, 1f);
        laserRenderer.endColor = new Color(0.5f, 0f, 1f, 0.5f);
        laserRenderer.enabled = false;

        // Glow layer (outer glow)
        GameObject glowObject = new GameObject("LaserGlow");
        glowObject.transform.SetParent(laserObject.transform);
        glowObject.transform.localPosition = Vector3.zero;

        laserGlowRenderer = glowObject.AddComponent<LineRenderer>();
        laserGlowMaterial = new Material(Shader.Find("Sprites/Default"));
        laserGlowMaterial.color = new Color(0.7f, 0f, 1f, 0.3f);
        laserGlowRenderer.material = laserGlowMaterial;
        laserGlowRenderer.startWidth = 0.35f;
        laserGlowRenderer.endWidth = 0.15f;
        laserGlowRenderer.positionCount = 2;
        laserGlowRenderer.useWorldSpace = true;
        laserGlowRenderer.sortingOrder = 199;
        laserGlowRenderer.startColor = new Color(0.5f, 0f, 1f, 0.4f);
        laserGlowRenderer.endColor = new Color(0.3f, 0f, 0.8f, 0.1f);
        laserGlowRenderer.enabled = false;



        // Laser start particles (muzzle flash effect)
        GameObject startParticlesObj = new GameObject("LaserStartParticles");
        startParticlesObj.transform.SetParent(laserObject.transform);
        startParticlesObj.transform.localPosition = Vector3.zero;

        laserStartParticles = startParticlesObj.AddComponent<ParticleSystem>();
        var startMain = laserStartParticles.main;
        startMain.startLifetime = 0.1f;
        startMain.startSpeed = 0.2f;
        startMain.startSize = 0.03f;
        startMain.startColor = new Color(0.7f, 0f, 1f, 0.3f);
        startMain.maxParticles = 5;

        var startEmission = laserStartParticles.emission;
        startEmission.rateOverTime = 10f;

        var startShape = laserStartParticles.shape;
        startShape.shapeType = ParticleSystemShapeType.Cone;
        startShape.angle = 10f;
        startShape.radius = 0.05f;

        var startColorOverLifetime = laserStartParticles.colorOverLifetime;
        startColorOverLifetime.enabled = true;
        Gradient startGradient = new Gradient();
        startGradient.SetKeys(
            new GradientColorKey[] {
            new GradientColorKey(new Color(0.7f, 0f, 1f), 0f),
            new GradientColorKey(new Color(0.5f, 0f, 1f), 1f)
            },
            new GradientAlphaKey[] {
            new GradientAlphaKey(0.8f, 0f),
            new GradientAlphaKey(0f, 1f)
            }
        );
        startColorOverLifetime.color = startGradient;

        laserStartParticles.Stop();

        // Laser impact particles
        GameObject impactParticlesObj = new GameObject("LaserImpactParticles");
        impactParticlesObj.transform.SetParent(laserObject.transform);

        laserImpactParticles = impactParticlesObj.AddComponent<ParticleSystem>();
        var impactMain = laserImpactParticles.main;
        impactMain.startLifetime = 0.2f;
        impactMain.startSpeed = 1.5f;
        impactMain.startSize = 0.1f;
        impactMain.startColor = new Color(1f, 0.3f, 1f, 1f);
        impactMain.maxParticles = 50;
        impactMain.loop = true;

        var impactEmission = laserImpactParticles.emission;
        impactEmission.rateOverTime = 50f;

        var impactShape = laserImpactParticles.shape;
        impactShape.shapeType = ParticleSystemShapeType.Sphere;
        impactShape.radius = 0.1f;

        var impactColorOverLifetime = laserImpactParticles.colorOverLifetime;
        impactColorOverLifetime.enabled = true;
        Gradient impactGradient = new Gradient();
        impactGradient.SetKeys(
            new GradientColorKey[] {
            new GradientColorKey(new Color(1f, 0.5f, 1f), 0f),
            new GradientColorKey(new Color(0.5f, 0f, 1f), 1f)
            },
            new GradientAlphaKey[] {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(0f, 1f)
            }
        );
        impactColorOverLifetime.color = impactGradient;

        laserImpactParticles.Stop();
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
        laserRenderer.enabled = true;
        /*
        Debug.Log($"✓✓✓ LASER ACTIVATED! LineRenderer enabled: {laserRenderer.enabled}, " +
                  $"color: {laserRenderer.startColor}, width: {laserRenderer.startWidth}");
                  */
    }


    void DisableLaser()
    {
        isLaserActive = false;
        if (laserRenderer != null)
        {
            laserRenderer.enabled = false;
        }
        if (laserGlowRenderer != null)
        {
            laserGlowRenderer.enabled = false;
        }
        if (laserStartParticles != null && laserStartParticles.isPlaying)
        {
            laserStartParticles.Stop();
        }
        if (laserImpactParticles != null && laserImpactParticles.isPlaying)
        {
            laserImpactParticles.Stop();
        }
    }

    void UpdateLaserBeam()
    {
        if (!isLaserActive || currentTarget == null) return;
        if (laserRenderer == null)
        {
            Debug.LogError("laserRenderer is NULL");
            return;
        }

        // Energy consumption
        float energyCost = (damage * 0.1f * Time.deltaTime) * energyCostMultiplier;
        if (currentEnergy < energyCost)
        {
            DisableLaser();
            return;
        }
        ConsumeEnergy(energyCost);

        // Calculate beam positions
        Vector3 startPos = transform.position;
        Vector3 endPos = currentTarget.transform.position;
        Vector3 beamDirection = (endPos - startPos).normalized;
        laserFlickerTimer += Time.deltaTime * 15f;
        float wobbleAmount = 0.015f;
        Vector3 perpendicular = new Vector3(-beamDirection.y, beamDirection.x, 0f);
        int segmentCount = 4;
        Vector3[] beamPositions = new Vector3[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            float t = (float)i / (segmentCount - 1);
            Vector3 basePos = Vector3.Lerp(startPos, endPos, t);
            float wavePhase = laserFlickerTimer + (t * 5f);
            float wave = Mathf.Sin(wavePhase) * wobbleAmount * Mathf.Sin(t * Mathf.PI) * 0.5f;

            beamPositions[i] = basePos + (perpendicular * wave);
        }

        // Update main beam with segments
        laserRenderer.positionCount = segmentCount;
        laserRenderer.SetPositions(beamPositions);
        laserRenderer.enabled = true;

        // Update glow layer with same positions
        if (laserGlowRenderer != null)
        {
            laserGlowRenderer.positionCount = segmentCount;
            laserGlowRenderer.SetPositions(beamPositions);
            laserGlowRenderer.enabled = true;

            // Pulsate glow - SLOWER pulse
            float glowPulse = 0.3f + Mathf.Sin(laserFlickerTimer * 0.3f) * 0.08f;
            laserGlowMaterial.color = new Color(0.7f, 0f, 1f, glowPulse);
        }

        // Animate color intensity 
        float colorPulse = 0.9f + Mathf.Sin(laserFlickerTimer * 1.5f) * 0.1f;
        laserRenderer.startColor = new Color(0.7f * colorPulse, 0f, 1f * colorPulse);
        laserRenderer.endColor = new Color(0.5f * colorPulse, 0f, 1f * colorPulse, 0.5f);
        float widthPulse = 1f + Mathf.Sin(laserFlickerTimer * 2f) * 0.05f;
        laserRenderer.startWidth = 0.12f * widthPulse;
        laserRenderer.endWidth = 0.07f * widthPulse;

        // Update particle systems
        if (laserStartParticles != null)
        {
            laserStartParticles.transform.position = startPos;
            laserStartParticles.transform.rotation = Quaternion.LookRotation(Vector3.forward, beamDirection);
            if (!laserStartParticles.isPlaying)
                laserStartParticles.Play();
        }

        if (laserImpactParticles != null)
        {
            laserImpactParticles.transform.position = endPos;
            if (!laserImpactParticles.isPlaying)
                laserImpactParticles.Play();
        }

        // Apply damage
        float damageThisFrame = damage * Time.deltaTime;
        var targetStats = currentTarget.GetComponent<EnemyStats>();
        if (targetStats != null)
        {
            targetStats.TakeDamage(damageThisFrame);
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

        // Calculate energy to generate based on rate and interval
        float energyToGenerate = energyGenerationRate * generationInterval;

        // Validate the calculated energy
        if (float.IsNaN(energyToGenerate) || float.IsInfinity(energyToGenerate))
        {
            Debug.LogWarning($"Tower '{towerName}': Invalid energyToGenerate calculated: {energyToGenerate}, skipping generation");
            return;
        }

        //int energyAmount = Mathf.RoundToInt(energyToGenerate);
        int energyAmount = Mathf.RoundToInt(energyToGenerate * EnergyManager.Instance.globalResourceMultiplier);

        // Give energy to the player
        EnergyManager.Instance.GivePlayerEnergy(energyAmount);

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
        //Debug.Log($"Energy Generator '{towerName}' generated {energyAmount} energy for player");
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

        // Ensure SpriteRenderer exists and is properly initialized
        spriteRenderer = GetComponent<SpriteRenderer>();
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

    void LoadSprite()
    {
        var sprites = Resources.LoadAll<Sprite>(spriteResourcePath);
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
        energyBar.energyBarOffset = energyBarOffsetOverride > 0f
            ? energyBarOffsetOverride
            : GetDefaultEnergyBarOffset(towerType);
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
            default: return 4.4f;
        }
    }
    #endregion



    #region Targeting & Combat
    void UpdateTargeting()
    {
        enemiesInRange.RemoveAll(e => e == null || !IsValidTarget(e));

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

    bool IsEnemy(GameObject target)
    {
        if (target.GetComponent<PlayerMovement>() || target.CompareTag("Player") || target.GetComponent<Tower>()) return false;
        if (target.layer == LayerMask.NameToLayer("Enemy")) return true;
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
            stats?.TakeDamage(meleeDamage);
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

                var proj = Instantiate(projectilePrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));
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
                    enemyStats.TakeDamage(effectiveBaseDamage);
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
                //Debug.Log($"Tower '{towerName}' destroyed by enemy, refunded {refundValue} energy");
            }

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
    public void SetUpgradeLevel(int level) => upgradeLevel = level;
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
        // Cleanup laser
        if (laserObject != null) DestroyImmediate(laserObject);
        if (laserStartParticles != null) DestroyImmediate(laserStartParticles.gameObject);
        if (laserImpactParticles != null) DestroyImmediate(laserImpactParticles.gameObject);


        // Mark as destroyed to prevent further operations
        isDestroyed = true;

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
                $"Generator: {energyGenerationRate}/sec");
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
