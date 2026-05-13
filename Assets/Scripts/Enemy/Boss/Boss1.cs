using UnityEngine;
using System.Collections;

/// Boss1 Features: Laser attack, Detachable Head armor system.
/// Melee attacks and movement are handled by EnemyController.
/// Melee hit timing is driven by EnemyData.hitFrame through the unified system.

public class Boss1 : BaseBossStats //, IDamageable
{
    [Header("Boss Collider")]
    [SerializeField] private float bossColliderRadius = 4f;
    [SerializeField] private float bossColliderOffsetXFacingRight = 0f;
    [SerializeField] private float bossColliderOffsetXFacingLeft = 0f;
    [SerializeField] private float bossColliderOffsetY = 0f;

    [Header("Health Bar")]
    [SerializeField] private float healthBarXOffset = 0f;
    [SerializeField] private float healthBarYReduction = 1.5f; // how much to lower from sprite top
    private float healthBarYOffset = 0f;

    [Header("Grappling Hook Offsets")]
    [Tooltip("X offset for the grapple attach point (flips with sprite). " +
             "Pivot is already at the visual center — leave 0 unless you want " +
             "the grapple point somewhere specific (e.g. the glowing orb).")]
    [SerializeField] private float grapplePointXOffset = 0f;
    [Tooltip("Y offset for the grapple attach point relative to collider center")]
    [SerializeField] private float grapplePointYOffset = 0f;
    [Tooltip("Extra Y padding above the health bar for the hook indicator icon")]
    [SerializeField] private float hookIndicatorYAboveHealthBar = 0.3f;

    private EnemyAnimationController animController;
    private bool isPerformingLaserAttack = false;

    [Header("Boss1 Configuration")]
    [SerializeField] private float bossMaxArmor = 1000f;
    [SerializeField] private float bossMaxHealth = 1000f;

    [Header("Laser Attack")]
    //private FMOD.Studio.EventInstance? laserChargeSoundInstance;
    private FMOD.Studio.EventInstance laserChargeSoundInstance;
    private bool hasLaserSound = false;
    [SerializeField] private float laserDamagePerSecond = 35f;
    [SerializeField] private float laserRange = 10f;
    [SerializeField] private float laserChargeDuration = 1.1f;
    [SerializeField] private float laserFireDuration = 1.0f;
    [SerializeField] private float laserCooldown = 6f;
    [SerializeField] private Vector2 laserSpawnLocalOffset = new Vector2(0f, 0f);
    [SerializeField] private string laserSpritePath = "Sprites/EnemySprites/LaserBeam";
    [SerializeField] private float laserBeamStartFraction = 0.1056f;
    [SerializeField] private LayerMask laserTargetLayers;
    [SerializeField] private float meleeOnlyRange = 3f;

    private SpriteRenderer bossSprite;
    private SmoothSpriteFlip bossSmoothFlip;

    [Header("Laser Tracking Behavior")]
    [SerializeField] private LaserTrackingMode trackingMode = LaserTrackingMode.DelayedTracking;
    [SerializeField] private float trackingRotationSpeed = 90f;
    [SerializeField] private float trackingDelay = 0.3f;
    [SerializeField] private bool showWarningTelegraph = true;
    [SerializeField] private Color warningLineColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("Laser Audio")]
    [SerializeField] private bool playLaserChargeSound = true;

    public enum LaserTrackingMode
    {
        DelayedTracking,
        LockOnFire,
        PerfectTracking,
        SlowTracking,
        Prediction
    }

    [Header("Attack Behavior")]
    [SerializeField] private float attackRange = 8f;

    [Header("Head System")]
    [SerializeField] private bool spawnDetachableHead = true;
    [SerializeField] private float headSpawnMinDistance = 10f;
    [SerializeField] private float headSpawnMaxDistance = 20f;
    [SerializeField] private string headSpritePath = "Sprites/EnemySprites/Boss1/boss1_head_sprite";
    [SerializeField] private float headMapBoundsMin = -45f;
    [SerializeField] private float headMapBoundsMax = 45f;

    [Header("Health Bar")]
    [SerializeField] private float healthBarExtraYPadding = 0.5f;

    [Header("Disintegration Effect")]
    [SerializeField] private float disintegrationDuration = 1.5f;

    [Header("Boss Physics")]
    [SerializeField] private float bossRigidbodyMass = 100f;
    [SerializeField] private float bossLinearDrag = 5f;

    // Internal state 
    private Transform currentTarget;
    private bool wasFlippedBeforeLaser = false;
    private bool isDying = false;

    // Laser system 
    private GameObject laserObject;
    private SpriteRenderer laserRenderer;
    private Sprite[] laserSprites;
    private bool isLaserActive = false;
    private float lastLaserTime = -999f;
    private Vector3 lockedLaserDirection;
    private Vector3 predictedTargetPosition;
    private float laserMarginCropUnits;

    // Night-mode laser illumination — point lights along the beam
    private System.Collections.Generic.List<GameObject> laserNightLights =
        new System.Collections.Generic.List<GameObject>();

    // Warning telegraph 
    private LineRenderer laserWarningLine;
    private GameObject laserWarningObject;

    // Delayed tracking 
    private System.Collections.Generic.List<PositionSnapshot> positionHistory =
        new System.Collections.Generic.List<PositionSnapshot>();

    [System.Serializable]
    private class PositionSnapshot
    {
        public Vector3 position;
        public float timestamp;
        public PositionSnapshot(Vector3 pos, float time) { position = pos; timestamp = time; }
    }

    // Ground hit sound 
    private bool groundHitSoundPending = false;



    // INITIALIZATION


    protected override void Awake()
    {
        maxArmor = bossMaxArmor;
        maxHealth = enemyData != null ? enemyData.maxHealth : bossMaxHealth;
        base.Awake();

        if (laserTargetLayers == 0)
            laserTargetLayers = ~LayerMask.GetMask("Enemy");
    }
    private void ConfigureBossCollider()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col == null)
            col = gameObject.AddComponent<CircleCollider2D>();

        col.isTrigger = false;
        col.radius = bossColliderRadius;
        UpdateColliderOffset();
    }
    protected override void Start()
    {
        base.Start();
        bossSprite = GetComponent<SpriteRenderer>();
        bossSmoothFlip = GetComponent<SmoothSpriteFlip>();
        if (bossSmoothFlip == null)
            bossSmoothFlip = gameObject.AddComponent<SmoothSpriteFlip>();
        // Minimal mode: skip color writes and motion trail. Boss has
        // other scripts reading flipX (collider offset, health bar, grapple
        // point) and writing color (armor-break flash, damage flash) — a
        // minimal flip avoids stepping on them.
        bossSmoothFlip.SetMinimalMode(true);
        animController = GetComponent<EnemyAnimationController>();
        // Instantiate health bar (same as EnemyStats.Start())
        //if (healthBarPrefab != null)
        //{
        //    GameObject bar = Instantiate(healthBarPrefab);
        // Use reflection or make healthBar accessible — see note below
        //}
        //Debug.Log($"Boss1 tag: '{gameObject.tag}' (should be 'Enemy')");
        //Debug.Log($"Boss1 layer: '{LayerMask.LayerToName(gameObject.layer)}'");
        //var col = GetComponent<Collider2D>();
        //Debug.Log($"Boss1 Collider2D: {(col != null ? col.GetType().Name + " enabled=" + col.enabled : "NONE!")}");
        //Debug.Log($"Boss1 Collider2D: {col.GetType().Name} enabled={col.enabled} isTrigger={col.isTrigger} radius={((CircleCollider2D)col).radius}");

        ConfigureRigidbody();
        ConfigureBossCollider();
        InitializeLaser();
        InitializeBossHealthBar();

        if (spawnDetachableHead)
            SpawnBossHead();
    }

    private void ConfigureRigidbody()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;
        float mass = (enemyData != null) ? enemyData.mass : bossRigidbodyMass;
        rb.mass = Mathf.Max(mass, 50f);
        rb.linearDamping = bossLinearDrag;
    }


    /// Public so GrapplingTarget.Awake() can pull offsets immediately when the component is added dynamically (after Boss1.Start() has already run).

    public void ApplyGrapplingOffsets(GrapplingTarget gt)
    {
        if (gt == null || bossSprite == null) return;

        float xOff = bossSprite.flipX ? -grapplePointXOffset : grapplePointXOffset;
        gt.grapplePointOffset = new Vector3(xOff, grapplePointYOffset, 0f);

        // Place indicator above the health bar
        float indicatorY = healthBarYOffset + hookIndicatorYAboveHealthBar;
        gt.indicatorExtraOffset = new Vector3(0f, indicatorY, 0f);
    }

    private void UpdateGrapplingTargetOffset()
    {
        var gt = GetComponent<GrapplingTarget>();
        if (gt != null) ApplyGrapplingOffsets(gt);
    }

    private void InitializeBossHealthBar()
    {
        if (HealthBar == null)
        {
            Debug.LogWarning("Boss1: HealthBar is null! Make sure healthBarPrefab is assigned.");
            return;
        }

        float totalMaxHealth = maxHealth + maxArmor;
        HealthBar.Initialize(transform, totalMaxHealth);

        // Calculate Y from sprite top, then subtract reduction
        healthBarYOffset = healthBarExtraYPadding;
        if (bossSprite != null && bossSprite.sprite != null)
        {
            Bounds worldBounds = bossSprite.bounds;
            float spriteTopWorld = worldBounds.max.y;
            float bossY = transform.position.y;
            healthBarYOffset = (spriteTopWorld - bossY) + healthBarExtraYPadding - healthBarYReduction;
        }

        UpdateHealthBarOffset();

        Canvas canvas = HealthBar.GetComponentInChildren<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
        }
    }

    private void UpdateHealthBarOffset()
    {
        if (HealthBar == null) return;

        // Flip the X offset when the sprite is flipped
        float xOff = (bossSprite != null && bossSprite.flipX) ? -healthBarXOffset : healthBarXOffset;
        HealthBar.SetOffset(new Vector3(xOff, healthBarYOffset, 0f));
    }
    private void InitializeLaser()
    {
        laserSprites = Resources.LoadAll<Sprite>(laserSpritePath);

        if (laserSprites == null || laserSprites.Length < 63)
        {
            Debug.LogError($"Boss1: Failed to load laser sprites from {laserSpritePath}. " +
                           $"Expected ≥63 frames, found {laserSprites?.Length ?? 0}");
            return;
        }

        System.Array.Sort(laserSprites, (a, b) => a.name.CompareTo(b.name));

        Sprite referenceSprite = laserSprites[Mathf.Min(34, laserSprites.Length - 1)];
        float spriteWidthUnits = referenceSprite.bounds.size.x;
        laserMarginCropUnits = spriteWidthUnits * laserBeamStartFraction;

        laserObject = new GameObject("Boss1_Laser");
        laserRenderer = laserObject.AddComponent<SpriteRenderer>();
        laserRenderer.sortingLayerName = bossSprite != null ? bossSprite.sortingLayerName : "Default";
        laserRenderer.sortingOrder = (bossSprite != null ? bossSprite.sortingOrder : 0) + 10;
        laserRenderer.enabled = false;

        if (showWarningTelegraph)
        {
            laserWarningObject = new GameObject("Boss1_LaserWarning");
            laserWarningLine = laserWarningObject.AddComponent<LineRenderer>();

            Material lineMat = new Material(Shader.Find("Sprites/Default"));
            lineMat.color = warningLineColor;
            laserWarningLine.material = lineMat;
            laserWarningLine.startWidth = 0.15f;
            laserWarningLine.endWidth = 0.15f;
            laserWarningLine.positionCount = 2;
            laserWarningLine.sortingLayerName = bossSprite != null ? bossSprite.sortingLayerName : "Default";
            laserWarningLine.sortingOrder = (bossSprite != null ? bossSprite.sortingOrder : 0) + 9;
            laserWarningLine.enabled = false;
        }
    }

    private Vector3 GetLaserSpawnPositionOLD()
    {
        if (bossSprite == null) return transform.position;
        Vector2 offset = laserSpawnLocalOffset;
        if (bossSprite.flipX) offset.x = -offset.x;
        return transform.position + (Vector3)offset;
    }
    private Vector3 GetLaserSpawnPosition()
    {
        if (bossSprite == null) return transform.position;
        Vector2 offset = laserSpawnLocalOffset;
        if (bossSprite.flipX) offset.x = -offset.x;
        // TransformPoint respects rotation AND scale, not just position
        return transform.TransformPoint(offset);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isDying) return;

        var wp = other.GetComponent<WeaponProjectile>();
        if (wp != null)
        {
            float dmg = wp.GetDamage();
            //Debug.Log($"Boss1 taking {dmg} damage from WeaponProjectile");
            TakeDamage(dmg);  // calls Boss1's override TakeDamage(float)
            Destroy(other.gameObject);
            return;
        }

        var proj = other.GetComponent<Projectile>();
        if (proj != null)
        {
            //Debug.Log($"Boss1 taking {proj.damage} damage from Projectile");
            TakeDamage(proj.damage);
            Destroy(other.gameObject);
            return;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        //Debug.Log($"Boss1 OnCollisionEnter2D: {collision.collider.name} tag={collision.collider.tag}");
    }
    private void UpdateColliderOffset()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col == null || bossSprite == null) return;

        float xOff = bossSprite.flipX ? bossColliderOffsetXFacingLeft : bossColliderOffsetXFacingRight;
        col.offset = new Vector2(xOff, bossColliderOffsetY);
    }
    private void Update()
    {
        if (isDying) return;

        // DEBUG: detect any collider overlap
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            ContactFilter2D filter = new ContactFilter2D();
            filter.NoFilter();
            var results = new System.Collections.Generic.List<Collider2D>();
            col.Overlap(filter, results);
            foreach (var r in results)
            {
                if (r.GetComponent<WeaponProjectile>() != null || r.GetComponent<Projectile>() != null || r.GetComponent<Weapon>() != null)
                    Debug.Log($"Boss1 overlapping with: {r.name} tag={r.tag} trigger={r.isTrigger}");
            }
        }


        UpdateHealthBarOffset();
        UpdateColliderOffset();
        UpdateGrapplingTargetOffset();

        FindTarget();

        if (trackingMode == LaserTrackingMode.DelayedTracking
            && currentTarget != null && isLaserActive)
            RecordTargetPosition();

        if (currentTarget != null)
        {
            float distance = Vector2.Distance(transform.position, currentTarget.position);

            // Laser is the only attack Boss1 manages directly.
            // Melee is fully handled by EnemyController.
            //if (distance <= attackRange && !isPerformingLaserAttack)
            if (distance > meleeOnlyRange && distance <= attackRange && !isPerformingLaserAttack)

                TryLaser();
        }
    }

    // DAMAGE / DEATH
    public override void TakeDamage(float amount)
    {
        if (isDying) return;

        //Debug.Log($">>> Boss1.TakeDamage called! amount={amount}, armor={bossArmor}, health={currentHealth}");

        if (!armorDestroyed && bossArmor > 0)
        {
            bossArmor -= amount;
            if (bossArmor <= 0)
            {
                float overflow = -bossArmor;
                bossArmor = 0;
                OnArmorDestroyed();
                currentHealth -= overflow;
            }
        }
        else
        {
            currentHealth -= amount;
        }

        CallStartDamageFlash();
        UpdateBossHealthBar();
        //Debug.Log($"Boss1 TakeDamage: armor={bossArmor}, health={currentHealth}, HealthBar={HealthBar != null}");

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            ExecuteBossDeath();
        }
    }

    public override void Die()
    {
        if (isDying) return;
        currentHealth = 0f;
        ExecuteBossDeath();
    }
    private void ExecuteBossDeath()
    {
        //Debug.Log("[BOSS] ExecuteBossDeath called");

        isDying = true;  // FIRST — before any checks
        if (!gameObject.scene.isLoaded) return;
        transform.rotation = Quaternion.identity;
        // Boss death freeze + shake — tune duration here
        CombatJuice.OnBossKilled(gameObject);
        // Reset to idle frame 0 BEFORE disabling animController or calling VFX
        if (bossSprite != null && enemyData != null)
        {
            var allSprites = Resources.LoadAll<Sprite>(enemyData.spriteFolderPath);
            if (allSprites != null && allSprites.Length > 0)
            {
                System.Array.Sort(allSprites, (a, b) => a.name.CompareTo(b.name));
                bossSprite.sprite = allSprites[enemyData.idle.startFrame];
            }
        }
        // Destroy health bar
        if (HealthBar != null)
            Destroy(HealthBar.gameObject);

        StopAllCoroutines();
        StopLaserChargeSound();

        isLaserActive = false;
        isPerformingLaserAttack = false;
        groundHitSoundPending = false;
        CleanupLaserNightLights();

        if (laserRenderer != null) laserRenderer.enabled = false;
        if (laserWarningLine != null) laserWarningLine.enabled = false;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.simulated = false;
        }

        foreach (var col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        var ec = GetComponent<EnemyController>();
        if (ec != null) ec.enabled = false;
        if (animController != null) animController.enabled = false;

        bossArmor = 0f;
        armorDestroyed = true;

        Vector3 deathPos = transform.position;

        // Drop boss energy rewards
        for (int i = 0; i < 10; i++)
        {
            float angle = (360f / 10) * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 1.5f;
            Vector3 spawnPos = deathPos + offset;
            int energyValue = (EnergyDropManager.Instance != null)
                ? EnergyDropManager.Instance.defaultEnergyValue
                : 10;
            EnergyDrop.CreateEnergyDrop(spawnPos, energyValue);
        }

        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(gameObject);

        if (spawnedHead != null && spawnedHead.gameObject != null)
        {
            Destroy(spawnedHead.gameObject);
            spawnedHead = null;
        }

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: disintegrationDuration,
            onComplete: () =>
            {
                if (AudioManager.instance != null && FMODEvents.instance != null)
                    AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, deathPos);
            });
    }


    // ARMOR BREAK FLASH
    protected override void OnArmorDestroyed()
    {
        base.OnArmorDestroyed();
        if (bossSprite != null)
            StartCoroutine(ArmorBreakFlash());
    }

    private IEnumerator ArmorBreakFlash()
    {
        if (bossSprite == null) yield break;
        Color original = bossSprite.color;
        bossSprite.color = Color.white;
        yield return new WaitForSeconds(0.1f);
        if (bossSprite != null) bossSprite.color = new Color(1f, 0.5f, 0.5f);
        yield return new WaitForSeconds(0.1f);
        if (bossSprite != null) bossSprite.color = Color.white;
        yield return new WaitForSeconds(0.1f);
        if (bossSprite != null) bossSprite.color = original;
    }


    // GROUND HIT SOUND
    // Called by EnemyController.PerformHit() when the boss's melee hit connects.
    // The hit timing is now driven by EnemyData.hitFrame through the unified system.

    public void PlayGroundHitSound()
    {
        if (isDying) return;

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(
                FMODEvents.instance.bossGroundHit, transform.position);
    }


    // DELAYED TRACKING HELPERS


    private void RecordTargetPosition()
    {
        if (currentTarget == null) return;
        positionHistory.Add(new PositionSnapshot(currentTarget.position, Time.time));
        float cutoff = Time.time - 2f;
        positionHistory.RemoveAll(s => s.timestamp < cutoff);
    }

    private Vector3 GetDelayedTargetPosition()
    {
        if (currentTarget == null || positionHistory.Count == 0)
            return currentTarget != null ? currentTarget.position : transform.position;

        float targetTime = Time.time - trackingDelay;
        PositionSnapshot closest = positionHistory[0];
        float closestDiff = Mathf.Abs(closest.timestamp - targetTime);

        foreach (var snap in positionHistory)
        {
            float diff = Mathf.Abs(snap.timestamp - targetTime);
            if (diff < closestDiff) { closest = snap; closestDiff = diff; }
        }
        return closest.position;
    }

    private void FindTarget()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            currentTarget = player.transform;
            return;
        }

        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null)
        {
            currentTarget = core.transform;
            return;
        }

        currentTarget = null;
    }


    // HEAD SYSTEM


    private void SpawnBossHead()
    {
        Sprite headSprite = Resources.Load<Sprite>(headSpritePath);
        if (headSprite == null)
        {
            Debug.LogError($"Boss1: Head sprite not found at {headSpritePath}");
            return;
        }

        Transform coreTransform = null;
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null) coreTransform = core.transform;

        Vector3 spawnCenter = coreTransform != null ? coreTransform.position : transform.position;
        Vector3 spawnPosition = Vector3.zero;
        bool foundValid = false;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float distance = Random.Range(headSpawnMinDistance, headSpawnMaxDistance);
            spawnPosition = spawnCenter + (Vector3)(dir * distance);

            if (spawnPosition.x >= headMapBoundsMin && spawnPosition.x <= headMapBoundsMax &&
                spawnPosition.y >= headMapBoundsMin && spawnPosition.y <= headMapBoundsMax)
            {
                foundValid = true;
                break;
            }
        }

        if (!foundValid)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float safeDist = Mathf.Min(headSpawnMinDistance, headMapBoundsMax * 0.5f);
            spawnPosition = spawnCenter + (Vector3)(dir * safeDist);
            spawnPosition.x = Mathf.Clamp(spawnPosition.x, headMapBoundsMin, headMapBoundsMax);
            spawnPosition.y = Mathf.Clamp(spawnPosition.y, headMapBoundsMin, headMapBoundsMax);
        }

        GameObject headObj = new GameObject("Boss1_Head");
        headObj.transform.position = spawnPosition;
        headObj.layer = LayerMask.NameToLayer("Enemy");
        headObj.tag = "Enemy";

        SpriteRenderer sr = headObj.AddComponent<SpriteRenderer>();
        sr.sprite = headSprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 2000; // Always above grass Y-sort range (400-1600)

        CircleCollider2D col = headObj.AddComponent<CircleCollider2D>();
        col.radius = 0.5f;
        col.isTrigger = true;

        Rigidbody2D headRb = headObj.AddComponent<Rigidbody2D>();
        headRb.bodyType = RigidbodyType2D.Kinematic;
        headRb.gravityScale = 0;

        spawnedHead = headObj.AddComponent<BossHead>();
        spawnedHead.Initialize(this);
        spawnedHead.SetSpawnConfig(coreTransform,
                                   headSpawnMinDistance, headSpawnMaxDistance,
                                   headMapBoundsMin, headMapBoundsMax);

        var grapplingTarget = headObj.AddComponent<GrapplingTarget>();
        grapplingTarget.canBeGrappled = true;
        grapplingTarget.isSolidTarget = false;
        grapplingTarget.grapplePointOffset = Vector3.zero;
    }

    public override void OnHeadDestroyed()
    {
        base.OnHeadDestroyed();
    }


    // LASER ATTACK


    private void TryLaser()
    {
        if (isLaserActive || isPerformingLaserAttack
            || Time.time < lastLaserTime + laserCooldown) return;
        if (laserSprites == null || laserSprites.Length < 63) return;

        StartCoroutine(PerformLaserAttack());
    }
    private void StopLaserChargeSound()
    {
        if (hasLaserSound)
        {
            laserChargeSoundInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            laserChargeSoundInstance.release();
            hasLaserSound = false;
        }
    }
    private IEnumerator PerformLaserAttack()
    {
        isPerformingLaserAttack = true;
        isLaserActive = true;
        lastLaserTime = Time.time;

        if (bossSprite != null) wasFlippedBeforeLaser = bossSprite.flipX;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (animController != null) animController.PlayLaserAttackAnimation();

        laserRenderer.enabled = true;
        positionHistory.Clear();

        if (trackingMode == LaserTrackingMode.Prediction && currentTarget != null)
            predictedTargetPosition = currentTarget.position;

        if (trackingMode == LaserTrackingMode.DelayedTracking && currentTarget != null)
            positionHistory.Add(new PositionSnapshot(currentTarget.position, Time.time));

        if (showWarningTelegraph && laserWarningLine != null)
            laserWarningLine.enabled = true;

        //if (playLaserChargeSound
        //    && AudioManager.instance != null && FMODEvents.instance != null)
        //    AudioManager.instance.PlayOneShot(
        //        FMODEvents.instance.bossLaserShot, transform.position);

        if (playLaserChargeSound && FMODEvents.instance != null)
        {
            StopLaserChargeSound(); // stop any previous instance
            laserChargeSoundInstance = FMODUnity.RuntimeManager.CreateInstance(
                FMODEvents.instance.bossLaserShot);
            laserChargeSoundInstance.set3DAttributes(
                FMODUnity.RuntimeUtils.To3DAttributes(transform.position));
            laserChargeSoundInstance.start();
            hasLaserSound = true;
        }

        float chargeFrameTime = laserChargeDuration / 34f;
        for (int frame = 0; frame <= 33; frame++)
        {
            if (isDying) yield break;
            if (frame < laserSprites.Length)
            {
                laserRenderer.sprite = laserSprites[frame];
                UpdateBossFlipForLaser();
                UpdateLaserTransform(false);

                if (showWarningTelegraph && laserWarningLine != null && currentTarget != null)
                    UpdateWarningLine();
            }
            yield return new WaitForSeconds(chargeFrameTime);
        }

        if (laserWarningLine != null) laserWarningLine.enabled = false;

        if (currentTarget != null)
        {
            Vector3 spawnPos = GetLaserSpawnPosition();
            switch (trackingMode)
            {
                case LaserTrackingMode.LockOnFire:
                    lockedLaserDirection = (currentTarget.position - spawnPos).normalized;
                    break;
                case LaserTrackingMode.Prediction:
                    lockedLaserDirection = (predictedTargetPosition - spawnPos).normalized;
                    break;
                case LaserTrackingMode.PerfectTracking:
                case LaserTrackingMode.SlowTracking:
                    lockedLaserDirection = Vector3.zero;
                    break;
            }
        }

        float fireFrameTime = laserFireDuration / 29f;
        float damagePerFrame = laserDamagePerSecond * fireFrameTime;

        // Night mode: spawn point lights along the beam so it illuminates through darkness
        SpawnLaserNightLights();

        for (int frame = 34; frame <= 62 && frame < laserSprites.Length; frame++)
        {
            if (isDying) yield break;

            laserRenderer.sprite = laserSprites[frame];
            UpdateBossFlipForLaser();
            UpdateLaserTransform(true);

            // Night mode: reposition lights along current beam direction
            UpdateLaserNightLights();

            Vector3 damageDir = GetLaserDamageDirection();
            if (damageDir != Vector3.zero)
            {
                Vector3 spawnPos = GetLaserSpawnPosition();

                ContactFilter2D filter = new ContactFilter2D();
                filter.SetLayerMask(laserTargetLayers);
                filter.useLayerMask = true;
                filter.useTriggers = false;   // ignore trigger colliders (e.g. tower range sensors)

                RaycastHit2D[] hits = new RaycastHit2D[16];
                int count = Physics2D.Raycast(spawnPos, damageDir, filter, hits, laserRange);

                for (int i = 0; i < count; i++)
                {
                    var hit = hits[i];
                    if (hit.collider == null) continue;
                    if (hit.collider.gameObject == gameObject) continue;
                    if (hit.collider.CompareTag("Enemy")) continue;
                    ApplyDamageToTarget(hit.collider.gameObject, damagePerFrame);
                }
            }
            yield return new WaitForSeconds(fireFrameTime);
        }

        CleanupLaserNightLights();
        laserRenderer.enabled = false;
        isLaserActive = false;
        lockedLaserDirection = Vector3.zero;
        positionHistory.Clear();
        StopLaserChargeSound();

        if (laserWarningLine != null) laserWarningLine.enabled = false;

        if (animController != null)
        {
            animController.StopLaserAttackAnimation();
            yield return null;
        }

        RestoreBossFlipAfterLaser();
        isPerformingLaserAttack = false;
    }

    private void UpdateBossFlipForLaser()
    {
        if (bossSprite == null || currentTarget == null) return;
        float dx = currentTarget.position.x - transform.position.x;
        // Deadband: don't flip when the target is near the centerline.
        // This laser coroutine runs this method every sprite frame (~60/s);
        // without a deadband, jitter near dx = 0 would oscillate the flip
        // direction. 0.5 world units is safely outside normal movement jitter.
        const float flipDeadband = 0.5f;
        if (dx < -flipDeadband) bossSmoothFlip.SetFacingLeft(true);
        else if (dx > flipDeadband) bossSmoothFlip.SetFacingLeft(false);
        // Inside the deadband: hold current facing.
    }

    private void RestoreBossFlipAfterLaser()
    {
        if (bossSprite == null) return;
        if (currentTarget != null)
            bossSmoothFlip.SetFacingLeft(currentTarget.position.x - transform.position.x < 0);
        else
            bossSmoothFlip.SetFacingLeft(wasFlippedBeforeLaser);
    }

    private void UpdateWarningLine()
    {
        if (currentTarget == null || laserWarningLine == null) return;
        // Stay above boss as YSortEntity updates each frame
        if (bossSprite != null)
        {
            laserWarningLine.sortingLayerID = bossSprite.sortingLayerID;
            laserWarningLine.sortingOrder = bossSprite.sortingOrder + 9;
        }
        Vector3 spawnPos = GetLaserSpawnPosition();
        Vector3 direction = (currentTarget.position - spawnPos).normalized;

        laserWarningLine.SetPosition(0, spawnPos);
        laserWarningLine.SetPosition(1, spawnPos + direction * laserRange);

        float pulseAlpha = 0.3f + Mathf.PingPong(Time.time * 3f, 0.4f);
        Color pulseColor = warningLineColor;
        pulseColor.a = pulseAlpha;
        laserWarningLine.material.color = pulseColor;
    }

    private Vector3 GetLaserDamageDirection()
    {
        if (currentTarget == null) return Vector3.zero;
        Vector3 spawnPos = GetLaserSpawnPosition();

        switch (trackingMode)
        {
            case LaserTrackingMode.DelayedTracking:
                return (GetDelayedTargetPosition() - spawnPos).normalized;
            case LaserTrackingMode.LockOnFire:
                return lockedLaserDirection;
            case LaserTrackingMode.PerfectTracking:
                return (currentTarget.position - spawnPos).normalized;
            case LaserTrackingMode.SlowTracking:
                return laserObject.transform.right;
            case LaserTrackingMode.Prediction:
                return lockedLaserDirection;
            default:
                return (currentTarget.position - spawnPos).normalized;
        }
    }

    private void UpdateLaserTransform(bool isFiring)
    {
        if (currentTarget == null || laserRenderer.sprite == null) return;
        // Keep laser drawn above the boss even as YSortEntity updates boss sorting order each frame
        if (bossSprite != null)
        {
            laserRenderer.sortingLayerID = bossSprite.sortingLayerID;
            laserRenderer.sortingOrder = bossSprite.sortingOrder + 10;
        }
        Vector3 laserSpawnPos = GetLaserSpawnPosition();
        Vector3 directionToUse;

        if (!isFiring)
        {
            directionToUse = (currentTarget.position - laserSpawnPos).normalized;
        }
        else
        {
            switch (trackingMode)
            {
                case LaserTrackingMode.DelayedTracking:
                    directionToUse = (GetDelayedTargetPosition() - laserSpawnPos).normalized;
                    break;
                case LaserTrackingMode.LockOnFire:
                    directionToUse = lockedLaserDirection;
                    break;
                case LaserTrackingMode.PerfectTracking:
                    directionToUse = (currentTarget.position - laserSpawnPos).normalized;
                    break;
                case LaserTrackingMode.SlowTracking:
                    Vector3 targetDir = (currentTarget.position - laserSpawnPos).normalized;
                    Vector3 curDir = laserObject.transform.right;
                    float angleToTgt = Vector3.SignedAngle(curDir, targetDir, Vector3.forward);
                    float maxRot = trackingRotationSpeed * Time.deltaTime;
                    float rotAmt = Mathf.Clamp(angleToTgt, -maxRot, maxRot);
                    directionToUse = Quaternion.AngleAxis(rotAmt, Vector3.forward) * curDir;
                    break;
                case LaserTrackingMode.Prediction:
                    directionToUse = lockedLaserDirection;
                    break;
                default:
                    directionToUse = (currentTarget.position - laserSpawnPos).normalized;
                    break;
            }
        }

        float angle = Mathf.Atan2(directionToUse.y, directionToUse.x) * Mathf.Rad2Deg;
        laserObject.transform.rotation = Quaternion.Euler(0, 0, angle);

        Bounds spriteBounds = laserRenderer.sprite.bounds;
        float beamStartX = spriteBounds.min.x + laserMarginCropUnits;
        laserObject.transform.position = laserSpawnPos
                                       - laserObject.transform.right * beamStartX;
    }






    private void ApplyDamageToTarget(GameObject target, float damage)
    {
        var stats = target.GetComponent<CharacterStats>();
        //if (stats != null) { stats.TakeDamage(damage); return; }
        if (stats != null)
        {
            if (ShieldBlockHelper.TryBlock(gameObject, target)) return;
            stats.TakeDamage(damage);
            return;
        }
        var consumer = target.GetComponent<IEnergyConsumer>();
        if (consumer != null && EnergyManager.Instance != null)
            EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, gameObject);
    }


    // NIGHT MODE — LASER ILLUMINATION

    private void SpawnLaserNightLights()
    {
        CleanupLaserNightLights();
        if (NightOverlay.Instance == null) return;

        // Dense strip of small overlapping lights — reads as one continuous illuminated beam
        int pointCount = 12;
        for (int i = 0; i < pointCount; i++)
        {
            GameObject lightObj = new GameObject($"LaserNightLight_{i}");
            NightLight nl = lightObj.AddComponent<NightLight>();
            nl.radius = 1.0f;
            nl.intensity = 0.7f;
            nl.lightColor = new Color(1f, 0.3f, 0.15f); // red laser tint
            nl.warmTintStrength = 0.6f;
            nl.flickerSpeed = 8f;
            nl.flickerAmount = 0.08f;
            laserNightLights.Add(lightObj);
        }

        UpdateLaserNightLights();
    }

    private void UpdateLaserNightLights()
    {
        if (laserNightLights.Count == 0) return;

        Vector3 spawnPos = GetLaserSpawnPosition();
        Vector3 dir = GetLaserDamageDirection();
        if (dir == Vector3.zero && currentTarget != null)
            dir = (currentTarget.position - spawnPos).normalized;
        if (dir == Vector3.zero) return;

        int count = laserNightLights.Count;
        for (int i = 0; i < count; i++)
        {
            if (laserNightLights[i] == null) continue;
            float t = Mathf.Lerp(0.05f, 0.95f, i / (float)(count - 1));
            laserNightLights[i].transform.position = spawnPos + dir * (laserRange * t);
        }
    }

    private void CleanupLaserNightLights()
    {
        foreach (var go in laserNightLights)
            if (go != null) Destroy(go);
        laserNightLights.Clear();
    }


    // CLEANUP

    private void OnDestroy()
    {
        CleanupLaserNightLights();
        if (laserObject != null) Destroy(laserObject);
        if (laserWarningObject != null) Destroy(laserWarningObject);
        if (spawnedHead != null && spawnedHead.gameObject != null)
            Destroy(spawnedHead.gameObject);
        if (HealthBar != null)
            Destroy(HealthBar.gameObject);

        StopLaserChargeSound();

        isLaserActive = false;
        isPerformingLaserAttack = false;
        if (laserRenderer != null) laserRenderer.enabled = false;
        if (laserWarningLine != null) laserWarningLine.enabled = false;
    }


    // DEBUG GIZMOS


#if UNITY_EDITOR
    [Header("Debug Visualization")]
    [SerializeField] private bool showLaserDebug = true;
    [SerializeField] private bool showSpriteDebug = true;
    [SerializeField] private Color laserRayColor = Color.cyan;
    [SerializeField] private Color laserEndMarkerColor = Color.red;
    [SerializeField] private Color spriteBoundsColor = new Color(1f, 0f, 1f, 0.5f);

    private void OnDrawGizmos()
    {
        Vector3 spawnPos = GetLaserSpawnPosition();
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(spawnPos, 0.3f);
        Gizmos.DrawSphere(spawnPos, 0.15f);

        UnityEditor.Handles.color = Color.green;
        UnityEditor.Handles.Label(spawnPos + Vector3.up * 0.5f, "LASER SPAWN");

        if (showLaserDebug && isLaserActive
            && laserRenderer != null && laserRenderer.sprite != null)
        {
            DrawLaserDebug();
            DrawRuntimeRaycast();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (currentTarget != null && showLaserDebug)
        {
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            Gizmos.color = laserRayColor;
            Gizmos.DrawLine(transform.position, transform.position + dir * laserRange);
            Gizmos.color = laserEndMarkerColor;
            Gizmos.DrawWireSphere(transform.position + dir * laserRange, 0.5f);
        }

        if (spawnDetachableHead)
        {
            Transform coreT = null;
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null) coreT = core.transform;

            Vector3 center = coreT != null ? coreT.position : transform.position;
            UnityEditor.Handles.color = new Color(0f, 1f, 0f, 0.1f);
            UnityEditor.Handles.DrawWireDisc(center, Vector3.forward, headSpawnMinDistance);
            UnityEditor.Handles.DrawWireDisc(center, Vector3.forward, headSpawnMaxDistance);
        }

        if (showLaserDebug && !isLaserActive
            && laserObject != null && laserRenderer != null && laserRenderer.sprite != null)
            DrawLaserDebug();
    }

    private void DrawLaserDebug()
    {
        if (laserRenderer == null || laserRenderer.sprite == null || currentTarget == null) return;

        Bounds spriteBounds = laserRenderer.sprite.bounds;
        Vector3 spriteSize = spriteBounds.size;
        Vector3 directionToTarget = (currentTarget.position - transform.position).normalized;
        float angle = Mathf.Atan2(directionToTarget.y, directionToTarget.x);

        if (showSpriteDebug)
        {
            Gizmos.color = spriteBoundsColor;
            Vector3 spriteCenter = laserObject.transform.position;
            Vector3 halfSize = spriteSize * 0.5f;

            Vector3[] corners = new Vector3[4]
            {
                new Vector3(-halfSize.x, -halfSize.y, 0),
                new Vector3( halfSize.x, -halfSize.y, 0),
                new Vector3( halfSize.x,  halfSize.y, 0),
                new Vector3(-halfSize.x,  halfSize.y, 0),
            };

            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            for (int i = 0; i < corners.Length; i++)
                corners[i] = spriteCenter + new Vector3(
                    corners[i].x * cos - corners[i].y * sin,
                    corners[i].x * sin + corners[i].y * cos, 0f);

            for (int i = 0; i < corners.Length; i++)
                Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Length]);
        }

        float effectiveLen = spriteBounds.max.x - (spriteBounds.min.x + laserMarginCropUnits);
        Vector3 laserStart = laserObject.transform.position
                                - laserObject.transform.right *
                                  (spriteBounds.min.x + laserMarginCropUnits);
        Vector3 laserVisualEnd = laserStart + laserObject.transform.right * effectiveLen;

        UnityEditor.Handles.color = new Color(1f, 1f, 0f, 0.8f);
        UnityEditor.Handles.DrawLine(laserStart, laserVisualEnd, 5f);

        Gizmos.color = Color.green; Gizmos.DrawWireSphere(laserStart, 0.3f);
        Gizmos.color = Color.magenta; Gizmos.DrawWireSphere(laserVisualEnd, 0.4f);

        Vector3 sPos = GetLaserSpawnPosition();
        Vector3 rayEnd = sPos + directionToTarget * laserRange;
        UnityEditor.Handles.color = new Color(1f, 0f, 0f, 0.8f);
        UnityEditor.Handles.DrawLine(sPos, rayEnd, 3f);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(laserVisualEnd + Vector3.up * 0.8f,
            $"Sprite End: {effectiveLen:F2}u");
        UnityEditor.Handles.Label(rayEnd + Vector3.up * 0.8f,
            $"Damage End: {laserRange:F2}u");
    }

    private void DrawRuntimeRaycast()
    {
        if (!isLaserActive || currentTarget == null) return;

        Vector3 sPos = GetLaserSpawnPosition();
        Vector3 dir = (currentTarget.position - sPos).normalized;
        RaycastHit2D[] hits = Physics2D.RaycastAll(sPos, dir, laserRange, laserTargetLayers);

        bool anyHits = false;
        foreach (var hit in hits)
        {
            if (hit.collider != null
                && hit.collider.gameObject != gameObject
                && !hit.collider.CompareTag("Enemy")
                //&& !(hit.collider.GetComponent<Tower>() != null)

                )
            {
                anyHits = true;
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(hit.point, 0.5f);
                Gizmos.DrawLine(sPos, hit.point);
            }
        }

        if (!anyHits)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
            Gizmos.DrawLine(sPos, sPos + dir * laserRange);
        }
    }
#endif
}
