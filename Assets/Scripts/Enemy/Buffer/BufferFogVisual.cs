using System.Collections.Generic;
using UnityEngine;

// Procedural fog visual for the Buffer's lingering patch.

public class BufferFogVisual : MonoBehaviour
{
    [Header("Palette")]
    [Tooltip("Mist body color. Alpha drives additive intensity, not transparency.")]
    [SerializeField] private Color mistColor = new Color(0.35f, 0.10f, 0.55f, 0.28f);

    [Tooltip("Wisp / tendril color. A pale lilac reads nicely against the dark mist.")]
    [SerializeField] private Color wispColor = new Color(0.75f, 0.55f, 1.00f, 0.55f);

    [Tooltip("Bright lightning-flash color. Saturated, brief.")]
    [SerializeField] private Color lightningColor = new Color(0.95f, 0.55f, 1.00f, 0.95f);

    [Tooltip("Stasis storm arc color. Cool cyan-lilac for the 'electric haze' feel.")]
    [SerializeField] private Color stasisColor = new Color(0.65f, 0.80f, 1.00f, 0.50f);

    [Tooltip("Transparent edge color for mist falloff. Keep alpha 0.")]
    [SerializeField] private Color outerColor = new Color(0.15f, 0.05f, 0.30f, 0f);

    [Header("Mist  ──────────────────────────────────")]
    [SerializeField] private bool enableMist = true;

    [Tooltip("How many soft mist puffs to spawn. 6 reads as a wispy haze; " +
             "9+ becomes opaque.")]
    [SerializeField] private int mistPuffCount = 6;

    [Tooltip("Min/Max scale of each puff, as a fraction of fog radius.")]
    [SerializeField] private Vector2 mistScaleRange = new Vector2(0.40f, 0.75f);

    [Tooltip("Puff orbital drift speed (radians/sec).")]
    [SerializeField] private float mistDrift = 0.6f;

    [Tooltip("Irregular vertical bob amplitude per puff (world units).")]
    [SerializeField] private float mistBobAmplitude = 0.25f;

    [Tooltip("Base vertical bob speed (radians/sec).")]
    [SerializeField] private float mistBobSpeed = 1.1f;

    [Tooltip("Subtle scale pulse amplitude (fraction of base scale).")]
    [SerializeField] private float mistPulseAmplitude = 0.08f;
    [SerializeField] private float mistPulseSpeed = 1.4f;

    [Header("Wisps  ─────────────────────────────────")]
    [SerializeField] private bool enableWisps = true;

    [Tooltip("Average seconds between wisp spawns. Lower = more wisps.")]
    [SerializeField] private float wispSpawnInterval = 0.45f;

    [Tooltip("Wisp lifetime in seconds.")]
    [SerializeField] private float wispLifetime = 0.85f;

    [Tooltip("How far a wisp extends from its origin, as a fraction of fog radius.")]
    [SerializeField] private float wispReach = 0.85f;

    [Tooltip("Number of line segments per wisp. More = smoother curl.")]
    [SerializeField] private int wispSegments = 10;

    [Tooltip("How tightly the wisp curls. Higher = more sinuous.")]
    [SerializeField] private float wispCurl = 0.6f;

    [Tooltip("Wisp line width at the base (tapers to ~0 at the tip).")]
    [SerializeField] private float wispWidth = 0.08f;

    [Header("Lightning  ─────────────────────────────")]
    [Tooltip("Rare bright flash arc spanning the cloud. Distinct from the " +
             "Stasis Storm — this is a punctuation, not ambient.")]
    [SerializeField] private bool enableLightning = true;

    [Tooltip("Average seconds between lightning flashes. 2.5s feels rare; " +
             "1.0s starts to feel constant.")]
    [SerializeField] private float lightningInterval = 2.5f;

    [Tooltip("Number of jagged segments in a lightning bolt.")]
    [SerializeField] private int lightningSegments = 12;

    [Tooltip("Lightning line width.")]
    [SerializeField] private float lightningWidth = 0.07f;

    [Tooltip("How long a single lightning flash remains visible (seconds).")]
    [SerializeField] private float lightningDuration = 0.12f;

    [Header("Stasis Storm  ─────────────────────────")]
    [Tooltip("Continuous subtle electric threads crackling between points " +
             "inside the cloud. The 'always-on' equivalent of Lightning. " +
             "Keep alpha low (stasisColor.a) and threadCount modest — it's " +
             "meant to add motion and texture, not dominate.")]
    [SerializeField] private bool enableStasisStorm = true;

    [Tooltip("How many electric threads exist at once.")]
    [SerializeField] private int stasisThreadCount = 4;

    [Tooltip("Number of segments per thread (jaggedness resolution).")]
    [SerializeField] private int stasisSegments = 8;

    [Tooltip("Thread line width. Keep thin — these are crackles, not bolts.")]
    [SerializeField] private float stasisWidth = 0.025f;

    [Tooltip("How long each thread holds its current shape before re-rolling " +
             "to a new path (seconds). 0.12 reads as a fast crackle; 0.4 as " +
             "slow lazy arcs.")]
    [SerializeField] private float stasisRegenerateInterval = 0.15f;

    [Tooltip("Max length of a thread, as a fraction of fog radius. Threads " +
             "are randomly between 30% and this value, so most are short.")]
    [SerializeField] private float stasisThreadMaxLength = 0.9f;

    [Tooltip("Jaggedness amplitude (world units) of intermediate segments.")]
    [SerializeField] private float stasisJag = 0.10f;

    [Header("Fade")]
    [Range(0f, 1f)][SerializeField] private float fadeInFraction = 0.12f;
    [Range(0f, 1f)][SerializeField] private float fadeOutFraction = 0.25f;

    [Header("Rendering")]
    [Tooltip("Sorting order for all fog elements. Must be HIGHER than grass " +
             "(~1000-1600) and LOWER than character sprites (~3000+). 2000 " +
             "is the safe middle.")]
    [SerializeField] private int fogSortingOrder = 2000;

    [Tooltip("Z-offset applied once toward the camera. Belt-and-braces vs " +
             "any system that sorts by Z; sortingOrder does the real work.")]
    [SerializeField] private float fogZOffset = -0.5f;

    private float radius = 2.5f;
    private float duration = 5f;
    private float elapsed = 0f;

    private float wispSpawnTimer = 0f;
    private float nextLightningTime = 0f;
    private float stasisRegenerateTimer = 0f;

    // Cached shared materials.
    private Material sharedMaterial;
    private Material lineMaterial;

    private Puff[] mistPuffs;
    private readonly List<Wisp> activeWisps = new List<Wisp>();
    private LightningFlash currentLightning;
    private StasisThread[] stasisThreads;


    private struct Puff
    {
        public Transform tr;
        public MeshFilter filter;
        public Vector2 orbitCenter;
        public float orbitRadius;
        public float orbitPhase;
        public float baseScale;
        public float pulsePhase;
        public float bobPhaseA, bobPhaseB;
        public float bobRateA, bobRateB;
        public float bobAmpScale;
    }

    private struct Wisp
    {
        public GameObject go;
        public LineRenderer lr;
        public Vector2 origin;
        public float dirAngle;
        public float curlPhase;
        public float spawnTime;
    }

    private struct LightningFlash
    {
        public GameObject go;
        public LineRenderer lr;
        public float startTime;
    }

    private struct StasisThread
    {
        public GameObject go;
        public LineRenderer lr;
    }


    public void Configure(float radius, float duration,
                          bool? enableMist = null,
                          bool? enableWisps = null,
                          bool? enableLightning = null,
                          bool? enableStasisStorm = null)
    {
        this.radius = radius;
        this.duration = duration;

        // Toggle overrides 
        if (enableMist.HasValue) this.enableMist = enableMist.Value;
        if (enableWisps.HasValue) this.enableWisps = enableWisps.Value;
        if (enableLightning.HasValue) this.enableLightning = enableLightning.Value;
        if (enableStasisStorm.HasValue) this.enableStasisStorm = enableStasisStorm.Value;

        // Lift forward toward the camera.
        var rootPos = transform.position;
        rootPos.z += fogZOffset;
        transform.position = rootPos;

        BuildSharedMaterial();

        if (this.enableMist) BuildMist();
        if (this.enableStasisStorm) BuildStasisThreads();
        // Wisps & lightning are built on demand (spawn at intervals).

        nextLightningTime = Time.time + Random.Range(lightningInterval * 0.5f, lightningInterval * 1.5f);
    }


    private void BuildSharedMaterial()
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");

        // One additive vertex-color material
        sharedMaterial = new Material(sh);
        sharedMaterial.mainTexture = Texture2D.whiteTexture;
        sharedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        sharedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        sharedMaterial.SetInt("_ZWrite", 0);

        // lineMaterial is intentionally the same reference as sharedMaterial.
        // Kept as a separate variable name so the call sites stay readable
        // (lr.material = lineMaterial reads better than lr.material = sharedMaterial)
        // and so future-you can split them again safely if you ever need to.
        lineMaterial = sharedMaterial;
    }

    private void BuildMist()
    {
        mistPuffs = new Puff[mistPuffCount];
        for (int i = 0; i < mistPuffCount; i++)
        {
            var go = new GameObject("MistPuff_" + i);
            go.transform.SetParent(transform, false);

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = radius * Mathf.Sqrt(Random.Range(0f, 1f)) * 0.7f;
            go.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = BuildBlobMesh();

            var mr = go.AddComponent<MeshRenderer>();
            mr.material = sharedMaterial;
            mr.sortingLayerName = "Default";
            mr.sortingOrder = fogSortingOrder;

            float scale = radius * Random.Range(mistScaleRange.x, mistScaleRange.y);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            mistPuffs[i] = new Puff
            {
                tr = go.transform,
                filter = mf,
                orbitCenter = go.transform.localPosition,
                orbitRadius = Random.Range(0.05f, 0.20f) * radius,
                orbitPhase = Random.Range(0f, Mathf.PI * 2f),
                baseScale = scale,
                pulsePhase = Random.Range(0f, Mathf.PI * 2f),
                bobPhaseA = Random.Range(0f, Mathf.PI * 2f),
                bobPhaseB = Random.Range(0f, Mathf.PI * 2f),
                bobRateA = mistBobSpeed * Random.Range(0.8f, 1.3f),
                bobRateB = mistBobSpeed * Random.Range(0.35f, 0.65f),
                bobAmpScale = Random.Range(0.7f, 1.3f),
            };
        }
    }

    private void BuildStasisThreads()
    {
        stasisThreads = new StasisThread[stasisThreadCount];
        for (int i = 0; i < stasisThreadCount; i++)
        {
            var go = new GameObject("StasisThread_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            // Start disabled — RerollStasisThreads below will set positions
            // and re-enable. Prevents a one-frame degenerate-line flash.
            lr.enabled = false;
            lr.useWorldSpace = false;
            lr.material = lineMaterial;
            lr.startWidth = stasisWidth;
            lr.endWidth = stasisWidth * 0.5f;
            lr.positionCount = stasisSegments;
            lr.sortingLayerName = "Default";
            lr.sortingOrder = fogSortingOrder + 1;
            lr.numCornerVertices = 1;
            // Pre-clear color so even an accidentally-enabled frame is invisible.
            Color clear = stasisColor; clear.a = 0f;
            lr.startColor = clear;
            lr.endColor = clear;

            stasisThreads[i] = new StasisThread { go = go, lr = lr };
        }

        // Roll initial paths so threads aren't all at origin on frame 1,
        // then enable. After this point the threads are safe.
        RerollStasisThreads(fade: 1f);
        for (int i = 0; i < stasisThreads.Length; i++)
        {
            if (stasisThreads[i].lr != null) stasisThreads[i].lr.enabled = true;
        }
    }


    private void Update()
    {
        elapsed += Time.deltaTime;
        float lifeFrac = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
        float fade = ComputeFade(lifeFrac);

        if (enableMist) UpdateMist(fade);
        if (enableWisps) UpdateWisps(fade);
        if (enableLightning) UpdateLightning(fade);
        if (enableStasisStorm) UpdateStasisStorm(fade);
    }

    private float ComputeFade(float lifeFrac)
    {
        float fade = 1f;
        if (fadeInFraction > 0f && lifeFrac < fadeInFraction)
            fade = lifeFrac / fadeInFraction;
        else if (fadeOutFraction > 0f && lifeFrac > 1f - fadeOutFraction)
            fade = (1f - lifeFrac) / fadeOutFraction;
        return Mathf.Clamp01(fade);
    }


    private void UpdateMist(float fade)
    {
        if (mistPuffs == null) return;

        for (int i = 0; i < mistPuffs.Length; i++)
        {
            var p = mistPuffs[i];
            if (p.tr == null) continue;

            float t = Time.time * mistDrift + p.orbitPhase;
            float bob = (Mathf.Sin(Time.time * p.bobRateA + p.bobPhaseA) * 0.7f
                       + Mathf.Sin(Time.time * p.bobRateB + p.bobPhaseB) * 0.3f)
                      * mistBobAmplitude * p.bobAmpScale;

            Vector3 pos = new Vector3(
                p.orbitCenter.x + Mathf.Cos(t) * p.orbitRadius,
                p.orbitCenter.y + Mathf.Sin(t * 0.9f) * p.orbitRadius + bob,
                0f);
            p.tr.localPosition = pos;

            float pulse = (Mathf.Sin(Time.time * mistPulseSpeed + p.pulsePhase) + 1f) * 0.5f;
            float scale = p.baseScale * (1f + (pulse - 0.5f) * 2f * mistPulseAmplitude);
            p.tr.localScale = new Vector3(scale, scale, 1f);

            if (p.filter != null && p.filter.mesh != null)
            {
                var mesh = p.filter.mesh;
                var cols = mesh.colors;
                if (cols != null && cols.Length == 9)
                {
                    Color inner = mistColor;
                    inner.a = mistColor.a * fade * Mathf.Lerp(0.85f, 1.1f, pulse);

                    Color mid = Color.Lerp(mistColor, outerColor, 0.5f);
                    mid.a = mid.a * fade;

                    cols[4] = inner;
                    cols[1] = cols[3] = cols[5] = cols[7] = mid;
                    cols[0] = cols[2] = cols[6] = cols[8] = outerColor;
                    mesh.colors = cols;
                }
            }
        }
    }


    private void UpdateWisps(float fade)
    {
        wispSpawnTimer += Time.deltaTime;
        // Don't spawn new wisps during fade-out — let existing ones drain.
        if (fade > 0.5f && wispSpawnTimer >= wispSpawnInterval)
        {
            wispSpawnTimer = 0f;
            SpawnWisp();
        }

        for (int i = activeWisps.Count - 1; i >= 0; i--)
        {
            var w = activeWisps[i];
            float age = Time.time - w.spawnTime;
            float lifeT = age / wispLifetime;
            if (lifeT >= 1f || w.go == null)
            {
                if (w.go != null) Destroy(w.go);
                activeWisps.RemoveAt(i);
                continue;
            }

            float reach = wispReach * radius * Mathf.SmoothStep(0f, 1f, lifeT);
            float dx0 = Mathf.Cos(w.dirAngle);
            float dy0 = Mathf.Sin(w.dirAngle);
            float perpX = -dy0, perpY = dx0;

            for (int s = 0; s < wispSegments; s++)
            {
                float u = s / (float)(wispSegments - 1);
                float baseX = dx0 * reach * u;
                float baseY = dy0 * reach * u;
                float curlAmt = Mathf.Sin(u * Mathf.PI * 1.5f + w.curlPhase + Time.time * 2f);
                float lateral = curlAmt * wispCurl * u * radius * 0.25f;
                Vector3 p = new Vector3(
                    w.origin.x + baseX + perpX * lateral,
                    w.origin.y + baseY + perpY * lateral,
                    0f);
                w.lr.SetPosition(s, p);
            }

            // Fade in over first 25%, hold, fade out over last 35%.
            float alphaT;
            if (lifeT < 0.25f) alphaT = lifeT / 0.25f;
            else if (lifeT > 0.65f) alphaT = (1f - lifeT) / 0.35f;
            else alphaT = 1f;
            alphaT = Mathf.Clamp01(alphaT) * fade;

            Color sc = wispColor; sc.a *= alphaT;
            Color ec = wispColor; ec.a = 0f;
            w.lr.startColor = sc;
            w.lr.endColor = ec;
        }
    }

    private void SpawnWisp()
    {
        var go = new GameObject("Wisp");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        var lr = go.AddComponent<LineRenderer>();
        // Disable until we've set positions on the same frame. A brand-new
        // LineRenderer with no positions defaults to (0,0,0)→(0,0,0) and,
        // textured with white, renders as a giant white quad for one frame
        // before the first Update() corrects it. Disable-then-enable on
        // same frame is invisible to the user.
        lr.enabled = false;
        lr.useWorldSpace = false;
        lr.material = lineMaterial;
        lr.startWidth = wispWidth;
        lr.endWidth = 0f;
        lr.positionCount = wispSegments;
        lr.sortingLayerName = "Default";
        lr.sortingOrder = fogSortingOrder + 1;
        lr.numCornerVertices = 1;

        float oa = Random.Range(0f, Mathf.PI * 2f);
        float orad = radius * Mathf.Sqrt(Random.Range(0f, 1f)) * 0.5f;
        Vector2 origin = new Vector2(Mathf.Cos(oa) * orad, Mathf.Sin(oa) * orad);

        // Pre-fill positions so the first rendered frame isn't a degenerate
        // line. All segments collapsed at origin = invisible (zero-length).
        for (int s = 0; s < wispSegments; s++)
            lr.SetPosition(s, new Vector3(origin.x, origin.y, 0f));

        // Start fully transparent so even a degenerate frame is invisible.
        Color clear = wispColor; clear.a = 0f;
        lr.startColor = clear;
        lr.endColor = clear;

        lr.enabled = true;

        activeWisps.Add(new Wisp
        {
            go = go,
            lr = lr,
            origin = origin,
            dirAngle = Random.Range(0f, Mathf.PI * 2f),
            curlPhase = Random.Range(0f, Mathf.PI * 2f),
            spawnTime = Time.time,
        });
    }


    private void UpdateLightning(float fade)
    {
        // Retire a finished flash.
        if (currentLightning.go != null && Time.time - currentLightning.startTime >= lightningDuration)
        {
            Destroy(currentLightning.go);
            currentLightning = default;
        }

        // Schedule next — never during the fade-out tail.
        if (fade > 0.5f && currentLightning.go == null && Time.time >= nextLightningTime)
        {
            SpawnLightning();
            nextLightningTime = Time.time + lightningInterval * Random.Range(0.6f, 1.4f);
        }

        // Animate brightness: quick attack, slower decay.
        if (currentLightning.go != null && currentLightning.lr != null)
        {
            float t = (Time.time - currentLightning.startTime) / Mathf.Max(0.0001f, lightningDuration);
            float a = (t < 0.25f) ? (t / 0.25f) : (1f - (t - 0.25f) / 0.75f);
            a = Mathf.Clamp01(a) * fade;
            Color sc = lightningColor; sc.a = a;
            Color ec = lightningColor; ec.a = a * 0.3f;
            currentLightning.lr.startColor = sc;
            currentLightning.lr.endColor = ec;
        }
    }

    private void SpawnLightning()
    {
        var go = new GameObject("Lightning");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        var lr = go.AddComponent<LineRenderer>();
        // Same one-frame-flash protection as wisps.
        lr.enabled = false;
        lr.useWorldSpace = false;
        lr.material = lineMaterial;
        lr.startWidth = lightningWidth;
        lr.endWidth = lightningWidth * 0.5f;
        lr.positionCount = lightningSegments;
        lr.sortingLayerName = "Default";
        lr.sortingOrder = fogSortingOrder + 3; // top of the stack
        lr.numCornerVertices = 2;

        // Two opposite-ish points on the cloud body.
        float a1 = Random.Range(0f, Mathf.PI * 2f);
        float a2 = a1 + Mathf.PI + Random.Range(-0.6f, 0.6f);
        Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius * Random.Range(0.5f, 0.95f);
        Vector2 p2 = new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * radius * Random.Range(0.5f, 0.95f);

        for (int s = 0; s < lightningSegments; s++)
        {
            float u = s / (float)(lightningSegments - 1);
            Vector2 lerp = Vector2.Lerp(p1, p2, u);
            if (s > 0 && s < lightningSegments - 1)
            {
                lerp.x += Random.Range(-0.2f, 0.2f) * radius * 0.3f;
                lerp.y += Random.Range(-0.2f, 0.2f) * radius * 0.3f;
            }
            lr.SetPosition(s, new Vector3(lerp.x, lerp.y, 0f));
        }

        // Start clear so even the enable-frame can't flash.
        Color clear = lightningColor; clear.a = 0f;
        lr.startColor = clear;
        lr.endColor = clear;
        lr.enabled = true;

        currentLightning = new LightningFlash { go = go, lr = lr, startTime = Time.time };
    }


    private void UpdateStasisStorm(float fade)
    {
        if (stasisThreads == null) return;

        stasisRegenerateTimer += Time.deltaTime;
        if (stasisRegenerateTimer >= stasisRegenerateInterval)
        {
            stasisRegenerateTimer = 0f;
            RerollStasisThreads(fade);
        }
        else
        {
            // Even between rerolls, refresh per-thread alpha so threads fade
            // smoothly with the overall fog rather than snapping at reroll.
            for (int i = 0; i < stasisThreads.Length; i++)
            {
                var t = stasisThreads[i];
                if (t.lr == null) continue;
                // Each thread independently jitters its alpha for that
                // "flickering" electrical look. Sub-1 multiplier keeps the
                // base subtle; jitter is small.
                float jitter = 0.75f + Random.Range(-0.15f, 0.15f);
                Color sc = stasisColor; sc.a = stasisColor.a * fade * jitter;
                Color ec = stasisColor; ec.a = sc.a * 0.4f;
                t.lr.startColor = sc;
                t.lr.endColor = ec;
            }
        }
    }

    private void RerollStasisThreads(float fade)
    {
        for (int i = 0; i < stasisThreads.Length; i++)
        {
            var t = stasisThreads[i];
            if (t.lr == null) continue;

            // Pick two random points inside the cloud body. Threads are
            // short (length between 30% and stasisThreadMaxLength of radius)
            // and biased toward being interior so they thread through the
            // mist rather than spanning it edge-to-edge — that's lightning's
            // job.
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float len = radius * Random.Range(0.3f, stasisThreadMaxLength);
            // Start somewhere inside the cloud.
            float startA = Random.Range(0f, Mathf.PI * 2f);
            float startR = radius * Mathf.Sqrt(Random.Range(0f, 1f)) * 0.6f;
            Vector2 start = new Vector2(Mathf.Cos(startA) * startR, Mathf.Sin(startA) * startR);
            Vector2 end = start + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len;

            for (int s = 0; s < stasisSegments; s++)
            {
                float u = s / (float)(stasisSegments - 1);
                Vector2 lerp = Vector2.Lerp(start, end, u);
                if (s > 0 && s < stasisSegments - 1)
                {
                    // Per-segment jag, fresh each reroll — that's the
                    // "crackling" motion. Endpoints stay clean.
                    lerp.x += Random.Range(-stasisJag, stasisJag);
                    lerp.y += Random.Range(-stasisJag, stasisJag);
                }
                t.lr.SetPosition(s, new Vector3(lerp.x, lerp.y, 0f));
            }

            // Set fresh alpha with per-thread jitter.
            float jitter = 0.75f + Random.Range(-0.15f, 0.15f);
            Color sc = stasisColor; sc.a = stasisColor.a * fade * jitter;
            Color ec = stasisColor; ec.a = sc.a * 0.4f;
            t.lr.startColor = sc;
            t.lr.endColor = ec;
        }
    }

    //  Mesh helper 

    private static Mesh BuildBlobMesh()
    {
        Mesh m = new Mesh { name = "BufferFogBlob" };
        Vector3[] v = new Vector3[9];
        Color[] c = new Color[9];

        v[0] = new Vector3(-0.5f, -0.5f, 0f);
        v[1] = new Vector3(0.0f, -0.5f, 0f);
        v[2] = new Vector3(0.5f, -0.5f, 0f);
        v[3] = new Vector3(-0.5f, 0.0f, 0f);
        v[4] = new Vector3(0.0f, 0.0f, 0f);
        v[5] = new Vector3(0.5f, 0.0f, 0f);
        v[6] = new Vector3(-0.5f, 0.5f, 0f);
        v[7] = new Vector3(0.0f, 0.5f, 0f);
        v[8] = new Vector3(0.5f, 0.5f, 0f);

        // ALL vertices start fully transparent. UpdatePuffs / UpdateSparks /
        // UpdateFlecks overwrite these with real colors on first frame they
        // run; until then, nothing renders.
        Color clear = new Color(1, 1, 1, 0f);
        for (int i = 0; i < 9; i++) c[i] = clear;

        int[] tris =
        {
            0,3,1,  3,4,1,
            1,4,2,  4,5,2,
            3,6,4,  6,7,4,
            4,7,5,  7,8,5,
        };

        m.vertices = v;
        m.colors = c;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    private void OnDestroy()
    {
        if (sharedMaterial != null) Destroy(sharedMaterial);
        // lineMaterial is the same reference as sharedMaterial — don't double-destroy.
        lineMaterial = null;
    }
}

