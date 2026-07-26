using UnityEngine;
using System.Collections;

// Boss1 Features: Laser attack, Detachable Head armor system.
// Melee attacks and movement are handled by EnemyController.
// Melee hit timing is driven by EnemyData.hitFrame through the unified system.

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
    private EnemyController enemyController;
    private bool isPerformingLaserAttack = false;

    /// True for the WHOLE laser routine (charge → fire → cleanup), not just the
    /// laser animation. EnemyController reads this so it never opens a melee
    /// cycle mid-laser — PlayMeleeAttackAnimation() early-outs while the laser
    /// animation owns the sprite, which would deal damage with no swing on screen.
    public bool IsPerformingLaserAttack => isPerformingLaserAttack;

    [Header("Boss1 Configuration")]
    [SerializeField] private float bossMaxArmor = 1000f;
    [SerializeField] private float bossMaxHealth = 1000f;

    [Header("Laser Attack")]
    //private FMOD.Studio.EventInstance? laserChargeSoundInstance;
    private FMOD.Studio.EventInstance laserChargeSoundInstance;
    private bool hasLaserSound = false;
    private float nextLaserSoundLogTime;
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

    [Header("Laser Rendering Order")]
    [Tooltip("Sorting order for the laser beam. This must be a FIXED value ABOVE the " +
             "Y-sort band, NOT derived from the boss's own sorting order.\n\n" +
             "Why: every Y-sorted sprite in the project uses\n" +
             "    sortingOrder = 1000 + round(-footY * 10)\n" +
             "so the band runs roughly 400-1600 and a sprite one world unit lower on " +
             "screen sorts 10 higher. The laser is a long beam that spans many Y " +
             "values at once, so no single Y-derived order can ever be correct for " +
             "it — it has to opt out of Y-sorting entirely.\n\n" +
             "Existing fixed orders in the project for reference:\n" +
             "    -1   terrain\n" +
             "    500  connection lines\n" +
             "    600  non-blocking layout decorations\n" +
             "    400-1600  Y-sorted sprites (boss, blocking obstacles, trees, rocks)\n" +
             "    2000 boss head, enemy projectiles\n" +
             "    2500 obstacle-drawing line")]
    [SerializeField] private int laserSortingOrder = 2100;

    [Header("Laser Charge Telegraph")]
    [Tooltip("Show the charge-up tell (muzzle glow + short aim stub) while the " +
             "laser winds up. Off = no telegraph at all.")]
    [SerializeField] private bool showWarningTelegraph = true;
    [Tooltip("Tint of the charge telegraph. Keep it close to the beam's colour so " +
             "players read it as 'that thing is about to fire'.")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.32f, 0.22f, 1f);
    [Tooltip("Peak opacity of the telegraph at full charge. This is the main " +
             "subtlety dial — lower = quieter tell.")]
    [Range(0f, 1f)][SerializeField] private float telegraphMaxAlpha = 0.55f;
    [Tooltip("World radius of the glow at the muzzle when fully charged. It grows " +
             "into this from ~35% over the charge.")]
    [SerializeField] private float telegraphOrbRadius = 0.45f;
    [Tooltip("Length (world units) of the short aim stub at full charge. This only " +
             "hints at the direction — it deliberately does NOT reach the target " +
             "the way the old full-length warning line did.")]
    [SerializeField] private float telegraphStubLength = 2.2f;
    [Tooltip("Width of the aim stub at the muzzle end. It tapers to a point.")]
    [SerializeField] private float telegraphStubWidth = 0.14f;
    [Tooltip("Draw the contracting intake ring that collapses into the muzzle. " +
             "This is what reads as 'charging' rather than just 'glowing'.")]
    [SerializeField] private bool telegraphShowIntakeRing = true;

    [Header("Laser Audio")]
    [SerializeField] private bool playLaserChargeSound = true;
    [Tooltip("Log the live FMOD instance count for bossLaserShot every time a boss " +
             "creates/stops one, plus the FMOD.RESULT of start(). Turn this on for " +
             "one playtest to prove whether instances are leaking (count climbs and " +
             "never returns to 0) or whether the event is hitting Max Instances " +
             "(start() returns ERR_STUDIO_MAX_INSTANCES / count sits at the cap).")]
    [SerializeField] private bool logLaserSoundDiagnostics = false;

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

    // Charge telegraph — muzzle glow + contracting intake ring + short aim stub.
    // Replaces the old full-length red LineRenderer warning.
    private GameObject telegraphRoot;
    private SpriteRenderer telegraphOrb;
    private SpriteRenderer telegraphRing;
    private LineRenderer telegraphStub;
    private Gradient telegraphGradient;
    private GradientColorKey[] telegraphColorKeys;
    private GradientAlphaKey[] telegraphAlphaKeys;
    private Sprite telegraphGlowSprite;
    private Sprite telegraphRingSprite;

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
        // Skip color writes and motion trail. Boss has
        // other scripts reading flipX (collider offset, health bar, grapple
        // point) and writing color (armor-break flash, damage flash) — a
        // minimal flip avoids stepping on them.
        bossSmoothFlip.SetMinimalMode(true);
        animController = GetComponent<EnemyAnimationController>();
        enemyController = GetComponent<EnemyController>();
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

        // Keep the bar above the cartoon-grass overlay.
        // GrassCartoonOverlay bakes its tufts on the *Default* sorting layer at
        //     sortingOrder = 1000 + round(-y * 10)   ->  roughly 400..1600 across the map.
        // A fixed order of 1000 sits INSIDE that band: it only wins against grass
        // above y = 0 and loses to every tuft below it, so the bar gets buried.
        // 4000 clears the whole band (and matches what EnemyHealthBar.Initialize
        // already sets) while staying under fog (5000) and the night overlay (6000).
        const int HEALTH_BAR_SORTING_ORDER = 4000;

        // includeInactive: the prefab hides its bar UI in Awake(), so an active-only
        // search can miss the Canvas depending on where it lives in the hierarchy.
        Canvas canvas = HealthBar.GetComponentInChildren<Canvas>(true);
        if (canvas == null) canvas = HealthBar.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = HEALTH_BAR_SORTING_ORDER;
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
        // Fixed order above the Y-sort band. Previously this was bossSprite.sortingOrder
        // + 10, which only bought the beam ONE world unit of headroom — anything whose
        // foot sat more than 1 unit below the boss sorted over it.
        laserRenderer.sortingOrder = laserSortingOrder;
        laserRenderer.enabled = false;

        InitializeChargeTelegraph();
    }

    // LASER CHARGE TELEGRAPH
    //
    // The old tell was a LineRenderer stretched the full laserRange at the target
    // (thick, flat red, opaque). This replaces it with three quiet cues that all
    // ramp in over the charge, so the boss reads as "winding up, pointed THAT way"
    // without painting a red stripe across the arena:
    //   1. a soft glow at the muzzle that grows + brightens as the charge fills
    //   2. an intake ring that repeatedly collapses inward into the muzzle
    //   3. a short aim stub that tapers and fades out well short of the target
    // All three are built procedurally, so there are no prefabs/assets to wire up.

    private void InitializeChargeTelegraph()
    {
        if (!showWarningTelegraph) return;

        telegraphGlowSprite = BuildRadialSprite(isRing: false);
        telegraphRingSprite = BuildRadialSprite(isRing: true);

        telegraphRoot = new GameObject("Boss1_LaserChargeTelegraph");

        // 1. Muzzle charge glow.
        var orbGo = new GameObject("ChargeOrb");
        orbGo.transform.SetParent(telegraphRoot.transform, false);
        telegraphOrb = orbGo.AddComponent<SpriteRenderer>();
        telegraphOrb.sprite = telegraphGlowSprite;
        telegraphOrb.color = Color.clear;

        // 2. Intake ring (energy being pulled into the muzzle).
        if (telegraphShowIntakeRing)
        {
            var ringGo = new GameObject("IntakeRing");
            ringGo.transform.SetParent(telegraphRoot.transform, false);
            telegraphRing = ringGo.AddComponent<SpriteRenderer>();
            telegraphRing.sprite = telegraphRingSprite;
            telegraphRing.color = Color.clear;
        }

        // 3. Short, tapered, fading aim stub.
        var stubGo = new GameObject("AimStub");
        stubGo.transform.SetParent(telegraphRoot.transform, false);
        telegraphStub = stubGo.AddComponent<LineRenderer>();
        telegraphStub.material = new Material(Shader.Find("Sprites/Default"));
        telegraphStub.useWorldSpace = true;
        telegraphStub.positionCount = 2;
        telegraphStub.numCapVertices = 4;
        telegraphStub.textureMode = LineTextureMode.Stretch;
        telegraphStub.alignment = LineAlignment.View;
        telegraphStub.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        telegraphStub.receiveShadows = false;
        // Fat at the muzzle, needle-thin at the tip.
        telegraphStub.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
        telegraphStub.widthMultiplier = telegraphStubWidth;

        // Cached so the per-frame update doesn't allocate a Gradient every frame.
        telegraphGradient = new Gradient();
        telegraphColorKeys = new[]
        {
            new GradientColorKey(telegraphColor, 0f),
            new GradientColorKey(telegraphColor, 1f)
        };
        telegraphAlphaKeys = new[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(0.45f, 0.5f),
            new GradientAlphaKey(0f, 1f)
        };

        telegraphRoot.SetActive(false);
    }

    // 32x32 soft radial sprite, center-pivoted, pixelsPerUnit == size so the
    // sprite is exactly 1 world unit across and localScale == world diameter.
    // Same trick BossHead.BuildGlowSprite uses for its drip glow.
    private Sprite BuildRadialSprite(bool isRing)
    {
        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color[s * s];
        float center = s * 0.5f;

        for (int py = 0; py < s; py++)
            for (int px = 0; px < s; px++)
            {
                // 0 at the center, 1 at the sprite edge
                float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f),
                                           new Vector2(center, center)) / center;
                float a;
                if (isRing)
                {
                    // Thin soft annulus sitting just inside the sprite edge.
                    a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.16f);
                    a *= a;
                }
                else
                {
                    // Soft falloff blob, hot in the middle.
                    a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                }
                pixels[py * s + px] = a > 0.004f ? new Color(1f, 1f, 1f, a) : Color.clear;
            }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), Vector2.one * 0.5f, s);
    }

    private void SetChargeTelegraphActive(bool active)
    {
        if (telegraphRoot == null) return;
        telegraphRoot.SetActive(active);
    }

    // progress01: 0 at the start of the wind-up, 1 on the frame before it fires.
    private void UpdateChargeTelegraph(float progress01)
    {
        if (telegraphRoot == null || !telegraphRoot.activeSelf || currentTarget == null) return;

        Vector3 spawnPos = GetLaserSpawnPosition();
        Vector3 dir = (currentTarget.position - spawnPos);
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        float t = Mathf.Clamp01(progress01);
        // Quadratic ramp: nearly invisible early, firms up right before the shot,
        // so the tell doesn't dominate the fight for the whole wind-up.
        float ramp = t * t;
        // Fast, low-amplitude flicker — reads as unstable energy, not a strobe.
        float flicker = 1f + Mathf.Sin(Time.time * 22f) * 0.10f * t;
        float alpha = telegraphMaxAlpha * ramp;

        // Fixed order just under the beam, for the same reason the beam is fixed: the
        // telegraph is a gameplay-critical tell and must never be hidden behind a rock.
        int layerId = bossSprite != null ? bossSprite.sortingLayerID : 0;
        int order = laserSortingOrder - 10;

        // 1. Muzzle glow — grows 35% -> 100% and brightens with the charge.
        if (telegraphOrb != null)
        {
            telegraphOrb.sortingLayerID = layerId;
            telegraphOrb.sortingOrder = order;
            telegraphOrb.transform.position = spawnPos;
            float dia = telegraphOrbRadius * 2f * (0.35f + 0.65f * ramp) * flicker;
            telegraphOrb.transform.localScale = Vector3.one * dia;

            Color c = telegraphColor;
            // Glow core runs a bit hotter than the stub so the muzzle is the focal point.
            c.a = Mathf.Clamp01(alpha * 1.25f);
            telegraphOrb.color = c;
        }

        // 2. Intake ring — collapses inward, loops ~2.2x/sec, speeding up slightly
        //    as the charge fills. Brightest mid-collapse, gone at the center.
        if (telegraphRing != null)
        {
            telegraphRing.sortingLayerID = layerId;
            telegraphRing.sortingOrder = order - 1;
            telegraphRing.transform.position = spawnPos;

            float cycle = Mathf.Repeat(Time.time * (2.2f + 1.6f * t), 1f);
            float ringDia = Mathf.Lerp(telegraphOrbRadius * 5f, telegraphOrbRadius * 1.2f,
                                       cycle * cycle);
            telegraphRing.transform.localScale = Vector3.one * ringDia;

            Color rc = telegraphColor;
            rc.a = alpha * 0.7f * Mathf.Sin(cycle * Mathf.PI); // fade in and back out
            telegraphRing.color = rc;
        }

        // 3. Aim stub — short taper pointing where the beam will go. Starts just
        //    outside the glow so the two don't muddy each other, and stops well
        //    short of the target (that's the whole point vs. the old line).
        if (telegraphStub != null)
        {
            telegraphStub.sortingLayerID = layerId;
            telegraphStub.sortingOrder = order;

            float len = telegraphStubLength * (0.45f + 0.55f * ramp);
            telegraphStub.SetPosition(0, spawnPos + dir * (telegraphOrbRadius * 0.5f));
            telegraphStub.SetPosition(1, spawnPos + dir * len);
            telegraphStub.widthMultiplier = telegraphStubWidth * (0.7f + 0.3f * flicker);

            telegraphColorKeys[0].color = telegraphColor;
            telegraphColorKeys[1].color = telegraphColor;
            telegraphAlphaKeys[0].alpha = alpha;
            telegraphAlphaKeys[1].alpha = alpha * 0.45f;
            telegraphAlphaKeys[2].alpha = 0f;
            telegraphGradient.SetKeys(telegraphColorKeys, telegraphAlphaKeys);
            telegraphStub.colorGradient = telegraphGradient;
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
            CombatStats.ReportPlayerDamageDealt(wp.GetOwner(), dmg, transform.position);
            Destroy(other.gameObject);
            return;
        }

        var proj = other.GetComponent<Projectile>();
        if (proj != null)
        {
            //Debug.Log($"Boss1 taking {proj.damage} damage from Projectile");
            TakeDamage(proj.damage);
            CombatStats.ReportTowerDamageDealt(proj.damage);
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
        UpdateLaserChargeSoundPosition();

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
            //
            // ATTACK COMMITMENT: the boss finishes whatever it started.
            // IsMeleeAttackInProgress() blocks the laser while a melee cycle is
            // live. Without it, a player who steps from inside meleeOnlyRange to
            // outside it mid-swing pushes the boss straight into TryLaser(), and
            // PlayLaserAttackAnimation() stops the running animation coroutine —
            // the swing visibly gets chopped off partway through.
            // Update() re-evaluates every frame, so the laser simply fires on the
            // first frame after the swing recovers (laserCooldown is unaffected;
            // lastLaserTime is only stamped when the laser actually starts).
            if (distance > meleeOnlyRange && distance <= attackRange
                && !isPerformingLaserAttack && !IsMeleeAttackInProgress())

                TryLaser();
        }
    }

    // DAMAGE / DEATH
    public override void TakeDamage(float amount)
    {
        if (DebugCheats.DamageBlocked(this)) return;
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

        // Release the held FMOD instance BEFORE the early-out below. This used to sit
        // ~20 lines further down, after "if (!gameObject.scene.isLoaded) return;", so a
        // boss dying during a scene unload while its laser was charging orphaned its
        // instance. FMOD's system outlives scene loads, so an orphan keeps counting
        // against the event's Max Instances for the rest of the session.
        StopLaserChargeSound(immediate: true);

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
        StopLaserChargeSound(immediate: true); // no-op if already released above

        isLaserActive = false;
        isPerformingLaserAttack = false;
        groundHitSoundPending = false;
        CleanupLaserNightLights();

        if (laserRenderer != null) laserRenderer.enabled = false;
        SetChargeTelegraphActive(false);

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

        // Roll for a permanent weapon/tool blueprint drop
        RollBlueprintDrop(deathPos);

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
        // Stealth Cloak: while the player is invisible, the boss must not
        // acquire the player as a target. Fall through to the core.
        if (!PlayerCloakEffect.IsActive)
        {
            // Co-op: target the nearest alive player (no range limit, as before).
            var nearestPlayer = PlayerRegistry.Instance.NearestAlive(
                transform.position, includeCloaked: true);
            if (nearestPlayer != null)
            {
                currentTarget = nearestPlayer.transform;
                return;
            }
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


    /// True from the first frame of a melee cycle until its recovery ends.
    /// Two sources, deliberately OR'd:
    ///   • EnemyController.IsAttacking — the authoritative cycle flag (wind-up,
    ///     hit frame, recovery), covering the timer-driven fallback path too.
    ///   • animController.IsPlayingMeleeAttack() — covers the single frame at the
    ///     tail of the cycle where the controller has cleared its flag but the
    ///     animation coroutine hasn't returned to idle yet.
    private bool IsMeleeAttackInProgress()
    {
        if (enemyController != null && enemyController.IsAttacking) return true;
        return animController != null && animController.IsPlayingMeleeAttack();
    }

    private void TryLaser()
    {
        // Second line of defence: TryLaser() is also the entry point any future
        // caller would use, so re-check here rather than trusting Update() alone.
        if (IsMeleeAttackInProgress()) return;

        if (isLaserActive || isPerformingLaserAttack
            || Time.time < lastLaserTime + laserCooldown) return;
        if (laserSprites == null || laserSprites.Length < 63) return;

        StartCoroutine(PerformLaserAttack());
    }
    // LASER CHARGE SOUND — instance lifecycle
    //
    // This is the ONLY held FMOD EventInstance in the codebase (everything else is
    // fire-and-forget AudioManager.PlayOneShot), so it is the only sound that can
    // leak an instance or act on a recycled handle. Rules enforced below:
    //   • hasLaserSound is cleared BEFORE the stop/release, so a re-entrant or
    //     double call can never release the same handle twice. A double release is
    //     what lets one boss's cleanup silence ANOTHER boss: FMOD recycles handle
    //     pointers, so a stale struct copy can end up pointing at whatever instance
    //     was created into that slot next.
    //   • clearHandle() zeroes the struct after release so a stale copy can't be
    //     used even by accident.
    //   • isValid() guards every call.
    //   • 3D attributes are refreshed every frame from Update() with POSITION ONLY,
    //     which is exactly what AudioManager.PlayOneShot(evt, pos) does for every
    //     other sound in the game. Previously set3DAttributes was called once at
    //     creation, so the sound stayed pinned where the boss started charging.
    //
    // Deliberately NOT using RuntimeManager.AttachInstanceToGameObject here. Attaching
    // hands FMOD the boss's full transform + Rigidbody2D, which adds two things none of
    // your other (working) sounds have:
    //   – orientation: the event's panner/cone would rotate with the boss, so volume
    //     would depend on which way the boss faces. Boss1 rotates for the laser.
    //   – velocity: FMOD would apply Doppler off the Rigidbody2D as the boss walks.
    // Position-only updates keep the laser spatialising identically to the sounds you
    // already have tuned, and sidestep the deprecated Transform overload entirely.

    private void PlayLaserChargeSound()
    {
        if (!playLaserChargeSound || FMODEvents.instance == null) return;
        if (FMODEvents.instance.bossLaserShot.IsNull) return;

        // Release any previous instance from this boss first.
        StopLaserChargeSound(immediate: true);

        laserChargeSoundInstance = FMODUnity.RuntimeManager.CreateInstance(
            FMODEvents.instance.bossLaserShot);

        if (!laserChargeSoundInstance.isValid())
        {
            laserChargeSoundInstance.clearHandle();
            hasLaserSound = false;
            if (logLaserSoundDiagnostics)
                Debug.LogWarning($"[Boss1 Laser SFX] {name}: CreateInstance returned an invalid instance.");
            return;
        }

        // Same call shape AudioManager.PlayOneShot uses; refreshed each frame below.
        laserChargeSoundInstance.set3DAttributes(
            FMODUnity.RuntimeUtils.To3DAttributes(transform.position));

        FMOD.RESULT startResult = laserChargeSoundInstance.start();

        if (startResult != FMOD.RESULT.OK)
        {
            // ERR_STUDIO_MAX_INSTANCES here = the event's Max Instances cap in FMOD
            // Studio is full. That is exactly what "the sound just wasn't there"
            // looks like from the player's side.
            Debug.LogWarning($"[Boss1 Laser SFX] {name}: start() failed with {startResult}. " +
                             $"Check Max Instances / stealing on the bossLaserShot event.");
            laserChargeSoundInstance.release();
            laserChargeSoundInstance.clearHandle();
            hasLaserSound = false;
            return;
        }

        hasLaserSound = true;
        LogLaserSoundDiagnostics("start");
    }

    // immediate == true  → hard cut (boss died, or a new laser is starting).
    // immediate == false → ALLOWFADEOUT, so the event's own release/tail plays out
    //                      instead of being chopped at exactly chargeDuration +
    //                      fireDuration. This is the "sound cut short" fix.
    // Keeps the held instance sitting on the boss as it moves. Position only — no
    // orientation, no velocity — matching AudioManager.PlayOneShot's behaviour.
    private void UpdateLaserChargeSoundPosition()
    {
        if (!hasLaserSound) return;
        if (!laserChargeSoundInstance.isValid()) return;

        laserChargeSoundInstance.set3DAttributes(
            FMODUnity.RuntimeUtils.To3DAttributes(transform.position));

        // Watch the sound while it plays — this is what catches a voice being stolen
        // mid-charge, or finalVol collapsing as you step away.
        if (logLaserSoundDiagnostics && Time.time >= nextLaserSoundLogTime)
        {
            nextLaserSoundLogTime = Time.time + 0.25f;
            LogLaserSoundDiagnostics("playing");
        }
    }

    private void StopLaserChargeSound(bool immediate = true)
    {
        if (!hasLaserSound) return;

        // Clear FIRST — before any FMOD call — so nothing can re-enter and release twice.
        hasLaserSound = false;

        if (laserChargeSoundInstance.isValid())
        {
            laserChargeSoundInstance.stop(immediate
                ? FMOD.Studio.STOP_MODE.IMMEDIATE
                : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            laserChargeSoundInstance.release();
        }

        // Zero the struct so this boss can never touch the (now recycled) handle.
        laserChargeSoundInstance.clearHandle();
        LogLaserSoundDiagnostics(immediate ? "stop-immediate" : "stop-fadeout");
    }

    // Asks FMOD at runtime what it actually thinks about this event. This answers the
    // questions Boss1.cs alone cannot, because everything below is authored in FMOD
    // Studio, not in C#:
    //
    //   minDist/maxDist — the event's 3D attenuation range, in Unity units. maxDist is
    //       a HARD cutoff: past it the event is silent/culled no matter how loud it is.
    //       If maxDist is small (say 5-10) and your arena is 40 wide, the laser will
    //       literally vanish a few steps away. minDist is the full-volume radius.
    //   instances — live bossLaserShot instances across ALL bosses. Two bosses charging
    //       should read 2. If it climbs and never returns to 0, something leaks. If it
    //       pins at a low number while a boss is silent, you are hitting Max Instances.
    //   state — the instance's own playback state. If this reads STOPPED while the boss
    //       is still visibly charging, FMOD stole the voice (Max Instances + a stealing
    //       mode of Quietest/Furthest/Oldest). That is the classic two-boss symptom:
    //       which boss is "quietest"/"furthest" flips as you walk, so the sound you can
    //       hear swaps between them or drops out entirely.
    //   finalVol — volume AFTER all attenuation, panning and stealing. 0 while close to
    //       the boss means the problem is attenuation/virtualisation, not the C# code.
    //   listenerDist — distance from the FMOD listener to this boss. Compare against
    //       minDist/maxDist above. Also reveals where your listener actually is: if this
    //       never drops below ~10 even when standing on the boss, the listener is on the
    //       camera (z = -10) rather than the player, and every 3D sound is being
    //       attenuated from a 10-unit floor.
    private void LogLaserSoundDiagnostics(string phase)
    {
        if (!logLaserSoundDiagnostics || FMODEvents.instance == null) return;
        if (FMODEvents.instance.bossLaserShot.IsNull) return;

        var desc = FMODUnity.RuntimeManager.GetEventDescription(FMODEvents.instance.bossLaserShot);
        if (!desc.isValid()) return;

        desc.getInstanceCount(out int count);
        desc.getMinMaxDistance(out float minDist, out float maxDist);
        desc.is3D(out bool is3D);
        desc.isOneshot(out bool oneshot);
        desc.getLength(out int lengthMs);

        string state = "-", finalVol = "-";
        if (laserChargeSoundInstance.isValid())
        {
            laserChargeSoundInstance.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE ps);
            state = ps.ToString();
            laserChargeSoundInstance.getVolume(out float v, out float fv);
            finalVol = $"{fv:F3} (set {v:F2})";
        }

        float listenerDist = -1f;
        var studio = FMODUnity.RuntimeManager.StudioSystem;
        if (studio.isValid()
            && studio.getListenerAttributes(0, out FMOD.ATTRIBUTES_3D la) == FMOD.RESULT.OK)
        {
            listenerDist = Vector3.Distance(
                new Vector3(la.position.x, la.position.y, la.position.z), transform.position);
        }

        Debug.Log(
            $"[Boss1 Laser SFX] {name} | {phase} @{Time.time:F2}s\n" +
            $"    instances={count}  state={state}  finalVol={finalVol}\n" +
            $"    listenerDist={listenerDist:F2}  minDist={minDist:F2}  maxDist={maxDist:F2}\n" +
            $"    is3D={is3D}  oneshot={oneshot}  eventLength={lengthMs}ms  " +
            $"bossPos={transform.position}");
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

        SetChargeTelegraphActive(true);
        UpdateChargeTelegraph(0f);

        PlayLaserChargeSound();

        float chargeFrameTime = laserChargeDuration / 34f;
        for (int frame = 0; frame <= 33; frame++)
        {
            if (isDying) yield break;
            if (frame < laserSprites.Length)
            {
                laserRenderer.sprite = laserSprites[frame];
                UpdateBossFlipForLaser();
                UpdateLaserTransform(false);

                // Ramp the charge tell across the 34 wind-up frames.
                UpdateChargeTelegraph(frame / 33f);
            }
            yield return new WaitForSeconds(chargeFrameTime);
        }

        SetChargeTelegraphActive(false);

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
        // Per-stage scaling for the boss's own special attack (opt-in via
        // "Scale Bosses With Stage"). 1× when the toggle is off.
        float damagePerFrame = laserDamagePerSecond * fireFrameTime * BossStageDamageMultiplier;

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
        // Natural end of the attack: don't chop the event, let its own fade/tail run.
        StopLaserChargeSound(immediate: false);

        SetChargeTelegraphActive(false);

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

        // Track the boss's sorting LAYER only. The sorting ORDER is deliberately fixed
        // (set in InitializeLaser) and must NOT be re-derived from bossSprite.sortingOrder
        // here — that is what put the beam back underneath the obstacles every frame.
        if (bossSprite != null)
            laserRenderer.sortingLayerID = bossSprite.sortingLayerID;

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
        if (telegraphRoot != null) Destroy(telegraphRoot);
        if (spawnedHead != null && spawnedHead.gameObject != null)
            Destroy(spawnedHead.gameObject);
        if (HealthBar != null)
            Destroy(HealthBar.gameObject);

        StopLaserChargeSound(immediate: true);

        isLaserActive = false;
        isPerformingLaserAttack = false;
        if (laserRenderer != null) laserRenderer.enabled = false;
        SetChargeTelegraphActive(false);
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
