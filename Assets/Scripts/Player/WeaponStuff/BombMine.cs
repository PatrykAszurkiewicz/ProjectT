using UnityEngine;
using System.Collections.Generic;


// Proximity mine for the Bomb Launcher weapon.

public class BombMine : MonoBehaviour
{
    // Config
    private float damage;
    private float proximityRadius = 1.5f;
    private float explosionRadius = 3f;
    private bool friendlyFire = false;
    private float armDelay = 0.5f;

    // State
    private bool isArmed = false;
    private float armTimer;
    private bool hasExploded = false;
    private bool isDisintegrating = false;

    // Visuals
    private SpriteRenderer bodyRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer coreRenderer; // bright white inner flash (sits on top of glow halo)
    private SpriteRenderer runeRenderer; // decorative ring around the bomb
    private float blinkTimer;
    private float blinkInterval = 0.7f;
    // Peak: near-white core with a hint of magenta — pops on both day (green grass) and night (dark) biomes.
    // Off:  deep purple at low alpha — readable as "this thing is alive but quiet."
    private Color glowColor = new Color(1f, 0.85f, 1f, 1f);
    private Color glowOffColor = new Color(0.4f, 0.1f, 0.6f, 0.10f);
    // Tinted outer halo that surrounds the white core (uses the existing glow renderer at full saturation).
    private Color glowHaloColor = new Color(0.85f, 0.25f, 1f, 0.85f);
    private float spawnScale = 0f; // for pop-in animation

    private const float SORT_PRECISION = 10f;
    private const int SORT_ORDER_BASE = 1000;
    private SpriteRenderer cachedPlayerRenderer;

    public void Initialize(float damage, float proximityRadius, float explosionRadius,
                           bool friendlyFire, float armDelay)
    {
        this.damage = damage;
        this.proximityRadius = proximityRadius;
        this.explosionRadius = explosionRadius;
        this.friendlyFire = friendlyFire;
        this.armDelay = armDelay;
        this.armTimer = armDelay;

        if (this.damage <= 0f)
            this.damage = 50f;
    }

    private void Start()
    {
        BuildVisual();
        isArmed = false;
        spawnScale = 0f; // start at 0 for pop-in
    }

    private void Update()
    {
        if (isDisintegrating) return; // handled by DisintegrateMine component
        if (hasExploded) return;

        // Pop-in animation
        if (spawnScale < 1f)
        {
            spawnScale = Mathf.Min(spawnScale + Time.deltaTime / 0.2f, 1f);
            float ease = 1f + 2.7f * Mathf.Pow(spawnScale - 1f, 3f) + 1.7f * Mathf.Pow(spawnScale - 1f, 2f);
            transform.localScale = Vector3.one * ease;
        }

        // Arm delay
        if (!isArmed)
        {
            armTimer -= Time.deltaTime;
            if (armTimer <= 0f)
                isArmed = true;
        }

        // Glow pulse
        if (glowRenderer != null)
        {
            float interval = isArmed ? blinkInterval : blinkInterval * 2.5f;
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= interval) blinkTimer -= interval;

            float phase = blinkTimer / interval; // 0..1 across the cycle

            // Sharp blink: ramp up fast (0..0.1), hold near peak (0.1..0.25),
            // decay (0.25..0.5), then dark hold (0.5..1).
            float pulse;
            if (phase < 0.10f)
                pulse = phase / 0.10f;                     // attack
            else if (phase < 0.25f)
                pulse = 1f;                                // hold at peak
            else if (phase < 0.50f)
                pulse = 1f - (phase - 0.25f) / 0.25f;      // decay
            else
                pulse = 0f;                                // dark hold

            // Outer halo: tinted purple at peak, dim purple when off.
            glowRenderer.color = Color.Lerp(glowOffColor, glowHaloColor, pulse);
            glowRenderer.transform.localScale = Vector3.one * (0.22f + pulse * 0.28f);

            // Inner core flash: nearly white at peak, fully invisible when off.
            // Brighter peak alpha and tighter scale make it pop against grass + dark biomes.
            if (coreRenderer != null)
            {
                Color core = glowColor;
                core.a = pulse * 0.95f;
                coreRenderer.color = core;
                coreRenderer.transform.localScale = Vector3.one * (0.08f + pulse * 0.18f);
            }
        }

        // Rune ring slow rotation
        if (runeRenderer != null)
            runeRenderer.transform.Rotate(0, 0, 30f * Time.deltaTime);

        // Y-sort: hybrid approach.
        const float SORT_Y_OFFSET = 3f;
        if (bodyRenderer != null)
        {
            float sortY = transform.position.y + SORT_Y_OFFSET;
            int order = SORT_ORDER_BASE + Mathf.RoundToInt(-sortY * SORT_PRECISION);

            if (cachedPlayerRenderer == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) cachedPlayerRenderer = p.GetComponent<SpriteRenderer>();
            }
            if (cachedPlayerRenderer != null)
            {
                int maxAllowed = cachedPlayerRenderer.sortingOrder - 1;
                if (order > maxAllowed) order = maxAllowed;
            }

            bodyRenderer.sortingOrder = order;
            if (glowRenderer != null) glowRenderer.sortingOrder = order + 1;
            if (coreRenderer != null) coreRenderer.sortingOrder = order + 2;
            if (runeRenderer != null) runeRenderer.sortingOrder = order - 1;
        }

        if (isArmed)
            CheckProximity();
    }

    //  PUBLIC: smooth removal when replaced by newer mine 
    // Called by BombLauncherSystem when this mine is the oldest and must be removed.
    public void Disintegrate()
    {
        if (isDisintegrating || hasExploded) return;
        isDisintegrating = true;
        isArmed = false; // disable proximity

        // Hide core flash — DisintegrateMine only fades body/glow/rune.
        // The core is a transient blink layer; just snap it off so it doesn't
        // sit at full brightness while the rest of the mine fades.
        if (coreRenderer != null)
        {
            Color c = coreRenderer.color;
            c.a = 0f;
            coreRenderer.color = c;
        }

        var fx = gameObject.AddComponent<DisintegrateMine>();
        fx.Initialize(bodyRenderer, glowRenderer, runeRenderer);
    }

    //  PROXIMITY 

    private void CheckProximity()
    {
        Vector2 pos = transform.position;
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (var go in enemies)
        {
            if (go == null || !go.activeInHierarchy) continue;
            if (Vector2.Distance(pos, (Vector2)go.transform.position) <= proximityRadius)
            {
                Explode();
                return;
            }
        }
    }

    //  EXPLOSION 

    private void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        Vector2 pos = transform.position;
        SpawnExplosionEffect();

        int hits = 0;
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        HashSet<GameObject> damaged = new HashSet<GameObject>();

        foreach (var go in enemies)
        {
            if (go == null || !go.activeInHierarchy) continue;
            if (damaged.Contains(go)) continue;

            float dist = Vector2.Distance(pos, (Vector2)go.transform.position);
            if (dist > explosionRadius) continue;

            float falloff = 1f - (dist / explosionRadius) * 0.5f;
            float dmg = damage * Mathf.Max(falloff, 0.3f);
            damaged.Add(go);

            EnemyStats enemyStats = go.GetComponent<EnemyStats>();
            if (enemyStats != null)
            {
                enemyStats.TakeDamage(dmg);
                CombatJuice.OnPlayerHitEnemy(go, isMelee: false);
                hits++;
                continue;
            }

            Boss1 boss = go.GetComponent<Boss1>();
            if (boss != null)
            {
                boss.TakeDamage(dmg);
                CombatJuice.OnPlayerHitEnemy(go, isMelee: false);
                hits++;
                continue;
            }

            CharacterStats cs = go.GetComponent<CharacterStats>();
            if (cs != null)
            {
                cs.TakeDamage(dmg);
                CombatJuice.OnPlayerHitEnemy(go, isMelee: false);
                hits++;
                continue;
            }

            IDamageable dmgable = go.GetComponent<IDamageable>();
            if (dmgable != null)
            {
                dmgable.TakeDamage(dmg, gameObject);
                CombatJuice.OnPlayerHitEnemy(go, isMelee: false);
                hits++;
            }
        }

        // Backup: FindObjectsByType
        if (hits == 0)
        {
            EnemyStats[] allStats = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);
            foreach (var es in allStats)
            {
                if (es == null) continue;
                float dist = Vector2.Distance(pos, (Vector2)es.transform.position);
                if (dist <= explosionRadius)
                {
                    float falloff = 1f - (dist / explosionRadius) * 0.5f;
                    es.TakeDamage(damage * Mathf.Max(falloff, 0.3f));
                    CombatJuice.OnPlayerHitEnemy(es.gameObject, isMelee: false);
                    hits++;

                }
            }
        }

        // BossHead
        var heads = FindObjectsByType<BossHead>(FindObjectsSortMode.None);
        foreach (var head in heads)
        {
            if (head == null) continue;
            float dist = Vector2.Distance(pos, (Vector2)head.transform.position);
            if (dist > explosionRadius) continue;
            var d = head.GetComponent<IDamageable>();
            if (d != null)
            {
                float falloff = 1f - (dist / explosionRadius) * 0.5f;
                d.TakeDamage(damage * Mathf.Max(falloff, 0.3f), gameObject);
            }
        }

        if (friendlyFire)
            ApplyFriendlyFire(pos);

        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.mineExplosion.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.mineExplosion, transform.position);
        }

        Destroy(gameObject, 0.05f);
    }

    private void ApplyFriendlyFire(Vector2 pos)
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            float dist = Vector2.Distance(pos, (Vector2)playerObj.transform.position);
            if (dist <= explosionRadius)
            {
                var ps = playerObj.GetComponent<CharacterStats>();
                if (ps != null)
                {
                    float falloff = 1f - (dist / explosionRadius) * 0.5f;
                    ps.TakeDamage(damage * Mathf.Max(falloff, 0.3f));
                }
            }
        }

        Collider2D[] structHits = Physics2D.OverlapCircleAll(pos, explosionRadius);
        foreach (var hit in structHits)
        {
            if (hit.CompareTag("Tower") || hit.CompareTag("Core"))
            {
                var consumer = hit.GetComponent<IEnergyConsumer>();
                if (consumer != null && EnergyManager.Instance != null)
                {
                    float dist = Vector2.Distance(pos, hit.bounds.center);
                    float falloff = 1f - (dist / explosionRadius) * 0.5f;
                    EnergyManager.Instance.DamageEnergyConsumer(
                        consumer, damage * Mathf.Max(falloff, 0.3f), gameObject);
                }
            }
        }
    }

    private void SpawnExplosionEffect()
    {
        GameObject fx = new GameObject("BombExplosionFX");
        fx.transform.position = transform.position;
        fx.AddComponent<BombExplosionVFX>().Initialize(explosionRadius);
    }

    //  VISUALS 

    private void BuildVisual()
    {
        // Rune circle on ground beneath bomb
        GameObject runeObj = new GameObject("Rune");
        runeObj.transform.SetParent(transform, false);
        runeObj.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        runeRenderer = runeObj.AddComponent<SpriteRenderer>();
        runeRenderer.sprite = GenerateRuneSprite();
        runeRenderer.sortingOrder = SORT_ORDER_BASE - 1;
        runeRenderer.color = new Color(0.5f, 0.2f, 0.7f, 0.25f);
        runeObj.transform.localScale = Vector3.one * 0.7f;

        // Main bomb body
        GameObject bodyObj = new GameObject("BombBody");
        bodyObj.transform.SetParent(transform, false);
        bodyRenderer = bodyObj.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = GenerateBombSprite();
        bodyRenderer.sortingOrder = SORT_ORDER_BASE;
        bodyObj.transform.localScale = Vector3.one * 0.55f;

        // Glow indicator (outer halo, tinted)
        GameObject glowObj = new GameObject("BombGlow");
        glowObj.transform.SetParent(transform, false);
        glowObj.transform.localPosition = Vector3.zero;
        glowRenderer = glowObj.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = GenerateGlowSprite();
        glowRenderer.sortingOrder = SORT_ORDER_BASE + 1;
        glowRenderer.color = glowOffColor;
        glowObj.transform.localScale = Vector3.one * 0.22f;

        // Core flash — small bright white center that punches through grass
        // overlap and reads clearly on both day (green) and night (dark) biomes.
        GameObject coreObj = new GameObject("BombCore");
        coreObj.transform.SetParent(transform, false);
        coreObj.transform.localPosition = Vector3.zero;
        coreRenderer = coreObj.AddComponent<SpriteRenderer>();
        coreRenderer.sprite = GenerateGlowSprite(); // same soft disc sprite, just tinted/scaled differently
        coreRenderer.sortingOrder = SORT_ORDER_BASE + 2;
        coreRenderer.color = new Color(1f, 1f, 1f, 0f); // starts invisible
        coreObj.transform.localScale = Vector3.one * 0.10f;
    }

    //  PROCEDURAL SPRITES 

    private static Sprite _cachedBombSprite;
    private static Sprite GenerateBombSprite()
    {
        if (_cachedBombSprite != null) return _cachedBombSprite;
        const int SIZE = 48;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[SIZE * SIZE];
        Vector2 center = new Vector2(SIZE * 0.5f, SIZE * 0.48f);
        float outerR = SIZE * 0.42f, innerR = outerR - 2f;
        Color shell = new Color(0.28f, 0.18f, 0.12f, 1f);
        Color shellLight = new Color(0.45f, 0.32f, 0.18f, 1f);
        Color rivetCol = new Color(0.55f, 0.45f, 0.25f, 1f);
        Color bandCol = new Color(0.35f, 0.22f, 0.10f, 1f);
        float[] rivetAng = { 30, 90, 150, 210, 270, 330 };
        float rivetDist = outerR * 0.7f, rivetR = 1.8f;

        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
            {
                Vector2 pos = new Vector2(x, y);
                float dist = Vector2.Distance(pos, center);
                Color c = Color.clear;
                if (dist <= outerR)
                {
                    float nx = (x - center.x) / outerR, ny = (y - center.y) / outerR;
                    c = Color.Lerp(shell, shellLight, Mathf.Clamp01(0.5f + nx * 0.3f + ny * 0.3f));
                    if (Mathf.Abs(y - center.y) < 3f) c = Color.Lerp(c, bandCol, 0.6f);
                    if (dist > innerR) c.a = 1f - (dist - innerR) / (outerR - innerR);
                    foreach (float a in rivetAng)
                    {
                        float rad = a * Mathf.Deg2Rad;
                        Vector2 rp = center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * rivetDist;
                        float rd = Vector2.Distance(pos, rp);
                        if (rd < rivetR) c = Color.Lerp(c, rivetCol, (1f - rd / rivetR) * 0.8f);
                    }
                }
                Vector2 fb = new Vector2(SIZE * 0.5f, SIZE * 0.82f);
                float fd = Vector2.Distance(pos, fb);
                if (fd < 3.5f) { float ft = 1f - fd / 3.5f; c = Color.Lerp(c, new Color(0.4f, 0.3f, 0.15f, 1f), ft); c.a = Mathf.Max(c.a, ft); }
                pixels[y * SIZE + x] = c;
            }
        tex.SetPixels(pixels); tex.Apply();
        _cachedBombSprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), Vector2.one * 0.5f, SIZE);
        return _cachedBombSprite;
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

    private static Sprite _cachedRuneSprite;
    private static Sprite GenerateRuneSprite()
    {
        if (_cachedRuneSprite != null) return _cachedRuneSprite;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false)
        { filterMode = FilterMode.Bilinear };
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float outerR = S * 0.46f, innerR = S * 0.38f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = 0f;
                // Dashed ring
                if (d >= innerR && d <= outerR)
                {
                    float angle = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    bool dash = ((int)(angle / 30f)) % 2 == 0;
                    if (dash)
                    {
                        float mid = (innerR + outerR) * 0.5f;
                        float hw = (outerR - innerR) * 0.5f;
                        a = 1f - Mathf.Abs(d - mid) / hw;
                    }
                }
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        tex.SetPixels(px); tex.Apply();
        _cachedRuneSprite = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, S);
        return _cachedRuneSprite;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, proximityRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}


// Smooth disintegration effect for mines being replaced.

public class DisintegrateMine : MonoBehaviour
{
    private SpriteRenderer bodyRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer runeRenderer;

    private float timer;
    private const float DURATION = 0.5f;
    private readonly List<SparkParticle> sparks = new List<SparkParticle>();
    private bool sparksSpawned = false;

    public void Initialize(SpriteRenderer body, SpriteRenderer glow, SpriteRenderer rune)
    {
        bodyRenderer = body;
        glowRenderer = glow;
        runeRenderer = rune;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = timer / DURATION;

        if (!sparksSpawned)
        {
            sparksSpawned = true;
            SpawnSparks();
        }

        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // Shrink with wobble
        float scale = (1f - t) * (1f + 0.1f * Mathf.Sin(t * 30f));
        transform.localScale = Vector3.one * Mathf.Max(scale, 0f);

        // Fade all renderers to purple then transparent
        float alpha = 1f - t * t;
        Color fadeColor = new Color(0.5f, 0.15f, 0.7f, alpha);

        if (bodyRenderer != null) bodyRenderer.color = fadeColor;
        if (glowRenderer != null)
        {
            Color gc = glowRenderer.color;
            gc.a = alpha;
            glowRenderer.color = gc;
        }
        if (runeRenderer != null)
        {
            Color rc = runeRenderer.color;
            rc.a *= (1f - t);
            runeRenderer.color = rc;
        }

        // Update spark particles
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            if (s.go == null) { sparks.RemoveAt(i); continue; }
            s.lifetime -= Time.deltaTime;
            if (s.lifetime <= 0f) { Destroy(s.go); sparks.RemoveAt(i); continue; }

            s.velocity += Vector2.down * 2f * Time.deltaTime;
            s.go.transform.position += (Vector3)(s.velocity * Time.deltaTime);

            float st = 1f - (s.lifetime / s.maxLifetime);
            float sz = Mathf.Lerp(s.startSize, 0.01f, st);
            s.go.transform.localScale = Vector3.one * sz;

            Color sc = s.sr.color;
            sc.a = 1f - st;
            s.sr.color = sc;
        }
    }

    private void SpawnSparks()
    {
        Vector3 pos = transform.position;
        for (int i = 0; i < 8; i++)
        {
            GameObject go = new GameObject("DisintSpark");
            go.transform.position = pos;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 5100;

            // Procedural tiny circle
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

            // Purple-ish spark color
            sr.color = new Color(
                Random.Range(0.6f, 0.9f),
                Random.Range(0.1f, 0.4f),
                Random.Range(0.7f, 1f),
                1f);

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

