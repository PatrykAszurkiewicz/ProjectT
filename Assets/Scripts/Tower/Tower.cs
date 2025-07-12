using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Tower : MonoBehaviour, IEnergyConsumer
{
    #region Public Properties - Tower Settings
    [Header("Tower Properties")]
    public string towerName = "Basic Tower";
    public float damage = 10f;
    public float range = 3f;
    public float fireRate = 1f; // Shots per second
    public int cost = 100;
    public TowerType towerType = TowerType.Basic;

    [Header("Visual Settings")]
    public string spriteResourcePath = "Sprites/spritesheet_transparent2";
    public int spriteIndex = 0;
    public int spritesPerRow = 10;
    public Vector2 spriteSize = new Vector2(231, 185);
    public float spriteScale = 0.5f;
    public bool enableAnimation = true;
    public int animationFrameCount = 43;
    public float animationSpeed = 0.25f;

    [Header("Combat Settings")]
    public LayerMask targetLayer = -1;
    public GameObject projectilePrefab;
    public Transform firePoint;

    [Header("Upgrade Settings")]
    public bool canUpgrade = true;
    public Tower upgradeTowerPrefab;
    public int upgradeLevel = 1;
    public int maxUpgradeLevel = 3;

    [Header("Tentacle Turret Settings")]
    public bool useTentacleTurret = true;
    public float tentacleLength = 0.8f;
    public float tentacleWidth = 0.3f;
    public int tentacleSegments = 8;
    public float tentacleSwayAmount = 0.1f;
    public float tentacleSwaySpeed = 2f;
    public Color tentacleColor = new Color(0.337f, 0.176f, 0.259f, 0.8f);
    public Color tentacleTipColor = new Color(0.8f, 0.3f, 0.3f, 1f);
    public float tentacleAnimationSpeed = 1f;
    public Vector2 tentacleAttachmentOffset = new Vector2(0f, -0.3f);
    public float downwardShorteningFactor = 0.5f;

    [Header("Melee Attack Settings")]
    public float meleeRange = 1.0f;
    public float meleeDamageMultiplier = 1.5f;
    public float meleeAttackDuration = 0.3f;
    public float projectileRange = 5f;

    [Header("Melee Swipe Settings")]
    public float swipeArcDegrees = 60f;
    public float swipeSpeed = 8f;
    public float swipeReach = 0.4f;
    public float swipeIntensity = 1.5f;
    public AnimationCurve swipeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Rotation Settings")]
    public float rotationSpeed = 180f;
    public bool smoothRotation = true;
    public Transform turretTransform;

    [Header("Energy Settings")]
    public float maxEnergy = 100f;
    public float currentEnergy = 100f;
    public bool requiresEnergyToFunction = true;

    [Header("Energy Bar Settings")]
    public bool showEnergyBar = true;
    public float energyBarHeight = 0.1f;
    public float energyBarWidth = 1f;
    public float energyBarOffset = 1.5f;
    #endregion

    #region Private Core Variables
    // Core components
    private float lastFireTime;
    private GameObject currentTarget;
    private List<GameObject> enemiesInRange = new List<GameObject>();
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D rangeCollider;
    private TowerSlot parentSlot;

    // Targeting and rotation
    private float targetAngle;
    private float currentAngle;
    private bool hasValidTarget = false;

    // Visual state
    private Vector3 originalScale;
    private Color originalTowerColor;
    #endregion

    #region Private Tentacle System Variables
    // Tentacle rendering
    private LineRenderer tentacleRenderer;
    private GameObject tentacleContainer;
    private Vector3[] tentaclePoints;
    private float tentacleSwayTimer;

    // Animation states
    private bool isFiring = false;
    private float fireAnimationTimer = 0f;
    private bool isMeleeAttacking = false;
    private float meleeAttackTimer = 0f;

    // Melee swipe variables
    private bool isSwipingMelee = false;
    private float swipeTimer = 0f;
    private Vector3 swipeTargetPosition = Vector3.zero;

    // Attack tracking
    private AttackType currentAttackType = AttackType.None;
    #endregion

    #region Private Energy Variables
    // Energy state
    private bool isEnergyDepleted = false;
    private bool isEnergyLow = false;
    private EnergyBar energyBar;

    // Energy events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted;
    public System.Action OnEnergyRestored;
    #endregion

    #region Enums and Properties
    private enum AttackType
    {
        None,
        Melee,
        Projectile
    }

    public enum TowerType
    {
        Basic,
        Artillery,
        Laser,
        Ice,
        Poison
    }

    // Properties
    public bool CanUpgrade => canUpgrade && upgradeLevel < maxUpgradeLevel && upgradeTowerPrefab != null;
    public float NextFireTime => lastFireTime + (1f / fireRate);
    public bool CanFire => Time.time >= NextFireTime;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        InitializeTower();

        if (swipeCurve == null || swipeCurve.length == 0)
        {
            swipeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    void Start()
    {
        Application.targetFrameRate = 0;
        QualitySettings.vSyncCount = 0;
        LoadTowerSprite();
        SetupRangeCollider();
        SetupTentacleTurret();
        parentSlot = GetComponentInParent<TowerSlot>();

        // Calculate effective melee range based on tentacle reach
        float effectiveTentacleReach = tentacleLength + tentacleAttachmentOffset.magnitude;
        meleeRange = Mathf.Max(meleeRange, effectiveTentacleReach);

        if (projectileRange <= meleeRange)
        {
            projectileRange = meleeRange * 2.5f;
        }

        Debug.Log($"Tower melee range set to: {meleeRange}, projectile range: {projectileRange}");

        // Energy setup
        EnergyManager.Instance.RegisterEnergyConsumer(this);

        if (spriteRenderer != null)
        {
            originalTowerColor = spriteRenderer.color;
        }

        // Setup energy bar
        SetupEnergyBar();

        // Initialize energy visual state
        UpdateEnergyVisuals();
    }

    void Update()
    {
        UpdateEnergyState();

        if (CanOperate())
        {
            if (currentTarget == null)
            {
                FindTarget();
            }
            else
            {
                if (!IsTargetValid(currentTarget))
                {
                    currentTarget = null;
                    hasValidTarget = false;
                    return;
                }
                else
                {
                    hasValidTarget = true;
                    UpdateTargetAngle();
                }
            }

            if (smoothRotation && hasValidTarget && !isSwipingMelee)
            {
                UpdateSmoothRotation();
            }

            if (useTentacleTurret)
            {
                UpdateTentacle();
            }

            if (currentTarget != null && CanFire)
            {
                FireAtTarget();
            }
        }
        else
        {
            currentTarget = null;
            hasValidTarget = false;
        }
    }
    #endregion

    #region Initialization Methods
    void InitializeTower()
    {
        if (GetComponent<SpriteRenderer>() == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        else
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        spriteRenderer.sortingOrder = 20;
        spriteRenderer.sortingLayerName = "Default";

        originalScale = Vector3.one * spriteScale;
        transform.localScale = originalScale;

        rangeCollider = gameObject.AddComponent<CircleCollider2D>();
        rangeCollider.radius = range;
        rangeCollider.isTrigger = true;

        Debug.Log($"Tower initialized with sorting order: {spriteRenderer.sortingOrder}");
    }

    void SetupEnergyBar()
    {
        if (!showEnergyBar) return;

        energyBar = gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = showEnergyBar;
        energyBar.energyBarHeight = energyBarHeight;
        energyBar.energyBarWidth = energyBarWidth;
        energyBar.energyBarOffset = energyBarOffset;
        energyBar.showEnergyText = true;

        // Set colors based on EnergyManager settings
        energyBar.SetColors(
            EnergyManager.Instance.normalColor,
            EnergyManager.Instance.lowEnergyColor,
            EnergyManager.Instance.criticalEnergyColor,
            EnergyManager.Instance.depletedEnergyColor
        );

        energyBar.Initialize(this, spriteRenderer);
    }

    void SetupTentacleTurret()
    {
        if (!useTentacleTurret) return;

        tentacleContainer = new GameObject("TentacleContainer");
        tentacleContainer.transform.SetParent(transform);
        tentacleContainer.transform.localPosition = tentacleAttachmentOffset;

        tentacleRenderer = tentacleContainer.AddComponent<LineRenderer>();
        tentacleRenderer.material = CreateTentacleMaterial();
        tentacleRenderer.startWidth = tentacleWidth;
        tentacleRenderer.endWidth = tentacleWidth * 0.3f;
        tentacleRenderer.positionCount = tentacleSegments;
        tentacleRenderer.useWorldSpace = false;

        if (spriteRenderer != null)
        {
            tentacleRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
            tentacleRenderer.sortingOrder = spriteRenderer.sortingOrder - 4;
        }

        tentaclePoints = new Vector3[tentacleSegments];
        for (int i = 0; i < tentacleSegments; i++)
        {
            float t = (float)i / (tentacleSegments - 1);
            tentaclePoints[i] = Vector3.right * (tentacleLength * t);
        }

        turretTransform = tentacleContainer.transform;

        if (firePoint == null)
        {
            GameObject firePointObj = new GameObject("FirePoint");
            firePointObj.transform.SetParent(tentacleContainer.transform);
            firePointObj.transform.localPosition = Vector3.right * tentacleLength;
            firePoint = firePointObj.transform;
        }
    }

    Material CreateTentacleMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = tentacleColor;
        return mat;
    }

    void LoadTowerSprite()
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>(spriteResourcePath);
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError($"Could not load sprites from path: {spriteResourcePath}");
            return;
        }

        if (spriteIndex < sprites.Length)
        {
            spriteRenderer.sprite = sprites[spriteIndex];
            Debug.Log($"Loaded tower sprite: {towerName} at index {spriteIndex}");
            if (sprites.Length > 1)
            {
                StartCoroutine(
                    Utilities.AnimateSprite(
                        spriteRenderer,
                        sprites,
                        enableAnimation,
                        animationFrameCount,
                        spriteIndex,
                        animationSpeed
                    )
                );
            }
        }
        else
        {
            Debug.LogWarning($"Sprite index {spriteIndex} is out of range. Using first sprite.");
            spriteRenderer.sprite = sprites[0];
        }
    }

    void SetupRangeCollider()
    {
        if (rangeCollider != null)
        {
            rangeCollider.radius = Mathf.Max(range, projectileRange);
        }
    }
    #endregion

    #region Tentacle System - Complete Original Logic
    void UpdateTentacle()
    {
        if (!useTentacleTurret || tentacleRenderer == null)
            return;

        tentacleSwayTimer += Time.deltaTime * tentacleSwaySpeed;

        if (isFiring)
        {
            fireAnimationTimer += Time.deltaTime * tentacleAnimationSpeed;
            if (fireAnimationTimer >= 1f)
            {
                isFiring = false;
                fireAnimationTimer = 0f;
            }
        }

        if (isMeleeAttacking)
        {
            meleeAttackTimer += Time.deltaTime;
            if (meleeAttackTimer >= meleeAttackDuration)
            {
                isMeleeAttacking = false;
                meleeAttackTimer = 0f;
            }
        }

        if (isSwipingMelee)
        {
            swipeTimer += Time.deltaTime * swipeSpeed;
            if (swipeTimer >= 1f)
            {
                isSwipingMelee = false;
                swipeTimer = 0f;
            }
        }

        // Calculate tentacle curve
        for (int i = 0; i < tentacleSegments; i++)
        {
            float t = (float)i / (tentacleSegments - 1);
            Vector3 basePos = Vector3.right * (tentacleLength * t);

            float swayOffset = Mathf.Sin(tentacleSwayTimer + t * Mathf.PI) * tentacleSwayAmount * t;
            basePos.y += swayOffset;

            Vector3 worldDir = turretTransform.TransformDirection(basePos.normalized);
            if (worldDir.y < 0f)
            {
                float shorten = Mathf.Clamp01(1f - downwardShorteningFactor * -worldDir.y);
                basePos.x *= shorten;
                basePos.y *= shorten;
            }

            if (isFiring)
            {
                float fi = Mathf.Sin(fireAnimationTimer * Mathf.PI);
                basePos.x += fi * 0.3f * t;
                basePos.y *= (1f - fi * 0.5f);
            }

            if (isMeleeAttacking)
            {
                float mi = Mathf.Sin((meleeAttackTimer / meleeAttackDuration) * Mathf.PI);
                basePos.x += mi * 0.5f * t;
                float whip = Mathf.Sin(mi * Mathf.PI * 2f) * 0.3f * t;
                basePos.y += whip;
            }

            if (isSwipingMelee)
            {
                float swipeProgress = swipeCurve.Evaluate(swipeTimer);
                float swipeAngle = Mathf.Lerp(-swipeArcDegrees / 2f, swipeArcDegrees / 2f, swipeProgress);
                float swipeAngleRad = swipeAngle * Mathf.Deg2Rad;
                float swipeExtension = Mathf.Sin(swipeProgress * Mathf.PI) * swipeReach;
                basePos.x += swipeExtension * t;

                float radius = basePos.magnitude;
                float currentAngleRad = Mathf.Atan2(basePos.y, basePos.x);
                float newAngleRad = currentAngleRad + (swipeAngleRad * swipeIntensity * t);
                basePos.x = Mathf.Cos(newAngleRad) * radius;
                basePos.y = Mathf.Sin(newAngleRad) * radius;

                float whipEffect = Mathf.Sin(swipeProgress * Mathf.PI * 2f) * 0.2f * t;
                basePos.y += whipEffect;
            }

            if (hasValidTarget && currentTarget != null && !isSwipingMelee)
            {
                Vector3 tgtDir = transform.InverseTransformDirection(
                    (currentTarget.transform.position - transform.position).normalized);
                basePos += tgtDir * (t * 0.2f);
            }

            tentaclePoints[i] = basePos;
        }

        tentacleRenderer.SetPositions(tentaclePoints);

        if (firePoint != null)
        {
            firePoint.position = tentacleContainer.transform.TransformPoint(
                tentaclePoints[tentaclePoints.Length - 1]);
        }

        Gradient gradient = new Gradient();
        if (isFiring || isMeleeAttacking || isSwipingMelee)
        {
            Color tipColor = isSwipingMelee ? Color.Lerp(tentacleTipColor, Color.white, 0.3f) : tentacleTipColor;
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(tentacleColor, 0), new GradientColorKey(tipColor, 1) },
                new GradientAlphaKey[] { new GradientAlphaKey(tentacleColor.a, 0), new GradientAlphaKey(1, 1) }
            );
        }
        else
        {
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(tentacleColor, 0), new GradientColorKey(tentacleColor, 1) },
                new GradientAlphaKey[] { new GradientAlphaKey(tentacleColor.a, 0), new GradientAlphaKey(tentacleColor.a, 1) }
            );
        }
        tentacleRenderer.colorGradient = gradient;
    }
    #endregion

    #region Targeting and Rotation System
    void UpdateTargetAngle()
    {
        if (currentTarget == null) return;
        Vector2 direction = (currentTarget.transform.position - transform.position).normalized;
        targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        while (targetAngle > 180f) targetAngle -= 360f;
        while (targetAngle < -180f) targetAngle += 360f;
    }

    void UpdateSmoothRotation()
    {
        if (!hasValidTarget) return;

        float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
        float rotationStep = rotationSpeed * Time.deltaTime;

        if (Mathf.Abs(angleDifference) <= rotationStep)
        {
            currentAngle = targetAngle;
        }
        else
        {
            currentAngle += Mathf.Sign(angleDifference) * rotationStep;
        }

        while (currentAngle > 180f) currentAngle -= 360f;
        while (currentAngle < -180f) currentAngle += 360f;

        if (turretTransform != null)
        {
            turretTransform.rotation = Quaternion.AngleAxis(currentAngle, Vector3.forward);
        }
    }

    bool IsAimedAtTarget()
    {
        if (!smoothRotation) return true;
        float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
        return Mathf.Abs(angleDifference) <= 5f;
    }

    void FindTarget()
    {
        if (enemiesInRange.Count == 0) return;

        enemiesInRange.RemoveAll(enemy => enemy == null || !IsTargetValid(enemy));
        if (enemiesInRange.Count == 0) return;

        GameObject closestEnemy = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject enemy in enemiesInRange)
        {
            float distance = Vector2.Distance(transform.position, enemy.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = enemy;
            }
        }

        currentTarget = closestEnemy;
    }

    bool IsTargetValid(GameObject target)
    {
        if (target == null) return false;
        float distance = Vector2.Distance(transform.position, target.transform.position);
        return distance <= projectileRange;
    }

    bool IsValidTarget(GameObject target)
    {
        return ((1 << target.layer) & targetLayer) != 0;
    }
    #endregion

    #region Combat System
    void FireAtTarget()
    {
        if (currentTarget == null || !CanOperate()) return;

        float energyCost = damage * 0.1f;
        if (currentEnergy >= energyCost)
        {
            ConsumeEnergy(energyCost);

            float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);
            float tentacleReach = tentacleLength + tentacleAttachmentOffset.magnitude;

            if (distanceToTarget <= tentacleReach + 0.4f)
            {
                currentAttackType = AttackType.Melee;
                PerformMeleeAttack();
            }
            else if (distanceToTarget <= projectileRange)
            {
                currentAttackType = AttackType.Projectile;
                PerformProjectileAttack();
            }

            lastFireTime = Time.time;
        }
    }

    void PerformMeleeAttack()
    {
        if (useTentacleTurret)
        {
            isMeleeAttacking = true;
            meleeAttackTimer = 0f;
            isSwipingMelee = true;
            swipeTimer = 0f;

            if (currentTarget != null)
            {
                swipeTargetPosition = currentTarget.transform.position;
            }
        }

        if (currentTarget != null)
        {
            Health targetHealth = currentTarget.GetComponent<Health>();
            if (targetHealth != null)
            {
                float meleeDamage = damage * meleeDamageMultiplier;
                targetHealth.TakeDamage(meleeDamage);
                Debug.Log($"Melee swipe attack dealt {meleeDamage} damage to {currentTarget.name}");
            }
        }
    }

    void PerformProjectileAttack()
    {
        if (useTentacleTurret)
        {
            isFiring = true;
            fireAnimationTimer = 0f;
        }

        if (projectilePrefab != null)
        {
            FireProjectile();
        }
        else
        {
            DealDirectDamage();
        }
    }

    void FireProjectile()
    {
        Vector3 spawnPosition = firePoint.position;
        Vector3 direction = (currentTarget.transform.position - spawnPosition).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion projectileRotation = Quaternion.AngleAxis(angle, Vector3.forward);

        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, projectileRotation);
        Projectile projScript = projectile.GetComponent<Projectile>();
        if (projScript != null)
        {
            projScript.Initialize(currentTarget, damage, range);
        }
    }

    void DealDirectDamage()
    {
        if (currentTarget != null)
        {
            Health targetHealth = currentTarget.GetComponent<Health>();
            if (targetHealth != null)
            {
                targetHealth.TakeDamage(damage);
            }
        }
    }
    #endregion

    #region Collision Detection
    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsValidTarget(other.gameObject))
        {
            enemiesInRange.Add(other.gameObject);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (enemiesInRange.Contains(other.gameObject))
        {
            enemiesInRange.Remove(other.gameObject);

            if (currentTarget == other.gameObject)
            {
                currentTarget = null;
            }
        }
    }
    #endregion

    #region Upgrade System
    public void UpgradeTower()
    {
        if (!CanUpgrade) return;

        TowerSlot slot = GetComponentInParent<TowerSlot>();
        if (slot == null) return;

        slot.RemoveTower();
        GameObject upgradedTower = Instantiate(upgradeTowerPrefab.gameObject, slot.transform.position, Quaternion.identity);
        upgradedTower.transform.SetParent(slot.transform, false);
        upgradedTower.transform.localPosition = Vector3.zero;
        upgradedTower.transform.localRotation = Quaternion.identity;
        slot.currentTower = upgradedTower;
        slot.isOccupied = true;

        Tower upgradedTowerScript = upgradedTower.GetComponent<Tower>();
        if (upgradedTowerScript != null)
        {
            upgradedTowerScript.upgradeLevel = upgradeLevel + 1;
        }
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

    #region Energy Management - IEnergyConsumer Implementation
    public void ConsumeEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateEnergyVisuals();

            if (currentEnergy <= 0f && previousEnergy > 0f)
            {
                OnEnergyDepleted?.Invoke();
            }
        }
    }

    public void SupplyEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateEnergyVisuals();

            if (previousEnergy <= 0f && currentEnergy > 0f)
            {
                OnEnergyRestored?.Invoke();
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
            UpdateEnergyVisuals();
        }
    }

    public void SetMaxEnergy(float amount)
    {
        maxEnergy = amount;
        currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        UpdateEnergyVisuals();
    }

    public float GetEnergy()
    {
        return currentEnergy;
    }

    public float GetMaxEnergy()
    {
        return maxEnergy;
    }

    public float GetEnergyPercentage()
    {
        return maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    }

    public bool IsEnergyDepleted()
    {
        if (EnergyManager.Instance == null) return false;
        return GetEnergyPercentage() <= EnergyManager.Instance.GetTowerDeadThreshold();
    }

    public bool IsEnergyLow()
    {
        if (EnergyManager.Instance == null) return false;
        return GetEnergyPercentage() <= EnergyManager.Instance.GetTowerCriticalThreshold();
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }

    bool CanOperate()
    {
        return !requiresEnergyToFunction || !isEnergyDepleted;
    }
    #endregion

    #region Energy State Management
    void UpdateEnergyState()
    {
        bool wasEnergyDepleted = isEnergyDepleted;
        bool wasEnergyLow = isEnergyLow;

        isEnergyDepleted = IsEnergyDepleted();
        isEnergyLow = IsEnergyLow();

        if (isEnergyDepleted != wasEnergyDepleted || isEnergyLow != wasEnergyLow)
        {
            UpdateEnergyDependentSystems();
        }
    }

    void UpdateEnergyDependentSystems()
    {
        if (isEnergyDepleted)
        {
            // Tower completely stops functioning
        }
        else if (isEnergyLow)
        {
            // TODO Optional weaken attacks when energy is low
        }

        if (useTentacleTurret)
        {
            UpdateTentacleEnergyEffects();
        }
    }

    void UpdateTentacleEnergyEffects()
    {
        if (tentacleRenderer == null) return;

        float energyPercentage = GetEnergyPercentage();

        Color baseColor = tentacleColor;
        if (isEnergyDepleted)
        {
            baseColor = Color.Lerp(baseColor, EnergyManager.Instance.depletedEnergyColor, 0.7f);
        }
        else if (isEnergyLow)
        {
            baseColor = Color.Lerp(baseColor, EnergyManager.Instance.criticalEnergyColor, 0.5f);
        }

        if (tentacleRenderer.material != null)
        {
            tentacleRenderer.material.color = baseColor;
        }

        tentacleSwaySpeed = Mathf.Lerp(0.5f, 2f, energyPercentage);
        tentacleSwayAmount = Mathf.Lerp(0.05f, 0.1f, energyPercentage);
    }

    void UpdateEnergyVisuals()
    {
        if (spriteRenderer == null || EnergyManager.Instance == null) return;
        // Use EnergyManager's common visual update method
        EnergyManager.Instance.UpdateConsumerVisuals(this, spriteRenderer);
    }
    #endregion

    #region Public Accessors
    public void SetDamage(float newDamage) => damage = newDamage;
    public void SetRange(float newRange)
    {
        range = newRange;
        SetupRangeCollider();
    }
    public void SetFireRate(float newFireRate) => fireRate = newFireRate;
    public float GetDamage() => damage;
    public float GetRange() => range;
    public float GetFireRate() => fireRate;
    public int GetCost() => cost;
    public TowerType GetTowerType() => towerType;
    #endregion

    #region Cleanup
    void OnDestroy()
    {
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.UnregisterEnergyConsumer(this);
        }

        if (energyBar != null)
        {
            Destroy(energyBar);
        }
    }
    #endregion

#if UNITY_EDITOR
    #region Editor Gizmos
    void OnDrawGizmosSelected()
    {
        // Draw projectile range circle
        Gizmos.color = Color.red;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, projectileRange);

        // Draw melee range circle
        Gizmos.color = Color.yellow;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, meleeRange);

        // Draw line to current target
        if (currentTarget != null)
        {
            float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);
            if (distanceToTarget <= meleeRange)
            {
                Gizmos.color = Color.yellow;
            }
            else
            {
                Gizmos.color = Color.red;
            }
            Gizmos.DrawLine(transform.position, currentTarget.transform.position);
        }

        // Draw tentacle preview in editor
        if (useTentacleTurret && Application.isPlaying && tentaclePoints != null)
        {
            Gizmos.color = tentacleColor;
            for (int i = 0; i < tentaclePoints.Length - 1; i++)
            {
                Vector3 worldPos1 = transform.TransformPoint(tentaclePoints[i]);
                Vector3 worldPos2 = transform.TransformPoint(tentaclePoints[i + 1]);
                Gizmos.DrawLine(worldPos1, worldPos2);
            }

            if (firePoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(firePoint.position, 0.1f);
            }
        }

        // Draw swipe arc preview
        if (useTentacleTurret && Application.isPlaying && isSwipingMelee)
        {
            Gizmos.color = Color.magenta;
            Vector3 tentacleBase = transform.position + (Vector3)tentacleAttachmentOffset;
            float currentRotation = turretTransform.eulerAngles.z;

            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                float arcAngle = Mathf.Lerp(-swipeArcDegrees / 2f, swipeArcDegrees / 2f, t);
                float totalAngle = (currentRotation + arcAngle) * Mathf.Deg2Rad;
                Vector3 arcPoint = tentacleBase + new Vector3(Mathf.Cos(totalAngle), Mathf.Sin(totalAngle)) * tentacleLength;

                if (i > 0)
                {
                    float prevT = (i - 1) / 10f;
                    float prevArcAngle = Mathf.Lerp(-swipeArcDegrees / 2f, swipeArcDegrees / 2f, prevT);
                    float prevTotalAngle = (currentRotation + prevArcAngle) * Mathf.Deg2Rad;
                    Vector3 prevArcPoint = tentacleBase + new Vector3(Mathf.Cos(prevTotalAngle), Mathf.Sin(prevTotalAngle)) * tentacleLength;

                    Gizmos.DrawLine(prevArcPoint, arcPoint);
                }
            }
        }

        // Draw energy bar above tower
        Vector3 energyBarPos = transform.position + Vector3.up * 1.5f;
        float energyBarWidth = 1f;
        float energyBarHeight = 0.1f;

        // Background bar
        Gizmos.color = Color.black;
        Gizmos.DrawCube(energyBarPos, new Vector3(energyBarWidth, energyBarHeight, 0.1f));

        // Energy bar
        float energyPercentage = GetEnergyPercentage();
        Color energyColor = EnergyManager.Instance.GetEnergyColor(this);

        Gizmos.color = energyColor;
        Vector3 energySize = new Vector3(energyBarWidth * energyPercentage, energyBarHeight * 0.8f, 0.1f);
        Vector3 energyPos = energyBarPos + Vector3.left * (energyBarWidth * (1f - energyPercentage) * 0.5f);
        Gizmos.DrawCube(energyPos, energySize);

        // Energy text
        UnityEditor.Handles.Label(energyBarPos + Vector3.up * 0.3f, $"Energy: {currentEnergy:F1}/{maxEnergy:F1}");
    }
    #endregion
#endif
}