using UnityEngine;
using System.Collections.Generic;


// Deployable decoy device. Attracts enemies within radius for a duration,

public class DecoyDevice : MonoBehaviour
{
    // Config (set by Initialize)
    private float attractRadius = 5f;
    private float duration = 7f;
    private float bossDuration = 3f;
    private float armDelay = 0.3f;
    // Per-boss nudge for the confusion marks. Defaults to zero: the marks are
    // anchored to the boss's head automatically, so this is only a deliberate
    // offset if you ever want one. (Was (-1, 2), which pushed them off to the side.)
    private Vector2 bossVFXOffset = Vector2.zero;

    // State
    private bool isArmed = false;
    private float armTimer;
    private float lifeTimer;
    private bool isExpired = false;
    private bool isDisintegrating = false;

    // ── Audio ────────────────────────────────────────────────────────────
    // DecoyAmbience is a REPEATING ONE-SHOT rather than a held looping instance:
    // the decoy never moves, so a fresh 3D one-shot at its position spatialises
    // exactly like every other sound in the game (AudioManager.PlayOneShot) and
    // there is no instance to leak if the decoy is destroyed mid-pulse.
    //
    // Public rather than [SerializeField] because DecoyDevice is built in code by
    // DecoyLauncherSystem (AddComponent on a bare GameObject), so there is no
    // prefab inspector to set it in — change the default here, or assign it from
    // DecoyLauncherSystem.PlaceDecoy before/after Initialize.
    [Tooltip("Seconds between DecoyAmbience pulses while the decoy is armed. 0 disables the sound.")]
    public float ambienceInterval = 4f;
    private float ambienceTimer = 0f;

    // Tracked lured enemies so we can release them on expire
    private readonly Dictionary<EnemyController, DecoyConfusionVFX> luredEnemies
        = new Dictionary<EnemyController, DecoyConfusionVFX>();

    // Per-enemy lure timers (for bosses with shorter duration)
    private readonly Dictionary<EnemyController, float> lureTimers
        = new Dictionary<EnemyController, float>();

    // Visuals
    private SpriteRenderer bodyRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer pulseRingRenderer;
    private SpriteRenderer pulseRingRenderer2; // second staggered wave
    private SpriteRenderer groundHaloRenderer;  // soft ground glow
    private SpriteRenderer antennaGlowRenderer;
    private float pulseTimer;
    private float spawnScale = 0f;
    private const float DECOY_SCALE = 1.6f;

    private static readonly Color DECOY_GLOW = new Color(0.2f, 0.75f, 0.9f, 0.9f);
    private static readonly Color DECOY_GLOW_OFF = new Color(0.2f, 0.75f, 0.9f, 0.1f);
    private static readonly Color PULSE_COLOR = new Color(0.3f, 0.85f, 1f, 0.45f);

    private const float SORT_PRECISION = 10f;
    private const int SORT_ORDER_BASE = 1000;

    public bool IsExpired => isExpired;
    public bool IsDisintegrating => isDisintegrating;

    public void Initialize(float attractRadius, float duration, float bossDuration, float armDelay, Vector2 bossVFXOffset)
    {
        this.attractRadius = attractRadius;
        this.duration = duration;
        this.bossDuration = bossDuration;
        this.armDelay = armDelay;
        this.bossVFXOffset = bossVFXOffset;
        this.armTimer = armDelay;
        this.lifeTimer = duration;
    }

    private void Start()
    {
        BuildVisual();
        spawnScale = 0f;
    }

    private void Update()
    {
        if (isDisintegrating || isExpired) return;

        // Pop-in animation
        if (spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.2f, 1f);
            float ease = 1f + 2.7f * Mathf.Pow(spawnScale - 1f, 3f) + 1.7f * Mathf.Pow(spawnScale - 1f, 2f);
            transform.localScale = Vector3.one * ease * DECOY_SCALE;
        }

        // Arm delay
        if (!isArmed)
        {
            armTimer -= Time.deltaTime;
            if (armTimer <= 0f)
                isArmed = true;
        }

        // Life timer
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Expire();
            return;
        }

        // Glow pulse — brighter, larger breathing
        if (glowRenderer != null)
        {
            float interval = isArmed ? 0.8f : 2.4f;
            pulseTimer += Time.deltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin((pulseTimer / interval) * Mathf.PI * 2f);
            glowRenderer.color = Color.Lerp(DECOY_GLOW_OFF, DECOY_GLOW, pulse);
            glowRenderer.transform.localScale = Vector3.one * (0.35f + pulse * 0.12f);
        }

        // Antenna glow flicker
        if (antennaGlowRenderer != null)
        {
            float flicker = 0.6f + 0.4f * Mathf.Sin(Time.time * 8f);
            antennaGlowRenderer.color = new Color(DECOY_GLOW.r, DECOY_GLOW.g, DECOY_GLOW.b, flicker * 0.9f);
        }

        // Ground halo — gentle pulsing glow on the floor
        if (groundHaloRenderer != null && isArmed)
        {
            float haloPulse = 0.35f + 0.2f * Mathf.Sin(Time.time * 2.5f);
            groundHaloRenderer.color = new Color(0.2f, 0.75f, 0.9f, haloPulse);
            float haloScale = 1.6f + 0.15f * Mathf.Sin(Time.time * 2.5f);
            groundHaloRenderer.transform.localScale = Vector3.one * haloScale;
        }

        // Expanding pulse rings — two staggered radar waves
        if (isArmed)
        {
            // Ring 1
            if (pulseRingRenderer != null)
            {
                float ringCycle1 = Mathf.Repeat(Time.time * 0.5f, 1f);
                float ringScale1 = Mathf.Lerp(0.4f, 3.2f, ringCycle1);
                float ringAlpha1 = Mathf.Lerp(0.5f, 0f, ringCycle1 * ringCycle1);
                pulseRingRenderer.transform.localScale = Vector3.one * ringScale1;
                pulseRingRenderer.color = new Color(PULSE_COLOR.r, PULSE_COLOR.g, PULSE_COLOR.b, ringAlpha1);
            }
            // Ring 2 — offset by half a cycle
            if (pulseRingRenderer2 != null)
            {
                float ringCycle2 = Mathf.Repeat(Time.time * 0.5f + 0.5f, 1f);
                float ringScale2 = Mathf.Lerp(0.4f, 3.2f, ringCycle2);
                float ringAlpha2 = Mathf.Lerp(0.5f, 0f, ringCycle2 * ringCycle2);
                pulseRingRenderer2.transform.localScale = Vector3.one * ringScale2;
                pulseRingRenderer2.color = new Color(PULSE_COLOR.r, PULSE_COLOR.g, PULSE_COLOR.b, ringAlpha2);
            }
        }

        if (pulseRingRenderer != null)
            pulseRingRenderer.transform.Rotate(0, 0, 25f * Time.deltaTime);
        if (pulseRingRenderer2 != null)
            pulseRingRenderer2.transform.Rotate(0, 0, -18f * Time.deltaTime);

        // Y-sort
        if (bodyRenderer != null)
        {
            float sortY = transform.position.y - 0.15f;
            int order = SORT_ORDER_BASE + Mathf.RoundToInt(-sortY * SORT_PRECISION);
            bodyRenderer.sortingOrder = order;
            if (glowRenderer != null) glowRenderer.sortingOrder = order + 1;
            if (antennaGlowRenderer != null) antennaGlowRenderer.sortingOrder = order + 2;
            if (pulseRingRenderer != null) pulseRingRenderer.sortingOrder = order - 1;
        }

        if (isArmed)
            AttractEnemies();

        UpdateAmbience();
    }

    // Pulses DecoyAmbience every `ambienceInterval` seconds for as long as the decoy
    // is armed and alive. The first pulse lands the moment it arms (ambienceTimer
    // starts at 0), which is just after the decoySetup one-shot Weapon.cs fires on
    // placement — so the two read as "deployed … then it starts working" rather than
    // stacking on the same frame.
    //
    // Nothing needs to stop this: Update() early-returns once the decoy expires or
    // starts disintegrating, so the pulses simply stop arriving.
    private void UpdateAmbience()
    {
        if (!isArmed || ambienceInterval <= 0f) return;

        ambienceTimer -= Time.deltaTime;
        if (ambienceTimer > 0f) return;

        ambienceTimer = ambienceInterval;

        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.decoyAmbience.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.decoyAmbience, transform.position);
        }
    }

    private bool IsBoss(GameObject go)
    {
        return go.GetComponent<Boss1>() != null || go.GetComponent<BaseBossStats>() != null;
    }

    private void AttractEnemies()
    {
        Vector2 pos = transform.position;
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");

        HashSet<EnemyController> stillInRange = new HashSet<EnemyController>();

        foreach (var go in enemies)
        {
            if (go == null || !go.activeInHierarchy) continue;
            float dist = Vector2.Distance(pos, (Vector2)go.transform.position);

            var controller = go.GetComponent<EnemyController>();
            if (controller == null) continue;

            if (dist <= attractRadius)
            {
                stillInRange.Add(controller);

                if (!luredEnemies.ContainsKey(controller))
                {
                    // Newly lured
                    controller.SetDecoyTarget(transform);
                    var vfx = AttachConfusionVFX(go);
                    luredEnemies[controller] = vfx;

                    bool isBoss = IsBoss(go);
                    lureTimers[controller] = isBoss ? bossDuration : duration;
                }
            }
        }

        // Tick per-enemy timers, release expired or out-of-range
        var toRemove = new List<EnemyController>();
        var keys = new List<EnemyController>(lureTimers.Keys);
        foreach (var ec in keys)
        {
            if (ec == null || ec.gameObject == null || !ec.gameObject.activeInHierarchy)
            {
                toRemove.Add(ec);
                continue;
            }

            if (!stillInRange.Contains(ec))
            {
                toRemove.Add(ec);
                continue;
            }

            lureTimers[ec] -= Time.deltaTime;
            if (lureTimers[ec] <= 0f)
                toRemove.Add(ec);
        }

        foreach (var ec in toRemove)
            ReleaseEnemy(ec);
    }

    private DecoyConfusionVFX AttachConfusionVFX(GameObject enemy)
    {
        var existingVfx = enemy.GetComponentInChildren<DecoyConfusionVFX>();
        if (existingVfx != null) return existingVfx;

        bool isBoss = IsBoss(enemy);

        GameObject vfxObj = new GameObject("ConfusionVFX");
        vfxObj.transform.position = enemy.transform.position;
        vfxObj.transform.SetParent(enemy.transform, true);

        var vfx = vfxObj.AddComponent<DecoyConfusionVFX>();
        if (isBoss)
            vfx.SetBossMode(bossVFXOffset);

        Debug.Log($"[Decoy] Attached ConfusionVFX to {enemy.name} isBoss={isBoss} " +
                  $"bossNudge={bossVFXOffset} enemyScale={enemy.transform.lossyScale}");

        return vfx;
    }

    private void Expire()
    {
        isExpired = true;
        ReleaseAllEnemies();
        Disintegrate();
    }

    public void Disintegrate()
    {
        if (isDisintegrating) return;
        isDisintegrating = true;
        isArmed = false;

        ReleaseAllEnemies();

        var fx = gameObject.AddComponent<DisintegrateDecoy>();
        fx.Initialize(bodyRenderer, glowRenderer, pulseRingRenderer, pulseRingRenderer2, groundHaloRenderer, antennaGlowRenderer);
    }

    private void ReleaseAllEnemies()
    {
        foreach (var kvp in luredEnemies)
        {
            if (kvp.Key != null && kvp.Key.gameObject != null)
            {
                kvp.Key.ClearDecoyTarget();
                if (kvp.Value != null && kvp.Value.gameObject != null)
                    Object.Destroy(kvp.Value.gameObject);
            }
        }
        luredEnemies.Clear();
        lureTimers.Clear();
    }

    private void ReleaseEnemy(EnemyController controller)
    {
        if (controller != null && controller.gameObject != null)
        {
            controller.ClearDecoyTarget();
            if (luredEnemies.TryGetValue(controller, out var vfx))
            {
                if (vfx != null && vfx.gameObject != null)
                    Object.Destroy(vfx.gameObject);
            }
        }
        luredEnemies.Remove(controller);
        lureTimers.Remove(controller);
    }

    void OnDestroy()
    {
        ReleaseAllEnemies();
    }

    //  VISUALS 

    private void BuildVisual()
    {
        // Ground halo — large soft circle on the floor
        GameObject haloObj = new GameObject("GroundHalo");
        haloObj.transform.SetParent(transform, false);
        haloObj.transform.localPosition = new Vector3(0f, -0.04f, 0f);
        groundHaloRenderer = haloObj.AddComponent<SpriteRenderer>();
        groundHaloRenderer.sprite = GenerateGlowSprite();
        groundHaloRenderer.sortingOrder = SORT_ORDER_BASE - 2;
        groundHaloRenderer.color = new Color(0.2f, 0.75f, 0.9f, 0f);
        haloObj.transform.localScale = Vector3.one * 1.6f;

        // Pulse ring 1
        GameObject ringObj = new GameObject("PulseRing");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = Vector3.zero;
        pulseRingRenderer = ringObj.AddComponent<SpriteRenderer>();
        pulseRingRenderer.sprite = GenerateRingSprite();
        pulseRingRenderer.sortingOrder = SORT_ORDER_BASE - 1;
        pulseRingRenderer.color = new Color(PULSE_COLOR.r, PULSE_COLOR.g, PULSE_COLOR.b, 0f);
        ringObj.transform.localScale = Vector3.one * 0.4f;

        // Pulse ring 2 (staggered)
        GameObject ringObj2 = new GameObject("PulseRing2");
        ringObj2.transform.SetParent(transform, false);
        ringObj2.transform.localPosition = Vector3.zero;
        pulseRingRenderer2 = ringObj2.AddComponent<SpriteRenderer>();
        pulseRingRenderer2.sprite = GenerateRingSprite();
        pulseRingRenderer2.sortingOrder = SORT_ORDER_BASE - 1;
        pulseRingRenderer2.color = new Color(PULSE_COLOR.r, PULSE_COLOR.g, PULSE_COLOR.b, 0f);
        ringObj2.transform.localScale = Vector3.one * 0.4f;

        // Main body
        GameObject bodyObj = new GameObject("DecoyBody");
        bodyObj.transform.SetParent(transform, false);
        bodyRenderer = bodyObj.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = GenerateDecoyBodySprite();
        bodyRenderer.sortingOrder = SORT_ORDER_BASE;
        bodyObj.transform.localScale = Vector3.one * 0.45f;

        // Center glow — bigger and brighter
        GameObject glowObj = new GameObject("DecoyGlow");
        glowObj.transform.SetParent(transform, false);
        glowObj.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        glowRenderer = glowObj.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = GenerateGlowSprite();
        glowRenderer.sortingOrder = SORT_ORDER_BASE + 1;
        glowRenderer.color = DECOY_GLOW_OFF;
        glowObj.transform.localScale = Vector3.one * 0.35f;

        // Antenna tip glow — slightly larger
        GameObject antennaObj = new GameObject("AntennaGlow");
        antennaObj.transform.SetParent(transform, false);
        antennaObj.transform.localPosition = new Vector3(0f, 0.22f, 0f);
        antennaGlowRenderer = antennaObj.AddComponent<SpriteRenderer>();
        antennaGlowRenderer.sprite = GenerateGlowSprite();
        antennaGlowRenderer.sortingOrder = SORT_ORDER_BASE + 2;
        antennaGlowRenderer.color = new Color(DECOY_GLOW.r, DECOY_GLOW.g, DECOY_GLOW.b, 0.8f);
        antennaObj.transform.localScale = Vector3.one * 0.14f;
    }

    //  PROCEDURAL SPRITES 

    private static Sprite _cachedDecoyBody;
    private static Sprite GenerateDecoyBodySprite()
    {
        if (_cachedDecoyBody != null) return _cachedDecoyBody;
        const int SIZE = 48;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[SIZE * SIZE];
        Vector2 center = new Vector2(SIZE * 0.5f, SIZE * 0.38f);
        float bodyRadiusX = SIZE * 0.30f, bodyRadiusY = SIZE * 0.25f;
        Color shellDark = new Color(0.15f, 0.35f, 0.42f, 1f);
        Color shellLight = new Color(0.25f, 0.55f, 0.65f, 1f);
        Color accent = new Color(0.3f, 0.85f, 1f, 1f);
        Color antennaCol = new Color(0.2f, 0.6f, 0.7f, 1f);
        Color baseCol = new Color(0.12f, 0.25f, 0.3f, 1f);
        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
            {
                Vector2 pos = new Vector2(x, y);
                Color c = Color.clear;
                float ex = (x - center.x) / bodyRadiusX, ey = (y - center.y) / bodyRadiusY;
                float ellipse = ex * ex + ey * ey;
                if (ellipse <= 1f)
                {
                    c = Color.Lerp(shellDark, shellLight, Mathf.Clamp01(0.4f + ex * 0.3f + ey * 0.25f));
                    if (Mathf.Abs(y - center.y) < 2.5f) c = Color.Lerp(c, accent, 0.5f);
                    if (ellipse > 0.85f) c.a = 1f - (ellipse - 0.85f) / 0.15f;
                }
                float baseTop = center.y - bodyRadiusY * 0.7f, baseBottom = center.y - bodyRadiusY * 1.1f;
                if (y >= baseBottom && y <= baseTop && Mathf.Abs(x - center.x) < bodyRadiusX * 0.6f) { c = baseCol; c.a = 1f; }
                float[] legXs = { center.x - bodyRadiusX * 0.4f, center.x, center.x + bodyRadiusX * 0.4f };
                foreach (float lx in legXs) { if (Vector2.Distance(pos, new Vector2(lx, center.y - bodyRadiusY * 1.1f)) < 2.5f) { c = baseCol; c.a = 1f; } }
                float antennaX = SIZE * 0.5f, antennaBottom = center.y + bodyRadiusY * 0.6f, antennaTop = SIZE * 0.88f;
                if (Mathf.Abs(x - antennaX) < 1.2f && y >= antennaBottom && y <= antennaTop) { c = antennaCol; c.a = 1f; }
                Vector2 tipCenter = new Vector2(antennaX, antennaTop);
                float tipDist = Vector2.Distance(pos, tipCenter);
                if (tipDist < 3f) { float t = 1f - tipDist / 3f; c = Color.Lerp(c.a > 0 ? c : Color.clear, accent, t); c.a = Mathf.Max(c.a, t); }
                pixels[y * SIZE + x] = c;
            }
        tex.SetPixels(pixels); tex.Apply();
        _cachedDecoyBody = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), Vector2.one * 0.5f, SIZE);
        return _cachedDecoyBody;
    }

    private static Sprite _cachedGlow;
    private static Sprite GenerateGlowSprite()
    {
        if (_cachedGlow != null) return _cachedGlow;
        const int S = 24;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f); float r = S * 0.45f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Vector2.Distance(new Vector2(x, y), c); float a = 1f - Mathf.Clamp01(d / r); a *= a; px[y * S + x] = new Color(1f, 1f, 1f, a); }
        tex.SetPixels(px); tex.Apply();
        _cachedGlow = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedGlow;
    }

    private static Sprite _cachedRing;
    private static Sprite GenerateRingSprite()
    {
        if (_cachedRing != null) return _cachedRing;
        const int S = 96; // higher res for smoother ring
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outerR = S * 0.47f;
        float innerR = S * 0.34f;
        float midR = (innerR + outerR) * 0.5f;
        float halfWidth = (outerR - innerR) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                if (d >= innerR && d <= outerR)
                {
                    // Smooth ring falloff (no dashes — solid, bright)
                    float ringDist = Mathf.Abs(d - midR) / halfWidth;
                    a = 1f - ringDist * ringDist; // quadratic falloff for soft edges
                    a = Mathf.Clamp01(a);

                    // Add subtle dashes (wider segments, less gap)
                    float angle = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    float segAngle = Mathf.Repeat(angle, 60f);
                    // 45° visible, 15° gap per 60° segment
                    if (segAngle > 45f)
                        a *= Mathf.Clamp01(1f - (segAngle - 45f) / 5f); // soft fade into gap
                }
                // Inner soft glow for filled feel
                if (d < innerR)
                {
                    float innerGlow = Mathf.Clamp01((innerR - d) / (innerR * 0.15f));
                    // very faint fill inside the ring
                    a = Mathf.Max(a, innerGlow * 0.08f);
                }
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedRing = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedRing;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.75f, 0.9f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, attractRadius);
    }
}



/// Confusion VFX: orbiting question marks above enemy heads.

public class DecoyConfusionVFX : MonoBehaviour
{
    private SpriteRenderer[] iconRenderers;
    private SpriteRenderer parentSR;
    private Transform enemyTransform;
    private float rotationAngle = 0f;

    // Offset from enemy position to VFX center
    private Vector2 baseOffset;
    private bool isBoss = false;
    // Optional per-weapon nudge for bosses. Defaults to none — the mark is
    // anchored to the sprite's head automatically. X flips with facing.
    private Vector2 bossOffset = Vector2.zero;

    private const int ICON_COUNT = 3;
    private const float ORBIT_RADIUS = 0.55f;
    private const float ROTATION_SPEED = 180f;
    private const float BOB_SPEED = 3f;
    private const float BOB_AMPLITUDE = 0.08f;
    private const float ICON_SCALE = 0.45f;

    // Extra clearance above the sprite's top edge so the marks don't overlap the head.
    private const float BOSS_HEAD_CLEARANCE = 0.4f;
    private const float ENEMY_HEAD_CLEARANCE = 0.2f;

    // Confusion marks render ABOVE health bars but BELOW biome fog. Everything
    // here lives on the Default sorting layer; the project's numeric bands are:
    //   400–1600  Y-sorted world sprites (enemies, obstacles)
    //   2000      boss head / enemy projectiles
    //   4000      enemy + boss health bars
    //   5000      biome fog          <- marks must stay UNDER this
    //   6000      night overlay
    // 4500 lands cleanly in the gap between the health bar and the fog.
    private const string OVERLAY_SORT_LAYER = "Default";
    private const int CONFUSION_SORT_ORDER = 4500;


    // Call before Start() to configure boss-specific offset. The X component will flip when the boss sprite flips.

    public void SetBossMode(Vector2 offset)
    {
        isBoss = true;
        bossOffset = offset;
    }

    void Start()
    {
        enemyTransform = transform.parent;
        parentSR = enemyTransform != null ? enemyTransform.GetComponent<SpriteRenderer>() : null;

        // Seed a sane fallback; the real anchor is measured live in GetCurrentOffset().
        float yOff = 0.8f;
        if (parentSR != null && parentSR.sprite != null)
        {
            Bounds b = parentSR.bounds;
            yOff = (b.max.y - enemyTransform.position.y)
                 + (isBoss ? BOSS_HEAD_CLEARANCE : ENEMY_HEAD_CLEARANCE);
        }
        baseOffset = new Vector2(0f, yOff);

        iconRenderers = new SpriteRenderer[ICON_COUNT];
        Sprite questionSprite = GenerateQuestionSprite();

        // Draw the marks on the same sorting layer as the health bars / fog so the
        // order values compare directly. (Was inheriting the enemy's layer + order,
        // which sat ~450–1650 — underneath the 4000 health bar, so the marks
        // rendered behind it.)
        string sortLayerName = OVERLAY_SORT_LAYER;
        int sortLayerID = SortingLayer.NameToID(OVERLAY_SORT_LAYER);

        Color[] colors = new Color[]
        {
            new Color(1f, 0.9f, 0.15f, 1f),   // bright yellow
            new Color(0.3f, 0.9f, 1f, 1f),     // bright cyan
            new Color(1f, 0.65f, 0.1f, 1f),    // orange
        };

        for (int i = 0; i < ICON_COUNT; i++)
        {
            GameObject iconObj = new GameObject($"ConfQ{i}");
            iconObj.transform.position = GetIconWorldPos(i, 0f, 0f);

            var sr = iconObj.AddComponent<SpriteRenderer>();
            sr.sprite = questionSprite;
            sr.sortingLayerName = sortLayerName;
            sr.sortingLayerID = sortLayerID;
            sr.sortingOrder = CONFUSION_SORT_ORDER;
            sr.color = colors[i % colors.Length];

            iconObj.transform.localScale = Vector3.one * ICON_SCALE;
            iconRenderers[i] = sr;
        }

        Debug.Log($"[DecoyConfusionVFX] Spawned on {enemyTransform?.name ?? "null"} " +
                  $"isBoss={isBoss} bossNudge={bossOffset} sortLayer={sortLayerName}");
    }

    private Vector2 GetCurrentOffset()
    {
        // Anchor to the actually-rendered sprite: horizontal center of the sprite,
        // just above its top edge. World bounds already bake in the enemy's scale,
        // pivot and flipX, so this stays centered on the head for any size/facing.
        if (parentSR != null && parentSR.sprite != null && enemyTransform != null)
        {
            Bounds b = parentSR.bounds;
            Vector3 p = enemyTransform.position;

            float xOff = b.center.x - p.x;    // sprite center, not pivot
            float yOff = (b.max.y - p.y)
                       + (isBoss ? BOSS_HEAD_CLEARANCE : ENEMY_HEAD_CLEARANCE);

            if (isBoss)
            {
                // Optional per-weapon nudge (defaults to zero). X flips with facing
                // so a deliberate sideways nudge stays on the same visual side.
                xOff += parentSR.flipX ? -bossOffset.x : bossOffset.x;
                yOff += bossOffset.y;
            }
            return new Vector2(xOff, yOff);
        }

        return baseOffset; // fallback if there's no sprite to measure
    }

    private Vector3 GetIconWorldPos(int index, float angle, float bob)
    {
        if (enemyTransform == null) return transform.position;
        Vector2 offset = GetCurrentOffset();
        float a = (angle + index * (360f / ICON_COUNT)) * Mathf.Deg2Rad;
        float x = Mathf.Cos(a) * ORBIT_RADIUS;
        float y = Mathf.Sin(a) * ORBIT_RADIUS * 0.45f + bob;
        return enemyTransform.position + new Vector3(offset.x + x, offset.y + y, 0f);
    }

    void Update()
    {
        if (enemyTransform == null)
        {
            DestroyAllIcons();
            Destroy(gameObject);
            return;
        }

        rotationAngle += ROTATION_SPEED * Time.deltaTime;
        float bob = Mathf.Sin(Time.time * BOB_SPEED) * BOB_AMPLITUDE;

        for (int i = 0; i < ICON_COUNT; i++)
        {
            if (iconRenderers[i] == null) continue;

            iconRenderers[i].transform.position = GetIconWorldPos(i, rotationAngle, bob);

            float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 5f + i * 2.1f);
            iconRenderers[i].transform.localScale = Vector3.one * ICON_SCALE * pulse;

            // Fixed order — above health bars (4000), below fog (5000). The +i only
            // keeps THIS enemy's three marks layered consistently among themselves.
            iconRenderers[i].sortingOrder = CONFUSION_SORT_ORDER + i;
        }
    }

    private void DestroyAllIcons()
    {
        if (iconRenderers == null) return;
        foreach (var sr in iconRenderers)
            if (sr != null && sr.gameObject != null)
                Object.Destroy(sr.gameObject);
    }

    //  PROCEDURAL SPRITE 

    private static Sprite _cachedQuestion;
    private static Sprite GenerateQuestionSprite()
    {
        if (_cachedQuestion != null) return _cachedQuestion;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float a = 0f;

                // Top arc
                Vector2 arcCenter = new Vector2(S * 0.5f, S * 0.68f);
                float arcR = S * 0.22f;
                float arcDist = Vector2.Distance(new Vector2(x, y), arcCenter);
                float arcThickness = S * 0.09f;
                if (arcDist >= arcR - arcThickness && arcDist <= arcR + arcThickness)
                {
                    float arcAngle = Mathf.Atan2(y - arcCenter.y, x - arcCenter.x) * Mathf.Rad2Deg;
                    if (arcAngle < 0) arcAngle += 360f;
                    if (arcAngle >= 250f || arcAngle <= 180f)
                    {
                        float edgeDist = Mathf.Abs(arcDist - arcR);
                        a = Mathf.Max(a, Mathf.Clamp01(1f - edgeDist / arcThickness));
                    }
                }

                // Stem
                float stemX = S * 0.5f, stemTop = S * 0.50f, stemBottom = S * 0.36f, stemW = S * 0.085f;
                if (Mathf.Abs(x - stemX) < stemW && y >= stemBottom && y <= stemTop)
                    a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(x - stemX) / stemW));

                // Dot
                Vector2 dotCenter = new Vector2(S * 0.5f, S * 0.2f);
                float dotR = S * 0.08f;
                float dotDist = Vector2.Distance(new Vector2(x, y), dotCenter);
                if (dotDist < dotR)
                    a = Mathf.Max(a, Mathf.Clamp01(1f - dotDist / dotR));

                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedQuestion = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedQuestion;
    }

    void OnDestroy()
    {
        DestroyAllIcons();
    }
}



// Smooth disintegration effect for decoys being replaced or expiring.

public class DisintegrateDecoy : MonoBehaviour
{
    private SpriteRenderer bodyRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer pulseRingRenderer;
    private SpriteRenderer pulseRingRenderer2;
    private SpriteRenderer groundHaloRenderer;
    private SpriteRenderer antennaGlowRenderer;

    private float startScale = 1f;

    private float timer;
    private const float DURATION = 0.5f;

    public void Initialize(SpriteRenderer body, SpriteRenderer glow,
                           SpriteRenderer ring, SpriteRenderer ring2,
                           SpriteRenderer halo, SpriteRenderer antenna)
    {
        bodyRenderer = body; glowRenderer = glow;
        pulseRingRenderer = ring; pulseRingRenderer2 = ring2;
        groundHaloRenderer = halo; antennaGlowRenderer = antenna;
        startScale = transform.localScale.x;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / DURATION;
        if (t >= 1f) { Destroy(gameObject); return; }
        float scale = (1f - t) * (1f + 0.1f * Mathf.Sin(t * 30f)) * startScale;
        transform.localScale = Vector3.one * Mathf.Max(scale, 0f);
        float alpha = 1f - t * t;
        Color fadeColor = new Color(0.2f, 0.75f, 0.9f, alpha);
        if (bodyRenderer != null) bodyRenderer.color = fadeColor;
        if (glowRenderer != null) { Color gc = glowRenderer.color; gc.a = alpha; glowRenderer.color = gc; }
        if (pulseRingRenderer != null) { Color rc = pulseRingRenderer.color; rc.a *= (1f - t); pulseRingRenderer.color = rc; }
        if (pulseRingRenderer2 != null) { Color rc2 = pulseRingRenderer2.color; rc2.a *= (1f - t); pulseRingRenderer2.color = rc2; }
        if (groundHaloRenderer != null) { Color hc = groundHaloRenderer.color; hc.a *= (1f - t); groundHaloRenderer.color = hc; }
        if (antennaGlowRenderer != null) { Color ac = antennaGlowRenderer.color; ac.a *= (1f - t); antennaGlowRenderer.color = ac; }
    }
}





