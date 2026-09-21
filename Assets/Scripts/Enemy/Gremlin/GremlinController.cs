using UnityEngine;
using System.Collections;

public class GremlinController : MonoBehaviour, IDamageable
{
    // Frames live at Assets/Resources/Sprites/EnemySprites/Gremlin/00.png .. 24.png.
    [Header("Sprites")]
    [Tooltip("Gremlin frames as DIRECT references, in order. When set, the legacy " +
             "Resources path below is ignored. The Gremlin has no EnemyData asset " +
             "(one is built in code at runtime), so the EnemyData migration tool " +
             "cannot reach it — assign these by hand on the prefab.")]
    [SerializeField] private Sprite[] gremlinSpriteFrames;

    // DEPRECATED fallback — used only when gremlinSpriteFrames is empty.
    private const string SpriteFolderPath = "Sprites/EnemySprites/Gremlin";

    [Header("Gremlin Settings")]
    public float fleeRange = 4f;
    [Tooltip("Hysteresis: once fleeing, the gremlin keeps hopping until the player " +
             "is this much FARTHER than fleeRange. Stops the idle/jump state (and the " +
             "sprite) from flickering when the player paces it at the flee boundary.")]
    public float fleeStopBuffer = 1.5f;
    public float playerSpeedPercent = 0.7f;
    public int energyDropCount = 3;
    public int energyDropValue = 10;

    [Header("Energy Aura")]
    public bool showAura = true;
    public Color auraColor = new Color(0.25f, 0.62f, 1f);   // energy blue
    public float auraPulseSpeed = 3f;
    [Range(0f, 1f)] public float auraMinAlpha = 0.12f;
    [Range(0f, 1f)] public float auraMaxAlpha = 0.42f;
    public float auraMinScale = 1.05f;
    public float auraMaxScale = 1.35f;

    [Header("Belly Glow")]
    public bool showBellyGlow = true;
    public Color bellyGlowColor = new Color(0.5f, 0.85f, 1f);
    [Range(0f, 1f)] public float bellyMinAlpha = 0.15f;
    [Range(0f, 1f)] public float bellyMaxAlpha = 0.55f;

    [Header("Idle Breathing")]
    public float idleBreathSpeed = 2.2f;
    [Range(0f, 0.3f)] public float idleBreathAmount = 0.06f;

    [Header("Jump / Hop (while fleeing)")]
    [Tooltip("Seconds per frame. frames × this = one full hop period, and it sets the " +
             "hop rhythm (lift, squash, and forward burst are all locked to this clock).")]
    public float jumpFrameTime = 0.04f;

    [Tooltip("How high (world units) the sprite lifts off the ground at the peak of a " +
             "hop. This is the visual that makes it a JUMP. The shadow stays on the ground.")]
    public float hopHeight = 0.8f;

    [Tooltip("Squash & stretch during a hop. Crouch before take-off, stretch on the " +
             "way up, squash on landing. 0 = frames only.")]
    [Range(0f, 0.5f)] public float hopSquashAmount = 0.16f;

    [Tooltip("How pulsed the forward motion is. 0 = constant glide (old sliding look). " +
             "1 = full stop-and-leap. Average speed is preserved either way.")]
    [Range(0f, 1f)] public float hopLurch = 1f;

    [Header("Hop Shadow")]
    public bool showShadow = true;
    public Color shadowColor = new Color(0f, 0f, 0f, 1f);
    [Range(0f, 1f)] public float shadowMaxAlpha = 0.40f; // grounded
    [Range(0f, 1f)] public float shadowMinAlpha = 0.14f; // at apex
    [Range(0.1f, 1f)] public float shadowFlatten = 0.45f; // ellipse squish

    [Header("Death VFX")]
    [Tooltip("< 1.0 → light 'classic chunks' disintegration (good for a small enemy). " +
             "≥ 1.0 → boss-style pixel-shatter (needs Read/Write on the PNGs).")]
    public float deathVfxDuration = 0.9f;

    private Rigidbody2D rb;
    private SpriteRenderer bodyRenderer;    // stays on the ground; Y-sort anchor + death-shatter source
    private SpriteRenderer spriteRenderer;  // the VISIBLE sprite, lives on the hopping child
    private Transform visual;               // child that lifts/squashes; body stays put
    private Transform playerTransform;

    private bool isFleeingFromPlayer = false;
    private bool isDead = false;
    private float moveSpeed;

    private Sprite[] gremlinSprites;
    private bool isJumping = false;

    // Shared hop clock — movement, frame, lift, and squash all read from this so they
    // stay phase-locked. That coupling is what makes it read as a jump, not a slide.
    private float hopElapsed = 0f;
    private float hopPeriod = 1f;
    private float hopSpeedMean = 1f;   // avg of the speed profile → bursts still average to moveSpeed
    private float currentHopFrac = 0f; // 0 grounded .. 1 apex, shared with the shadow

    private SpriteRenderer auraRenderer;
    private SpriteRenderer bellyRenderer;
    private SpriteRenderer shadowRenderer;
    private Transform shadowTf;
    private float auraBaseScale = 1f;
    private float bellyBaseScale = 1f;
    private float shadowBaseScale = 1f;

    private Vector3 baseScale = Vector3.one;               // body transform's authored scale (untouched)
    private readonly Vector3 visualBaseScale = Vector3.one; // child modulates around this

    void Awake()
    {
        SetupComponents();
        FindPlayer();
        CalculateMoveSpeed();
        ComputeHopSpeedMean();
    }

    void Start()
    {
        baseScale = transform.localScale;
        LoadSprites();
        SetupGremlinProperties();
        ShowRestingFrame();
        EnsureVisibility();
        SetupShadow();
        SetupAura();
    }

    void SetupComponents()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyRenderer = GetComponent<SpriteRenderer>();

        rb.gravityScale = 0f;
        rb.linearDamping = 5f;
        rb.freezeRotation = true;

        bodyRenderer.sortingLayerName = "Default";
        bodyRenderer.sortingOrder = 0; // Will be overridden by YSortEntity

        // Move the visible sprite onto a child so it can hop off the ground without
        // dragging the collider / Rigidbody / Y-sort with it. The body renderer stays
        // as the ground-anchored Y-sort source (kept invisible during play).
        //
        // FIX — THE "DOUBLE GREMLIN". This blindly created a new child every time, and
        // SetupComponents() is called from Awake(). GremlinSpawner builds its template
        // with `new GameObject("Gremlin")`, which is ACTIVE, so AddComponent<GremlinController>
        // ran Awake IMMEDIATELY and built a GremlinVisual on the TEMPLATE — before
        // SetActive(false). Instantiate then COPIED that child into every clone, and the
        // clone's own Awake built a SECOND one. Only the newest is driven by the hop
        // animation; the copied one kept the template's sprite and rode along on the
        // parent transform, reading as a second gremlin trailing the real one.
        //
        // (SetupShadow() escaped this because it is called from Start(), which never runs
        // on the template — it is deactivated in the same frame it is created.)
        //
        // Reusing an existing child makes this idempotent, so it stays correct no matter
        // how many times Awake runs or who clones a live gremlin.
        var existingVisual = transform.Find("GremlinVisual");
        var visualGo = existingVisual != null ? existingVisual.gameObject
                                              : new GameObject("GremlinVisual");
        visual = visualGo.transform;
        visual.SetParent(transform, false);
        visual.localPosition = Vector3.zero;

        spriteRenderer = visualGo.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = visualGo.AddComponent<SpriteRenderer>();
        spriteRenderer.sharedMaterial = bodyRenderer.sharedMaterial;
        spriteRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
        spriteRenderer.sortingOrder = bodyRenderer.sortingOrder;

        bodyRenderer.color = new Color(1f, 1f, 1f, 0f); // invisible, but still drives Y-sort

        // Y-Sort on the body (ground position), so lifting the visual never changes
        // how the gremlin sorts against grass/other entities.
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
            if (playerObject != null) playerTransform = playerObject.transform;
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

    /// Inject frames from GremlinSpawner, which owns the serialized references because
    /// the Gremlin itself is built in code and has no prefab asset. Must be called
    /// before LoadSprites() runs.
    public void SetSpriteFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0) return;
        gremlinSpriteFrames = frames;

        // DO NOT call LoadSprites() here.
        //
        // An earlier version did, "in case the spawner injected late". That re-ran the
        // whole resolve on the TEMPLATE object mid-construction — before
        // SetupVisuals() had hidden bodyRenderer — so the root renderer was left
        // opaque showing a static frame 0. Every spawned gremlin then had a second,
        // non-animating sprite sitting at ground level behind the hopping one.
        //
        // GremlinSpawner injects during CreateGremlinPrefab(), long before any clone
        // is activated, so the normal Awake/Start resolve picks these up anyway.
        // If the renderer is already live, refresh only the frame — never the colours.
        if (spriteRenderer != null && gremlinSprites == null)
        {
            gremlinSprites = (Sprite[])frames.Clone();
            System.Array.Sort(gremlinSprites, (a, b) => string.CompareOrdinal(a.name, b.name));
            spriteRenderer.sprite = gremlinSprites[0];
        }
    }

    void LoadSprites()
    {
        // FIX: this only ever read the hardcoded SpriteFolderPath const, so the Gremlin
        // was invisible to the EnemyData migration tool (it has no EnemyData asset —
        // GremlinController builds one in code at runtime). Moving Gremlin/ out of
        // Resources would have broken it completely, with only a warning at runtime.
        // A direct reference assigned on the prefab now takes priority.
        if (gremlinSpriteFrames != null && gremlinSpriteFrames.Length > 0)
        {
            // COPY, never alias. gremlinSpriteFrames is the SERIALIZED array owned by
            // GremlinSpawner. Assigning it directly made gremlinSprites the same object,
            // and the Array.Sort below then re-ordered the asset itself — a permanent,
            // silent edit to a scene/prefab asset caused by pressing Play.
            gremlinSprites = (Sprite[])gremlinSpriteFrames.Clone();
        }
        else
        {
            gremlinSprites = Resources.LoadAll<Sprite>(SpriteFolderPath);
            if (gremlinSprites != null && gremlinSprites.Length > 0)
                Debug.LogWarning($"[Gremlin] Frames still loading from Resources/{SpriteFolderPath}. " +
                                 "Assign Gremlin Sprite Frames on the Gremlin prefab so this art " +
                                 "can leave Resources/.");
        }

        if (gremlinSprites != null && gremlinSprites.Length > 0)
        {
            System.Array.Sort(gremlinSprites, (a, b) => string.CompareOrdinal(a.name, b.name));
            spriteRenderer.sprite = gremlinSprites[0];
            spriteRenderer.color = Color.white;
        }
        else
        {
            Debug.LogWarning($"[Gremlin] No sprites found at Resources/{SpriteFolderPath}. Using fallback.");
            spriteRenderer.sprite = CreateFallbackSprite();
            spriteRenderer.color = Color.green;
        }
    }

    void SetupGremlinProperties()
    {
        var grapplingTarget = GetComponent<GremlinGrapplingTarget>();
        if (grapplingTarget == null) grapplingTarget = gameObject.AddComponent<GremlinGrapplingTarget>();
        grapplingTarget.gremlinController = this;

        var enemyStats = GetComponent<EnemyStats>();
        if (enemyStats == null) enemyStats = gameObject.AddComponent<EnemyStats>();

        var enemyData = ScriptableObject.CreateInstance<EnemyData>();
        enemyData.enemyName = "Gremlin";
        enemyData.maxHealth = 1f;
        enemyData.moveSpeed = moveSpeed;
        enemyData.mass = 5f;
        // Hand the resolved frames to the runtime-built EnemyData as well, so anything
        // reading through EnemyData (EnemyAnimationController, the boss death-pose code)
        // works without touching Resources.
        if (gremlinSprites != null && gremlinSprites.Length > 0)
            enemyData.frames = gremlinSprites;
        else
            enemyData.spriteFolderPath = SpriteFolderPath;

        enemyStats.enemyData = enemyData;
        enemyStats.maxHealth = 1f;
        enemyStats.currentHealth = 1f;
        enemyStats.canDropEnergy = true;
        enemyStats.energyDropChance = 1f;
        enemyStats.energyDropValue = energyDropValue;
    }

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
        Color c = spriteRenderer.color;
        c.a = 1f;
        spriteRenderer.color = c;
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

        bool shouldFlee = isFleeingFromPlayer
            ? distanceToPlayer <= fleeRange + fleeStopBuffer
            : distanceToPlayer <= fleeRange;

        if (shouldFlee != isFleeingFromPlayer)
        {
            isFleeingFromPlayer = shouldFlee;
            UpdateAnimation();
        }

        if (isJumping)
            DriveJumpAnimation();
        else if (!isFleeingFromPlayer)
            ApplyIdleBreathing();
    }

    void FixedUpdate()
    {
        if (isDead) return;

        if (isFleeingFromPlayer && playerTransform != null)
        {
            Vector3 fleeDirection = (transform.position - playerTransform.position).normalized;

            // Speed pulses with the hop: burst on take-off / through the air, near-still
            // during crouch and landing. Normalised by the profile mean so the average
            // still equals moveSpeed (escape distance unchanged).
            float p = HopPhase();
            float profile = HopSpeedShape(p) / hopSpeedMean;
            float speedMul = Mathf.Lerp(1f, profile, hopLurch);

            rb.linearVelocity = fleeDirection * moveSpeed * speedMul;
        }
        else
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 3f);
        }
    }

    // Gentle inflate/deflate around the resting frame while grounded.
    void ApplyIdleBreathing()
    {
        if (!HasSprites()) return;

        float s = Mathf.Sin(Time.time * idleBreathSpeed) * idleBreathAmount;
        visual.localScale = new Vector3(
            visualBaseScale.x * (1f + s * 0.5f),
            visualBaseScale.y * (1f + s),
            visualBaseScale.z);
        currentHopFrac = 0f;
        visual.localPosition = Vector3.zero;
    }

    void UpdateAnimation()
    {
        if (!HasSprites()) return;

        if (isFleeingFromPlayer && !isJumping)
        {
            isJumping = true;
            hopElapsed = 0f; // begin each flee at the anticipation crouch
            hopPeriod = Mathf.Max(0.05f, gremlinSprites.Length * jumpFrameTime);
        }
        else if (!isFleeingFromPlayer && isJumping)
        {
            isJumping = false;
            ShowRestingFrame();
            visual.localScale = visualBaseScale;
            visual.localPosition = Vector3.zero;
            currentHopFrac = 0f;
        }
    }

    // Advances the hop clock and applies frame + lift + squash for the current phase.
    // FixedUpdate reads the same clock for the forward burst, so lift, stretch, and
    // motion all peak together — that's what sells the jump.
    void DriveJumpAnimation()
    {
        if (!HasSprites()) return;

        hopElapsed += Time.deltaTime;
        float p = HopPhase();

        int frame = Mathf.Clamp(Mathf.FloorToInt(p * gremlinSprites.Length), 0, gremlinSprites.Length - 1);
        spriteRenderer.sprite = gremlinSprites[frame];

        // Vertical arc — the actual lift off the ground.
        currentHopFrac = HopHeightFrac(p);
        visual.localPosition = new Vector3(0f, hopHeight * currentHopFrac, 0f);

        // Squash/stretch: crouch before take-off, stretch rising, squash on landing.
        float stretch = Mathf.Clamp(HopStretch(p), -1.3f, 1.3f);
        visual.localScale = new Vector3(
            visualBaseScale.x * (1f - stretch * hopSquashAmount * 0.55f),
            visualBaseScale.y * (1f + stretch * hopSquashAmount),
            visualBaseScale.z);
    }

    float HopPhase() => hopPeriod > 0f ? Mathf.Repeat(hopElapsed, hopPeriod) / hopPeriod : 0f;

    // Height arc: on the ground through the crouch (p<0.15) and after landing (p>0.80),
    // a smooth sine arch in between → leaves ground, peaks mid-air, lands.
    static float HopHeightFrac(float p)
    {
        const float takeoff = 0.15f, land = 0.80f;
        if (p < takeoff || p > land) return 0f;
        return Mathf.Sin((p - takeoff) / (land - takeoff) * Mathf.PI);
    }

    static float HopLeapBump(float p) => Mathf.Exp(-Mathf.Pow((p - 0.45f) / 0.20f, 2f));
    static float HopSpeedShape(float p) => 0.10f + HopLeapBump(p);

    // Squash envelope: anticipation crouch (~0.09), stretch on take-off/rise (~0.26),
    // hard squash on landing (~0.80).
    static float HopStretch(float p)
    {
        float takeoff = Mathf.Exp(-Mathf.Pow((p - 0.26f) / 0.15f, 2f));
        float landing = Mathf.Exp(-Mathf.Pow((p - 0.80f) / 0.08f, 2f));
        float anticip = Mathf.Exp(-Mathf.Pow((p - 0.09f) / 0.07f, 2f));
        return takeoff - 0.95f * landing - 0.75f * anticip;
    }

    void ComputeHopSpeedMean()
    {
        const int samples = 128;
        float sum = 0f;
        for (int i = 0; i < samples; i++) sum += HopSpeedShape((i + 0.5f) / samples);
        hopSpeedMean = Mathf.Max(0.0001f, sum / samples);
    }

    bool HasSprites() => gremlinSprites != null && gremlinSprites.Length > 0;

    // SHADOW / AURA / GLOW --------------------------------------------------

    void SetupShadow()
    {
        if (!showShadow) return;
        const float GlowSpriteUnits = 256f / 100f;

        float bodyW = 2f, feetY = -0.4f;
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Vector3 b = spriteRenderer.sprite.bounds.size;
            bodyW = b.x;
            feetY = -spriteRenderer.sprite.bounds.extents.y * 0.78f; // near the feet
        }
        shadowBaseScale = (bodyW * 0.75f) / GlowSpriteUnits;

        // Same idempotency guard as GremlinVisual above. The shadow is not currently
        // duplicated (SetupShadow runs from Start, which the template never reaches),
        // but that is an accident of call-site rather than a guarantee — one refactor
        // moving this into Awake would reintroduce the double-gremlin bug.
        var existingShadow = transform.Find("GremlinShadow");
        var go = existingShadow != null ? existingShadow.gameObject
                                        : new GameObject("GremlinShadow");
        shadowTf = go.transform;
        shadowTf.SetParent(transform, false);            // parented to BODY → stays on the ground
        shadowTf.localPosition = new Vector3(0f, feetY, 0f);

        shadowRenderer = go.GetComponent<SpriteRenderer>();
        if (shadowRenderer == null) shadowRenderer = go.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = GetGlowSprite();
        shadowRenderer.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowMaxAlpha);
        if (bodyRenderer != null)
        {
            shadowRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
            shadowRenderer.sortingOrder = bodyRenderer.sortingOrder;
        }
    }

    void SetupAura()
    {
        const float GlowSpriteUnits = 256f / 100f;

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
            float yOffset = (spriteRenderer != null && spriteRenderer.sprite != null)
                ? -spriteRenderer.sprite.bounds.size.y * 0.15f : -0.3f;
            bellyRenderer = CreateGlowChild("GremlinBellyGlow", bellyGlowColor, yOffset);
            bellyBaseScale = fit * 0.45f;
        }
    }

    // Aura + belly parent to the VISUAL so they ride the hop up with the body.
    SpriteRenderer CreateGlowChild(string name, Color color, float yOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(visual, false);
        go.transform.localPosition = new Vector3(0f, yOffset, 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetGlowSprite();
        sr.color = new Color(color.r, color.g, color.b, 0f);
        if (bodyRenderer != null)
        {
            sr.sortingLayerID = bodyRenderer.sortingLayerID;
            sr.sortingOrder = bodyRenderer.sortingOrder;
        }
        return sr;
    }

    // Keeps the child renderers' sorting locked to the body's (Y-sorted) order and
    // drives the aura/belly pulse + the shadow's reaction to hop height. LateUpdate,
    // so it reads the body order after YSortEntity has set it this frame.
    void LateUpdate()
    {
        if (isDead) return;

        int baseOrder = (bodyRenderer != null) ? bodyRenderer.sortingOrder : 0;
        int layerId = (bodyRenderer != null) ? bodyRenderer.sortingLayerID : 0;

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingLayerID = layerId;
            spriteRenderer.sortingOrder = baseOrder; // the visible sprite sits at the body's order
        }

        float auraPulse = Mathf.Sin(Time.time * auraPulseSpeed) * 0.5f + 0.5f;
        float bellyPulse = Mathf.Sin(Time.time * auraPulseSpeed + 1.1f) * 0.5f + 0.5f;

        if (auraRenderer != null)
        {
            auraRenderer.sortingLayerID = layerId;
            auraRenderer.sortingOrder = baseOrder - 1;
            float s = auraBaseScale * Mathf.Lerp(auraMinScale, auraMaxScale, auraPulse);
            auraRenderer.transform.localScale = new Vector3(s, s, 1f);
            var c = auraColor; c.a = Mathf.Lerp(auraMinAlpha, auraMaxAlpha, auraPulse);
            auraRenderer.color = c;
        }

        if (bellyRenderer != null)
        {
            bellyRenderer.sortingLayerID = layerId;
            bellyRenderer.sortingOrder = baseOrder + 1;
            float s = bellyBaseScale * Mathf.Lerp(0.9f, 1.1f, bellyPulse);
            bellyRenderer.transform.localScale = new Vector3(s, s, 1f);
            var c = bellyGlowColor; c.a = Mathf.Lerp(bellyMinAlpha, bellyMaxAlpha, bellyPulse);
            bellyRenderer.color = c;
        }

        if (shadowRenderer != null)
        {
            shadowRenderer.sortingLayerID = layerId;
            shadowRenderer.sortingOrder = baseOrder - 2; // under everything of the gremlin
            // Higher hop → smaller, fainter shadow.
            float s = shadowBaseScale * Mathf.Lerp(1f, 0.6f, currentHopFrac);
            shadowTf.localScale = new Vector3(s, s * shadowFlatten, 1f);
            var c = shadowColor; c.a = Mathf.Lerp(shadowMaxAlpha, shadowMinAlpha, currentHopFrac);
            shadowRenderer.color = c;
        }
    }

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
                a = a * a;
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
            for (int y = 0; y < size; y++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                colors[y * size + x] = distance <= radius ? new Color(0.2f, 0.8f, 0.2f, 1f) : Color.clear;
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

        // Settle to a grounded resting pose. The disintegration snapshots the BODY
        // renderer (first SpriteRenderer found), so make it show the resting frame at
        // full alpha and hide the hopping child, giving a clean silhouette to shatter.
        isJumping = false;
        currentHopFrac = 0f;
        if (visual != null)
        {
            visual.localPosition = Vector3.zero;
            visual.localScale = visualBaseScale;
        }
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        if (bodyRenderer != null)
        {
            if (HasSprites()) bodyRenderer.sprite = gremlinSprites[0];
            bodyRenderer.color = Color.white;
        }

        // Make the sibling EnemyStats agree that this Gremlin is dead.
        // BUGFIX: Die() only set the LOCAL isDead flag, so for the whole
        // disintegration the EnemyStats still reported IsDead() == false with
        // currentHealth > 0. Anything scanning for living enemies - the Berserk's
        // prey search, the Buffer's ally search, CountLivingEnemiesInScene - kept
        // counting the corpse. Redundant on the EnemyStats.PerformDeath path (health
        // is already 0 there); load-bearing on the direct OnTriggerEnter2D path
        // below, which bypasses EnemyStats entirely.
        var stats = GetComponent<EnemyStats>();
        if (stats != null) stats.currentHealth = 0f;

        PlayDeathSound();
        SpawnEnergyDrops();

        // Shared death book-keeping: wave counter -> augment 335 tithe ->
        // EnergyManager kill event -> attribution cleanup, in that exact order.
        // BUGFIX: this used to call EnergyManager.OnEnemyKilled alone - the same gap
        // FireCommonDeathHooks was written to close for the bosses, EyeStats and
        // BomberController. The Gremlin was missed, so a dead Gremlin never
        // decremented WaveSpawner's live counter, never paid the Energy Tithe (335)
        // on a tower kill, and leaked a TowerKillAttribution entry per corpse.
        //
        // FireCommonDeathHooks fires EnergyManager.OnEnemyKilled itself (step 3), so
        // the direct call that used to be here is gone rather than duplicated -
        // double-firing it would double-count the kill for every OnEnemyKilledEvent
        // subscriber (Berserker's Fury stacks, Health on Kill, Lifesteal,
        // Energy Scavenging).
        // notifyWaveSpawner: FALSE. The Gremlin is treated as an ambient, non-combat
        // enemy everywhere else - GameOrchestrator.CountLivingEnemiesInScene skips it,
        // FriendlyFire skips it, the Buffer and Scarecrow skip it - and this path
        // NEVER decremented WaveSpawner's counter before. Passing true would start
        // decrementing a counter that may never have been incremented for a Gremlin,
        // which would complete waves early. Keeping it false makes this change purely
        // additive: the tithe and attribution cleanup are gained, wave behaviour is
        // byte-identical to before. Flip to true ONLY if Gremlins are wave-spawned.
        EnemyStats.FireCommonDeathHooks(gameObject, notifyWaveSpawner: false);

        TriggerDisintegration();

        // The Gremlin never calls Destroy itself - EnemyDeathVFX owns teardown. If
        // that effect fails to complete, the corpse stays in the scene forever and
        // keeps counting as a living enemy, which can stall a stage. Same guarantee
        // EnemyStats.ScheduleDeathFailsafe gives the paths it owns (that method is
        // protected and this is not an EnemyStats, hence the inline Destroy).
        Destroy(gameObject, Mathf.Max(0f, deathVfxDuration) + 5f);
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

    private void TriggerDisintegration()
    {
        string sourceTexturePath = null;
        if (bodyRenderer != null && bodyRenderer.sprite != null)
            sourceTexturePath = $"{SpriteFolderPath}/{bodyRenderer.sprite.name}";

        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: deathVfxDuration,
            onComplete: null,
            sourceTexturePath: sourceTexturePath);
    }

    private void PlayDeathSound()
    {
        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.gremlinDeath, transform.position);
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

                if (!otherEnemiesMovingFast) ForceImmediateDeath();
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



