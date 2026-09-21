using System.Collections.Generic;
using UnityEngine;

// Snow-contrast layer for the Parfumer's poison cloud.

[DisallowMultipleComponent]
public class PoisonCloudVisual : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Overall strength of the contrast layer. Raise if the mist is still faint on snow; lower if it looks too dark on grass.")]
        [Range(0f, 2f)] public float opacity = 1f;

        [Tooltip("Size of the smoke relative to the cloud radius.")]
        public float sizeMultiplier = 1.0f;

        [Header("Colors (the shading comes from the textures)")]
        [Tooltip("Deep toxic green for the dense, shadowed parts of the smoke.")]
        public Color deepColor = new Color(0.16f, 0.52f, 0.08f, 1f);

        [Tooltip("Lighter yellow-green for the lit tops of the puffs.")]
        public Color lightColor = new Color(0.46f, 0.80f, 0.18f, 1f);

        [Header("Shade")]
        [Range(0f, 1f)] public float shadeOpacity = 0.40f;

        [Header("Puffs")]
        [Range(0, 12)] public int puffCount = 7;
        [Range(0f, 1f)] public float puffOpacity = 0.34f;

        [Tooltip("How much the smoke spreads out over the cloud's lifetime (0.2 = +20% size).")]
        public float expansion = 0.22f;

        [Tooltip("How far puffs drift from where they started, as a fraction of the radius.")]
        public float drift = 0.18f;

        [Header("Curls")]
        [Range(0, 6)] public int curlCount = 3;
        [Range(0f, 1f)] public float curlOpacity = 0.22f;

        [Header("Timing")]
        public float fadeInSeconds = 0.5f;
        public float fadeOutSeconds = 1.2f;

        [Tooltip("When clouds overlap (a parked Parfumer stacks several), share visibility so they don't darken into a blob.")]
        public bool dampenOverlaps = true;
    }

    /// Global multiplier for every cloud. Set it from your biome setup, e.g. 1.3 in
    /// a snow biome and 0.6 in a dark swamp. Defaults to 1.
    public static float BiomeContrast = 1f;

    private Settings s;
    private float radius = 2.5f, duration = 5f, elapsed, time;
    private bool configured;

    private Transform container;           // parent of our own renderers
    private int fallbackLayer, fallbackOrder;
    private int sortingFramesLeft = 4;     // re-check fog renderers for a few frames (they may be built lazily)

    private SpriteRenderer shade;
    private float shadeAngle;

    private sealed class Puff
    {
        public SpriteRenderer sr;
        public Vector2 start, dir;
        public float size, baseAngle, wobblePhase, noiseSeed, driftSpeed;
        public Color tint;
    }

    private sealed class Curl
    {
        public SpriteRenderer sr;
        public float angle, spin, clock, period, size;
        public bool flip;
    }

    private readonly List<Puff> puffs = new List<Puff>();
    private readonly List<Curl> curls = new List<Curl>();

    private float fade, groundMul = 1f;

    private static readonly List<PoisonCloudVisual> Active = new List<PoisonCloudVisual>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Active.Clear(); BiomeContrast = 1f; }

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    /// Call right after the fog visual is configured. fallbackSortingLayerID/Order
    /// are only used if the fog has no renderers to sort under.
    public void Configure(float radius, float duration, Settings settings,
                          int fallbackSortingLayerID, int fallbackSortingOrder)
    {
        if (configured) return;
        configured = true;

        s = settings ?? new Settings();
        this.radius = Mathf.Max(0.05f, radius);
        this.duration = Mathf.Max(0.1f, duration);
        fallbackLayer = fallbackSortingLayerID;
        fallbackOrder = fallbackSortingOrder;
        time = Random.Range(0f, 100f);

        Build();
        ResolveSorting();
        Tick(0f);
    }

    /// Bake the shared textures ahead of time (loading screen).
    public static void WarmUp()
    {
        _ = ShadeSprite;
        for (int i = 0; i < PuffVariants; i++) _ = GetPuffSprite(i);
        _ = CurlSprite;
    }

    private void Build()
    {
        container = new GameObject("SnowContrast").transform;
        container.SetParent(transform, false);

        float R = radius * s.sizeMultiplier;

        shade = MakeSprite("Shade", ShadeSprite);
        shadeAngle = Random.Range(0f, 360f);

        for (int i = 0; i < s.puffCount; i++)
        {
            var p = new Puff
            {
                sr = MakeSprite("Puff", GetPuffSprite(i % PuffVariants)),
                start = Random.insideUnitCircle * R * 0.6f,
                size = R * Random.Range(1.3f, 1.8f),
                baseAngle = Random.Range(-12f, 12f),   // keep roughly upright so the top-lighting stays believable
                wobblePhase = Random.Range(0f, 10f),
                noiseSeed = Random.Range(0f, 100f),
                driftSpeed = Random.Range(0.6f, 1.2f),
                tint = Color.Lerp(s.deepColor, s.lightColor, Random.Range(0f, 0.7f)),
            };
            p.dir = p.start.sqrMagnitude > 1e-4f ? p.start.normalized : Random.insideUnitCircle.normalized;
            p.sr.flipX = Random.value < 0.5f;
            puffs.Add(p);
        }

        for (int i = 0; i < s.curlCount; i++)
        {
            var c = new Curl
            {
                sr = MakeSprite("Curl", CurlSprite),
                angle = Random.Range(0f, 360f),
                spin = Random.Range(10f, 22f) * (Random.value < 0.5f ? -1f : 1f),
                period = Random.Range(3.0f, 4.5f),
                size = R * Random.Range(1.2f, 1.6f),
                flip = Random.value < 0.5f,
            };
            c.clock = Random.Range(0f, c.period);
            c.sr.flipX = c.flip;
            curls.Add(c);
        }
    }

    private SpriteRenderer MakeSprite(string childName, Sprite sprite)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(container, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        return sr;
    }

    // Draw just beneath the fog: shade lowest, then puffs, then curls.
    private void ResolveSorting()
    {
        int layer = fallbackLayer, order = fallbackOrder;
        bool found = false;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform.IsChildOf(container)) continue;
            if (!found || r.sortingOrder < order)
            {
                order = r.sortingOrder;
                layer = r.sortingLayerID;
                found = true;
            }
        }

        if (found) order -= 1;  // below the fog's lowest renderer

        if (shade != null) { shade.sortingLayerID = layer; shade.sortingOrder = order - 2; }
        foreach (var p in puffs) { p.sr.sortingLayerID = layer; p.sr.sortingOrder = order - 1; }
        foreach (var c in curls) { c.sr.sortingLayerID = layer; c.sr.sortingOrder = order; }
    }

    private void Update()
    {
        if (!configured) return;

        float dt = Time.deltaTime;
        elapsed += dt;
        time += dt;

        if (sortingFramesLeft > 0) { sortingFramesLeft--; ResolveSorting(); }

        Tick(dt);

        if (elapsed >= duration + 1f) Destroy(gameObject); // safety; PoisonCloud normally owns the lifetime
    }

    private void Tick(float dt)
    {
        float fadeIn = s.fadeInSeconds > 0f ? Mathf.Clamp01(elapsed / s.fadeInSeconds) : 1f;
        float outLen = Mathf.Min(Mathf.Max(0.01f, s.fadeOutSeconds), duration * 0.5f);
        float fadeOut = Mathf.Clamp01((duration - elapsed) / outLen);
        fade = Smooth01(fadeIn) * Smooth01(fadeOut);

        UpdateCrowding(dt);

        float life = Mathf.Clamp01(elapsed / duration);
        float grow = Mathf.Lerp(0.75f, 1f, EaseOutCubic(fadeIn))      // billows out on release
                   * (1f + s.expansion * life);                       // keeps spreading as it ages
        float master = fade * groundMul * s.opacity * Mathf.Max(0f, BiomeContrast);
        float R = radius * s.sizeMultiplier;

        if (shade != null)
        {
            shadeAngle += dt * 4f;
            shade.transform.localRotation = Quaternion.Euler(0f, 0f, shadeAngle);
            SetScale(shade.transform, 2.5f * R * grow);
            shade.color = WithAlpha(s.deepColor, s.shadeOpacity * master);
        }

        for (int i = 0; i < puffs.Count; i++)
        {
            var p = puffs[i];

            // Drift outward along its own direction plus slow turbulent wander.
            float nx = Mathf.PerlinNoise(p.noiseSeed, time * 0.25f) - 0.5f;
            float ny = Mathf.PerlinNoise(p.noiseSeed + 31.7f, time * 0.25f) - 0.5f;
            Vector2 pos = p.start * grow
                        + p.dir * (s.drift * R * life * p.driftSpeed)
                        + new Vector2(nx, ny) * (R * 0.12f);
            p.sr.transform.localPosition = new Vector3(pos.x, pos.y, 0f);

            float wobble = Mathf.Sin(time * 0.35f + p.wobblePhase) * 8f;
            p.sr.transform.localRotation = Quaternion.Euler(0f, 0f, p.baseAngle + wobble);

            float breathe = 1f + 0.04f * Mathf.Sin(time * 0.9f + p.wobblePhase);
            SetScale(p.sr.transform, p.size * grow * breathe);

            // Each puff thins at its own pace, and the whole smoke thins as it expands.
            float density = Mathf.Lerp(0.7f, 1f, Mathf.PerlinNoise(p.noiseSeed + 7.3f, time * 0.4f))
                          * Mathf.Lerp(1f, 0.75f, life);
            p.sr.color = WithAlpha(p.tint, s.puffOpacity * density * master);
        }

        for (int i = 0; i < curls.Count; i++)
        {
            var c = curls[i];
            c.clock += dt;
            if (c.clock >= c.period)
            {
                c.clock -= c.period;
                c.angle = Random.Range(0f, 360f);
                c.flip = !c.flip;
                c.sr.flipX = c.flip;
            }

            float k = c.clock / c.period;
            float env = Mathf.Sin(k * Mathf.PI);   // appear and dissolve smoothly
            env *= env;

            c.angle += c.spin * dt;
            c.sr.transform.localRotation = Quaternion.Euler(0f, 0f, c.angle);
            SetScale(c.sr.transform, c.size * grow * Mathf.Lerp(0.9f, 1.1f, k));
            c.sr.color = WithAlpha(Color.Lerp(s.deepColor, s.lightColor, 0.35f), s.curlOpacity * env * master);
        }
    }

    private void UpdateCrowding(float dt)
    {
        if (!s.dampenOverlaps) { groundMul = 1f; return; }

        float crowd = 0f;
        Vector2 p = transform.position;
        for (int i = 0; i < Active.Count; i++)
        {
            var o = Active[i];
            if (o == null || o == this || !o.configured) continue;
            float reach = radius + o.radius;
            float d = Vector2.Distance(p, o.transform.position);
            if (d < reach) crowd += (1f - d / reach) * o.fade;
        }

        float target = 1f / (1f + crowd * 0.9f);
        groundMul = elapsed <= dt ? target : Mathf.MoveTowards(groundMul, target, dt * 1.5f);
    }

    //  Helpers

    private static void SetScale(Transform t, float d) => t.localScale = new Vector3(d, d, 1f);
    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    private static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    private static float Smooth(float a, float b, float x) => Smooth01((x - a) / (b - a));
    private static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }

    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2.03f;
        }
        return sum / norm;
    }

    //  Shared textures (grayscale + alpha, tinted per renderer, built once).
    //  Every one feathers to zero alpha well inside its border.

    private const int PuffVariants = 3;
    private static Sprite _shade, _curl;
    private static readonly Sprite[] _puffs = new Sprite[PuffVariants];

    private static Sprite ShadeSprite => _shade != null ? _shade : (_shade = BuildShade());
    private static Sprite CurlSprite => _curl != null ? _curl : (_curl = BuildCurl());

    private static Sprite GetPuffSprite(int i)
    {
        if (_puffs[i] == null) _puffs[i] = BuildPuff(4.1f + i * 17.3f, 9.7f + i * 5.9f, "PoisonSmokePuff" + i);
        return _puffs[i];
    }

    private static Sprite ToSprite(Color[] px, int size, string name)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = name
        };
        tex.SetPixels(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                   size, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    // Broad, very soft tint with gentle noise so it never reads as a disc.
    private static Sprite BuildShade()
    {
        const int N = 128;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float n = Fbm(u * 1.6f + 13.1f, v * 1.6f + 2.3f, 4);
                float d = Mathf.Sqrt(u * u + v * v) + (n - 0.5f) * 0.35f;
                float a = (1f - Smooth(0.10f, 0.92f, d)) * Mathf.Lerp(0.6f, 1f, Smooth(0.3f, 0.7f, n));
                a *= 1f - Smooth(0.85f, 1f, Mathf.Sqrt(u * u + v * v)); // guarantee a clean border
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        return ToSprite(px, N, "PoisonSmokeShade");
    }

    // Billowy smoke puff: domain-warped noise, eroded soft silhouette, lit from above.
    private static Sprite BuildPuff(float ox, float oy, string name)
    {
        const int N = 128;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);

                float wx = Fbm(u * 1.5f + ox + 5.2f, v * 1.5f + oy + 1.3f, 3) - 0.5f;
                float wy = Fbm(u * 1.5f + ox + 8.1f, v * 1.5f + oy + 6.7f, 3) - 0.5f;
                float n = Fbm(u * 2.2f + wx * 1.8f + ox, v * 2.2f + wy * 1.8f + oy, 5);

                // Noise pushes the silhouette in and out, so edges are lumpy but feathered.
                float edge = 0.62f + (n - 0.5f) * 0.55f;
                float a = 1f - Smooth(edge - 0.40f, edge, r);
                a *= Mathf.Lerp(0.45f, 1f, Smooth(0.32f, 0.68f, n));
                a *= 1f - Smooth(0.80f, 0.98f, r);

                // Light from above: lighter tops and dense bright billows, darker undersides.
                float shadeN = Smooth(0.35f, 0.72f, n);
                float g = Mathf.Lerp(0.55f, 1f, 0.6f * shadeN + 0.4f * Smooth(-0.8f, 0.8f, v));

                px[y * N + x] = new Color(g, g, g, a);
            }
        return ToSprite(px, N, name);
    }

    // A faint swirl of smoke: a wide, blurry spiral band broken up by noise.
    // Its width is a large fraction of its radius, so it reads as smoke, not a stroke.
    private static Sprite BuildCurl()
    {
        const int N = 128;
        var px = new Color[N * N];
        float sweep = Mathf.PI * 1.4f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;

                // Warp the sample point so the spiral wanders organically.
                float wx = (Fbm(u * 2.0f + 3.3f, v * 2.0f + 8.8f, 3) - 0.5f) * 0.30f;
                float wy = (Fbm(u * 2.0f + 11.9f, v * 2.0f + 4.4f, 3) - 0.5f) * 0.30f;
                float uu = u + wx, vv = v + wy;

                float r = Mathf.Sqrt(uu * uu + vv * vv);
                float ang = Mathf.Atan2(vv, uu);
                if (ang < 0f) ang += Mathf.PI * 2f;
                float t = ang / sweep;

                float a = 0f;
                if (t <= 1f)
                {
                    float target = Mathf.Lerp(0.22f, 0.70f, t);
                    float width = Mathf.Lerp(0.07f, 0.17f, t);
                    float k = (r - target) / width;
                    a = Mathf.Exp(-k * k) * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.2f);
                    a *= Mathf.Lerp(0.15f, 1f, Smooth(0.35f, 0.7f, Fbm(u * 3.5f + 21f, v * 3.5f + 17f, 4)));
                }
                a *= 1f - Smooth(0.80f, 0.98f, Mathf.Sqrt(u * u + v * v));

                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        return ToSprite(px, N, "PoisonSmokeCurl");
    }
}
