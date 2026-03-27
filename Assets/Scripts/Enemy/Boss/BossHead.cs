using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BossHead : MonoBehaviour, IDamageable
{
    private BaseBossStats bossStats;
    private bool isDestroyed = false;

    [Header("Floating Movement")]
    [SerializeField] private float floatSpeed = 0.8f;
    [SerializeField] private float directionChangeInterval = 3f;
    [SerializeField] private float wobbleAmplitude = 0.2f;
    [SerializeField] private float wobbleFrequency = 1.5f;

    private Rigidbody2D rb;
    private Vector2 currentDirection;
    private float directionTimer;

    // Map and distance constraints
    private float mapMinX = -50f, mapMaxX = 50f;
    private float mapMinY = -50f, mapMaxY = 50f;
    private Transform coreTransform;
    private float minCoreDistance = 5f;
    private float maxCoreDistance = 12f;

    // Gunge drops
    [Header("Gunge Splats")]
    [SerializeField] private float gungeLifetime = 20f;
    [SerializeField] private float gungeFadeTime = 4f;
    [SerializeField] private int maxGungeDrops = 14;

    private Sprite gungeSplatSprite;
    private static readonly List<GameObject> SceneGungeDrops = new List<GameObject>();

    // Gunge drip
    [Header("Gunge Drip")]
    [SerializeField] private float dripInterval = 0.35f;
    [SerializeField] private float dripInitialSpeed = 0.4f;
    [SerializeField] private float dripGravity = 1.5f;
    [SerializeField] private float dripLifetime = 0.7f;
    [SerializeField] private float dripMinScale = 0.5f;
    [SerializeField] private float dripMaxScale = 0.9f;
    [SerializeField] private int maxActiveDrips = 16;
    [SerializeField] private float dripSpawnXSpread = 0.7f;
    [SerializeField] private float dripSpawnBelowOffset = 0.05f;
    [SerializeField] private bool dripsLeaveSplats = true;
    [SerializeField] private float dripSplatScale = 0.5f;

    private float dripTimer;
    private Sprite dripSprite;
    private readonly List<DripData> activeDrips = new List<DripData>();

    private class DripData
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float elapsed;
        public float lifetime;
        public float startScale;
        public float wobblePhase;
        // Glow halo
        public GameObject glowGo;
        public SpriteRenderer glowSr;
        // Shimmer & spin
        public float shimmerPhase;
        public float shimmerSpeed;
        public float spinSpeed;
    }

    // Pulse rings
    [Header("Pulse Rings")]
    [SerializeField] private float pulseInterval = 10.0f;
    [SerializeField] private float pulseMaxRadius = 35f;
    [SerializeField] private float pulseDuration = 5.0f;
    [SerializeField] private float pulseAlphaPeak = 0.45f;

    private Sprite pulseRingSprite;
    private readonly List<GameObject> activeRings = new List<GameObject>();

    // Cached head sprite renderer
    private SpriteRenderer headSR;


    public void Initialize(BaseBossStats boss) => bossStats = boss;

    public void SetSpawnConfig(Transform core, float minDist, float maxDist,
                               float mapBoundsMin, float mapBoundsMax)
    {
        coreTransform = core;
        minCoreDistance = minDist;
        maxCoreDistance = maxDist;
        mapMinX = mapMinY = mapBoundsMin;
        mapMaxX = mapMaxY = mapBoundsMax;
    }



    // LIFECYCLE


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0;

        headSR = GetComponent<SpriteRenderer>();

        if (coreTransform == null)
        {
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null) coreTransform = core.transform;
        }

        PickNewDirection();
        directionTimer = directionChangeInterval;
        dripTimer = 0.5f;

        BuildGungeSplatSprite();
        BuildDripSprite();
        BuildPulseRingSprite();

        StartCoroutine(StaggeredPulseLoop());
    }

    void Update()
    {
        if (isDestroyed) return;
        UpdateFloatMovement();
        UpdateDrips();
    }


    // FLOATING MOVEMENT


    void UpdateFloatMovement()
    {
        directionTimer -= Time.deltaTime;
        if (directionTimer <= 0f)
        {
            PickNewDirection();
            directionTimer = directionChangeInterval + Random.Range(-0.5f, 0.5f);
        }

        Vector2 perp = new Vector2(-currentDirection.y, currentDirection.x);
        float wobble = Mathf.Sin(Time.time * wobbleFrequency) * wobbleAmplitude;
        Vector2 move = currentDirection * floatSpeed + perp * wobble * floatSpeed * 0.5f;
        rb.linearVelocity = move;

        EnforceBoundaries();

        if (headSR != null && Mathf.Abs(move.x) > 0.05f)
            headSR.flipX = move.x < 0;
    }

    void PickNewDirection()
    {
        if (coreTransform != null)
        {
            float dist = Vector2.Distance(transform.position, coreTransform.position);
            if (dist > maxCoreDistance * 0.75f)
            {
                Vector2 toCore = ((Vector2)coreTransform.position - (Vector2)transform.position).normalized;
                currentDirection = (toCore + Random.insideUnitCircle * 0.4f).normalized;
                return;
            }
            if (dist < minCoreDistance * 1.2f)
            {
                Vector2 away = ((Vector2)transform.position - (Vector2)coreTransform.position).normalized;
                currentDirection = (away + Random.insideUnitCircle * 0.4f).normalized;
                return;
            }
        }
        currentDirection = Random.insideUnitCircle.normalized;
    }

    void EnforceBoundaries()
    {
        Vector3 pos = transform.position;
        bool needsRedirect = false;

        if (pos.x < mapMinX || pos.x > mapMaxX || pos.y < mapMinY || pos.y > mapMaxY)
        {
            pos.x = Mathf.Clamp(pos.x, mapMinX, mapMaxX);
            pos.y = Mathf.Clamp(pos.y, mapMinY, mapMaxY);
            transform.position = pos;
            needsRedirect = true;
        }

        if (coreTransform != null)
        {
            float d = Vector2.Distance(pos, coreTransform.position);
            if (d > maxCoreDistance || d < minCoreDistance) needsRedirect = true;
        }

        if (needsRedirect) PickNewDirection();
    }


    // GUNGE TRAIL


    void BuildGungeSplatSprite()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

        System.Random rng = new System.Random(17);
        int blobCount = 7;
        Vector2[] centres = new Vector2[blobCount];
        float[] radii = new float[blobCount];
        for (int b = 0; b < blobCount; b++)
        {
            centres[b] = center + new Vector2(
                (float)(rng.NextDouble() - 0.5) * size * 0.55f,
                (float)(rng.NextDouble() - 0.5) * size * 0.55f);
            radii[b] = size * (0.14f + (float)rng.NextDouble() * 0.22f);
        }

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float influence = 0f;
                for (int b = 0; b < blobCount; b++)
                {
                    float d = Vector2.Distance(p, centres[b]);
                    if (d < radii[b] * 1.5f)
                        influence += Mathf.Pow(1f - Mathf.Clamp01(d / radii[b]), 2f);
                }

                float threshold = 0.55f;
                if (influence >= threshold)
                {
                    float interior = Mathf.Clamp01((influence - threshold) / 0.4f);
                    float n = Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                    // Purple shades based on #e954f6
                    pixels[y * size + x] = new Color(
                        0.75f + n * 0.16f,
                        0.18f + n * 0.15f,
                        0.82f + n * 0.14f,
                        Mathf.Lerp(0.75f, 0.95f, interior));
                }
                else
                {
                    pixels[y * size + x] = Color.clear;
                }
            }

        tex.SetPixels(pixels);
        tex.Apply();
        gungeSplatSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                         Vector2.one * 0.5f, 32f);
    }

    int GetGungeSortingOrder()
    {
        // Gunge drops should always be above grass (400-1600) but below the head (2000)
        return 1999;
    }

    void DropGungeAt(Vector3 position, float scale)
    {
        SceneGungeDrops.RemoveAll(g => g == null);

        while (SceneGungeDrops.Count >= maxGungeDrops)
        {
            if (SceneGungeDrops[0] != null) Destroy(SceneGungeDrops[0]);
            SceneGungeDrops.RemoveAt(0);
        }

        position.z = 0f;

        GameObject drop = new GameObject("BossHead_Gunge");
        drop.transform.position = position;
        drop.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        drop.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = drop.AddComponent<SpriteRenderer>();
        sr.sprite = gungeSplatSprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = GetGungeSortingOrder();
        sr.color = Color.white;

        TimedDestroy td = drop.AddComponent<TimedDestroy>();
        td.lifetime = gungeLifetime + gungeFadeTime;
        td.fadeDelay = gungeLifetime;

        SceneGungeDrops.Add(drop);
    }


    // GUNGE DRIP 


    void BuildDripSprite()
    {
        // 16x24 teardrop — same size, improved shading
        int w = 16, h = 24;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[w * h];

        float cx = w * 0.5f;

        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                // Teardrop: wider at top, narrows toward bottom
                float normY = (float)py / h; // 0 = bottom (pointy), 1 = top (round)
                float widthAtY = Mathf.Lerp(0.15f, 1f, Mathf.Sqrt(normY));
                float halfW = (w * 0.45f) * widthAtY;

                float dx = Mathf.Abs(px - cx);
                float distFromEdge = 1f - Mathf.Clamp01(dx / Mathf.Max(halfW, 0.01f));

                // Vertical fade at very bottom tip
                float tipFade = Mathf.Clamp01(normY / 0.15f);

                float alpha = distFromEdge * distFromEdge * tipFade;

                if (alpha > 0.02f)
                {
                    float n = Mathf.PerlinNoise(px * 0.3f + 5f, py * 0.3f + 5f);

                    // Base purple
                    float r = 0.78f + n * 0.14f;
                    float g = 0.22f + n * 0.12f;
                    float b = 0.84f + n * 0.12f;

                    // Small specular highlight upper-left area
                    float hlX = (px - cx * 0.7f) / (float)w;
                    float hlY = (py - h * 0.75f) / (float)h;
                    float hlDist = hlX * hlX + hlY * hlY;
                    float spec = Mathf.Pow(Mathf.Clamp01(1f - hlDist * 18f), 3f) * 0.35f;
                    r += spec;
                    g += spec;
                    b += spec;

                    pixels[py * w + px] = new Color(
                        Mathf.Min(r, 1f),
                        Mathf.Min(g, 1f),
                        Mathf.Min(b, 1f),
                        alpha * 0.92f);
                }
                else
                {
                    pixels[py * w + px] = Color.clear;
                }
            }

        tex.SetPixels(pixels);
        tex.Apply();
        // Pivot at top-center (0.5, 1) 
        dripSprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                                    new Vector2(0.5f, 1f), 32f);

        BuildGlowSprite();
    }

    private Sprite dripGlowSprite;

    void BuildGlowSprite()
    {
        // Simple soft radial glow — 16x16, center-pivoted
        int s = 16;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[s * s];
        float center = s * 0.5f;

        for (int py = 0; py < s; py++)
            for (int px = 0; px < s; px++)
            {
                float d = Vector2.Distance(new Vector2(px, py), new Vector2(center, center)) / center;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f) * 0.35f;
                pixels[py * s + px] = (a > 0.005f)
                    ? new Color(0.91f, 0.33f, 0.96f, a)
                    : Color.clear;
            }

        tex.SetPixels(pixels);
        tex.Apply();
        dripGlowSprite = Sprite.Create(tex, new Rect(0, 0, s, s),
                                        Vector2.one * 0.5f, 16f);
    }

    /// World-space bottom center of the head sprite, offset slightly below.
    Vector3 GetDripSpawnPosition()
    {
        if (headSR != null && headSR.sprite != null)
        {
            Bounds b = headSR.bounds;
            float halfWidth = b.extents.x * dripSpawnXSpread;
            float xOffset = Random.Range(-halfWidth, halfWidth);
            // Spawn just below the sprite bottom so the drip is never hidden
            return new Vector3(b.center.x + xOffset, b.min.y - dripSpawnBelowOffset, 0f);
        }
        return transform.position + Vector3.down * 0.5f
               + new Vector3(Random.Range(-0.3f, 0.3f), 0f, 0f);
    }

    void UpdateDrips()
    {
        // Spawn new drips on timer
        dripTimer -= Time.deltaTime;
        if (dripTimer <= 0f)
        {
            dripTimer = dripInterval + Random.Range(-dripInterval * 0.25f, dripInterval * 0.25f);
            SpawnDrip();
        }

        // Tick active drips
        for (int i = activeDrips.Count - 1; i >= 0; i--)
        {
            var drip = activeDrips[i];
            if (drip.go == null)
            {
                if (drip.glowGo != null) Destroy(drip.glowGo);
                activeDrips.RemoveAt(i);
                continue;
            }

            drip.elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(drip.elapsed / drip.lifetime);

            // Gravity: accelerate downward
            drip.velocity.y -= dripGravity * Time.deltaTime;

            // Subtle horizontal wobble
            float wobbleX = Mathf.Sin(drip.elapsed * 5f + drip.wobblePhase) * 0.15f * Time.deltaTime;
            Vector3 movement = (Vector3)drip.velocity * Time.deltaTime;
            movement.x += wobbleX;
            drip.go.transform.position += movement;

            // Gentle spin 
            drip.go.transform.Rotate(0f, 0f, drip.spinSpeed * Time.deltaTime);

            // Scale: hold size for first half, then shrink
            float scaleMult;
            if (t < 0.4f)
                scaleMult = 1f;
            else
                scaleMult = Mathf.Lerp(1f, 0f, (t - 0.4f) / 0.6f);

            float baseScale = drip.startScale * Mathf.Max(scaleMult, 0.01f);

            // Stretch vertically based on fall speed, squish horizontally to match
            float stretch = 1f + Mathf.Clamp(Mathf.Abs(drip.velocity.y) * 0.12f, 0f, 1.5f);
            float squish = 1f / Mathf.Max(Mathf.Sqrt(stretch), 0.8f);
            drip.go.transform.localScale = new Vector3(baseScale * squish, baseScale * stretch, 1f);

            // Fade with opacity shimmer
            float baseAlpha;
            if (t < 0.3f)
                baseAlpha = 0.95f;
            else
                baseAlpha = Mathf.Lerp(0.95f, 0f, (t - 0.3f) / 0.7f);

            float shimmer = 1f - Mathf.Sin(drip.elapsed * drip.shimmerSpeed + drip.shimmerPhase) * 0.12f;
            float alpha = baseAlpha * shimmer;

            Color c = drip.sr.color;
            c.a = alpha;
            drip.sr.color = c;

            // Sync glow halo — follow position, scale with drip, fade together
            if (drip.glowGo != null && drip.glowSr != null)
            {
                drip.glowGo.transform.position = drip.go.transform.position;
                float glowScale = baseScale * 2.2f;
                drip.glowGo.transform.localScale = Vector3.one * glowScale;
                Color gc = drip.glowSr.color;
                gc.a = alpha * 0.3f;
                drip.glowSr.color = gc;
            }

            // Drip expired 
            if (drip.elapsed >= drip.lifetime)
            {
                if (dripsLeaveSplats)
                {
                    Vector3 splatPos = drip.go.transform.position;
                    DropGungeAt(splatPos, dripSplatScale + Random.Range(-0.1f, 0.15f));
                }

                if (drip.glowGo != null) Destroy(drip.glowGo);
                Destroy(drip.go);
                activeDrips.RemoveAt(i);
            }
        }
    }

    void SpawnDrip()
    {
        // Enforce max active drips
        while (activeDrips.Count >= maxActiveDrips)
        {
            var old = activeDrips[0];
            if (old.go != null) Destroy(old.go);
            if (old.glowGo != null) Destroy(old.glowGo);
            activeDrips.RemoveAt(0);
        }

        Vector3 spawnPos = GetDripSpawnPosition();

        GameObject go = new GameObject("BossHead_Drip");
        go.transform.position = spawnPos;

        float scale = Random.Range(dripMinScale, dripMaxScale);
        go.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = dripSprite;
        sr.sortingLayerName = "Default";

        int headOrder = (headSR != null) ? headSR.sortingOrder : 2000;
        sr.sortingOrder = headOrder + 1;

        // Per-drip color variation 
        float n = Random.value;
        Color dripColor;
        if (n < 0.25f)
            dripColor = new Color(0.68f, 0.16f, 0.74f, 0.95f);   // deeper purple
        else if (n > 0.8f)
            dripColor = new Color(0.94f, 0.44f, 0.98f, 0.95f);   // lighter
        else
            dripColor = new Color(
                0.78f + (n - 0.25f) * 0.2f,
                0.20f + (n - 0.25f) * 0.2f,
                0.82f + (n - 0.25f) * 0.15f,
                0.95f);     // standard range
        sr.color = dripColor;

        // Glow halo — child object behind the drip
        GameObject glowGo = null;
        SpriteRenderer glowSr = null;
        if (dripGlowSprite != null)
        {
            glowGo = new GameObject("BossHead_DripGlow");
            glowGo.transform.position = spawnPos;
            glowGo.transform.localScale = Vector3.one * scale * 2.2f;
            glowSr = glowGo.AddComponent<SpriteRenderer>();
            glowSr.sprite = dripGlowSprite;
            glowSr.sortingLayerName = "Default";
            glowSr.sortingOrder = headOrder; // one behind the drip
            glowSr.color = new Color(dripColor.r, dripColor.g, dripColor.b, 0.3f);
        }

        // Initial velocity
        Vector2 vel = new Vector2(
            Random.Range(-0.12f, 0.12f),
            -dripInitialSpeed);

        activeDrips.Add(new DripData
        {
            go = go,
            sr = sr,
            velocity = vel,
            elapsed = 0f,
            lifetime = dripLifetime + Random.Range(-0.15f, 0.15f),
            startScale = scale,
            wobblePhase = Random.Range(0f, Mathf.PI * 2f),
            glowGo = glowGo,
            glowSr = glowSr,
            shimmerPhase = Random.Range(0f, Mathf.PI * 2f),
            shimmerSpeed = Random.Range(6f, 10f),
            spinSpeed = Random.Range(-15f, 15f)
        });
    }

    void CleanupAllDrips()
    {
        foreach (var drip in activeDrips)
        {
            if (drip.go != null) Destroy(drip.go);
            if (drip.glowGo != null) Destroy(drip.glowGo);
        }
        activeDrips.Clear();
    }

    void CleanupAllRings()
    {
        foreach (var ring in activeRings)
            if (ring != null) Destroy(ring);
        activeRings.Clear();
    }


    // PULSE RINGS


    void BuildPulseRingSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[size * size];
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float outer = size * 0.50f;
                float inner = size * 0.42f;
                float mid = (inner + outer) * 0.5f;
                float halfW = (outer - inner) * 0.5f;
                if (d >= inner && d <= outer)
                {
                    float norm = (d - mid) / halfW;
                    float bell = Mathf.Exp(-norm * norm * 3.5f);
                    pixels[y * size + x] = new Color(0.91f, 0.33f, 0.96f, bell * 0.6f);
                }
                else
                {
                    pixels[y * size + x] = Color.clear;
                }
            }

        tex.SetPixels(pixels);
        tex.Apply();
        pulseRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                        Vector2.one * 0.5f, 32f);
    }

    IEnumerator StaggeredPulseLoop()
    {
        yield return new WaitForSeconds(0.4f);

        while (!isDestroyed)
        {
            StartCoroutine(ExpandRing());
            yield return new WaitForSeconds(pulseInterval * 0.5f);

            if (!isDestroyed) StartCoroutine(ExpandRing());
            yield return new WaitForSeconds(pulseInterval * 0.5f);
        }
    }

    IEnumerator ExpandRing()
    {
        if (pulseRingSprite == null) yield break;

        Vector3 origin = transform.position;

        GameObject ringObj = new GameObject("BossHead_PulseRing");
        ringObj.transform.position = origin;
        activeRings.Add(ringObj);

        SpriteRenderer sr = ringObj.AddComponent<SpriteRenderer>();
        sr.sprite = pulseRingSprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 3000; // Above grass (~1600 max), below fog (5000)
        sr.color = new Color(0.91f, 0.33f, 0.96f, 1f); // light purple 

        const float spriteWorldSize = 4f;
        float startScale = 0.08f;

        float safeDuration = Mathf.Max(pulseDuration, 0.1f);
        float safeEndScale = (Mathf.Max(pulseMaxRadius, 0.1f) * 2f) / spriteWorldSize;

        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            if (ringObj == null || isDestroyed) break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);

            float scale = Mathf.Lerp(startScale, safeEndScale, 1f - Mathf.Pow(1f - t, 2.2f));
            ringObj.transform.localScale = Vector3.one * scale;

            float fadeIn = Mathf.Clamp01(t / 0.15f);
            float fadeOut = Mathf.Pow(1f - t, 1.5f);
            float alpha = pulseAlphaPeak * fadeIn * fadeOut;
            sr.color = new Color(0.91f, 0.33f, 0.96f, Mathf.Max(0f, alpha));

            yield return null;
        }

        if (ringObj != null)
        {
            activeRings.Remove(ringObj);
            Destroy(ringObj);
        }
    }


    // DAMAGE and DEATH


    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (isDestroyed) return false;
        isDestroyed = true;

        bossStats?.OnHeadDestroyed();

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDeath, transform.position);

        CleanupAllDrips();
        CleanupAllRings();

        // Death VFX 
        EnemyDeathVFX.Trigger(
            enemy: gameObject,
            duration: 0.8f,
            onComplete: null
        );

        return true;
    }

    void OnDestroy()
    {
        CleanupAllDrips();
        CleanupAllRings();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDestroyed) return;
        if (IsPlayerAttack(other)) TakeDamage(100f, other.gameObject);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDestroyed) return;
        if (IsPlayerAttack(collision.collider)) TakeDamage(100f, collision.gameObject);
    }

    bool IsPlayerAttack(Collider2D other) =>
        other.CompareTag("Player") ||
        other.GetComponent<PlayerMovement>() != null ||
        other.GetComponent<Weapon>() != null ||
        other.GetComponent<Projectile>() != null ||
        other.GetComponent<WeaponProjectile>() != null;

    public bool CanTakeDamage() => !isDestroyed;
    public float GetCurrentHealth() => isDestroyed ? 0f : 100f;
    public float GetMaxHealth() => 100f;
    public float GetHealthPercentage() => isDestroyed ? 0f : 1f;
    public bool IsDestroyed() => isDestroyed;
}



// TimedDestroy 

public class TimedDestroy : MonoBehaviour
{
    public float lifetime = 20f;
    public float fadeDelay = 15f;

    private SpriteRenderer sr;
    private float elapsed;

    void Start() => sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        elapsed += Time.deltaTime;

        if (elapsed >= fadeDelay && sr != null)
        {
            float t = (elapsed - fadeDelay) / (lifetime - fadeDelay);
            Color c = sr.color;
            c.a = Mathf.Lerp(1f, 0f, t);
            sr.color = c;
        }

        if (elapsed >= lifetime)
            Destroy(gameObject);
    }
}
