using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;

// ===== TOWER TARGETING SYSTEM =====
public class TowerTargeting
{


    private readonly Tower tower;
    private readonly List<GameObject> enemiesInRange = new List<GameObject>();

    private GameObject currentTarget;
    private float targetAngle, currentAngle;
    private bool hasValidTarget;

    public GameObject CurrentTarget => currentTarget;
    public bool HasValidTarget => hasValidTarget;

    public TowerTargeting(Tower tower)
    {
        this.tower = tower;
    }

    public void UpdateTargeting()
    {
        if (currentTarget == null || !IsTargetValid(currentTarget))
        {
            FindTarget();
            hasValidTarget = currentTarget != null;
        }

        if (hasValidTarget)
        {
            UpdateTargetAngle();
            // Only update rotation if not using tentacles or tentacles are not swiping
            if (tower.smoothRotation && !IsSwipingMelee())
                UpdateSmoothRotation();
        }
    }

    bool IsSwipingMelee()
    {
        // Check if tentacle system exists and is swiping
        // We'll let the tentacle system handle its own rotation during swipe attacks
        return false; // For now, let tentacle system handle all rotation
    }

    void FindTarget()
    {
        enemiesInRange.RemoveAll(enemy => enemy == null || !IsTargetValid(enemy));
        currentTarget = GetClosestValidTarget();
    }

    GameObject GetClosestValidTarget()
    {
        GameObject closest = null;
        float closestDistance = float.MaxValue;

        foreach (var enemy in enemiesInRange)
        {
            float distance = Vector2.Distance(tower.transform.position, enemy.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = enemy;
            }
        }
        return closest;
    }

    bool IsTargetValid(GameObject target)
    {
        if (target == null) return false;
        float distance = Vector2.Distance(tower.transform.position, target.transform.position);
        bool inRange = distance <= tower.ProjectileRange;
        bool validLayer = ((1 << target.layer) & tower.targetLayer) != 0;

        // Debug range issues more frequently
        if (!inRange && Time.frameCount % 30 == 0) // Every half second at 60fps
        {
            //Debug.Log($"Target {target.name} OUT OF RANGE: distance={distance:F2}, max range={tower.ProjectileRange:F2}");
        }
        else if (inRange && Time.frameCount % 60 == 0) // Every second when in range
        {
            //Debug.Log($"Target {target.name} IN RANGE: distance={distance:F2}, max range={tower.ProjectileRange:F2}");
        }

        return inRange && validLayer;
    }

    void UpdateTargetAngle()
    {
        if (currentTarget == null) return;
        Vector2 direction = (currentTarget.transform.position - tower.transform.position).normalized;
        targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        targetAngle = Mathf.Repeat(targetAngle + 180f, 360f) - 180f;
    }

    void UpdateSmoothRotation()
    {
        float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
        float rotationStep = tower.rotationSpeed * Time.deltaTime;

        currentAngle = Mathf.Abs(angleDifference) <= rotationStep ?
            targetAngle :
            currentAngle + Mathf.Sign(angleDifference) * rotationStep;

        currentAngle = Mathf.Repeat(currentAngle + 180f, 360f) - 180f;

        // Apply rotation to tentacle system
        var tentacleTransform = tower.FirePoint?.parent;
        if (tentacleTransform != null)
            tentacleTransform.rotation = Quaternion.AngleAxis(currentAngle, Vector3.forward);
    }

    public void OnTriggerEnter(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & tower.targetLayer) != 0)
        {
            enemiesInRange.Add(other.gameObject);
            //Debug.Log($"Enemy {other.gameObject.name} entered range. Total enemies: {enemiesInRange.Count}");
        }
    }

    public void OnTriggerExit(Collider2D other)
    {
        if (enemiesInRange.Remove(other.gameObject))
        {
            //Debug.Log($"Enemy {other.gameObject.name} left range. Total enemies: {enemiesInRange.Count}");
            if (currentTarget == other.gameObject)
                currentTarget = null;
        }
    }
}

// ===== TOWER COMBAT SYSTEM =====
public class TowerCombat
{
    //[SerializeField] private EventReference shotSound;
    private readonly Tower tower;

    public TowerCombat(Tower tower)
    {
        this.tower = tower;
    }

    public void TryFire(GameObject target)
    {
        if (target != null && tower.CanFire)
        {
            //Debug.Log($"Tower {tower.towerName} attempting to fire at {target.name}. CanFire: {tower.CanFire}");
            tower.FireAtTarget(target);
        }
        else if (target != null && !tower.CanFire)
        {
            float timeUntilNextFire = tower.LastFireTime + (1f / tower.GetFireRate()) - Time.time;
            //Debug.Log($"Tower {tower.towerName} can't fire yet. Next fire in: {timeUntilNextFire:F2}s");
        }
    }

    public void PerformMeleeAttack(GameObject target)
    {
        //Debug.Log($"Tower {tower.towerName} performing melee attack on {target.name}");
        tower.TriggerMeleeAttack();

        var targetHealth = target?.GetComponent<Health>();
        if (targetHealth != null)
        {
            float meleeDamage = tower.damage * tower.meleeConfig.damageMultiplier;
            targetHealth.TakeDamage(meleeDamage);
            //Debug.Log($"Melee attack dealt {meleeDamage} damage to {target.name}");
        }
    }

    public void PerformProjectileAttack(GameObject target)
    {
        //Debug.Log($"Tower {tower.towerName} performing projectile attack on {target.name}");
        tower.TriggerProjectileAttack();

        if (tower.projectilePrefab != null)
        {
            FireProjectile(target);
        }
        else
        {
            //Debug.LogWarning($"Tower {tower.towerName} has no projectile prefab, using direct damage");
            DealDirectDamage(target);
        }
    }

    void FireProjectile(GameObject target)
    {
        //AudioManager.instance.PlayOneShot(FMODEvents.instance.shotSound, tower.FirePoint.position);
        AudioManager.instance.PlayOneShot(FMODEvents.instance.multiShotSound, tower.FirePoint.position);

        Vector3 spawnPosition = tower.FirePoint != null ? tower.FirePoint.position : tower.transform.position;
        Vector3 direction = (target.transform.position - spawnPosition).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        //Debug.Log($"Firing projectile from {spawnPosition} towards {target.transform.position}");
        //Debug.Log($"Fire point is {(tower.FirePoint != null ? "valid" : "NULL")}");

        var projectile = Object.Instantiate(tower.projectilePrefab, spawnPosition, Quaternion.AngleAxis(angle, Vector3.forward));
        var projScript = projectile.GetComponent<Projectile>();
        if (projScript != null)
        {
            projScript.Initialize(target, tower.damage, tower.range);
            //Debug.Log($"Projectile initialized successfully");
        }
        else
        {
            //Debug.LogWarning($"Projectile prefab {tower.projectilePrefab.name} missing Projectile component!");
        }
    }



    void DealDirectDamage(GameObject target) => target?.GetComponent<Health>()?.TakeDamage(tower.damage);
}

// ===== TOWER VISUALS SYSTEM =====
public class TowerVisuals
{
    private readonly Tower tower;
    private readonly SpriteRenderer spriteRenderer;

    public TowerVisuals(Tower tower, SpriteRenderer spriteRenderer)
    {
        this.tower = tower;
        this.spriteRenderer = spriteRenderer;
    }

    public void LoadSprite()
    {
        if (spriteRenderer == null)
        {
            Debug.LogWarning("TowerVisuals: SpriteRenderer is null, cannot load sprite");
            return;
        }

        var sprites = Resources.LoadAll<Sprite>(tower.spriteResourcePath);
        if (sprites?.Length > tower.spriteIndex)
        {
            spriteRenderer.sprite = sprites[tower.spriteIndex];
            if (tower.enableAnimation && sprites.Length > 1)
            {
                tower.StartCoroutine(Utilities.AnimateSprite(
                    spriteRenderer, sprites, tower.enableAnimation,
                    tower.animationFrameCount, tower.spriteIndex, tower.animationSpeed));
            }
        }
        else
        {
            Debug.LogWarning($"TowerVisuals: Could not find sprite at index {tower.spriteIndex} in {tower.spriteResourcePath}");
        }
    }
}

// ===== TOWER ENERGY SYSTEM =====
public class TowerEnergy
{
    private readonly Tower tower;
    private EnergyBar energyBar;

    public TowerEnergy(Tower tower)
    {
        this.tower = tower;
    }

    public bool UpdateEnergyState()
    {
        tower.UpdateEnergyState();
        return tower.GetEnergyPercentage() > (EnergyManager.Instance?.GetTowerDeadThreshold() ?? 0.05f);
    }

    public void SetupEnergyBar()
    {
        if (!tower.showEnergyBar) return;

        energyBar = tower.gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = tower.showEnergyBar;
        energyBar.energyBarHeight = 0.1f;
        energyBar.energyBarWidth = 1f;
        energyBar.energyBarOffset = 1.5f;
        energyBar.showEnergyText = true;

        if (EnergyManager.Instance != null)
        {
            energyBar.SetColors(
                EnergyManager.Instance.normalColor,
                EnergyManager.Instance.lowEnergyColor,
                EnergyManager.Instance.criticalEnergyColor,
                EnergyManager.Instance.depletedEnergyColor
            );
        }

        var spriteRenderer = tower.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            energyBar.Initialize(tower, spriteRenderer);
        }
        else
        {
            Debug.LogWarning("TowerEnergy: SpriteRenderer not found, energy bar may not display correctly");
        }
    }

    public void UpdateVisuals()
    {
        var spriteRenderer = tower.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && EnergyManager.Instance != null)
            EnergyManager.Instance.UpdateConsumerVisuals(tower, spriteRenderer);
    }
}

// ===== TOWER TENTACLE SYSTEM =====
public class TowerTentacleSystem
{
    private readonly Tower tower;
    private readonly Tower.TentacleConfig config;
    private readonly Tower.MeleeConfig meleeConfig;

    // Components
    private LineRenderer tentacleRenderer;
    private GameObject tentacleContainer;
    private Vector3[] tentaclePoints;
    private Transform firePoint;

    // Animation state
    private float swayTimer;
    private bool isFiring, isMeleeAttacking, isSwipingMelee;
    private float fireAnimationTimer, meleeAttackTimer, swipeTimer;

    public Transform FirePoint => firePoint;

    public TowerTentacleSystem(Tower tower, Tower.TentacleConfig config, Tower.MeleeConfig meleeConfig)
    {
        this.tower = tower;
        this.config = config;
        this.meleeConfig = meleeConfig;
    }

    public void Initialize()
    {
        CreateTentacleContainer();
        SetupLineRenderer();
        CreateFirePoint();
        InitializeTentaclePoints();
    }

    void CreateTentacleContainer()
    {
        tentacleContainer = new GameObject("TentacleContainer");
        tentacleContainer.transform.SetParent(tower.transform);
        tentacleContainer.transform.localPosition = config.attachmentOffset;
    }

    void SetupLineRenderer()
    {
        tentacleRenderer = tentacleContainer.AddComponent<LineRenderer>();
        tentacleRenderer.material = CreateTentacleMaterial();
        tentacleRenderer.startWidth = config.width;
        tentacleRenderer.endWidth = config.width * 0.3f;
        tentacleRenderer.positionCount = config.segments;
        tentacleRenderer.useWorldSpace = false;

        // Safely get SpriteRenderer reference
        var spriteRenderer = tower.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            tentacleRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
            tentacleRenderer.sortingOrder = spriteRenderer.sortingOrder - 4;
        }
        else
        {
            // Fallback values if SpriteRenderer isn't ready yet
            tentacleRenderer.sortingLayerName = "Default";
            tentacleRenderer.sortingOrder = 16;
        }
    }

    Material CreateTentacleMaterial()
    {
        var material = new Material(Shader.Find("Sprites/Default"));
        material.color = config.color;
        return material;
    }

    void CreateFirePoint()
    {
        var firePointObj = new GameObject("FirePoint");
        firePointObj.transform.SetParent(tentacleContainer.transform);
        firePointObj.transform.localPosition = Vector3.right * config.length;
        firePoint = firePointObj.transform;
    }

    void InitializeTentaclePoints()
    {
        tentaclePoints = new Vector3[config.segments];
        for (int i = 0; i < config.segments; i++)
        {
            float t = (float)i / (config.segments - 1);
            tentaclePoints[i] = Vector3.right * (config.length * t);
        }
    }

    public void UpdateTentacle(bool hasValidTarget, GameObject currentTarget)
    {
        UpdateTimers();
        UpdateTentacleShape(hasValidTarget, currentTarget);
        UpdateTentacleVisuals();
        UpdateFirePointPosition();
    }

    void UpdateTimers()
    {
        swayTimer += Time.deltaTime * config.swaySpeed;

        if (isFiring)
        {
            fireAnimationTimer += Time.deltaTime * config.animationSpeed;
            if (fireAnimationTimer >= 1f)
            {
                isFiring = false;
                fireAnimationTimer = 0f;
            }
        }

        if (isMeleeAttacking)
        {
            meleeAttackTimer += Time.deltaTime;
            if (meleeAttackTimer >= meleeConfig.attackDuration)
            {
                isMeleeAttacking = false;
                meleeAttackTimer = 0f;
            }
        }

        if (isSwipingMelee)
        {
            swipeTimer += Time.deltaTime * meleeConfig.swipeSpeed;
            if (swipeTimer >= 1f)
            {
                isSwipingMelee = false;
                swipeTimer = 0f;
            }
        }
    }

    void UpdateTentacleShape(bool hasValidTarget, GameObject currentTarget)
    {
        for (int i = 0; i < config.segments; i++)
        {
            float t = (float)i / (config.segments - 1);
            Vector3 basePos = Vector3.right * (config.length * t);

            // Apply sway
            float swayOffset = Mathf.Sin(swayTimer + t * Mathf.PI) * config.swayAmount * t;
            basePos.y += swayOffset;

            // Apply downward shortening
            ApplyDownwardShortening(ref basePos);

            // Apply animation effects
            ApplyAnimationEffects(ref basePos, t);

            // Apply targeting
            if (hasValidTarget && currentTarget != null && !isSwipingMelee)
                ApplyTargeting(ref basePos, currentTarget, t);

            tentaclePoints[i] = basePos;
        }

        tentacleRenderer.SetPositions(tentaclePoints);
    }

    void ApplyDownwardShortening(ref Vector3 basePos)
    {
        Vector3 worldDir = tentacleContainer.transform.TransformDirection(basePos.normalized);
        if (worldDir.y < 0f)
        {
            float shorten = Mathf.Clamp01(1f - config.downwardShorteningFactor * -worldDir.y);
            basePos.x *= shorten;
            basePos.y *= shorten;
        }
    }

    void ApplyAnimationEffects(ref Vector3 basePos, float t)
    {
        if (isFiring)
        {
            float fi = Mathf.Sin(fireAnimationTimer * Mathf.PI);
            basePos.x += fi * 0.3f * t;
            basePos.y *= (1f - fi * 0.5f);
        }

        if (isMeleeAttacking)
        {
            float mi = Mathf.Sin((meleeAttackTimer / meleeConfig.attackDuration) * Mathf.PI);
            basePos.x += mi * 0.5f * t;
            float whip = Mathf.Sin(mi * Mathf.PI * 2f) * 0.3f * t;
            basePos.y += whip;
        }

        if (isSwipingMelee)
            ApplySwipeEffect(ref basePos, t);
    }

    void ApplySwipeEffect(ref Vector3 basePos, float t)
    {
        float swipeProgress = meleeConfig.swipeCurve.Evaluate(swipeTimer);
        float swipeAngle = Mathf.Lerp(-meleeConfig.swipeArcDegrees / 2f, meleeConfig.swipeArcDegrees / 2f, swipeProgress);
        float swipeAngleRad = swipeAngle * Mathf.Deg2Rad;
        float swipeExtension = Mathf.Sin(swipeProgress * Mathf.PI) * meleeConfig.swipeReach;

        basePos.x += swipeExtension * t;

        float radius = basePos.magnitude;
        float currentAngleRad = Mathf.Atan2(basePos.y, basePos.x);
        float newAngleRad = currentAngleRad + (swipeAngleRad * meleeConfig.swipeIntensity * t);

        basePos.x = Mathf.Cos(newAngleRad) * radius;
        basePos.y = Mathf.Sin(newAngleRad) * radius;

        float whipEffect = Mathf.Sin(swipeProgress * Mathf.PI * 2f) * 0.2f * t;
        basePos.y += whipEffect;
    }

    void ApplyTargeting(ref Vector3 basePos, GameObject currentTarget, float t)
    {
        Vector3 targetDirection = tower.transform.InverseTransformDirection(
            (currentTarget.transform.position - tower.transform.position).normalized);
        basePos += targetDirection * (t * 0.2f);
    }

    void UpdateTentacleVisuals()
    {
        var gradient = new Gradient();

        if (isFiring || isMeleeAttacking || isSwipingMelee)
        {
            Color tipColor = isSwipingMelee ? Color.Lerp(config.tipColor, Color.white, 0.3f) : config.tipColor;
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(config.color, 0), new GradientColorKey(tipColor, 1) },
                new GradientAlphaKey[] { new GradientAlphaKey(config.color.a, 0), new GradientAlphaKey(1, 1) }
            );
        }
        else
        {
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(config.color, 0), new GradientColorKey(config.color, 1) },
                new GradientAlphaKey[] { new GradientAlphaKey(config.color.a, 0), new GradientAlphaKey(config.color.a, 1) }
            );
        }

        tentacleRenderer.colorGradient = gradient;
    }

    void UpdateFirePointPosition()
    {
        if (firePoint != null && tentaclePoints.Length > 0)
        {
            // CRITICAL: Update fire point to the world position of the tentacle tip EVERY FRAME
            Vector3 localTipPosition = tentaclePoints[tentaclePoints.Length - 1];
            Vector3 worldTipPosition = tentacleContainer.transform.TransformPoint(localTipPosition);
            firePoint.position = worldTipPosition;

            // Debug fire point position occasionally
            if (Time.frameCount % 120 == 0) // Every 2 seconds at 60fps
            {
                //Debug.Log($"Fire point world position: {worldTipPosition}, Local tip: {localTipPosition}");
            }
        }
        else if (firePoint == null)
        {
            //Debug.LogWarning("TentacleSystem: Fire point is null!");
        }
    }

    public void StartMeleeAttack()
    {
        isMeleeAttacking = true;
        meleeAttackTimer = 0f;
        isSwipingMelee = true;
        swipeTimer = 0f;
    }

    public void StartProjectileAttack()
    {
        isFiring = true;
        fireAnimationTimer = 0f;
    }

    public void UpdateEnergyEffects(float energyPercentage, bool isEnergyDepleted, bool isEnergyLow)
    {
        if (tentacleRenderer?.material == null) return;

        Color baseColor = config.color;
        var energyManager = EnergyManager.Instance;

        if (isEnergyDepleted)
            baseColor = Color.Lerp(baseColor, energyManager.depletedEnergyColor, 0.7f);
        else if (isEnergyLow)
            baseColor = Color.Lerp(baseColor, energyManager.criticalEnergyColor, 0.5f);

        tentacleRenderer.material.color = baseColor;

        // Adjust movement based on energy
        config.swaySpeed = Mathf.Lerp(0.5f, 2f, energyPercentage);
        config.swayAmount = Mathf.Lerp(0.05f, 0.1f, energyPercentage);
    }

    public void Cleanup()
    {
        if (tentacleContainer != null)
            Object.DestroyImmediate(tentacleContainer);
    }
}