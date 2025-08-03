using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Tower : MonoBehaviour, IEnergyConsumer, IDamageable
{

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

    public enum TowerType { Basic, Artillery, Laser, Ice, Poison }

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

    [Header("Tentacle & Melee")]
    public bool useTentacleTurret = true;
    public TentacleConfig tentacleConfig = new TentacleConfig();
    public MeleeConfig meleeConfig = new MeleeConfig();

    [Header("Rotation & Energy")]
    public float rotationSpeed = 180f;
    public bool smoothRotation = true;
    public float maxEnergy = 100f;
    public float currentEnergy = 100f;
    public bool requiresEnergyToFunction = true;
    public bool showEnergyBar = true;

    [Header("Damage Settings")]
    public float armorReduction = 0f;
    public bool immuneToEnemyDamage = false;
    public float damageFlashDuration = 0.15f;

    // Properties
    public bool CanUpgrade => canUpgrade && upgradeLevel < maxUpgradeLevel && upgradeTowerPrefab != null;
    public bool CanFire => Time.time >= lastFireTime + (1f / fireRate);
    public float ProjectileRange { get; private set; }
    public Transform FirePoint => firePoint;

    // Events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted, OnEnergyRestored;
    public System.Action<float, GameObject> OnDamageTaken;
    public System.Action<GameObject> OnTowerDestroyed;

    // Components
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D rangeCollider;
    private TowerSlot parentSlot;
    private EnergyBar energyBar;

    // Targeting & Combat
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
    private Coroutine damageFlashCoroutine;

    void Awake()
    {
        LoadConfig();
        InitializeComponents();
    }

    void Start()
    {
        SetupTower();
        EnergyManager.Instance?.RegisterEnergyConsumer(this);
    }

    void Update()
    {
        if (IsEnergyDepleted() || isDisabledByDamage) return;

        UpdateTargeting();
        UpdateTentacles();
        TryFire();
    }

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

        // Ensure SpriteRenderer exists and is properly initialized
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        // Wait for component to be fully initialized before setting properties
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

        if (useTentacleTurret) InitializeTentacles();
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
        tentacleRenderer.sortingOrder = 16;

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
        float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;
        ProjectileRange = Mathf.Max(range * 2f, tentacleReach * 3.5f, 6f);
        rangeCollider.radius = ProjectileRange + 0.5f;
        LoadSprite();
        SetupSpriteCollision();
        SetupEnergyBar();
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

    // TODO remove the helper methods
    //public Bounds GetSpriteBounds() => SpriteCollisionManager.GetSpriteBounds(gameObject);
    //public bool IsPointWithinSprite(Vector3 worldPoint) => SpriteCollisionManager.IsPointWithinSprite(gameObject, worldPoint);
    //public void UpdateCollisionSettings() => spriteCollider = SpriteCollisionManager.UpdateCollisionSettings(gameObject, collisionConfig);

    void LoadSprite()
    {
        var sprites = Resources.LoadAll<Sprite>(spriteResourcePath);
        if (sprites?.Length > spriteIndex)
        {
            spriteRenderer.sprite = sprites[spriteIndex];
            if (enableAnimation && sprites.Length > 1)
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
        energyBar.energyBarOffset = 1.5f;
        energyBar.showEnergyText = true;

        if (EnergyManager.Instance != null)
        {
            energyBar.SetColors(EnergyManager.Instance.normalColor, EnergyManager.Instance.lowEnergyColor,
                              EnergyManager.Instance.criticalEnergyColor, EnergyManager.Instance.depletedEnergyColor);
        }

        energyBar.Initialize(this, spriteRenderer);
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

            if (smoothRotation && !isSwipingMelee)
            {
                float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
                float rotationStep = rotationSpeed * Time.deltaTime;

                currentAngle = Mathf.Abs(angleDifference) <= rotationStep ?
                    targetAngle : currentAngle + Mathf.Sign(angleDifference) * rotationStep;

                currentAngle = Mathf.Repeat(currentAngle + 180f, 360f) - 180f;

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

    public void FireAtTarget(GameObject target)
    {
        if (target == null || IsEnergyDepleted() || isDisabledByDamage) return;

        float energyCost = damage * 0.1f;
        if (currentEnergy < energyCost) return;

        ConsumeEnergy(energyCost);
        lastFireTime = Time.time;

        float dist = Vector2.Distance(transform.position, target.transform.position);
        float tentacleReach = tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude;

        if (dist <= tentacleReach + 0.4f)
        {
            // Melee Attack
            isMeleeAttacking = true;
            isSwipingMelee = true;
            meleeAnimTimer = 0f;
            swipeTimer = 0f;
            var stats = target.GetComponent<EnemyStats>();
            stats?.TakeDamage(damage * meleeConfig.damageMultiplier);
        }
        else if (dist <= ProjectileRange)
        {
            // Projectile Attack  
            isFiring = true;
            fireAnimTimer = 0f;

            if (projectilePrefab != null)
            {
                AudioManager.instance?.PlayOneShot(FMODEvents.instance.multiShotSound, FirePoint.position);
                Vector3 spawn = FirePoint?.position ?? transform.position;
                Vector3 dir = (target.transform.position - spawn).normalized;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                var proj = Instantiate(projectilePrefab, spawn, Quaternion.AngleAxis(angle, Vector3.forward));
                proj.GetComponent<Projectile>()?.Initialize(target, damage, range);
            }
            else
            {
                target.GetComponent<EnemyStats>()?.TakeDamage(damage);
            }
        }
    }
    #endregion

    #region Tentacle System
    void UpdateTentacles()
    {
        if (!useTentacleTurret || tentacleRenderer == null) return;

        swayTimer += Time.deltaTime * tentacleConfig.swaySpeed;

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
            pos.y += Mathf.Sin(swayTimer + t * Mathf.PI) * tentacleConfig.swayAmount * t;

            // Apply firing animation
            if (isFiring)
            {
                float fireAnim = Mathf.Sin(fireAnimTimer * Mathf.PI);
                pos.x += fireAnim * 0.3f * t;
                pos.y *= (1f - fireAnim * 0.5f);
            }

            // Apply melee attack animation
            if (isMeleeAttacking)
            {
                float meleeAnim = Mathf.Sin((meleeAnimTimer / meleeConfig.attackDuration) * Mathf.PI);
                pos.x += meleeAnim * 0.5f * t;
                float whip = Mathf.Sin(meleeAnim * Mathf.PI * 2f) * 0.3f * t;
                pos.y += whip;
            }

            // Apply swipe animation
            if (isSwipingMelee)
            {
                float swipeProgress = meleeConfig.swipeCurve.Evaluate(swipeTimer);
                float swipeAngle = Mathf.Lerp(-meleeConfig.swipeArcDegrees / 2f, meleeConfig.swipeArcDegrees / 2f, swipeProgress);
                float swipeAngleRad = swipeAngle * Mathf.Deg2Rad;
                float swipeExtension = Mathf.Sin(swipeProgress * Mathf.PI) * 0.4f;

                pos.x += swipeExtension * t;

                float radius = pos.magnitude;
                float currentAngleRad = Mathf.Atan2(pos.y, pos.x);
                float newAngleRad = currentAngleRad + (swipeAngleRad * 1.5f * t);

                pos.x = Mathf.Cos(newAngleRad) * radius;
                pos.y = Mathf.Sin(newAngleRad) * radius;

                float whipEffect = Mathf.Sin(swipeProgress * Mathf.PI * 2f) * 0.2f * t;
                pos.y += whipEffect;
            }

            // Apply target tracking (allow tracking during firing, but not during melee swipes)
            if (currentTarget != null && !isSwipingMelee)
            {
                Vector3 targetDir = transform.InverseTransformDirection((currentTarget.transform.position - transform.position).normalized);
                pos += targetDir * (t * 0.2f);
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
        float prev = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);
        if (currentEnergy != prev)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisuals();
            if (currentEnergy <= 0f && prev > 0f) OnEnergyDepleted?.Invoke();
        }
    }

    public void SupplyEnergy(float amount)
    {
        float prev = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);
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
        float prev = currentEnergy;
        currentEnergy = Mathf.Clamp(amount, 0f, maxEnergy);
        if (currentEnergy != prev) { OnEnergyChanged?.Invoke(currentEnergy); UpdateVisuals(); }
    }

    public void SetMaxEnergy(float amount) { maxEnergy = amount; currentEnergy = Mathf.Min(currentEnergy, maxEnergy); UpdateVisuals(); }
    public float GetEnergy() => currentEnergy;
    public float GetMaxEnergy() => maxEnergy;
    public float GetEnergyPercentage() => maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    public Vector3 GetPosition() => transform.position;
    public bool IsEnergyDepleted() => EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetTowerDeadThreshold();
    public bool IsEnergyLow() => EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetTowerCriticalThreshold();

    void UpdateVisuals()
    {
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
        if (immuneToEnemyDamage || isDisabledByDamage) return false;

        float actualDamage = damageAmount * (1f - armorReduction);
        ConsumeEnergy(actualDamage);

        StartDamageFlash();
        OnDamageTaken?.Invoke(actualDamage, damageSource);

        if (IsEnergyDepleted())
        {
            DisableTower();
            OnTowerDestroyed?.Invoke(damageSource);
            return true;
        }
        return false;
    }

    public bool CanTakeDamage() => !immuneToEnemyDamage && !isDisabledByDamage;
    public float GetCurrentHealth() => currentEnergy;
    public float GetMaxHealth() => maxEnergy;
    public float GetHealthPercentage() => GetEnergyPercentage();
    public bool IsDestroyed() => isDisabledByDamage || IsEnergyDepleted();

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
        Color orig = spriteRenderer.color;
        spriteRenderer.color = EnergyManager.Instance?.damageFlashColor ?? Color.red;
        yield return new WaitForSeconds(damageFlashDuration);
        spriteRenderer.color = orig;
        damageFlashCoroutine = null;
    }
    #endregion

    #region Collision & Public Interface
    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsEnemy(other.gameObject) && ((1 << other.gameObject.layer) & targetLayer) != 0)
            enemiesInRange.Add(other.gameObject);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (enemiesInRange.Remove(other.gameObject) && currentTarget == other.gameObject)
            currentTarget = null;
    }

    public bool IsTargetInMeleeRange(GameObject target) => target != null && Vector2.Distance(transform.position, target.transform.position) <= tentacleConfig.length + tentacleConfig.attachmentOffset.magnitude + 0.4f;
    public bool IsTargetInProjectileRange(GameObject target) => target != null && Vector2.Distance(transform.position, target.transform.position) <= ProjectileRange;
    public bool IsOperational() => !IsEnergyDepleted() && !isDisabledByDamage;

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
    public void SetDamage(float newDamage) => damage = newDamage;
    public void SetRange(float newRange) { range = newRange; rangeCollider.radius = Mathf.Max(range, ProjectileRange); }
    public void SetFireRate(float newFireRate) => fireRate = newFireRate;
    public void SetArmor(float newArmor) => armorReduction = Mathf.Clamp01(newArmor);
    public void SetUpgradeLevel(int level) => upgradeLevel = level;
    public float GetDamage() => damage;
    public float GetRange() => range;
    public float GetFireRate() => fireRate;
    public float GetArmor() => armorReduction;
    public int GetCost() => cost;
    public TowerType GetTowerType() => towerType;
    public float LastFireTime => lastFireTime;
    #endregion

    void Cleanup()
    {
        EnergyManager.Instance?.UnregisterEnergyConsumer(this);
        if (tentacleContainer != null) DestroyImmediate(tentacleContainer);
        if (energyBar != null) Destroy(energyBar);
        if (damageFlashCoroutine != null) StopCoroutine(damageFlashCoroutine);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Handles.color = Color.blue; Handles.DrawWireDisc(transform.position, Vector3.forward, range);
        Handles.color = Color.red; Handles.DrawWireDisc(transform.position, Vector3.forward, ProjectileRange);
        Handles.color = Color.green; Handles.DrawWireDisc(transform.position, Vector3.forward, rangeCollider?.radius ?? 0f);

        if (currentTarget != null)
        {
            float dist = Vector2.Distance(transform.position, currentTarget.transform.position);
            Handles.color = dist <= ProjectileRange ? Color.red : Color.gray;
            Handles.DrawLine(transform.position, currentTarget.transform.position);
            Handles.Label((transform.position + currentTarget.transform.position) / 2f, $"Dist: {dist:F1}");
        }

        Handles.Label(transform.position + Vector3.up * 1.5f, $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");
        Handles.Label(transform.position + Vector3.up * 1.2f, $"Status: {(isDisabledByDamage ? "DISABLED" : "ACTIVE")}");
    }
#endif
}

public interface IDamageable
{
    bool TakeDamage(float damage, GameObject source = null);
    bool CanTakeDamage();
    float GetCurrentHealth();
    float GetMaxHealth();
    float GetHealthPercentage();
    bool IsDestroyed();
}