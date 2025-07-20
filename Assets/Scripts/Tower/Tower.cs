using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Tower : MonoBehaviour, IEnergyConsumer, IDamageable
{
    #region Tower Configuration
    [Header("Tower Properties")]
    public string towerName = "Basic Tower";
    public float damage = 10f;
    public float range = 5f;
    public float fireRate = 1f;
    public int cost = 100;
    public TowerType towerType = TowerType.Basic;

    [Header("Visual Settings")]
    public string spriteResourcePath = "Sprites/spritesheet_transparent2";
    public int spriteIndex = 0;
    public float spriteScale = 0.5f;
    public bool enableAnimation = true;
    public int animationFrameCount = 43;
    public float animationSpeed = 0.25f;

    [Header("Combat Settings")]
    public LayerMask targetLayer = -1;
    public GameObject projectilePrefab;

    [Header("Upgrade Settings")]
    public bool canUpgrade = true;
    public Tower upgradeTowerPrefab;
    public int upgradeLevel = 1;
    public int maxUpgradeLevel = 3;

    [Header("Tentacle Settings")]
    public bool useTentacleTurret = true;
    public TentacleConfig tentacleConfig = new TentacleConfig();

    [Header("Melee Settings")]
    public MeleeConfig meleeConfig = new MeleeConfig();

    [Header("Rotation Settings")]
    public float rotationSpeed = 180f;
    public bool smoothRotation = true;

    [Header("Energy Settings")]
    public float maxEnergy = 100f;
    public float currentEnergy = 100f;
    public bool requiresEnergyToFunction = true;
    public bool showEnergyBar = true;

    [Header("Damage Settings")]
    public float armorReduction = 0f; // 0-1 range, reduces incoming damage
    public bool immuneToEnemyDamage = false;
    public float damageFlashDuration = 0.15f;
    public bool enableDamageEffects = true;
    #endregion

    #region Configuration Classes
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
        public float animationSpeed = 1f;
        public Vector2 attachmentOffset = new Vector2(0f, -0.3f);
        public float downwardShorteningFactor = 0.5f;
    }

    [System.Serializable]
    public class MeleeConfig
    {
        public float range = 1.5f;
        public float damageMultiplier = 1.5f;
        public float attackDuration = 0.3f;
        public float swipeArcDegrees = 60f;
        public float swipeSpeed = 8f;
        public float swipeReach = 0.4f;
        public float swipeIntensity = 1.5f;
        public AnimationCurve swipeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
    #endregion

    #region Properties and Events
    public enum TowerType { Basic, Artillery, Laser, Ice, Poison }

    public bool CanUpgrade => canUpgrade && upgradeLevel < maxUpgradeLevel && upgradeTowerPrefab != null;
    public bool CanFire => Time.time >= lastFireTime + (1f / fireRate);
    public float ProjectileRange { get; private set; }
    public Transform FirePoint => tentacleSystem?.FirePoint;

    // Energy Events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted;
    public System.Action OnEnergyRestored;

    // Damage Events
    public System.Action<float, GameObject> OnDamageTaken;
    public System.Action<GameObject> OnTowerDestroyed;
    public System.Action OnTowerDisabled;
    public System.Action OnTowerEnabled;
    #endregion

    #region Core Components
    private TowerCombat combat;
    private TowerTargeting targeting;
    private TowerVisuals visuals;
    private TowerTentacleSystem tentacleSystem;
    private TowerEnergy energy;

    // Basic components
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D rangeCollider;
    private TowerSlot parentSlot;
    private EnergyBar energyBar;

    // Core state
    private float lastFireTime;
    private bool isEnergyDepleted, isEnergyLow;
    private bool isDisabledByDamage = false;
    private Coroutine damageFlashCoroutine;

    // Public accessor for combat system debugging
    public float LastFireTime => lastFireTime;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        InitializeComponents();
        InitializeSystems();
    }

    void Start()
    {
        SetupTower();
        RegisterWithEnergyManager();
    }

    void Update()
    {
        if (!energy.UpdateEnergyState() || isDisabledByDamage) return;

        targeting.UpdateTargeting();

        // Update tentacle system every frame - CRITICAL for fire point updates
        if (useTentacleTurret && tentacleSystem != null)
        {
            tentacleSystem.UpdateTentacle(targeting.HasValidTarget, targeting.CurrentTarget);
        }

        combat.TryFire(targeting.CurrentTarget);
    }

    void OnDestroy() => Cleanup();
    #endregion

    #region Initialization
    void InitializeComponents()
    {
        // Ensure SpriteRenderer is properly created
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        // Wait a frame to ensure component is fully initialized
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = 20;
            spriteRenderer.sortingLayerName = "Default";
        }

        transform.localScale = Vector3.one * spriteScale;

        // Setup collider
        rangeCollider = GetComponent<CircleCollider2D>();
        if (rangeCollider == null)
        {
            rangeCollider = gameObject.AddComponent<CircleCollider2D>();
        }
        rangeCollider.isTrigger = true;
    }

    void InitializeSystems()
    {
        // Initialize all subsystems - ensure spriteRenderer is ready
        energy = new TowerEnergy(this);
        visuals = new TowerVisuals(this, spriteRenderer);
        targeting = new TowerTargeting(this);
        combat = new TowerCombat(this);

        if (useTentacleTurret)
        {
            tentacleSystem = new TowerTentacleSystem(this, tentacleConfig, meleeConfig);
        }
    }

    void SetupTower()
    {
        parentSlot = GetComponentInParent<TowerSlot>();

        // Setup tentacle system first
        if (useTentacleTurret)
        {
            tentacleSystem?.Initialize();
        }

        // Calculate ranges AFTER tentacle system is initialized
        float effectiveTentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;
        meleeConfig.range = Mathf.Max(meleeConfig.range, effectiveTentacleReach);

        // Make projectile range much more generous - use the larger of the base range or a generous multiplier
        float calculatedProjectileRange = meleeConfig.range * 3.5f;
        ProjectileRange = Mathf.Max(range * 2f, calculatedProjectileRange);

        // Make sure the range is reasonable - minimum 6 units
        ProjectileRange = Mathf.Max(ProjectileRange, 6f);

        // Setup collider to match the ACTUAL projectile range (add some buffer)
        rangeCollider.radius = ProjectileRange + 0.5f;

        visuals.LoadSprite();
        energy.SetupEnergyBar();

        //Debug.Log($"Tower {towerName} RANGES:");
        //Debug.Log($"  Base Range: {range}");
        //Debug.Log($"  Melee Range: {meleeConfig.range}");
        //Debug.Log($"  Projectile Range: {ProjectileRange}");
        //Debug.Log($"  Collider Radius: {rangeCollider.radius}");
        //Debug.Log($"  Tentacle Length: {tentacleConfig.length}");
    }

    void RegisterWithEnergyManager() => EnergyManager.Instance?.RegisterEnergyConsumer(this);
    #endregion

    #region IDamageable Implementation
    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (immuneToEnemyDamage || isDisabledByDamage) return false;

        // Apply armor reduction
        float actualDamage = damageAmount * (1f - armorReduction);

        // Remove energy based on damage
        ConsumeEnergy(actualDamage);

        // Trigger damage effects
        if (enableDamageEffects)
        {
            StartDamageFlash();
        }

        // Log damage
        string sourceName = damageSource != null ? damageSource.name : "Unknown";
        //Debug.Log($"Tower {towerName} took {actualDamage:F1} damage from {sourceName}. Energy: {currentEnergy:F1}/{maxEnergy:F1}");

        // Fire damage event
        OnDamageTaken?.Invoke(actualDamage, damageSource);

        // Check if tower is destroyed
        if (IsEnergyDepleted())
        {
            DisableTower();
            OnTowerDestroyed?.Invoke(damageSource);
            return true;
        }

        return false;
    }

    public bool CanTakeDamage()
    {
        return !immuneToEnemyDamage && !isDisabledByDamage;
    }

    public float GetCurrentHealth()
    {
        return currentEnergy;
    }

    public float GetMaxHealth()
    {
        return maxEnergy;
    }

    public float GetHealthPercentage()
    {
        return GetEnergyPercentage();
    }

    public bool IsDestroyed()
    {
        return isDisabledByDamage || IsEnergyDepleted();
    }

    public void DisableTower()
    {
        if (isDisabledByDamage) return;

        isDisabledByDamage = true;
        OnTowerDisabled?.Invoke();

        // Visual indication of disabled state
        if (spriteRenderer != null)
        {
            Color disabledColor = spriteRenderer.color;
            disabledColor.a = 0.5f;
            spriteRenderer.color = disabledColor;
        }

        //Debug.Log($"Tower {towerName} has been disabled by damage!");
    }

    public void EnableTower()
    {
        if (!isDisabledByDamage) return;

        isDisabledByDamage = false;
        OnTowerEnabled?.Invoke();

        // Restore visual state
        if (spriteRenderer != null)
        {
            Color enabledColor = spriteRenderer.color;
            enabledColor.a = 1f;
            spriteRenderer.color = enabledColor;
        }

        //Debug.Log($"Tower {towerName} has been re-enabled!");
    }

    private void StartDamageFlash()
    {
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
        }
        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;
        Color flashColor = EnergyManager.Instance?.damageFlashColor ?? Color.red;

        // Flash effect
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(damageFlashDuration);
        spriteRenderer.color = originalColor;

        damageFlashCoroutine = null;
    }
    #endregion

    #region Public Interface
    public bool IsTargetInMeleeRange(GameObject target)
    {
        if (target == null) return false;
        float distance = Vector2.Distance(transform.position, target.transform.position);
        float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;
        return distance <= tentacleReach + 0.4f;
    }

    public bool IsTargetInProjectileRange(GameObject target)
    {
        if (target == null) return false;
        float distance = Vector2.Distance(transform.position, target.transform.position);
        return distance <= ProjectileRange;
    }

    public void TriggerMeleeAttack() => tentacleSystem?.StartMeleeAttack();
    public void TriggerProjectileAttack() => tentacleSystem?.StartProjectileAttack();

    public bool IsOperational()
    {
        return CanOperate() && !isDisabledByDamage;
    }
    #endregion

    #region Combat Interface
    public void FireAtTarget(GameObject target)
    {
        if (target == null || !CanOperate() || isDisabledByDamage) return;

        float energyCost = damage * 0.1f;
        if (currentEnergy < energyCost) return;

        ConsumeEnergy(energyCost);

        float distanceToTarget = Vector2.Distance(transform.position, target.transform.position);
        float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;

        //Debug.Log($"FIRING DECISION for {towerName}:");
        //Debug.Log($"  Distance to target: {distanceToTarget:F2}");
        //Debug.Log($"  Tentacle reach: {tentacleReach:F2}");
        //Debug.Log($"  Melee range: {meleeConfig.range:F2}");
        //Debug.Log($"  Projectile range: {ProjectileRange:F2}");

        if (distanceToTarget <= tentacleReach + 0.4f)
        {
            //Debug.Log($"  -> MELEE ATTACK (within tentacle reach)");
            combat.PerformMeleeAttack(target);
        }
        else if (distanceToTarget <= ProjectileRange)
        {
            //Debug.Log($"  -> PROJECTILE ATTACK (within projectile range)");
            combat.PerformProjectileAttack(target);
        }
        else
        {
            //Debug.LogWarning($"  -> TARGET OUT OF RANGE! Distance: {distanceToTarget:F2}, Max range: {ProjectileRange:F2}");
        }

        lastFireTime = Time.time;
    }
    #endregion

    #region Energy Interface
    bool CanOperate() => !requiresEnergyToFunction || !isEnergyDepleted;

    public void UpdateEnergyState()
    {
        bool wasEnergyDepleted = isEnergyDepleted;
        bool wasEnergyLow = isEnergyLow;

        isEnergyDepleted = IsEnergyDepleted();
        isEnergyLow = IsEnergyLow();

        if (isEnergyDepleted != wasEnergyDepleted || isEnergyLow != wasEnergyLow)
        {
            tentacleSystem?.UpdateEnergyEffects(GetEnergyPercentage(), isEnergyDepleted, isEnergyLow);
            energy.UpdateVisuals();
        }
    }
    #endregion

    #region IEnergyConsumer Implementation
    public void ConsumeEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            energy.UpdateVisuals();

            if (currentEnergy <= 0f && previousEnergy > 0f)
                OnEnergyDepleted?.Invoke();
        }
    }

    public void SupplyEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            energy.UpdateVisuals();

            if (previousEnergy <= 0f && currentEnergy > 0f)
                OnEnergyRestored?.Invoke();

            // Re-enable tower if it was disabled and now has energy
            if (isDisabledByDamage && currentEnergy > 0f)
            {
                EnableTower();
            }
        }
    }

    public void SetEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Clamp(amount, 0f, maxEnergy);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            energy.UpdateVisuals();
        }
    }

    public void SetMaxEnergy(float amount)
    {
        maxEnergy = amount;
        currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        energy.UpdateVisuals();
    }

    public float GetEnergy() => currentEnergy;
    public float GetMaxEnergy() => maxEnergy;
    public float GetEnergyPercentage() => maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    public Vector3 GetPosition() => transform.position;

    public bool IsEnergyDepleted() =>
        EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetTowerDeadThreshold();

    public bool IsEnergyLow() =>
        EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetTowerCriticalThreshold();
    #endregion

    #region Collision Events
    void OnTriggerEnter2D(Collider2D other) => targeting.OnTriggerEnter(other);
    void OnTriggerExit2D(Collider2D other) => targeting.OnTriggerExit(other);
    #endregion

    #region Upgrade System
    public void UpgradeTower()
    {
        if (!CanUpgrade || parentSlot == null) return;

        parentSlot.RemoveTower();
        var upgradedTower = Instantiate(upgradeTowerPrefab.gameObject, parentSlot.transform.position, Quaternion.identity);
        upgradedTower.transform.SetParent(parentSlot.transform, false);
        upgradedTower.transform.localPosition = Vector3.zero;

        parentSlot.currentTower = upgradedTower;
        parentSlot.isOccupied = true;

        var upgradedScript = upgradedTower.GetComponent<Tower>();
        if (upgradedScript != null)
            upgradedScript.upgradeLevel = upgradeLevel + 1;
    }

    public void SellTower()
    {
        if (parentSlot != null)
        {
            int sellValue = Mathf.RoundToInt(cost * 0.3f);
            parentSlot.RemoveTower();
        }
    }
    #endregion

    #region Public Accessors
    public void SetDamage(float newDamage) => damage = newDamage;
    public void SetRange(float newRange) { range = newRange; rangeCollider.radius = Mathf.Max(range, ProjectileRange); }
    public void SetFireRate(float newFireRate) => fireRate = newFireRate;
    public void SetArmor(float newArmor) => armorReduction = Mathf.Clamp01(newArmor);
    public float GetDamage() => damage;
    public float GetRange() => range;
    public float GetFireRate() => fireRate;
    public float GetArmor() => armorReduction;
    public int GetCost() => cost;
    public TowerType GetTowerType() => towerType;
    #endregion

    #region Cleanup
    void Cleanup()
    {
        EnergyManager.Instance?.UnregisterEnergyConsumer(this);
        tentacleSystem?.Cleanup();
        if (energyBar != null) Destroy(energyBar);
        if (damageFlashCoroutine != null) StopCoroutine(damageFlashCoroutine);
    }
    #endregion

#if UNITY_EDITOR
    #region Editor Gizmos
    void OnDrawGizmosSelected()
    {
        // Draw base range (smallest)
        Handles.color = Color.blue;
        Handles.DrawWireDisc(transform.position, Vector3.forward, range);

        // Draw melee range 
        Handles.color = Color.yellow;
        Handles.DrawWireDisc(transform.position, Vector3.forward, meleeConfig.range);

        // Draw projectile range (should be largest)
        Handles.color = Color.red;
        Handles.DrawWireDisc(transform.position, Vector3.forward, ProjectileRange);

        // Draw collider range (detection range)
        Handles.color = Color.green;
        Handles.DrawWireDisc(transform.position, Vector3.forward, rangeCollider != null ? rangeCollider.radius : 0f);

        // Draw target line
        if (targeting?.CurrentTarget != null)
        {
            float distance = Vector2.Distance(transform.position, targeting.CurrentTarget.transform.position);

            if (distance <= meleeConfig.range)
                Handles.color = Color.yellow; // Melee range
            else if (distance <= ProjectileRange)
                Handles.color = Color.red; // Projectile range
            else
                Handles.color = Color.gray; // Out of range

            Handles.DrawLine(transform.position, targeting.CurrentTarget.transform.position);

            // Show distance text
            Vector3 midPoint = (transform.position + targeting.CurrentTarget.transform.position) / 2f;
            Handles.Label(midPoint, $"Dist: {distance:F1}");
        }

        // Draw range legend
        Vector3 legendPos = transform.position + Vector3.up * 3f;
        Handles.Label(legendPos, $"Ranges:");
        Handles.Label(legendPos + Vector3.down * 0.3f, $"Base: {range:F1} (Blue)");
        Handles.Label(legendPos + Vector3.down * 0.6f, $"Melee: {meleeConfig.range:F1} (Yellow)");
        Handles.Label(legendPos + Vector3.down * 0.9f, $"Projectile: {ProjectileRange:F1} (Red)");
        Handles.Label(legendPos + Vector3.down * 1.2f, $"Detection: {(rangeCollider != null ? rangeCollider.radius : 0f):F1} (Green)");

        // Draw energy info
        Vector3 energyPos = transform.position + Vector3.up * 1.5f;
        Handles.Label(energyPos, $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");

        // Draw damage info
        Vector3 damagePos = transform.position + Vector3.up * 1.2f;
        string damageStatus = isDisabledByDamage ? "DISABLED" : "ACTIVE";
        Handles.Label(damagePos, $"Status: {damageStatus} | Armor: {armorReduction * 100f:F0}%");
    }
    #endregion
#endif
}

// Interface for damageable objects
public interface IDamageable
{
    bool TakeDamage(float damage, GameObject source = null);
    bool CanTakeDamage();
    float GetCurrentHealth();
    float GetMaxHealth();
    float GetHealthPercentage();
    bool IsDestroyed();
}