using UnityEngine;
using System.Collections.Generic;


// A small portable turret that automatically targets and shoots nearby enemies.

public class TurretUnit : MonoBehaviour
{
    // Config (set via Initialize)
    private float damage;
    private float fireRate;       // shots per second
    private float range;
    private float projectileSpeed;
    private float rotationSpeed;

    // State
    private bool isArmed = false;
    private float armTimer;
    private bool isDisintegrating = false;
    private float spawnScale = 0f;
    private float fireCooldown = 0f;
    private Transform currentTarget;

    // Visuals
    private SpriteRenderer haloRenderer;         // bright ground halo for visibility
    private SpriteRenderer shadowRenderer;       // dark outline ring
    private SpriteRenderer baseRenderer;
    private SpriteRenderer turretBarrelRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer rangeIndicatorRenderer;
    private Transform barrelPivot;

    // Muzzle flash
    private SpriteRenderer muzzleFlashRenderer;
    private Transform muzzleFlashTransform;      // cached for projectile spawn pos
    private float muzzleFlashTimer;

    private const float SORT_PRECISION = 10f;
    private const int SORT_ORDER_BASE = 1000;

    public void Initialize(float damage, float range, float fireRate,
                           float projectileSpeed, float armDelay, float rotationSpeed = 300f)
    {
        this.damage = damage;
        this.range = range;
        this.fireRate = fireRate;
        this.projectileSpeed = projectileSpeed;
        this.armTimer = armDelay;
        this.rotationSpeed = rotationSpeed;

        if (this.damage <= 0f) this.damage = 8f;
        if (this.fireRate <= 0f) this.fireRate = 3f;
        if (this.range <= 0f) this.range = 6f;
        if (this.projectileSpeed <= 0f) this.projectileSpeed = 12f;
    }

    private void Start()
    {
        BuildVisual();
        isArmed = false;
        spawnScale = 0f;
    }

    private void Update()
    {
        if (isDisintegrating) return;

        // Pop-in animation
        if (spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.25f, 1f);
            float ease = 1f + 2.7f * Mathf.Pow(spawnScale - 1f, 3f) + 1.7f * Mathf.Pow(spawnScale - 1f, 2f);
            transform.localScale = Vector3.one * ease;
        }

        // Arm delay
        if (!isArmed)
        {
            armTimer -= Time.deltaTime;
            if (armTimer <= 0f)
            {
                isArmed = true;
                // Fade out range indicator once armed
                if (rangeIndicatorRenderer != null)
                    rangeIndicatorRenderer.color = new Color(0.3f, 0.8f, 1f, 0.06f);
            }
        }

        // Glow pulse
        if (glowRenderer != null)
        {
            float interval = isArmed ? 0.8f : 2.4f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time / interval * Mathf.PI * 2f);
            Color armed = new Color(0.2f, 0.85f, 1f, 0.85f);
            Color dim = new Color(0.2f, 0.85f, 1f, 0.15f);
            glowRenderer.color = Color.Lerp(dim, armed, pulse);
        }

        // Halo subtle pulse (so it breathes a little)
        if (haloRenderer != null)
        {
            float haloPulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f);
            float haloAlpha = Mathf.Lerp(0.18f, 0.30f, haloPulse);
            haloRenderer.color = new Color(0.2f, 0.75f, 1f, haloAlpha);
        }

        // Muzzle flash decay
        if (muzzleFlashRenderer != null && muzzleFlashTimer > 0f)
        {
            muzzleFlashTimer -= Time.deltaTime;
            float a = Mathf.Clamp01(muzzleFlashTimer / 0.06f);
            Color mc = muzzleFlashRenderer.color;
            mc.a = a;
            muzzleFlashRenderer.color = mc;
            muzzleFlashRenderer.transform.localScale = Vector3.one * (0.12f + (1f - a) * 0.08f);
        }

        // Y-sort
        if (baseRenderer != null)
        {
            float sortY = transform.position.y - 0.15f;
            int order = SORT_ORDER_BASE + Mathf.RoundToInt(-sortY * SORT_PRECISION);
            if (haloRenderer != null) haloRenderer.sortingOrder = order - 3;
            if (shadowRenderer != null) shadowRenderer.sortingOrder = order - 2;
            if (rangeIndicatorRenderer != null) rangeIndicatorRenderer.sortingOrder = order - 1;
            baseRenderer.sortingOrder = order;
            if (turretBarrelRenderer != null) turretBarrelRenderer.sortingOrder = order + 2;
            if (glowRenderer != null) glowRenderer.sortingOrder = order + 3;
            if (muzzleFlashRenderer != null) muzzleFlashRenderer.sortingOrder = order + 4;
        }

        if (isArmed)
        {
            UpdateTargeting();
            UpdateFiring();
        }
    }

    //  TARGETING 

    private void UpdateTargeting()
    {
        // Validate current target
        if (currentTarget != null)
        {
            if (!currentTarget.gameObject.activeInHierarchy ||
                Vector2.Distance(transform.position, currentTarget.position) > range)
            {
                currentTarget = null;
            }
        }

        // Find new target if needed
        if (currentTarget == null)
            currentTarget = FindClosestEnemy();

        // Rotate barrel toward target
        if (barrelPivot != null)
        {
            if (currentTarget != null)
            {
                Vector2 dir = ((Vector2)currentTarget.position - (Vector2)transform.position).normalized;
                float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float currentAngle = barrelPivot.eulerAngles.z;
                float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
                barrelPivot.rotation = Quaternion.Euler(0, 0, newAngle);
            }
            else
            {
                // Idle scan rotation
                float idleAngle = Mathf.Sin(Time.time * 0.5f) * 45f;
                float currentAngle = barrelPivot.eulerAngles.z;
                float newAngle = Mathf.MoveTowardsAngle(currentAngle, idleAngle, rotationSpeed * 0.3f * Time.deltaTime);
                barrelPivot.rotation = Quaternion.Euler(0, 0, newAngle);
            }
        }
    }

    private Transform FindClosestEnemy()
    {
        float bestDist = range;
        Transform best = null;

        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (var go in enemies)
        {
            if (go == null || !go.activeInHierarchy) continue;
            float dist = Vector2.Distance(transform.position, go.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = go.transform;
            }
        }
        return best;
    }

    // ── FIRING ──

    private void UpdateFiring()
    {
        fireCooldown -= Time.deltaTime;

        if (currentTarget == null || fireCooldown > 0f) return;

        // Check angle to target — only fire if barrel is roughly aimed
        Vector2 toTarget = ((Vector2)currentTarget.position - (Vector2)transform.position).normalized;
        float barrelAngle = barrelPivot.eulerAngles.z * Mathf.Deg2Rad;
        Vector2 barrelDir = new Vector2(Mathf.Cos(barrelAngle), Mathf.Sin(barrelAngle));
        float dot = Vector2.Dot(toTarget, barrelDir);
        if (dot < 0.95f) return; // within ~18 degrees

        FireProjectile(barrelDir);
        fireCooldown = 1f / fireRate;
    }

    private void FireProjectile(Vector2 direction)
    {
        // Spawn at the muzzle flash position (barrel tip) instead of turret center
        Vector3 spawnPos;
        if (muzzleFlashTransform != null)
            spawnPos = muzzleFlashTransform.position;
        else
            spawnPos = transform.position + (Vector3)(direction * 0.45f);

        GameObject projObj = new GameObject("TurretBullet");
        projObj.transform.position = spawnPos;
        projObj.layer = LayerMask.NameToLayer("Default");

        // Add a small collider
        var col = projObj.AddComponent<CircleCollider2D>();
        col.radius = 0.06f;
        col.isTrigger = true;

        // Add rigidbody for trigger detection
        var rb = projObj.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // Visual
        SpriteRenderer sr = projObj.AddComponent<SpriteRenderer>();
        sr.sprite = GenerateBulletSprite();
        sr.sortingOrder = 2500;
        sr.color = new Color(0.3f, 0.9f, 1f, 1f);
        projObj.transform.localScale = Vector3.one * 0.18f;

        // Rotate to face direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        projObj.transform.rotation = Quaternion.Euler(0, 0, angle);

        // Add bullet behaviour
        var bullet = projObj.AddComponent<TurretBullet>();
        bullet.Initialize(direction, damage, projectileSpeed, range);

        // Muzzle flash
        if (muzzleFlashRenderer != null)
        {
            muzzleFlashTimer = 0.08f;
            muzzleFlashRenderer.color = new Color(0.5f, 0.95f, 1f, 1f);
        }
    }

    // Disintegrate (called by TurretLauncherSystem) 

    public void Disintegrate()
    {
        if (isDisintegrating) return;
        isDisintegrating = true;
        isArmed = false;

        var fx = gameObject.AddComponent<DisintegrateTurret>();
        fx.Initialize(baseRenderer, turretBarrelRenderer, glowRenderer,
                      rangeIndicatorRenderer, muzzleFlashRenderer, haloRenderer, shadowRenderer);
    }

    //  VISUALS 

    private void BuildVisual()
    {
        //  Ground halo (bright soft circle for visibility on any background) 
        GameObject haloObj = new GameObject("TurretHalo");
        haloObj.transform.SetParent(transform, false);
        haloObj.transform.localPosition = new Vector3(0f, -0.03f, 0f);
        haloRenderer = haloObj.AddComponent<SpriteRenderer>();
        haloRenderer.sprite = GenerateSoftCircleSprite();
        haloRenderer.sortingOrder = SORT_ORDER_BASE - 3;
        haloRenderer.color = new Color(0.2f, 0.75f, 1f, 0.25f);
        haloObj.transform.localScale = Vector3.one * 1.1f; // large soft glow under turret

        //  Dark outline ring (contrast ring so it pops on bright backgrounds) 
        GameObject shadowObj = new GameObject("TurretShadow");
        shadowObj.transform.SetParent(transform, false);
        shadowObj.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        shadowRenderer = shadowObj.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = GenerateOutlineRingSprite();
        shadowRenderer.sortingOrder = SORT_ORDER_BASE - 2;
        shadowRenderer.color = new Color(0f, 0f, 0f, 0.35f);
        shadowObj.transform.localScale = Vector3.one * 0.65f;

        // Range indicator (subtle circle on ground)
        GameObject rangeObj = new GameObject("RangeIndicator");
        rangeObj.transform.SetParent(transform, false);
        rangeIndicatorRenderer = rangeObj.AddComponent<SpriteRenderer>();
        rangeIndicatorRenderer.sprite = GenerateRangeCircleSprite();
        rangeIndicatorRenderer.sortingOrder = SORT_ORDER_BASE - 1;
        rangeIndicatorRenderer.color = new Color(0.3f, 0.8f, 1f, 0.12f);
        rangeObj.transform.localScale = Vector3.one * (range * 2f); // diameter

        // Base platform (hexagonal-ish, brighter colors for visibility)
        GameObject baseObj = new GameObject("TurretBase");
        baseObj.transform.SetParent(transform, false);
        baseRenderer = baseObj.AddComponent<SpriteRenderer>();
        baseRenderer.sprite = GenerateBaseSprite();
        baseRenderer.sortingOrder = SORT_ORDER_BASE;
        baseObj.transform.localScale = Vector3.one * 0.55f;

        // Barrel pivot
        GameObject pivotObj = new GameObject("BarrelPivot");
        pivotObj.transform.SetParent(transform, false);
        pivotObj.transform.localPosition = Vector3.zero;
        barrelPivot = pivotObj.transform;

        // Barrel sprite
        GameObject barrelObj = new GameObject("Barrel");
        barrelObj.transform.SetParent(barrelPivot, false);
        barrelObj.transform.localPosition = new Vector3(0.12f, 0f, 0f); // offset right of pivot
        turretBarrelRenderer = barrelObj.AddComponent<SpriteRenderer>();
        turretBarrelRenderer.sprite = GenerateBarrelSprite();
        turretBarrelRenderer.sortingOrder = SORT_ORDER_BASE + 2;
        barrelObj.transform.localScale = Vector3.one * 0.55f;

        // Glow on top of base
        GameObject glowObj = new GameObject("TurretGlow");
        glowObj.transform.SetParent(transform, false);
        glowObj.transform.localPosition = Vector3.zero;
        glowRenderer = glowObj.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = GenerateGlowSprite();
        glowRenderer.sortingOrder = SORT_ORDER_BASE + 3;
        glowRenderer.color = new Color(0.2f, 0.85f, 1f, 0.15f);
        glowObj.transform.localScale = Vector3.one * 0.22f;

        // Muzzle flash (starts invisible) — positioned at barrel tip
        GameObject muzzleObj = new GameObject("MuzzleFlash");
        muzzleObj.transform.SetParent(barrelPivot, false);
        muzzleObj.transform.localPosition = new Vector3(0.65f, 0f, 0f);
        muzzleFlashRenderer = muzzleObj.AddComponent<SpriteRenderer>();
        muzzleFlashTransform = muzzleObj.transform;
        muzzleFlashRenderer.sprite = GenerateGlowSprite();
        muzzleFlashRenderer.sortingOrder = SORT_ORDER_BASE + 4;
        muzzleFlashRenderer.color = new Color(0.5f, 0.95f, 1f, 0f);
        muzzleObj.transform.localScale = Vector3.one * 0.12f;
    }

    //  PROCEDURAL SPRITES 

    private static Sprite _cachedBaseSprite;
    private static Sprite GenerateBaseSprite()
    {
        if (_cachedBaseSprite != null) return _cachedBaseSprite;
        const int SIZE = 48;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[SIZE * SIZE];
        Vector2 center = new Vector2(SIZE * 0.5f, SIZE * 0.48f);
        float outerR = SIZE * 0.42f;

        // Brighter body colors for visibility on all backgrounds
        Color bodyDark = new Color(0.30f, 0.34f, 0.42f, 1f);
        Color bodyLight = new Color(0.55f, 0.60f, 0.70f, 1f);
        Color rimColor = new Color(0.2f, 0.65f, 0.85f, 0.8f);
        Color edgeHighlight = new Color(0.5f, 0.85f, 1f, 0.7f);
        Color legColor = new Color(0.25f, 0.28f, 0.35f, 1f);

        // 3 legs at 120-degree intervals
        float[] legAngles = { 210f, 270f, 330f };
        float legLength = outerR * 1.1f;
        float legWidth = 2.8f;

        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
            {
                Vector2 pos = new Vector2(x, y);
                float dist = Vector2.Distance(pos, center);
                Color c = Color.clear;

                // Draw legs
                foreach (float angle in legAngles)
                {
                    float rad = angle * Mathf.Deg2Rad;
                    Vector2 legDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    Vector2 legPerp = new Vector2(-legDir.y, legDir.x);
                    Vector2 toPoint = pos - center;
                    float along = Vector2.Dot(toPoint, legDir);
                    float perp = Mathf.Abs(Vector2.Dot(toPoint, legPerp));
                    if (along > 0f && along < legLength && perp < legWidth)
                    {
                        float t = along / legLength;
                        c = Color.Lerp(legColor, new Color(legColor.r, legColor.g, legColor.b, 0.3f), t);
                    }
                }

                // Hexagonal body
                if (dist <= outerR)
                {
                    float angle = Mathf.Atan2(y - center.y, x - center.x);
                    float hexR = outerR * (0.85f + 0.15f * Mathf.Cos(6f * angle));
                    if (dist <= hexR)
                    {
                        float nx = (x - center.x) / outerR;
                        float ny = (y - center.y) / outerR;
                        c = Color.Lerp(bodyDark, bodyLight, Mathf.Clamp01(0.5f + nx * 0.35f + ny * 0.25f));

                        // Inner ring detail
                        float innerR = hexR * 0.6f;
                        if (Mathf.Abs(dist - innerR) < 1.5f)
                            c = Color.Lerp(c, rimColor, 0.5f);

                        // Bright edge highlight for contrast
                        if (dist > hexR - 2.5f && dist <= hexR)
                        {
                            float edgeT = 1f - (hexR - dist) / 2.5f;
                            c = Color.Lerp(c, edgeHighlight, edgeT * 0.5f);
                        }

                        // Edge anti-alias
                        if (dist > hexR - 1.5f)
                            c.a = 1f - (dist - (hexR - 1.5f)) / 1.5f;
                    }
                }

                pixels[y * SIZE + x] = c;
            }

        tex.SetPixels(pixels); tex.Apply();
        _cachedBaseSprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), Vector2.one * 0.5f, SIZE);
        return _cachedBaseSprite;
    }

    private static Sprite _cachedBarrelSprite;
    private static Sprite GenerateBarrelSprite()
    {
        if (_cachedBarrelSprite != null) return _cachedBarrelSprite;
        const int W = 32, H = 12;
        var tex = new Texture2D(W, H, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[W * H];

        // Brighter barrel
        Color barrelDark = new Color(0.32f, 0.36f, 0.42f, 1f);
        Color barrelLight = new Color(0.58f, 0.64f, 0.72f, 1f);
        Color muzzleColor = new Color(0.22f, 0.25f, 0.30f, 1f);
        Color bandColor = new Color(0.2f, 0.6f, 0.8f, 1f);

        float cy = H * 0.5f;
        float halfH = H * 0.4f;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dy = Mathf.Abs(y - cy);
                Color c = Color.clear;

                float taper = 1f - (float)x / W * 0.15f;
                float barrelHalfH = halfH * taper;

                if (dy <= barrelHalfH)
                {
                    float shade = (y - cy + halfH) / (2f * halfH);
                    c = Color.Lerp(barrelDark, barrelLight, shade);

                    // Muzzle end darkened
                    if (x >= W - 4)
                        c = Color.Lerp(c, muzzleColor, 0.5f);

                    // Cyan band detail
                    if (x >= 4 && x <= 7)
                        c = Color.Lerp(c, bandColor, 0.4f);

                    // AA at edges
                    if (dy > barrelHalfH - 1f)
                        c.a = 1f - (dy - (barrelHalfH - 1f));
                }

                pixels[y * W + x] = c;
            }

        tex.SetPixels(pixels); tex.Apply();
        _cachedBarrelSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), W);
        return _cachedBarrelSprite;
    }

    private static Sprite _cachedGlowSprite;
    private static Sprite GenerateGlowSprite()
    {
        if (_cachedGlowSprite != null) return _cachedGlowSprite;
        const int S = 24;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.45f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 1f - Mathf.Clamp01(d / r); a *= a;
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedGlowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedGlowSprite;
    }

    /// Soft large circle for the ground halo
    private static Sprite _cachedSoftCircle;
    private static Sprite GenerateSoftCircleSprite()
    {
        if (_cachedSoftCircle != null) return _cachedSoftCircle;
        const int S = 48;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float r = S * 0.48f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float t = Mathf.Clamp01(d / r);
                float a = Mathf.Clamp01(1f - t * t);  // soft quadratic falloff
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        _cachedSoftCircle = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedSoftCircle;
    }

    // Dark outline ring for contrast on bright backgrounds
    private static Sprite _cachedOutlineRing;
    private static Sprite GenerateOutlineRingSprite()
    {
        if (_cachedOutlineRing != null) return _cachedOutlineRing;
        const int S = 48;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outerR = S * 0.48f, innerR = S * 0.36f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                if (d >= innerR && d <= outerR)
                {
                    float mid = (innerR + outerR) * 0.5f;
                    float hw = (outerR - innerR) * 0.5f;
                    a = 1f - Mathf.Abs(d - mid) / hw;
                    a *= a; // sharpen falloff
                }
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _cachedOutlineRing = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedOutlineRing;
    }

    private static Sprite _cachedRangeSprite;
    private static Sprite GenerateRangeCircleSprite()
    {
        if (_cachedRangeSprite != null) return _cachedRangeSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outerR = S * 0.48f, innerR = S * 0.44f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                if (d >= innerR && d <= outerR)
                {
                    float mid = (innerR + outerR) * 0.5f;
                    float hw = (outerR - innerR) * 0.5f;
                    a = 1f - Mathf.Abs(d - mid) / hw;
                }
                if (d < innerR)
                    a = Mathf.Max(a, 0.08f * (1f - d / innerR));
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _cachedRangeSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedRangeSprite;
    }

    private static Sprite _cachedBulletSprite;
    private static Sprite GenerateBulletSprite()
    {
        if (_cachedBulletSprite != null) return _cachedBulletSprite;
        const int W = 12, H = 6;
        var tex = new Texture2D(W, H, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[W * H];
        float cy = H * 0.5f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dy = Mathf.Abs(y - cy) / (H * 0.5f);
                float dx = x / (float)(W - 1);
                float a = (1f - dy * dy) * (1f - dx * dx * 0.4f);
                px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _cachedBulletSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), W);
        return _cachedBulletSprite;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}



// Turret bullet 

public class TurretBullet : MonoBehaviour
{
    private Vector2 direction;
    private float damage;
    private float speed;
    private float maxRange;
    private Vector3 startPos;
    private bool hasHit = false;

    public void Initialize(Vector2 dir, float dmg, float spd, float maxRange)
    {
        this.direction = dir.normalized;
        this.damage = dmg;
        this.speed = spd;
        this.maxRange = maxRange;
        this.startPos = transform.position;
    }

    private void Update()
    {
        if (hasHit) return;
        transform.Translate(Vector3.right * speed * Time.deltaTime, Space.Self);

        if (Vector3.Distance(startPos, transform.position) > maxRange)
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;

        // Ignore player, energy, turrets themselves, etc.
        if (other.CompareTag("Player") ||
            other.name.Contains("EnergyCollectionTrigger") ||
            other.name.Contains("Energy") ||
            other.GetComponent<PlayerMovement>() != null ||
            other.GetComponent<TurretUnit>() != null)
            return;

        if (other.CompareTag("Enemy"))
        {
            hasHit = true;

            CharacterStats stats = other.GetComponent<CharacterStats>();
            if (stats != null)
            {
                stats.TakeDamage(damage);
                CombatFeel.OnHitEnemy(other.gameObject, isMelee: false);
                Destroy(gameObject);
                return;
            }

            IDamageable damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, gameObject);
                CombatFeel.OnHitEnemy(other.gameObject, isMelee: false);
                Destroy(gameObject);
                return;
            }
        }

        // Hit non-trigger non-enemy (wall) — destroy
        if (!other.isTrigger)
            Destroy(gameObject);
    }
}



// Smooth disintegration effect for turrets being replaced.

public class DisintegrateTurret : MonoBehaviour
{
    private SpriteRenderer baseRenderer;
    private SpriteRenderer barrelRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer rangeRenderer;
    private SpriteRenderer muzzleRenderer;
    private SpriteRenderer haloRenderer;
    private SpriteRenderer shadowRenderer;

    private float timer;
    private const float DURATION = 0.5f;
    private readonly List<SparkParticle> sparks = new List<SparkParticle>();
    private bool sparksSpawned = false;

    public void Initialize(SpriteRenderer baseSR, SpriteRenderer barrelSR,
                           SpriteRenderer glowSR, SpriteRenderer rangeSR, SpriteRenderer muzzleSR,
                           SpriteRenderer haloSR, SpriteRenderer shadowSR)
    {
        baseRenderer = baseSR;
        barrelRenderer = barrelSR;
        glowRenderer = glowSR;
        rangeRenderer = rangeSR;
        muzzleRenderer = muzzleSR;
        haloRenderer = haloSR;
        shadowRenderer = shadowSR;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / DURATION;

        if (!sparksSpawned) { sparksSpawned = true; SpawnSparks(); }

        if (t >= 1f) { Destroy(gameObject); return; }

        float scale = (1f - t) * (1f + 0.1f * Mathf.Sin(t * 30f));
        transform.localScale = Vector3.one * Mathf.Max(scale, 0f);

        float alpha = 1f - t * t;
        Color fadeColor = new Color(0.2f, 0.6f, 0.9f, alpha);

        if (baseRenderer != null) baseRenderer.color = fadeColor;
        if (barrelRenderer != null) barrelRenderer.color = fadeColor;
        if (glowRenderer != null) { Color gc = glowRenderer.color; gc.a = alpha; glowRenderer.color = gc; }
        if (rangeRenderer != null) { Color rc = rangeRenderer.color; rc.a *= (1f - t); rangeRenderer.color = rc; }
        if (haloRenderer != null) { Color hc = haloRenderer.color; hc.a *= (1f - t); haloRenderer.color = hc; }
        if (shadowRenderer != null) { Color sc = shadowRenderer.color; sc.a *= (1f - t); shadowRenderer.color = sc; }
        if (muzzleRenderer != null) muzzleRenderer.color = Color.clear;

        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            if (s.go == null) { sparks.RemoveAt(i); continue; }
            s.lifetime -= Time.deltaTime;
            if (s.lifetime <= 0f) { Destroy(s.go); sparks.RemoveAt(i); continue; }

            s.velocity += Vector2.down * 2f * Time.deltaTime;
            s.go.transform.position += (Vector3)(s.velocity * Time.deltaTime);

            float st = 1f - (s.lifetime / s.maxLifetime);
            s.go.transform.localScale = Vector3.one * Mathf.Lerp(s.startSize, 0.01f, st);

            Color sc2 = s.sr.color;
            sc2.a = 1f - st;
            s.sr.color = sc2;
        }
    }

    private void SpawnSparks()
    {
        Vector3 pos = transform.position;
        for (int i = 0; i < 10; i++)
        {
            GameObject go = new GameObject("TurretDisintSpark");
            go.transform.position = pos;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 5100;

            const int S = 8;
            var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
            Color[] px = new Color[S * S];
            Vector2 c = Vector2.one * S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - d / (S * 0.4f)));
                }
            tex.SetPixels(px); tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);

            sr.color = new Color(
                Random.Range(0.1f, 0.4f),
                Random.Range(0.6f, 0.9f),
                Random.Range(0.8f, 1f), 1f);

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float speed = Random.Range(1.5f, 4f);

            sparks.Add(new SparkParticle
            {
                go = go,
                sr = sr,
                velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed + Vector2.up * Random.Range(0.5f, 2f),
                lifetime = Random.Range(0.25f, 0.45f),
                maxLifetime = Random.Range(0.25f, 0.45f),
                startSize = Random.Range(0.06f, 0.12f)
            });
            go.transform.localScale = Vector3.one * sparks[sparks.Count - 1].startSize;
        }
    }

    private class SparkParticle
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float lifetime, maxLifetime, startSize;
    }
}
