using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

// FIERY EYES
// A burning evil eye

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(1000)] // after animation, lean and Y-sort have written this frame's state
public class EnemyFieryEyes : MonoBehaviour
{
    [Serializable]
    public class EyeFrame
    {
        public Sprite sprite;

        [Tooltip("Iris centre for this frame, sprite-local units, as drawn (unflipped).")]
        public Vector2 irisPosition;

        [Tooltip("False when the bake found no iris on this frame. The fire fades out while it shows.")]
        public bool eyeVisible = true;
    }

    [Header("Eye Placement (sprite-local units, as DRAWN)")]
    [Tooltip("Iris centre used for frames that were not baked. Default is measured from the " +
             "Pitcher art (1443×954 px, 200 PPU, centre pivot).")]
    [SerializeField] private Vector2 irisPosition = new Vector2(-0.9275f, -1.5085f);

    [Tooltip("Shift from the iris centre to the centre of the burning eye.")]
    [SerializeField] private Vector2 eyeCenterOffset = new Vector2(0.04f, 0f);

    [SerializeField] private Vector2 eyeSize = new Vector2(0.33f, 0.35f);

    [SerializeField, Range(-45f, 45f)] private float eyeTilt = 0f;

    [Header("Per-frame Tracking (filled by the Bake action)")]
    [SerializeField] private bool hideOnFramesWithoutEye = true;
    [SerializeField] private List<EyeFrame> bakedFrames = new List<EyeFrame>();

    [Header("Eye Look")]
    [SerializeField] private Color coreColor = new Color(1f, 1f, 0.9f);
    [SerializeField] private Color irisColor = new Color(1f, 0.75f, 0.2f);
    [SerializeField] private Color rimColor = new Color(1f, 0.25f, 0f);
    [SerializeField] private Color pupilColor = new Color(0.15f, 0f, 0f);
    [SerializeField, Range(0.03f, 0.4f)] private float pupilWidth = 0.11f;

    [Tooltip("Height of the angry brow cut across the top of the eye.")]
    [SerializeField, Range(-1f, 1f)] private float browHeight = 0.28f;

    [Tooltip("Slope of the brow cut. Positive suits art facing LEFT (the Pitcher); use negative for right-facing art.")]
    [SerializeField, Range(-1.5f, 1.5f)] private float browSlope = 0.45f;

    [SerializeField] private Color haloColor = new Color(1f, 0.3f, 0f, 0.5f);
    [SerializeField, Min(0.1f)] private float haloSize = 1.1f;

    [Header("Fire")]
    [Tooltip("Colour of the flames from faint edge (left) to white-hot core (right).")]
    [SerializeField] private Gradient fireGradient = DefaultFireGradient();

    [Tooltip("Flipbook playback speed.")]
    [SerializeField, Min(1f)] private float fireFps = 20f;

    [Tooltip("Base of the front flame relative to the eye centre.")]
    [SerializeField] private Vector2 frontFlameOffset = new Vector2(0.01f, -0.02f);
    [Tooltip("Width / height of the front flame.")]
    [SerializeField] private Vector2 frontFlameSize = new Vector2(0.48f, 0.8f);
    [Tooltip("Degrees the front flame leans toward the BACK of the head at rest.")]
    [SerializeField, Range(-45f, 45f)] private float frontFlameLean = 12f;

    [SerializeField] private Vector2 backFlameOffset = new Vector2(0.03f, -0.03f);
    [SerializeField] private Vector2 backFlameSize = new Vector2(0.62f, 1.0f);
    [SerializeField, Range(-45f, 45f)] private float backFlameLean = 22f;
    [Tooltip("Tint of the back flame (darker/redder reads as depth).")]
    [SerializeField] private Color backFlameTint = new Color(1f, 0.7f, 0.55f, 0.85f);

    [Tooltip("How much of the body's walk-lean the flames copy. 0 = always point straight up, 1 = rigid with the sprite.")]
    [SerializeField, Range(0f, 1f)] private float followBodyLean = 0.35f;

    [Tooltip("Degrees of backward sweep per sprite-local unit/second of movement.")]
    [SerializeField, Min(0f)] private float sweepPerSpeed = 4f;
    [SerializeField, Range(0f, 80f)] private float maxSweep = 35f;

    [Tooltip("Extra flame height per sprite-local unit/second of movement.")]
    [SerializeField, Min(0f)] private float stretchPerSpeed = 0.03f;
    [SerializeField, Range(0f, 1f)] private float maxStretch = 0.3f;

    [Header("Flicker")]
    [SerializeField, Min(0f)] private float flickerSpeed = 8f;
    [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.3f;

    [Header("Embers")]
    [Tooltip("Pool size. 0 disables embers.")]
    [SerializeField, Range(0, 24)] private int emberCount = 8;
    [SerializeField, Min(0.05f)] private float emberLifetime = 0.6f;
    [SerializeField] private float emberRiseSpeed = 1.6f;
    [SerializeField] private float emberSpread = 0.5f;
    [SerializeField] private float emberSize = 0.06f;
    [SerializeField] private Color emberHotColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color emberCoolColor = new Color(1f, 0.15f, 0f, 0f);

    [Header("Rendering")]
    [Tooltip("Optional. If empty, an unlit sprite shader is looked up at runtime so the fire " +
             "still glows in dark biomes lit by 2D lights. For builds, assign a material " +
             "(e.g. Sprite-Unlit-Default): Shader.Find only finds shaders included in the build.")]
    [SerializeField] private Material fxMaterial;

    [Tooltip("When a NightOverlay (night mode, Night / Corruption / Pitch Black biomes) is active, " +
             "draw the fire ABOVE the darkness so the eyes glow through it. Otherwise the darkness " +
             "(sorting order 6000) covers the eyes exactly like it covers the body.\n\n" +
             "Trade-off while it's dark: the fire is no longer Y-sorted, so it also draws over " +
             "trees, fog and enemies standing in front of this one.")]
    [SerializeField] private bool glowThroughDarkness = true;

    [Header("Death")]
    [SerializeField, Min(0.01f)] private float deathFadeTime = 0.25f;

    [Header("Bake Settings")]
    [Tooltip("Colour of the iris in the SOURCE art. Default matches the Pitcher's dark purple iris.")]
    [SerializeField] private Color32 bakeIrisColor = new Color32(55, 40, 65, 255);
    [Tooltip("RGB distance (0-255 scale) still counted as iris.")]
    [SerializeField, Range(1f, 128f)] private float bakeColorTolerance = 32f;
    [Tooltip("How far (sprite-local units) from the previous frame's eye to search. The nose is ~0.8 away.")]
    [SerializeField, Min(0.05f)] private float bakeSearchRadius = 0.5f;
    [Tooltip("Minimum matching pixels, as a fraction of the sprite's area, to count as 'eye found'.")]
    [SerializeField, Range(0.00001f, 0.01f)] private float bakeMinPixelFraction = 0.0003f;

    // ── Runtime 

    private struct Ember
    {
        public SpriteRenderer sr;
        public Vector3 pos, vel;
        public float age, life, size, seed;
        public bool alive;
    }

    private SpriteRenderer body;
    private EnemyAnimationController anim;
    private EnemyStats stats;

    private Transform root;
    private SortingGroup rootGroup;
    private SpriteRenderer haloSR, backSR, frontSR, eyeSR;
    private Ember[] embers;
    private float emberAccumulator;
    private Sprite[] fireFrames;

    private readonly Dictionary<Sprite, int> frameLookup = new Dictionary<Sprite, int>();
    private Sprite lastSprite;
    private bool frameResolved;
    private Vector2 currentIris;
    private bool currentFrameVisible = true;

    private float frameFade = 1f, deathFade = 1f, flare;
    private float noiseSeed;
    private int framePhase;
    private bool lookDirty;
    private bool bakeFoundAnyEye;
    private float lastMaster;

    private Vector3 lastEyeWorld, smoothedVel;
    private bool hasLastEye;
    private float frontAngle, backAngle;
    private bool anglesInitialized;

    /// Brief brighter burst: taller flames, bigger halo, a puff of embers.
    /// PitcherController calls this when the dart leaves.
    public void Flare(float strength = 1f)
    {
        flare = Mathf.Max(flare, strength);
        if (embers == null || !hasLastEye) return;
        float ws = Mathf.Max(1e-5f, transform.TransformVector(Vector3.up).magnitude);
        for (int i = 0; i < 3; i++) EmitEmber(lastEyeWorld, ws);
    }

    /// Optional: call during a loading screen so the one-time flame generation
    /// (a few tens of ms) doesn't land on the first Pitcher spawn.
    public static void Prewarm() => GetFireFrames(DefaultFireGradient());

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        anim = GetComponent<EnemyAnimationController>();
        stats = GetComponent<EnemyStats>();
        noiseSeed = UnityEngine.Random.value * 100f;
        framePhase = UnityEngine.Random.Range(0, FireFrameCount);
        currentIris = irisPosition;

        BuildLookup();
        BuildFx();
    }

    private void OnEnable()
    {
        if (root != null) root.gameObject.SetActive(true);
        hasLastEye = false;
        anglesInitialized = false;
    }

    private void OnDisable()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        // The FX root is not our child, so it must be cleaned up explicitly.
        if (root != null) Destroy(root.gameObject);
    }

    private void OnValidate()
    {
        if (Application.isPlaying && root != null) lookDirty = true;
    }

    private void BuildLookup()
    {
        frameLookup.Clear();
        bakeFoundAnyEye = false;
        for (int i = 0; i < bakedFrames.Count; i++)
        {
            var f = bakedFrames[i];
            if (f == null || f.sprite == null) continue;
            if (f.eyeVisible) bakeFoundAnyEye = true;
            if (!frameLookup.ContainsKey(f.sprite)) frameLookup.Add(f.sprite, i);
        }
        frameResolved = false;

        // A bake that found the eye on NO frame is a failed bake, not an eyeless enemy.
        // Ignore it (fall back to Iris Position) instead of hiding the fire forever.
        if (bakedFrames.Count > 0 && !bakeFoundAnyEye)
            Debug.LogWarning($"[FieryEyes] {name}: the baked data has no frame with a visible eye — " +
                             "ignoring it. Move Iris Position onto the eye and re-bake.", this);
    }

    private void BuildFx()
    {
        root = new GameObject($"FieryEyes ({name})").transform;
        root.gameObject.layer = gameObject.layer; // same camera culling mask as the body

        // One sorting unit: the FX sort together just above the body, and the layers
        // sort among themselves inside the group. Keeps them from poking through
        // Y-sorted neighbours standing just in front.
        rootGroup = root.gameObject.AddComponent<SortingGroup>();

        fireFrames = GetFireFrames(fireGradient);

        haloSR = CreateLayer("Halo", GetSoftDotSprite(), 0);
        backSR = CreateLayer("BackFlame", fireFrames[0], 1);
        frontSR = CreateLayer("FrontFlame", fireFrames[0], 2);
        eyeSR = CreateLayer("Eye", GetEyeSprite(), 3);

        embers = new Ember[emberCount];
        for (int i = 0; i < embers.Length; i++)
        {
            embers[i].sr = CreateLayer("Ember", GetSoftDotSprite(), 4);
            embers[i].sr.enabled = false;
        }
    }

    private SpriteRenderer CreateLayer(string layerName, Sprite sprite, int order)
    {
        var go = new GameObject(layerName);
        go.layer = gameObject.layer;
        go.transform.SetParent(root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        var mat = ResolveMaterial();
        if (mat != null) sr.sharedMaterial = mat;
        return sr;
    }

    // Art-space (sprite-local, unflipped) point → world, honouring flipX and every
    // transform (position, lean, scale — including a negative-scale flip).
    private Vector3 ArtToWorld(Vector2 art)
    {
        if (body.flipX) art.x = -art.x;
        return transform.TransformPoint(art);
    }

    private void LateUpdate()
    {
        if (root == null || body == null) return;

        if (lookDirty)
        {
            lookDirty = false;
            eyeSR.sprite = GetEyeSprite();
            fireFrames = GetFireFrames(fireGradient);
            BuildLookup();
        }

        float dt = Time.deltaTime;

        // 1. Which animation frame is showing → where the eye is on it.
        Sprite sprite = body.sprite;
        if (!frameResolved || sprite != lastSprite)
        {
            frameResolved = true;
            lastSprite = sprite;
            ResolveFrame(sprite);
        }

        // 2. Visibility.
        bool dying = (anim != null && anim.IsDying) || (stats != null && stats.IsDead());
        deathFade = dying ? Mathf.MoveTowards(deathFade, 0f, dt / deathFadeTime) : 1f;

        float frameTarget = (currentFrameVisible || !hideOnFramesWithoutEye) ? 1f : 0f;
        frameFade = Mathf.MoveTowards(frameFade, frameTarget, dt / 0.06f);

        // Alpha follows the body (fades / invisibility); its RGB tint is ignored so the
        // freeze (cyan) / confusion / hit-flash tints don't recolour the fire.
        bool bodyShown = body.enabled && sprite != null;
        float master = bodyShown ? Mathf.Clamp01(body.color.a) * frameFade * deathFade : 0f;
        bool show = master > 0.001f;
        lastMaster = master;

        flare = Mathf.MoveTowards(flare, 0f, dt / 0.35f);

        // 3. Body frame of reference.
        Vector3 right = transform.TransformVector(Vector3.right);
        Vector3 up = transform.TransformVector(Vector3.up);
        float worldScale = Mathf.Max(1e-5f, up.magnitude);
        float scaleX = right.magnitude;                       // follows a smooth-flip squash
        float bodyAngle = Mathf.Atan2(-up.x, up.y) * Mathf.Rad2Deg;
        bool mirrored = ((right.x * up.y - right.y * up.x) < 0f) ^ body.flipX;
        float s = mirrored ? -1f : 1f;

        Vector3 eyeWorld = ArtToWorld(currentIris + eyeCenterOffset);
        root.position = eyeWorld;

        // 4. Sorting: one step above the body's (Y-sorted) order — or above the night
        //    darkness when it's active, so the eyes glow in the dark.
        int wantOrder = body.sortingOrder + 1;
        if (glowThroughDarkness && TryGetDarknessOrder(out int darknessOrder))
            wantOrder = Mathf.Max(wantOrder, darknessOrder + 1);
        if (rootGroup.sortingLayerID != body.sortingLayerID) rootGroup.sortingLayerID = body.sortingLayerID;
        if (rootGroup.sortingOrder != wantOrder) rootGroup.sortingOrder = wantOrder;

        // 5. Movement → sweep & stretch. Measured on the eye itself, so walking,
        //    knockback and grapple pulls all drag the flames the same way.
        if (hasLastEye && dt > 0f)
        {
            Vector3 v = (eyeWorld - lastEyeWorld) / dt;
            if (v.sqrMagnitude > 2500f * worldScale * worldScale) v = Vector3.zero; // teleport / respawn
            smoothedVel = Vector3.Lerp(smoothedVel, v, 1f - Mathf.Exp(-8f * dt));
        }
        lastEyeWorld = eyeWorld;
        hasLastEye = true;

        float localSpeedX = smoothedVel.x / worldScale;
        float localSpeed = smoothedVel.magnitude / worldScale;
        float sweep = Mathf.Clamp(localSpeedX * sweepPerSpeed, -maxSweep, maxSweep); // moving left → tips right
        float stretch = 1f + Mathf.Min(localSpeed * stretchPerSpeed, maxStretch);

        // 6. Flicker noise.
        float t = Time.time * flickerSpeed;
        float n1 = Mathf.PerlinNoise(t, noiseSeed);
        float n2 = Mathf.PerlinNoise(noiseSeed, t * 0.7f);
        float n3 = Mathf.PerlinNoise(t * 0.5f, noiseSeed + 31.7f);
        float flick = 1f + (n1 - 0.5f) * 2f * flickerAmount;

        // 7. Eye.
        eyeSR.transform.SetPositionAndRotation(eyeWorld, Quaternion.Euler(0f, 0f, bodyAngle + s * eyeTilt));
        eyeSR.transform.localScale = new Vector3(eyeSize.x * scaleX * s, eyeSize.y * worldScale, 1f);
        float bright = Mathf.Lerp(0.88f, 1f, n1);
        eyeSR.color = new Color(bright, bright, bright, master);

        // 8. Halo.
        float halo = haloSize * worldScale * (1f + (n2 - 0.5f) * flickerAmount * 0.5f + flare * 0.5f);
        haloSR.transform.SetPositionAndRotation(eyeWorld, Quaternion.identity);
        haloSR.transform.localScale = new Vector3(halo, halo, 1f);
        haloSR.color = new Color(haloColor.r, haloColor.g, haloColor.b,
                                 Mathf.Clamp01(haloColor.a * flick * (1f + flare)) * master);

        // 9. Flames.
        bool artFacesLeft = stats != null && stats.enemyData != null && stats.enemyData.spriteFacesLeft;
        float backDir = artFacesLeft ? 1f : -1f;                 // art-space +x or -x is "back of the head"
        float baseAngle = bodyAngle * followBodyLean + sweep;
        float frontTarget = baseAngle - s * backDir * frontFlameLean + (n2 - 0.5f) * 10f;
        float backTarget = baseAngle - s * backDir * backFlameLean + (n3 - 0.5f) * 14f;

        if (!anglesInitialized)
        {
            frontAngle = frontTarget;
            backAngle = backTarget;
            anglesInitialized = true;
        }
        else
        {
            // Smoothed so a flip (lean direction swaps) swings the fire over instead of popping.
            float k = 1f - Mathf.Exp(-10f * dt);
            frontAngle = Mathf.LerpAngle(frontAngle, frontTarget, k);
            backAngle = Mathf.LerpAngle(backAngle, backTarget, k);
        }

        float grow = Mathf.Lerp(0.3f, 1f, deathFade) * (1f + flare * 0.4f) * stretch;
        int n = fireFrames.Length;
        int frontIdx = PositiveMod(Mathf.FloorToInt(Time.time * fireFps) + framePhase, n);
        int backIdx = PositiveMod(Mathf.FloorToInt(Time.time * fireFps * 0.85f) + framePhase + n / 2, n);

        PlaceFlame(frontSR, fireFrames[frontIdx], frontFlameOffset, frontFlameSize, frontAngle,
                   worldScale, grow * (1f + (n1 - 0.5f) * 0.16f), new Color(1f, 1f, 1f, master));
        PlaceFlame(backSR, fireFrames[backIdx], backFlameOffset, backFlameSize, backAngle,
                   worldScale, grow * (1f + (n3 - 0.5f) * 0.2f),
                   new Color(backFlameTint.r, backFlameTint.g, backFlameTint.b, backFlameTint.a * master));

        eyeSR.enabled = show;
        haloSR.enabled = show;
        frontSR.enabled = show;
        backSR.enabled = show;

        UpdateEmbers(dt, master, eyeWorld, worldScale);
    }

    private void PlaceFlame(SpriteRenderer sr, Sprite frame, Vector2 artOffset, Vector2 size,
                            float angle, float worldScale, float heightMul, Color color)
    {
        sr.sprite = frame;
        Vector3 basePos = ArtToWorld(currentIris + eyeCenterOffset + artOffset);
        sr.transform.SetPositionAndRotation(basePos, Quaternion.Euler(0f, 0f, angle));
        sr.transform.localScale = new Vector3(size.x / FireSpriteWidthUnits * worldScale,
                                              size.y * worldScale * heightMul, 1f);
        sr.color = color;
    }

    private static int PositiveMod(int a, int m) => ((a % m) + m) % m;

    private void ResolveFrame(Sprite sprite)
    {
        if (bakeFoundAnyEye && sprite != null && frameLookup.TryGetValue(sprite, out int idx))
        {
            var f = bakedFrames[idx];
            currentIris = f.irisPosition;
            currentFrameVisible = f.eyeVisible;
        }
        else
        {
            currentIris = irisPosition;
            currentFrameVisible = true;
        }
    }

    // ── Night darkness 
    // BiomeManager adds a NightOverlay at sortingOrder 6000 for night mode and the
    // Night / Corruption / Pitch Black biomes. Looked up at most twice a second for
    // ALL enemies together, so a wave of Pitchers doesn't each scan the scene.

    private static NightOverlay s_night;
    private static float s_nextNightLookup = float.NegativeInfinity;

    private static bool TryGetDarknessOrder(out int order)
    {
        if (Time.unscaledTime >= s_nextNightLookup)
        {
            s_nextNightLookup = Time.unscaledTime + 0.5f;
            s_night = FindFirstObjectByType<NightOverlay>();
        }

        if (s_night != null && s_night.isActiveAndEnabled)
        {
            order = s_night.sortingOrder;
            return true;
        }
        order = 0;
        return false;
    }

    // ── Diagnostics 

    [ContextMenu("Log Fire Eyes Diagnostics (Play Mode)")]
    private void LogDiagnostics()
    {
        if (!Application.isPlaying || root == null)
        {
            Debug.Log($"[FieryEyes] {name}: enter Play Mode and run this on a SPAWNED Pitcher " +
                      "(select it in the Hierarchy). Baked frames: " + bakedFrames.Count, this);
            return;
        }

        var cam = Camera.main;
        bool culledByCamera = cam != null && (cam.cullingMask & (1 << root.gameObject.layer)) == 0;
        bool darkness = TryGetDarknessOrder(out int darkOrder);
        var mat = frontSR.sharedMaterial;

        Debug.Log(
            $"[FieryEyes] {name}\n" +
            $"  visible alpha (master) = {lastMaster:F2}  → body.enabled={body.enabled}, body alpha={body.color.a:F2}, " +
            $"frameFade={frameFade:F2} (frame has eye: {currentFrameVisible}, bake usable: {bakeFoundAnyEye}, " +
            $"baked frames: {bakedFrames.Count}), deathFade={deathFade:F2}\n" +
            $"  FX root active={root.gameObject.activeInHierarchy}, eye renderer enabled={eyeSR.enabled}\n" +
            $"  sorting: body layer='{body.sortingLayerName}' order={body.sortingOrder} | fire layer='{rootGroup.sortingLayerName}' order={rootGroup.sortingOrder}" +
            (darkness ? $" | night overlay order={darkOrder}" : " | no night overlay") + "\n" +
            $"  eye world pos={eyeSR.transform.position} (body at {transform.position}), eye scale={eyeSR.transform.lossyScale}\n" +
            $"  material='{(mat != null ? mat.name : "null")}' shader='{(mat != null && mat.shader != null ? mat.shader.name : "null")}'\n" +
            $"  object layer={LayerMask.LayerToName(root.gameObject.layer)}" +
            (culledByCamera ? "  ← Camera.main does NOT render this layer!" : ""),
            this);
    }

    // ── Embers 

    private void UpdateEmbers(float dt, float master, Vector3 eyeWorld, float worldScale)
    {
        if (embers == null || embers.Length == 0) return;

        if (master > 0.001f && dt > 0f)
        {
            emberAccumulator += dt * embers.Length / emberLifetime;
            while (emberAccumulator >= 1f)
            {
                emberAccumulator -= 1f;
                EmitEmber(eyeWorld, worldScale);
            }
        }
        else
        {
            emberAccumulator = 0f;
        }

        for (int i = 0; i < embers.Length; i++)
        {
            ref Ember e = ref embers[i];
            if (!e.alive) continue;

            e.age += dt;
            float u = e.age / e.life;
            if (u >= 1f)
            {
                e.alive = false;
                e.sr.enabled = false;
                continue;
            }

            // World space: embers are left behind as the body moves, so they trail.
            e.vel.x *= Mathf.Exp(-2f * dt);
            e.pos += e.vel * dt;

            float wobble = Mathf.Sin(e.age * 11f + e.seed) * 0.04f * worldScale;
            float size = e.size * (1f - 0.6f * u) * worldScale;
            e.sr.transform.SetPositionAndRotation(e.pos + new Vector3(wobble, 0f, 0f), Quaternion.identity);
            e.sr.transform.localScale = new Vector3(size, size, 1f);

            Color c = Color.Lerp(emberHotColor, emberCoolColor, u);
            c.a *= master * Mathf.Clamp01(u * 8f);
            e.sr.color = c;
            e.sr.enabled = master > 0.001f;
        }
    }

    private void EmitEmber(Vector3 eyeWorld, float worldScale)
    {
        if (embers == null) return;
        for (int i = 0; i < embers.Length; i++)
        {
            if (embers[i].alive) continue;

            ref Ember e = ref embers[i];
            e.pos = eyeWorld + new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f),
                                           UnityEngine.Random.Range(0.1f, 0.45f), 0f) * worldScale;
            e.vel = new Vector3(UnityEngine.Random.Range(-1f, 1f) * emberSpread,
                                emberRiseSpeed * UnityEngine.Random.Range(0.7f, 1.3f), 0f) * worldScale;
            e.life = emberLifetime * UnityEngine.Random.Range(0.6f, 1.2f);
            e.size = emberSize * UnityEngine.Random.Range(0.6f, 1.4f);
            e.seed = UnityEngine.Random.value * 10f;
            e.age = 0f;
            e.alive = true;
            e.sr.enabled = true;
            e.sr.color = Color.clear;
            return;
        }
    }

    // ── Materials ────────────────────────────────────────────────────────────

    private static Material s_autoMaterial;
    private static bool s_autoMaterialResolved;

    private Material ResolveMaterial()
    {
        if (fxMaterial != null) return fxMaterial;
        if (!s_autoMaterialResolved)
        {
            s_autoMaterialResolved = true;
            Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null) s_autoMaterial = new Material(sh) { name = "FieryEyes (auto unlit)" };
        }
        return s_autoMaterial;
    }

    // ── Procedural fire flipbook ─────────────────────────────────────────────
    //
    // Each frame is a flame-shaped mask eroded by fBm value noise that scrolls upward.
    // The noise tiles vertically with period FireNoisePeriodY and scrolls exactly one
    // period over the whole loop, so frame N-1 flows seamlessly back into frame 0.

    private const int FireW = 48, FireH = 96, FireFrameCount = 24, FireCols = 6;
    private const int FireNoisePeriodX = 64, FireNoisePeriodY = 3;
    private const float FireCellsX = 4f, FireCellsY = 2f;
    private const float FireSpriteWidthUnits = (float)FireW / FireH; // sprite PPU = FireH → 1 unit tall

    private static readonly Dictionary<int, Sprite[]> s_fireFrames = new Dictionary<int, Sprite[]>();

    private static Gradient DefaultFireGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.45f, 0.02f, 0f), 0f),
                new GradientColorKey(new Color(0.85f, 0.12f, 0f), 0.3f),
                new GradientColorKey(new Color(1f, 0.45f, 0.03f), 0.55f),
                new GradientColorKey(new Color(1f, 0.82f, 0.25f), 0.8f),
                new GradientColorKey(new Color(1f, 1f, 0.88f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    private static int GradientKey(Gradient g)
    {
        unchecked
        {
            int h = 23;
            for (int i = 0; i <= 8; i++) h = h * 31 + g.Evaluate(i / 8f).GetHashCode();
            return h;
        }
    }

    private static Sprite[] GetFireFrames(Gradient gradient)
    {
        if (gradient == null) gradient = DefaultFireGradient();
        int key = GradientKey(gradient);
        if (s_fireFrames.TryGetValue(key, out Sprite[] cached) && cached != null && cached[0] != null)
            return cached;

        // Colour ramp baked into a lookup table so the pixel loop doesn't call Evaluate.
        const int RampSize = 256;
        var ramp = new Color32[RampSize];
        for (int i = 0; i < RampSize; i++) ramp[i] = gradient.Evaluate(i / (RampSize - 1f));

        int rows = Mathf.CeilToInt(FireFrameCount / (float)FireCols);
        int cellW = FireW + 2, cellH = FireH + 2;          // 1 px transparent gutter: no bilinear bleed
        int texW = cellW * FireCols, texH = cellH * rows;

        var px = new Color32[texW * texH];                  // defaults to fully transparent
        for (int f = 0; f < FireFrameCount; f++)
        {
            float t = f / (float)FireFrameCount;
            int ox = (f % FireCols) * cellW + 1;
            int oy = (f / FireCols) * cellH + 1;

            for (int y = 0; y < FireH; y++)
            {
                float v = (y + 0.5f) / FireH;                                   // 0 bottom → 1 top
                float ny = v * FireCellsY - t * FireNoisePeriodY;
                float width = 0.95f * Mathf.Pow(1f - v, 0.6f) + 0.03f;
                float baseFade = Edge(0f, 0.12f, v);
                float topFade = 1f - Edge(0.78f, 1f, v);

                for (int x = 0; x < FireW; x++)
                {
                    float u = (x + 0.5f) / FireW * 2f - 1f;                    // -1..1
                    float nx = (u + 1f) * 0.5f * FireCellsX;

                    float n = Fbm(nx + 17.3f, ny, FireNoisePeriodX, FireNoisePeriodY, 4);
                    float nWarp = Fbm(nx * 0.7f + 3.1f, ny + 5f, FireNoisePeriodX, FireNoisePeriodY, 3);
                    float uw = u + (nWarp - 0.5f) * 1.2f * v * v + (n - 0.5f) * 0.25f * v;

                    float e = Mathf.Clamp01(1f - Mathf.Abs(uw) / width);
                    float e2 = Mathf.Clamp01(1f - Mathf.Abs(uw) / (width * 1.3f));
                    float core = Mathf.Pow(e, 0.7f) * baseFade;

                    float intensity = core * (1.1f - 0.95f * v)
                                    + (n - 0.5f) * (0.45f + 0.8f * v) * Mathf.Sqrt(e2)
                                    - 0.05f;

                    // Split the top into licking tongues.
                    float tongues = Fbm(nx * 2f + 9.1f, ny * 2f, FireNoisePeriodX * 2, FireNoisePeriodY * 2, 2);
                    intensity -= Mathf.Max(0f, tongues - 0.45f) * 1.2f * v * v;
                    intensity *= topFade;

                    float alpha = Edge(0.16f, 0.3f, intensity);
                    if (alpha <= 0f) continue;

                    Color32 c = ramp[Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(intensity) * (RampSize - 1)), 0, RampSize - 1)];
                    c.a = (byte)Mathf.RoundToInt(alpha * 255f);
                    px[(oy + y) * texW + (ox + x)] = c;
                }
            }
        }

        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "FieryEyes_Fire"
        };
        tex.SetPixels32(px);
        tex.Apply(false, true); // no longer readable → frees the CPU copy

        var sprites = new Sprite[FireFrameCount];
        for (int f = 0; f < FireFrameCount; f++)
        {
            var rect = new Rect((f % FireCols) * cellW + 1, (f / FireCols) * cellH + 1, FireW, FireH);
            // Pivot near the bottom: the flame grows up out of the eye. FullRect avoids
            // generating a tight mesh per frame.
            sprites[f] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.1f), FireH, 0, SpriteMeshType.FullRect);
        }

        s_fireFrames[key] = sprites;
        return sprites;
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    // Value noise that tiles with period (px, py).
    private static float ValueNoise(float x, float y, int px, int py)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        int x0 = PositiveMod(xi, px), x1 = PositiveMod(xi + 1, px);
        int y0 = PositiveMod(yi, py), y1 = PositiveMod(yi + 1, py);

        float a = Hash(x0, y0), b = Hash(x1, y0), c = Hash(x0, y1), d = Hash(x1, y1);
        float ab = a + (b - a) * fx;
        float cd = c + (d - c) * fx;
        return ab + (cd - ab) * fy;
    }

    private static float Fbm(float x, float y, int px, int py, int octaves)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        int freq = 1;
        for (int k = 0; k < octaves; k++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, px * freq, py * freq);
            total += amp;
            amp *= 0.5f;
            freq *= 2;
        }
        return sum / total;
    }

    private static float Edge(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // ── Eye & halo sprites 

    private const int EyeTex = 96;
    private static readonly Dictionary<int, Sprite> s_eyeSprites = new Dictionary<int, Sprite>();
    private static Sprite s_softDot;

    private int EyeLookKey()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + coreColor.GetHashCode();
            h = h * 31 + irisColor.GetHashCode();
            h = h * 31 + rimColor.GetHashCode();
            h = h * 31 + pupilColor.GetHashCode();
            h = h * 31 + pupilWidth.GetHashCode();
            h = h * 31 + browHeight.GetHashCode();
            h = h * 31 + browSlope.GetHashCode();
            return h;
        }
    }

    private Sprite GetEyeSprite()
    {
        int key = EyeLookKey();
        if (s_eyeSprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(EyeTex, EyeTex, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "FieryEyes_Eye"
        };
        var px = new Color[EyeTex * EyeTex];

        for (int y = 0; y < EyeTex; y++)
        {
            for (int x = 0; x < EyeTex; x++)
            {
                float u = (x + 0.5f) / EyeTex * 2f - 1f;
                float v = (y + 0.5f) / EyeTex * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);

                // Round eye, soft edge, cut across the top by a slanted angry brow.
                float a = 1f - Edge(0.82f, 1f, d);
                float cut = browHeight + browSlope * u;
                a *= 1f - Edge(cut - 0.06f, cut + 0.06f, v);

                // White-hot core → yellow → red-orange rim.
                float toMid = Edge(0f, 0.5f, d);
                float toRim = Edge(0.55f, 0.95f, d);
                Color c = coreColor * (1f - toMid) + irisColor * (toMid - toRim) + rimColor * toRim;

                // Vertical slit pupil.
                float pu = u / pupilWidth, pv = v / 0.62f;
                float p = 1f - Edge(0.7f, 1f, Mathf.Sqrt(pu * pu + pv * pv));
                c = Color.Lerp(c, pupilColor, p);

                c.a = a;
                px[y * EyeTex + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, EyeTex, EyeTex), new Vector2(0.5f, 0.5f),
                                   EyeTex, 0, SpriteMeshType.FullRect);
        s_eyeSprites[key] = sprite;
        return sprite;
    }

    // Soft radial dot, 1 unit across. Used for the halo and the embers.
    private static Sprite GetSoftDotSprite()
    {
        if (s_softDot != null) return s_softDot;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "FieryEyes_SoftDot"
        };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f;
                float v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 2.2f));
            }
        tex.SetPixels(px);
        tex.Apply(false, true);
        s_softDot = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S, 0, SpriteMeshType.FullRect);
        return s_softDot;
    }

    // ── Editor bake ──────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void Reset()
    {
        // Auto-bake when the component is first added, if the data is already wired.
        var s = GetComponent<EnemyStats>();
        if (s != null && s.enemyData != null) BakeEyePositions();
    }

    private sealed class PixelData
    {
        public Color32[] px;
        public int w, h;
        public int importedW, importedH;
    }

    [ContextMenu("Bake Eye Positions From Sprites")]
    private void BakeEyePositions()
    {
        var s = GetComponent<EnemyStats>();
        EnemyData data = s != null ? s.enemyData : null;
        if (data == null)
        {
            Debug.LogWarning($"[FieryEyes] {name}: no EnemyStats.enemyData to bake from.", this);
            return;
        }

        // Each clip is scanned in order so every frame seeds the search for the next —
        // a head that drifts across a long clip is followed step by step.
        var sequences = new List<Sprite[]>();
        void AddSeq(Sprite[] seq) { if (seq != null && seq.Length > 0) sequences.Add(seq); }
        AddSeq(data.frames);
        AddSeq(data.idleFrames);
        AddSeq(data.attackFrames);
        AddSeq(data.deathFrames);

        if (sequences.Count == 0)
        {
            // Legacy Resources-path assets that haven't been migrated yet.
            foreach (string path in new[] { data.spriteFolderPath, data.idleFolderPath,
                                            data.attackFolderPath, data.deathFolderPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                Sprite[] loaded = Resources.LoadAll<Sprite>(path);
                Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
                AddSeq(loaded);
            }
        }

        if (sequences.Count == 0)
        {
            Debug.LogWarning($"[FieryEyes] {name}: EnemyData '{data.name}' has no sprites to bake.", this);
            return;
        }

        Undo.RecordObject(this, "Bake Fiery Eye Positions");
        bakedFrames.Clear();

        var cache = new Dictionary<string, PixelData>();
        var seen = new HashSet<Sprite>();
        var missing = new List<string>();
        int found = 0;

        foreach (Sprite[] seq in sequences)
        {
            Vector2 seed = irisPosition;
            foreach (Sprite sp in seq)
            {
                if (sp == null || !seen.Add(sp)) continue;

                if (TryLocateIris(sp, seed, cache, out Vector2 pos))
                {
                    bakedFrames.Add(new EyeFrame { sprite = sp, irisPosition = pos, eyeVisible = true });
                    seed = pos;
                    found++;
                }
                else
                {
                    bakedFrames.Add(new EyeFrame { sprite = sp, irisPosition = seed, eyeVisible = false });
                    missing.Add(sp.name);
                }
            }
        }

        EditorUtility.SetDirty(this);
        PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        BuildLookup();

        string msg = $"[FieryEyes] {name}: eye found on {found}/{bakedFrames.Count} frames.";
        if (missing.Count > 0)
        {
            msg += " Hidden on: " + string.Join(", ", missing.GetRange(0, Mathf.Min(12, missing.Count)))
                 + (missing.Count > 12 ? " …" : "")
                 + ". If those frames DO show the eye, raise Bake Search Radius / Color Tolerance.";
        }
        Debug.Log(msg, this);
    }

    private bool TryLocateIris(Sprite sp, Vector2 seedLocal,
                               Dictionary<string, PixelData> cache, out Vector2 result)
    {
        result = seedLocal;

        // Resolve the SPRITE's own asset (the PNG it was sliced from). sprite.texture is
        // not safe here: with a Sprite Atlas and the packer enabled in the editor it
        // returns the atlas page, whose file can't be decoded — which made every frame
        // "eye not found" and hid the fire entirely.
        string path = AssetDatabase.GetAssetPath(sp);
        if (string.IsNullOrEmpty(path)) return false;

        if (!cache.TryGetValue(path, out PixelData pd))
        {
            pd = null;
            // Read the source file: works with Read/Write off and at full resolution
            // even when the import downsizes the texture.
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (imported != null && File.Exists(path))
            {
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tmp.LoadImage(File.ReadAllBytes(path)))
                    pd = new PixelData
                    {
                        px = tmp.GetPixels32(),
                        w = tmp.width,
                        h = tmp.height,
                        importedW = imported.width,
                        importedH = imported.height
                    };
                DestroyImmediate(tmp);
            }
            if (pd == null)
                Debug.LogWarning($"[FieryEyes] Could not read '{path}' for sprite '{sp.name}'.", this);
            cache[path] = pd;
        }
        if (pd == null) return false;

        // sprite.rect lives in the imported texture's pixel space; scale it to the
        // source file. Local units are mapped through sprite.bounds, which is
        // independent of any import downscale.
        float kx = (float)pd.w / Mathf.Max(1, pd.importedW);
        float ky = (float)pd.h / Mathf.Max(1, pd.importedH);
        Rect r = sp.rect;
        float rx = r.x * kx, ry = r.y * ky, rw = r.width * kx, rh = r.height * ky;
        Bounds b = sp.bounds;
        if (b.size.x <= 0f || b.size.y <= 0f) return false;

        float ppuX = rw / b.size.x, ppuY = rh / b.size.y;
        float cx = rx + (seedLocal.x - b.min.x) * ppuX;
        float cy = ry + (seedLocal.y - b.min.y) * ppuY;
        float radPx = bakeSearchRadius * ppuX;
        float radSq = radPx * radPx;

        int xMin = Mathf.Max(0, Mathf.Max(Mathf.FloorToInt(rx), Mathf.FloorToInt(cx - radPx)));
        int xMax = Mathf.Min(pd.w - 1, Mathf.Min(Mathf.CeilToInt(rx + rw) - 1, Mathf.CeilToInt(cx + radPx)));
        int yMin = Mathf.Max(0, Mathf.Max(Mathf.FloorToInt(ry), Mathf.FloorToInt(cy - radPx)));
        int yMax = Mathf.Min(pd.h - 1, Mathf.Min(Mathf.CeilToInt(ry + rh) - 1, Mathf.CeilToInt(cy + radPx)));

        float tolSq = bakeColorTolerance * bakeColorTolerance;
        int tr = bakeIrisColor.r, tg = bakeIrisColor.g, tb = bakeIrisColor.b;

        double sumX = 0, sumY = 0;
        int count = 0;
        for (int y = yMin; y <= yMax; y++)
        {
            float dy = y - cy;
            int row = y * pd.w;
            for (int x = xMin; x <= xMax; x++)
            {
                float dx = x - cx;
                if (dx * dx + dy * dy > radSq) continue;

                Color32 p = pd.px[row + x];
                if (p.a < 200) continue;
                int er = p.r - tr, eg = p.g - tg, eb = p.b - tb;
                if (er * er + eg * eg + eb * eb > tolSq) continue;

                sumX += x;
                sumY += y;
                count++;
            }
        }

        int minCount = Mathf.Max(20, Mathf.RoundToInt(bakeMinPixelFraction * rw * rh));
        if (count < minCount) return false;

        float mx = (float)(sumX / count) + 0.5f;
        float my = (float)(sumY / count) + 0.5f;
        result = new Vector2(b.min.x + (mx - rx) / ppuX,
                             b.min.y + (my - ry) / ppuY);
        return true;
    }
#endif
}


