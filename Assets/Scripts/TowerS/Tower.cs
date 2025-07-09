using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Tower : MonoBehaviour
{
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
    public float spriteScale = 0.7f;

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
    public float tentacleLength = 1.2f;
    public float tentacleWidth = 0.3f;
    public int tentacleSegments = 8;
    public float tentacleSwayAmount = 0.1f;
    public float tentacleSwaySpeed = 2f;
    public Color tentacleColor = new Color(0.337f, 0.176f, 0.259f, 0.8f); // Purple color #562d42
    public Color tentacleTipColor = new Color(0.8f, 0.3f, 0.3f, 1f);
    public float tentacleAnimationSpeed = 1f;
    public Vector2 tentacleAttachmentOffset = new Vector2(0f, -0.3f); // Lower attachment point
    public float downwardShorteningFactor = 0.5f; // 0=no shorten, 1=full shorten when pointing down

    [Header("Melee Attack Settings")]
    public float meleeRange = 1.2f; // Should match tentacleLength
    public float meleeDamageMultiplier = 1.5f; // Bonus damage for melee attacks
    public float meleeAttackDuration = 0.3f; // How long the melee attack lasts
    public float projectileRange = 5f; // Extended range for projectile attacks

    // Private variables
    private float lastFireTime;
    private GameObject currentTarget;
    private List<GameObject> enemiesInRange = new List<GameObject>();
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D rangeCollider;
    private TowerSlot parentSlot;

    private float targetAngle;
    private float currentAngle;
    private bool hasValidTarget = false;

    [Header("Rotation Settings")]
    public float rotationSpeed = 180f;
    public bool smoothRotation = true;
    public Transform turretTransform;

    // Tentacle system
    private LineRenderer tentacleRenderer;
    private GameObject tentacleContainer;
    private Vector3[] tentaclePoints;
    private float tentacleSwayTimer;
    private bool isFiring = false;
    private float fireAnimationTimer = 0f;
    private bool isMeleeAttacking = false;
    private float meleeAttackTimer = 0f;
    private AttackType currentAttackType = AttackType.None;

    private enum AttackType
    {
        None,
        Melee,
        Projectile
    }

    // Properties
    public bool CanUpgrade => canUpgrade && upgradeLevel < maxUpgradeLevel && upgradeTowerPrefab != null;
    public float NextFireTime => lastFireTime + (1f / fireRate);
    public bool CanFire => Time.time >= NextFireTime;

    public enum TowerType
    {
        Basic,
        Artillery,
        Laser,
        Ice,
        Poison
    }

    void Awake()
    {
        InitializeTower();
    }

    void Start()
    {
        Application.targetFrameRate = 0;
        QualitySettings.vSyncCount = 0;
        LoadTowerSprite();
        SetupRangeCollider();
        SetupTentacleTurret();
        parentSlot = GetComponentInParent<TowerSlot>();

        // Ensure projectile range is properly set
        if (projectileRange <= meleeRange)
        {
            projectileRange = tentacleLength * 3f; // Increased multiplier for extended range
        }
    }

    void Update()
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

        if (smoothRotation && hasValidTarget)
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

    void SetupTentacleTurret()
    {
        if (!useTentacleTurret) return;

        // Create tentacle container
        tentacleContainer = new GameObject("TentacleContainer");
        tentacleContainer.transform.SetParent(transform);
        // Place at offset; z-order managed via sortingOrder
        tentacleContainer.transform.localPosition = tentacleAttachmentOffset;

        // Setup LineRenderer for tentacle
        tentacleRenderer = tentacleContainer.AddComponent<LineRenderer>();
        tentacleRenderer.material = CreateTentacleMaterial();
        tentacleRenderer.startWidth = tentacleWidth;
        tentacleRenderer.endWidth = tentacleWidth * 0.3f;
        tentacleRenderer.positionCount = tentacleSegments;
        tentacleRenderer.useWorldSpace = false;

        // Ensure the tentacle is rendered behind the tower sprite but above the player
        if (spriteRenderer != null)
        {
            // Use same sorting layer as tower
            tentacleRenderer.sortingLayerName = spriteRenderer.sortingLayerName;
            // Place just behind the tower's sprite order
            tentacleRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
        }

        // Initialize tentacle points
        tentaclePoints = new Vector3[tentacleSegments];
        for (int i = 0; i < tentacleSegments; i++)
        {
            float t = (float)i / (tentacleSegments - 1);
            tentaclePoints[i] = Vector3.right * (tentacleLength * t);
        }

        // Set turret transform to tentacle container
        turretTransform = tentacleContainer.transform;

        // Setup fire point at tentacle tip
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



    void UpdateTentacle()
    {
        if (!useTentacleTurret || tentacleRenderer == null)
            return;

        // Advance sway timer
        tentacleSwayTimer += Time.deltaTime * tentacleSwaySpeed;

        // Update fire animation
        if (isFiring)
        {
            fireAnimationTimer += Time.deltaTime * tentacleAnimationSpeed;
            if (fireAnimationTimer >= 1f)
            {
                isFiring = false;
                fireAnimationTimer = 0f;
            }
        }

        // Update melee attack animation
        if (isMeleeAttacking)
        {
            meleeAttackTimer += Time.deltaTime;
            if (meleeAttackTimer >= meleeAttackDuration)
            {
                isMeleeAttacking = false;
                meleeAttackTimer = 0f;
            }
        }

        // Calculate tentacle curve
        for (int i = 0; i < tentacleSegments; i++)
        {
            float t = (float)i / (tentacleSegments - 1);
            // Base local position
            Vector3 basePos = Vector3.right * (tentacleLength * t);
            // Add organic sway
            float swayOffset = Mathf.Sin(tentacleSwayTimer + t * Mathf.PI) * tentacleSwayAmount * t;
            basePos.y += swayOffset;

            // Determine world-space direction of this segment
            Vector3 worldDir = turretTransform.TransformDirection(basePos.normalized);
            if (worldDir.y < 0f)
            {
                // More downward (y→–1) yields stronger shortening
                float shorten = Mathf.Clamp01(1f - downwardShorteningFactor * -worldDir.y);
                basePos.x *= shorten;
                basePos.y *= shorten;
            }

            // Add firing animation influence
            if (isFiring)
            {
                float fi = Mathf.Sin(fireAnimationTimer * Mathf.PI);
                basePos.x += fi * 0.3f * t;
                basePos.y *= (1f - fi * 0.5f);
            }

            // Add melee attack animation
            if (isMeleeAttacking)
            {
                float mi = Mathf.Sin((meleeAttackTimer / meleeAttackDuration) * Mathf.PI);
                basePos.x += mi * 0.5f * t;
                float whip = Mathf.Sin(mi * Mathf.PI * 2f) * 0.3f * t;
                basePos.y += whip;
            }

            // Curve toward target
            if (hasValidTarget && currentTarget != null)
            {
                Vector3 tgtDir = transform.InverseTransformDirection(
                    (currentTarget.transform.position - transform.position).normalized);
                basePos += tgtDir * (t * 0.2f);
            }

            tentaclePoints[i] = basePos;
        }

        // Apply points
        tentacleRenderer.SetPositions(tentaclePoints);
        // Move fire point to tip
        if (firePoint != null)
        {
            firePoint.position = tentacleContainer.transform.TransformPoint(
                tentaclePoints[tentaclePoints.Length - 1]);
        }

        // Update color gradient
        Gradient gradient = new Gradient();
        if (isFiring || isMeleeAttacking)
        {
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(tentacleColor, 0), new GradientColorKey(tentacleTipColor, 1) },
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


    void UpdateTargetAngle()
    {
        if (currentTarget == null) return;
        Vector2 direction = (currentTarget.transform.position - transform.position).normalized;
        targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // Normalize angle to -180 to 180
        while (targetAngle > 180f) targetAngle -= 360f;
        while (targetAngle < -180f) targetAngle += 360f;
    }

    void UpdateSmoothRotation()
    {
        if (!hasValidTarget) return;

        // Calculate the shortest angular distance
        float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);

        // Rotate towards target
        float rotationStep = rotationSpeed * Time.deltaTime;
        if (Mathf.Abs(angleDifference) <= rotationStep)
        {
            currentAngle = targetAngle;
        }
        else
        {
            currentAngle += Mathf.Sign(angleDifference) * rotationStep;
        }

        // Normalize the current angle
        while (currentAngle > 180f) currentAngle -= 360f;
        while (currentAngle < -180f) currentAngle += 360f;

        // Rotate only the turret (tentacle), not the entire tower
        if (turretTransform != null)
        {
            turretTransform.rotation = Quaternion.AngleAxis(currentAngle, Vector3.forward);
        }
    }

    void InitializeTower()
    {
        // Add SpriteRenderer if not present
        if (GetComponent<SpriteRenderer>() == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        else
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        // Set sorting order to appear above the slots and terrain
        spriteRenderer.sortingOrder = 20;
        spriteRenderer.sortingLayerName = "Default";
        transform.localScale = Vector3.one * spriteScale;

        // Add range collider for enemy detection
        rangeCollider = gameObject.AddComponent<CircleCollider2D>();
        rangeCollider.radius = range;
        rangeCollider.isTrigger = true;

        Debug.Log($"Tower initialized with sorting order: {spriteRenderer.sortingOrder}");
    }

    bool IsAimedAtTarget()
    {
        if (!smoothRotation) return true;

        float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
        return Mathf.Abs(angleDifference) <= 5f;
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
                StartCoroutine(AnimateSprite(sprites));
            }
        }
        else
        {
            Debug.LogWarning($"Sprite index {spriteIndex} is out of range. Using first sprite.");
            spriteRenderer.sprite = sprites[0];
        }
    }

    System.Collections.IEnumerator AnimateSprite(Sprite[] sprites)
    {
        if (!enableAnimation || sprites == null || sprites.Length == 0)
            yield break;

        int frameCount = Mathf.Min(animationFrameCount, sprites.Length - spriteIndex);
        int currentFrame = 0;
        float timer = 0f;

        while (true)
        {
            timer += Time.deltaTime;
            if (timer > animationSpeed * 2f)
                timer = animationSpeed * 2f;

            if (timer >= animationSpeed)
            {
                timer -= animationSpeed;
                currentFrame = (currentFrame + 1) % frameCount;
                int idx = spriteIndex + currentFrame;
                if (idx < sprites.Length)
                    spriteRenderer.sprite = sprites[idx];
            }
            yield return null;
        }
    }

    void SetupRangeCollider()
    {
        if (rangeCollider != null)
        {
            // Set range to the maximum of projectile range for detection
            rangeCollider.radius = Mathf.Max(range, projectileRange);
        }
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
        return distance <= projectileRange; // Use projectile range as max targeting distance
    }

    void FireAtTarget()
    {
        if (currentTarget == null) return;

        // Determine attack type based on distance
        float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);

        if (distanceToTarget <= meleeRange)
        {
            // Melee attack
            currentAttackType = AttackType.Melee;
            PerformMeleeAttack();
        }
        else if (distanceToTarget <= projectileRange)
        {
            // Projectile attack
            currentAttackType = AttackType.Projectile;
            PerformProjectileAttack();
        }

        lastFireTime = Time.time;
    }

    void PerformMeleeAttack()
    {
        // Trigger melee attack animation
        if (useTentacleTurret)
        {
            isMeleeAttacking = true;
            meleeAttackTimer = 0f;
        }

        // Deal enhanced melee damage directly
        if (currentTarget != null)
        {
            Health targetHealth = currentTarget.GetComponent<Health>();
            if (targetHealth != null)
            {
                float meleeDamage = damage * meleeDamageMultiplier;
                targetHealth.TakeDamage(meleeDamage);
                Debug.Log($"Melee attack dealt {meleeDamage} damage to {currentTarget.name}");
            }
        }
    }

    void PerformProjectileAttack()
    {
        // Trigger projectile firing animation
        if (useTentacleTurret)
        {
            isFiring = true;
            fireAnimationTimer = 0f;
        }

        // Fire projectile from tentacle tip
        if (projectilePrefab != null)
        {
            FireProjectile();
        }
        else
        {
            // Fallback to direct damage if no projectile
            DealDirectDamage();
        }
    }

    void FireProjectile()
    {
        // Use the dynamically updated fire point position (tentacle tip)
        Vector3 spawnPosition = firePoint.position;

        // Calculate direction from tentacle tip to target
        Vector3 direction = (currentTarget.transform.position - spawnPosition).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion projectileRotation = Quaternion.AngleAxis(angle, Vector3.forward);

        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, projectileRotation);
        Projectile projScript = projectile.GetComponent<Projectile>();
        if (projScript != null)
        {
            projScript.Initialize(currentTarget, damage, range);
        }

        Debug.Log($"Projectile fired from tentacle tip at {spawnPosition}");
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

    bool IsValidTarget(GameObject target)
    {
        return ((1 << target.layer) & targetLayer) != 0;
    }

    public void UpgradeTower()
    {
        if (!CanUpgrade) return;

        TowerSlot slot = GetComponentInParent<TowerSlot>();
        if (slot == null) return;

        slot.RemoveTower();
        GameObject upgradedTower = Instantiate(upgradeTowerPrefab.gameObject, slot.transform.position, Quaternion.identity);
        upgradedTower.transform.parent = slot.transform;
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

#if UNITY_EDITOR
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
                Gizmos.color = Color.yellow; // Melee range
            }
            else
            {
                Gizmos.color = Color.red; // Projectile range
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

            // Draw fire point
            if (firePoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(firePoint.position, 0.1f);
            }
        }
    }
#endif

    // Public methods for external access
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
}