using UnityEngine;
using System.Collections;

public class GremlinController : MonoBehaviour, IDamageable
{
    // Frames live at Assets/Resources/Sprites/EnemySprites/Gremlin/00.png .. 24.png.
    // NOTE: capital "G" — the old code loaded from "...gremlin" which silently
    // failed on case-sensitive builds and fell back to the green dummy sprite.
    private const string SpriteFolderPath = "Sprites/EnemySprites/Gremlin";

    [Header("Gremlin Settings")]
    public float fleeRange = 4f;
    [Tooltip("Hysteresis: once fleeing, the gremlin keeps hopping until the player " +
             "is this much FARTHER than fleeRange. Prevents the idle/jump state from " +
             "flickering (and the sprite from blinking) when the player paces it right " +
             "at the edge of the flee range.")]
    public float fleeStopBuffer = 1.5f;
    public float playerSpeedPercent = 0.7f;
    public int energyDropCount = 3;
    public int energyDropValue = 10;

    [Header("Energy Aura")]
    [Tooltip("Soft pulsing halo drawn behind the gremlin.")]
    public bool showAura = true;
    public Color auraColor = new Color(0.25f, 0.62f, 1f);   // energy blue
    public float auraPulseSpeed = 3f;
    [Range(0f, 1f)] public float auraMinAlpha = 0.12f;
    [Range(0f, 1f)] public float auraMaxAlpha = 0.42f;
    [Tooltip("Aura size relative to the body, at the trough and peak of the pulse.")]
    public float auraMinScale = 1.05f;
    public float auraMaxScale = 1.35f;

    [Header("Belly Glow")]
    [Tooltip("Small pulsing glow over the belly, drawn in front of the gremlin.")]
    public bool showBellyGlow = true;
    public Color bellyGlowColor = new Color(0.5f, 0.85f, 1f);
    [Range(0f, 1f)] public float bellyMinAlpha = 0.15f;
    [Range(0f, 1f)] public float bellyMaxAlpha = 0.55f;

    [Header("Idle Breathing")]
    [Tooltip("How fast the resting gremlin inflates/deflates (radians per second).")]
    public float idleBreathSpeed = 2.2f;
    [Tooltip("Vertical scale swing of the breathing pulse. 0.06 = ±6%. " +
             "Horizontal swings at half this so it reads as an inflate, not a wobble.")]
    [Range(0f, 0.3f)]
    public float idleBreathAmount = 0.06f;

    [Header("Jump Animation (while fleeing)")]
    [Tooltip("Seconds per frame for the jump cycle. 25 frames × this = one full hop. " +
             "Lower = faster, more frantic hopping.")]
    public float jumpFrameTime = 0.04f;

    [Header("Death VFX")]
    [Tooltip("Passed to EnemyDeathVFX.Trigger on death.\n" +
             "< 1.0  → light 'classic chunks' disintegration (recommended for a small enemy).\n" +
             "≥ 1.0  → full boss-style pixel-shatter of the actual sprite " +
             "(needs 'Read/Write Enabled' on the Gremlin PNGs, and adds a dust shockwave).")]
    public float deathVfxDuration = 0.9f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Transform playerTransform;
    private bool isFleeingFromPlayer = false;
    private bool isDead = false;
    private float moveSpeed;

    private Sprite[] gremlinSprites;
    private Coroutine currentAnimationCoroutine;
    private bool isJumping = false;

    private SpriteRenderer auraRenderer;
    private SpriteRenderer bellyRenderer;
    private float auraBaseScale = 1f;
    private float bellyBaseScale = 1f;

    // Captured once so the breathing pulse and jump animation modulate around the
    // gremlin's authored scale rather than assuming (1,1,1).
    private Vector3 baseScale = Vector3.one;

    void Awake()
    {
        SetupComponents();
        FindPlayer();
        CalculateMoveSpeed();
    }

    void Start()
    {
        baseScale = transform.localScale;
        LoadSprites();
        SetupGremlinProperties();
        ShowRestingFrame();
        EnsureVisibility();
        SetupAura();
    }

    void SetupComponents()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.gravityScale = 0f;
        rb.linearDamping = 5f;
        rb.freezeRotation = true;

        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 0; // Will be overridden by YSortEntity

        // Y-Sort: dynamically sort against grass based on Y position
        if (GetComponent<YSortEntity>() == null)
        {
            var ysort = gameObject.AddComponent<YSortEntity>();
            ysort.sortPrecision = 10f;
            ysort.sortOrderBase = 1000;
            ysort.sortYOffset = -0.2f;
        }

        gameObject.tag = "Enemy";
        gameObject.layer = LayerMask.NameToLayer("Enemy");
    }

    void FindPlayer()
    {
        var playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
        }
        else
        {
            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                playerTransform = playerObject.transform;
            }
        }
    }

    void CalculateMoveSpeed()
    {
        if (playerTransform != null)
        {
            var playerStats = playerTransform.GetComponent<PlayerStats>();
            if (playerStats != null)
            {
                moveSpeed = playerStats.moveSpeed * playerSpeedPercent;
                return;
            }
        }
        moveSpeed = 3.5f;
    }

    void LoadSprites()
    {
        gremlinSprites = Resources.LoadAll<Sprite>(SpriteFolderPath);

        if (gremlinSprites != null && gremlinSprites.Length > 0)
        {
            // LoadAll does not guarantee order; sort by name so 00..24 line up.
            System.Array.Sort(gremlinSprites, (a, b) => string.CompareOrdinal(a.name, b.name));
            spriteRenderer.sprite = gremlinSprites[0];
            // The spawner's placeholder prefab leaves the tint RED. Clear it so the
            // real sprites render at their true colour instead of a red-tinted "old"
            // gremlin flashing through.
            spriteRenderer.color = Color.white;
        }
        else
        {
            Debug.LogWarning($"[Gremlin] No sprites found at Resources/{SpriteFolderPath}. " +
                             $"Using fallback sprite. Check the folder name/case.");
            spriteRenderer.sprite = CreateFallbackSprite();
            spriteRenderer.color = Color.green;
        }
    }

    void SetupGremlinProperties()
    {
        var grapplingTarget = GetComponent<GremlinGrapplingTarget>();
        if (grapplingTarget == null)
        {
            grapplingTarget = gameObject.AddComponent<GremlinGrapplingTarget>();
        }
        grapplingTarget.gremlinController = this;

        var enemyStats = GetComponent<EnemyStats>();
        if (enemyStats == null)
        {
            enemyStats = gameObject.AddComponent<EnemyStats>();
        }

        var enemyData = ScriptableObject.CreateInstance<EnemyData>();
        enemyData.enemyName = "Gremlin";
        enemyData.maxHealth = 1f;
        enemyData.moveSpeed = moveSpeed;
        enemyData.mass = 5f;
        enemyData.spriteFolderPath = SpriteFolderPath;

        enemyStats.enemyData = enemyData;
        enemyStats.maxHealth = 1f;
        enemyStats.currentHealth = 1f;
        enemyStats.canDropEnergy = true;

        enemyStats.energyDropChance = 1f;
        enemyStats.energyDropValue = energyDropValue;
    }

    // Frame 0 is the standing/resting pose. Idle just holds it while the
    // breathing pulse in Update() scales it in and out.
    void ShowRestingFrame()
    {
        if (HasSprites()) spriteRenderer.sprite = gremlinSprites[0];
    }

    void EnsureVisibility()
    {
        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = CreateFallbackSprite();
            spriteRenderer.color = Color.red;
        }
        Color currentColor = spriteRenderer.color;
        currentColor.a = 1f;
        spriteRenderer.color = currentColor;
        // sortingOrder is managed by YSortEntity — don't override it here
    }

    void Update()
    {
        if (isDead) return;

        if (playerTransform == null)
        {
            FindPlayer();
            if (playerTransform == null)
            {
                if (isFleeingFromPlayer)
                {
                    isFleeingFromPlayer = false;
                    UpdateAnimation();
                }
                ApplyIdleBreathing();
                return;
            }
        }

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // Hysteresis: start fleeing at fleeRange, but don't STOP until the player is
        // a buffer farther away. Without this the state flips every few frames when
        // the player paces the gremlin at the boundary, restarting the jump and
        // blinking the resting frame between hops.
        bool shouldFlee = isFleeingFromPlayer
            ? distanceToPlayer <= fleeRange + fleeStopBuffer
            : distanceToPlayer <= fleeRange;

        if (shouldFlee != isFleeingFromPlayer)
        {
            isFleeingFromPlayer = shouldFlee;
            UpdateAnimation();
        }

        // Breathing only reads while resting; the jump frames carry their own
        // squash/stretch, so we leave scale at baseScale during the flee.
        if (!isFleeingFromPlayer)
            ApplyIdleBreathing();
    }

    void FixedUpdate()
    {
        if (isDead) return;

        if (isFleeingFromPlayer && playerTransform != null)
        {
            Vector3 fleeDirection = (transform.position - playerTransform.position).normalized;
            rb.linearVelocity = fleeDirection * moveSpeed;
        }
        else
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 3f);
        }
    }

    // Gentle inflate/deflate around the resting frame. Vertical swings a little
    // more than horizontal so it reads as breathing, not a bounce.
    void ApplyIdleBreathing()
    {
        if (!HasSprites()) return;

        float s = Mathf.Sin(Time.time * idleBreathSpeed) * idleBreathAmount;
        transform.localScale = new Vector3(
            baseScale.x * (1f + s * 0.5f),
            baseScale.y * (1f + s),
            baseScale.z);
    }

    void UpdateAnimation()
    {
        if (!HasSprites()) return;

        if (isFleeingFromPlayer && !isJumping)
        {
            isJumping = true;
            StartJumpAnimation();
        }
        else if (!isFleeingFromPlayer && isJumping)
        {
            isJumping = false;
            StopAnimation();
            ShowRestingFrame();
        }
    }

    // Loops the whole 00..24 jump cycle. Because the last frame lands back on
    // the standing pose, looping it reads as continuous hopping.
    void StartJumpAnimation()
    {
        StopAnimation();
        // Neutralise any breathing squash before the hop cycle takes over.
        transform.localScale = baseScale;

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer, gremlinSprites, true,
            gremlinSprites.Length, 0, jumpFrameTime));
    }

    void StopAnimation()
    {
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }
    }

    bool HasSprites() => gremlinSprites != null && gremlinSprites.Length > 0;

    // AURA / BELLY GLOW ----------------------------------------------------

    // Builds the child glow objects, sized to the body. The aura sits behind the
    // gremlin and the belly glow in front; both pulse in LateUpdate so their
    // sorting can follow YSortEntity's per-frame changes to the body order.
    void SetupAura()
    {
        // Diameter (world units) of the generated glow sprite at localScale 1.
        const float GlowSpriteUnits = 256f / 100f; // 256 px @ 100 PPU

        float bodyMax = 2f;
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Vector3 b = spriteRenderer.sprite.bounds.size;
            bodyMax = Mathf.Max(b.x, b.y);
        }
        float fit = bodyMax / GlowSpriteUnits;

        if (showAura)
        {
            auraRenderer = CreateGlowChild("GremlinAura", auraColor, 0f);
            auraBaseScale = fit;
        }

        if (showBellyGlow)
        {
            // Sit the belly glow slightly below centre, over the lower body.
            float yOffset = (spriteRenderer != null && spriteRenderer.sprite != null)
                ? -spriteRenderer.sprite.bounds.size.y * 0.15f
                : -0.3f;
            bellyRenderer = CreateGlowChild("GremlinBellyGlow", bellyGlowColor, yOffset);
            bellyBaseScale = fit * 0.45f;
        }
    }

    SpriteRenderer CreateGlowChild(string name, Color color, float yOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, yOffset, 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetGlowSprite();
        sr.color = new Color(color.r, color.g, color.b, 0f);
        // Match the body's sorting layer; exact order is set each frame in LateUpdate.
        if (spriteRenderer != null)
        {
            sr.sortingLayerID = spriteRenderer.sortingLayerID;
            sr.sortingOrder = spriteRenderer.sortingOrder;
        }
        return sr;
    }

    // Drives the aura + belly pulse. Runs in LateUpdate so it reads the body's
    // sorting order AFTER YSortEntity has updated it this frame.
    void LateUpdate()
    {
        if (isDead) return;
        if (auraRenderer == null && bellyRenderer == null) return;

        int baseOrder = (spriteRenderer != null) ? spriteRenderer.sortingOrder : 0;
        int layerId = (spriteRenderer != null) ? spriteRenderer.sortingLayerID : 0;

        // 0..1 pulse; belly runs slightly out of phase so the two don't beat as one.
        float auraPulse = Mathf.Sin(Time.time * auraPulseSpeed) * 0.5f + 0.5f;
        float bellyPulse = Mathf.Sin(Time.time * auraPulseSpeed + 1.1f) * 0.5f + 0.5f;

        if (auraRenderer != null)
        {
            auraRenderer.sortingLayerID = layerId;
            auraRenderer.sortingOrder = baseOrder - 1; // behind the body
            float s = auraBaseScale * Mathf.Lerp(auraMinScale, auraMaxScale, auraPulse);
            auraRenderer.transform.localScale = new Vector3(s, s, 1f);
            var c = auraColor; c.a = Mathf.Lerp(auraMinAlpha, auraMaxAlpha, auraPulse);
            auraRenderer.color = c;
        }

        if (bellyRenderer != null)
        {
            bellyRenderer.sortingLayerID = layerId;
            bellyRenderer.sortingOrder = baseOrder + 1; // in front of the body
            float s = bellyBaseScale * Mathf.Lerp(0.9f, 1.1f, bellyPulse);
            bellyRenderer.transform.localScale = new Vector3(s, s, 1f);
            var c = bellyGlowColor; c.a = Mathf.Lerp(bellyMinAlpha, bellyMaxAlpha, bellyPulse);
            bellyRenderer.color = c;
        }
    }

    // Soft radial glow, generated once and shared by every gremlin. Alpha-blended;
    // swap the SpriteRenderer's material for an additive one if you want a neon look.
    private static Sprite _glowSprite;
    private static Sprite GetGlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;

        int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var px = new Color[size * size];
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / r;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // soft, fast falloff toward the edge
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
        return _glowSprite;
    }

    Sprite CreateFallbackSprite()
    {
        int size = 32;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                colors[y * size + x] = distance <= radius ? new Color(0.2f, 0.8f, 0.2f, 1f) : Color.clear;
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (isDead) return false;
        Die(damageSource);
        return true;
    }

    public bool CanTakeDamage() => !isDead;
    public float GetCurrentHealth() => isDead ? 0f : 1f;
    public float GetMaxHealth() => 1f;
    public float GetHealthPercentage() => isDead ? 0f : 1f;
    public bool IsDestroyed() => isDead;

    public void Die(GameObject killer = null)
    {
        if (isDead) return;
        isDead = true;

        StopAnimation();
        // Snap back to the true resting shape so the disintegration snapshot
        // isn't taken mid-breath (would shatter a slightly squashed silhouette).
        transform.localScale = baseScale;

        PlayDeathSound();
        SpawnEnergyDrops();

        if (EnergyManager.Instance != null)
            EnergyManager.Instance.OnEnemyKilled(gameObject);

        TriggerDisintegration();
    }

    private void SpawnEnergyDrops()
    {
        int stageIndex = GameOrchestrator.Instance?.CurrentStageIndex ?? 0;
        for (int i = 0; i < energyDropCount; i++)
        {
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle * 0.5f;
            EnergyDropManager.TrySpawnEnemyDrop(spawnPos, stageIndex);
        }
    }

    // Hands the gremlin to the shared enemy disintegration system. It snapshots
    // the current sprite, disables this object's renderer/collider/rigidbody and
    // scripts, plays the shatter + embers, and destroys the GameObject when done
    // — so no manual fade/Destroy is needed here anymore.
    private void TriggerDisintegration()
    {
        // Only used by the ≥1.0 pixel-shatter path; harmless otherwise. Lets the
        // real purple sprite shatter IF the PNGs have Read/Write enabled.
        string sourceTexturePath = null;
        if (spriteRenderer != null && spriteRenderer.sprite != null)
            sourceTexturePath = $"{SpriteFolderPath}/{spriteRenderer.sprite.name}";

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: deathVfxDuration,
            onComplete: null,
            sourceTexturePath: sourceTexturePath);
    }

    private void PlayDeathSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinDeath, transform.position);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead) return;
        if (IsPlayerAttack(other)) Die(other.gameObject);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;
        if (IsPlayerAttack(collision.collider)) Die(collision.gameObject);
    }

    bool IsPlayerAttack(Collider2D other)
    {
        return other.CompareTag("Player") || other.GetComponent<PlayerMovement>() ||
               other.GetComponent<Weapon>() || other.GetComponent<Projectile>() ||
               other.GetComponent<WeaponProjectile>();
    }
}

public class GremlinGrapplingTarget : MonoBehaviour, IGrapplingTarget
{
    [System.NonSerialized] public GremlinController gremlinController;
    private bool isDestroyed = false;

    void Awake()
    {
        gremlinController = GetComponent<GremlinController>();
    }

    void Update()
    {
        if (!isDestroyed && gremlinController != null && !gremlinController.IsDestroyed())
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null && rb.linearVelocity.magnitude > 15f)
            {
                var enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
                bool otherEnemiesMovingFast = false;
                foreach (var enemy in enemies)
                {
                    if (enemy != null && enemy.gameObject != gameObject)
                    {
                        var enemyRb = enemy.GetComponent<Rigidbody2D>();
                        if (enemyRb != null && enemyRb.linearVelocity.magnitude > 3f)
                        {
                            otherEnemiesMovingFast = true;
                            break;
                        }
                    }
                }

                if (!otherEnemiesMovingFast)
                {
                    ForceImmediateDeath();
                }
            }
        }
    }

    public bool CanBeGrappled() => !isDestroyed && gremlinController != null && !gremlinController.IsDestroyed();
    public Vector3 GetGrapplePoint() => isDestroyed ? Vector3.zero : transform.position;
    public bool IsSolidTarget() => false;
    public Transform GetTransform() => isDestroyed ? null : transform;

    public void OnGrappleHit(object hook)
    {
        if (!isDestroyed) ForceImmediateDeath();
    }

    public void OnGrappleRelease() { }

    public void ApplyGrapplePull(Vector3 direction, float force)
    {
        if (!isDestroyed) ForceImmediateDeath();
    }

    void ForceImmediateDeath()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        gremlinController?.Die(gameObject);

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        var colliders = GetComponents<Collider2D>();
        foreach (var col in colliders) col.enabled = false;

        enabled = false;
    }
}


