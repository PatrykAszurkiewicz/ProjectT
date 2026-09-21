using System;
using UnityEngine;

//  ENEMY SPAWN PORTAL — procedural rift that opens where a wave enemy appears.

[System.Serializable]
public class EnemyPortalStyle
{
    [Header("Size (relative to the enemy)")]
    [Tooltip("Portal radius as a multiple of the measured enemy radius. 1 = exactly as " +
             "wide as the enemy; the default is slightly wider so the enemy reads as " +
             "standing INSIDE the rift rather than in front of it.")]
    public float radiusMultiplier = 1.35f;

    [Tooltip("Hard floor on the portal radius, in world units. Stops a tiny enemy (or a " +
             "failed measurement) producing an invisible speck.")]
    public float minRadius = 0.35f;

    [Tooltip("Hard ceiling on the portal radius, in world units. Stops a mis-scaled " +
             "prefab producing a screen-filling rift.")]
    public float maxRadius = 6f;

    [Tooltip("Vertical squash. 1 = a perfect circle (side-on doorway look). Lower values " +
             "flatten it toward the ground plane, which suits a top-down / 2.5D camera. " +
             "The spinning vortex inside is sheared to match, so it reads as a disc lying " +
             "at an angle rather than a flat decal.")]
    [Range(0.25f, 1.5f)] public float verticalSquash = 0.85f;

    [Tooltip("Where on the enemy the portal is centred. 0 = at its feet, 0.5 = its middle, " +
             "1 = over its head. Slightly low looks best for a rift it steps out of.")]
    [Range(0f, 1f)] public float verticalAnchor = 0.38f;

    [Tooltip("Extra world-unit nudge applied after Vertical Anchor. Positive = upward.")]
    public float verticalOffset = 0f;

    [Header("Timing (seconds)")]
    [Tooltip("Crack-open phase: a bright slit snaps wide, then irises open.")]
    public float openDuration = 0.20f;

    [Tooltip("How long the rift stays fully open, spinning.")]
    public float holdDuration = 0.30f;

    [Tooltip("Collapse phase: the rift flattens back to a slit and blinks out.")]
    public float closeDuration = 0.25f;

    [Header("Colour")]
    [Tooltip("The 'hole' itself. Keep this dark and fairly opaque — it is what makes the " +
             "portal read as depth rather than as a glow decal.")]
    public Color voidColor = new Color(0.05f, 0.02f, 0.13f, 0.95f);

    [Tooltip("Light bleeding out of the rift, and the colour of the vortex arms.")]
    public Color innerGlow = new Color(0.44f, 0.24f, 1f, 0.90f);

    [Tooltip("The energy ring around the edge, its bloom, and the edge crackles.")]
    public Color rimColor = new Color(0.76f, 0.56f, 1f, 1f);

    [Tooltip("Embers thrown out on opening and sucked back in on closing.")]
    public Color sparkColor = new Color(0.92f, 0.84f, 1f, 1f);

    [Tooltip("Per-portal hue wobble, so a group of spawns doesn't look copy-pasted. " +
             "0 disables it and every portal is exactly the colours above.")]
    [Range(0f, 0.3f)] public float hueJitter = 0.06f;

    [Header("Detail & motion")]
    [Range(0, 20)] public int sparkCount = 10;
    [Range(0, 16)] public int crackleCount = 7;

    [Tooltip("Draws the lower half of the rim IN FRONT of the enemy, so it genuinely " +
             "looks like it is standing inside the ring. Turn off if it fights with " +
             "unusually short sprites.")]
    public bool drawFrontArc = true;

    [Tooltip("Degrees per second the vortex spins. Direction is randomised per portal.")]
    public float swirlSpeed = 220f;

    [Tooltip("Brief white burst covering the instant the enemy pops in. This is what " +
             "sells 'it came out of the portal' without the effect having to touch the " +
             "enemy at all. 0 disables it.")]
    [Range(0f, 1f)] public float arrivalFlashStrength = 0.85f;

    [Tooltip("Dark ground pool under the rift that lingers a moment after it shuts.")]
    public bool drawGroundScorch = true;

    [Header("Sorting")]
    [Tooltip("Used only until the enemy's own SpriteRenderer can be read, and after the " +
             "enemy dies mid-effect. Enemies Y-sort around order 1000, so a little below " +
             "that is the right neighbourhood.")]
    public int fallbackSortingOrder = 993;

    [Tooltip("Used only if the enemy has no SpriteRenderer at all.")]
    public string fallbackSortingLayer = "Default";

    [Header("Budget")]
    [Tooltip("Hard cap on portals playing at once. Extra spawns simply get no portal " +
             "rather than dropping frames during a burst wave.")]
    [Min(0)] public int maxConcurrentPortals = 12;

    [Tooltip("Skip the effect entirely when the spawn point is outside every active " +
             "camera. Saves work, but be careful in split-screen: it checks ALL enabled " +
             "cameras, so a portal visible to either player is still drawn.")]
    public bool skipWhenOffScreen = false;

    [Tooltip("Viewport padding for the off-screen test, in viewport units. 0.15 ≈ 15% " +
             "beyond each edge, so a portal just off-frame still plays.")]
    public float offScreenMargin = 0.15f;

    [Tooltip("Run on unscaled time. Leave OFF so the effect freezes correctly with the " +
             "pause and augment menus.")]
    public bool useUnscaledTime = false;
}


public class EnemySpawnPortal : MonoBehaviour
{
    // ── Live-instance budget ──────────────────────────────────────────────────
    // Reset explicitly on load: with Domain Reload disabled this static would
    // otherwise carry a stale count between play sessions and silently starve
    // every portal after the first run.
    private static int _live;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _live = 0; }

    // Belt-and-braces against litter: if the game freezes (timeScale 0 on a menu
    // or game-over) a scaled-time portal would sit on screen indefinitely.
    private const float RealtimeWatchdogSeconds = 20f;

    private EnemyPortalStyle _style;
    private GameObject _enemyGO;          // may become null mid-effect; that is fine
    private SpriteRenderer _enemySR;      // ditto
    private int _sortLayerId;
    private int _baseOrder;

    // True until the portal has been vertically anchored against a REAL sprite. On the
    // spawn frame the enemy usually has no sprite yet, so we open at its pivot and
    // correct the height once as soon as the sprite resolves — see RefineFromEnemy().
    private bool _anchorPending;

    private Transform _disk;              // squashed + scaled: everything on the rift plane
    private Transform _fx;                // unscaled: flash, embers, ground pool

    private SpriteRenderer _scorch, _glow, _void, _swirlA, _swirlB, _rimSoft, _rim, _frontArc, _flash;
    private SpriteRenderer[] _sparks;
    private Vector2[] _sparkDir;
    private float[] _sparkSpeed, _sparkSize, _sparkLife;
    private SpriteRenderer[] _crackles;
    private float[] _crackleAngle, _crackleLen, _cracklePhase;

    private float _radius;                // half-WIDTH of the rift, world units
    private float _t;                     // seconds since Play()
    private float _realStart;
    private float _swirlDir = 1f;
    private float _pulsePhase;
    private float _openMeasureWindow;     // keep re-measuring the enemy for this long

    private float TotalLife => _style.openDuration + _style.holdDuration + _style.closeDuration;

    // ══════════════════════════════════════════════════════════════════════════
    //  ENTRY POINT
    // ══════════════════════════════════════════════════════════════════════════

    /// Opens a portal for a just-spawned enemy. Never throws; returns null when the
    /// effect was skipped (disabled, over budget, off-screen, bad arguments).
    /// <param name="enemyInstance">The freshly instantiated enemy. Only read, never modified.</param>
    /// <param name="sourcePrefab">Prefab it came from — used for sizing on the spawn frame,
    /// before the instance has resolved its sprites in Start().</param>
    /// <param name="fallbackPosition">Where to open if the enemy cannot be measured.</param>
    public static EnemySpawnPortal Play(GameObject enemyInstance,
                                        GameObject sourcePrefab,
                                        Vector3 fallbackPosition,
                                        EnemyPortalStyle style)
    {
        if (style == null) style = new EnemyPortalStyle();
        if (style.maxConcurrentPortals > 0 && _live >= style.maxConcurrentPortals) return null;

        // Measure the enemy. On the spawn frame the instance usually has no sprite
        // yet (EnemyAnimationController resolves them in Start), so this normally
        // falls through to the prefab and is then refined during the open phase.
        float radius = MeasureRadius(enemyInstance, sourcePrefab,
                                     out float halfHeight, out Vector3 center, out bool centerKnown,
                                     fallbackPosition);

        // The vertical anchor is only meaningful against a measured sprite centre. With
        // only the pivot to go on, opening exactly at the pivot is the safe choice —
        // RefineFromEnemy() corrects it as soon as the sprite exists.
        Vector3 pos = center;
        if (centerKnown) pos.y += Mathf.Lerp(-halfHeight, halfHeight, style.verticalAnchor);
        pos.y += style.verticalOffset;

        if (style.skipWhenOffScreen && !IsVisibleToAnyCamera(pos, style.offScreenMargin)) return null;

        var go = new GameObject("EnemySpawnPortal");
        go.transform.SetPositionAndRotation(pos, Quaternion.identity);
        var portal = go.AddComponent<EnemySpawnPortal>();
        portal._anchorPending = !centerKnown;
        portal.Build(enemyInstance, radius, style);
        return portal;
    }

    //  MEASUREMENT

    /// Best-effort enemy half-width / half-height / centre, in world units.
    /// Tries, in order: live sprites on the instance → prefab colliders →
    /// prefab sprites → a sane default. Always succeeds.
    private static float MeasureRadius(GameObject instance, GameObject prefab,
                                       out float halfHeight, out Vector3 center, out bool centerKnown,
                                       Vector3 fallbackPos)
    {
        center = fallbackPos;
        centerKnown = false;
        halfHeight = 0.5f;

        // 1. The live instance, with real world-space bounds. Best source when available.
        if (instance != null && TryMeasureSprites(instance, out Vector2 half, out Vector3 worldCenter))
        {
            center = worldCenter;
            centerKnown = true;
            halfHeight = half.y;
            return RadiusFromHalfExtents(half);
        }

        // 2. Prefab colliders. Mirrors WaveSpawner.GetPrefabClearanceRadius' logic:
        //    most enemies carry a CircleCollider2D sized to their body.
        if (prefab != null)
        {
            float scale = Mathf.Max(Mathf.Abs(prefab.transform.localScale.x),
                                    Mathf.Abs(prefab.transform.localScale.y));
            if (scale < 0.0001f) scale = 1f;

            float best = 0f;
            var cols = prefab.GetComponentsInChildren<Collider2D>(true);
            foreach (var c in cols)
            {
                if (c == null || c.isTrigger) continue;
                float r = (c is CircleCollider2D circle)
                    ? circle.radius * scale
                    : Mathf.Max(c.bounds.extents.x, c.bounds.extents.y);
                if (r > best) best = r;
            }
            if (best > 0.01f)
            {
                halfHeight = best;
                return best;
            }

            // 3. Prefab sprites. Sizes are authored on the asset so this is valid even
            //    though the prefab's world position is meaningless — we only take size.
            if (TryMeasureSprites(prefab, out Vector2 pHalf, out _))
            {
                halfHeight = pHalf.y;
                return RadiusFromHalfExtents(pHalf);
            }
        }

        // 4. Nothing worked. A mid-sized enemy is the least-wrong guess.
        return 0.5f;
    }

    /// Portal width tracks the enemy's width; height contributes at a discount so a
    /// tall thin sprite doesn't produce an absurdly wide rift.
    private static float RadiusFromHalfExtents(Vector2 half) => Mathf.Max(half.x, half.y * 0.75f);

    private static bool TryMeasureSprites(GameObject root, out Vector2 half, out Vector3 center)
    {
        half = Vector2.zero;
        center = root != null ? root.transform.position : Vector3.zero;
        if (root == null) return false;

        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        bool any = false;
        Bounds acc = default(Bounds);

        foreach (var sr in renderers)
        {
            if (sr == null || sr.sprite == null) continue;

            // Computed from the sprite + lossyScale rather than sr.bounds so it also
            // works on a prefab asset, where bounds are not meaningfully positioned.
            Vector2 size = sr.drawMode == SpriteDrawMode.Simple ? (Vector2)sr.sprite.bounds.size : sr.size;
            Vector3 ls = sr.transform.lossyScale;
            var h = new Vector2(Mathf.Abs(size.x * ls.x), Mathf.Abs(size.y * ls.y)) * 0.5f;
            if (h.x < 0.0001f && h.y < 0.0001f) continue;

            if (h.x > half.x) half.x = h.x;
            if (h.y > half.y) half.y = h.y;

            var b = new Bounds(sr.transform.position, new Vector3(h.x * 2f, h.y * 2f, 0f));
            if (!any) { acc = b; any = true; } else acc.Encapsulate(b);
        }

        if (!any) return false;
        center = acc.center;
        return true;
    }

    private static bool IsVisibleToAnyCamera(Vector3 worldPos, float margin)
    {
        var cams = Camera.allCameras;
        if (cams == null || cams.Length == 0) return true;   // no cameras yet — don't skip

        float lo = -margin, hi = 1f + margin;
        foreach (var cam in cams)
        {
            if (cam == null || !cam.isActiveAndEnabled) continue;
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            if (vp.z < 0f) continue;
            if (vp.x >= lo && vp.x <= hi && vp.y >= lo && vp.y <= hi) return true;
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BUILD
    // ══════════════════════════════════════════════════════════════════════════

    private void Build(GameObject enemyInstance, float measuredRadius, EnemyPortalStyle style)
    {
        _style = style;
        _live++;
        _realStart = Time.realtimeSinceStartup;
        _pulsePhase = UnityEngine.Random.value * 6.2831853f;
        _swirlDir = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        _openMeasureWindow = Mathf.Max(0.05f, style.openDuration);

        _radius = Mathf.Clamp(measuredRadius * Mathf.Max(0.01f, style.radiusMultiplier),
                              Mathf.Max(0.01f, style.minRadius),
                              Mathf.Max(style.minRadius, style.maxRadius));

        _enemyGO = enemyInstance;
        if (enemyInstance != null) _enemySR = FindBestRenderer(enemyInstance);
        _sortLayerId = SortingLayer.NameToID(string.IsNullOrEmpty(style.fallbackSortingLayer)
                                                ? "Default" : style.fallbackSortingLayer);
        _baseOrder = style.fallbackSortingOrder;

        // Per-portal hue wobble so a cluster of spawns doesn't look stamped out.
        float seed = UnityEngine.Random.value;
        Color cVoid = _style.voidColor;                                   // the hole stays neutral
        Color cGlow = Jitter(_style.innerGlow, _style.hueJitter, seed);
        Color cRim = Jitter(_style.rimColor, _style.hueJitter, seed);
        Color cSpark = Jitter(_style.sparkColor, _style.hueJitter, seed);

        _disk = new GameObject("Disk").transform;
        _disk.SetParent(transform, false);

        _fx = new GameObject("FX").transform;
        _fx.SetParent(transform, false);

        // ── on the rift plane (scaled + squashed together) ────────────────────
        // Children are unit-sized sprites, so _disk.localScale IS the diameter.
        _glow = MakeChild(_disk, "Glow", EnemyPortalSprites.Disc, cGlow, 1.75f);
        _void = MakeChild(_disk, "Void", EnemyPortalSprites.DiscHard, cVoid, 1.02f);
        _swirlB = MakeChild(_disk, "SwirlBack", EnemyPortalSprites.Swirl, cGlow, 0.78f);
        _swirlA = MakeChild(_disk, "SwirlFront", EnemyPortalSprites.Swirl, cRim, 1.00f);
        _rimSoft = MakeChild(_disk, "RimBloom", EnemyPortalSprites.RingSoft, cRim, 1.16f);
        _rim = MakeChild(_disk, "Rim", EnemyPortalSprites.RingSharp, cRim, 1.00f);

        if (_style.drawFrontArc)
            _frontArc = MakeChild(_disk, "RimFrontArc", EnemyPortalSprites.HalfRing, cRim, 1.005f);

        // Edge crackles — random angles and lengths per portal, so the silhouette
        // is never twice the same.
        int cc = Mathf.Max(0, _style.crackleCount);
        _crackles = new SpriteRenderer[cc];
        _crackleAngle = new float[cc];
        _crackleLen = new float[cc];
        _cracklePhase = new float[cc];
        for (int i = 0; i < cc; i++)
        {
            _crackles[i] = MakeChild(_disk, "Crackle" + i, EnemyPortalSprites.Streak, cRim, 1f);
            _crackleAngle[i] = UnityEngine.Random.Range(0f, 360f);
            _crackleLen[i] = UnityEngine.Random.Range(0.10f, 0.26f);
            _cracklePhase[i] = UnityEngine.Random.Range(0f, 6.2831853f);
        }

        // ── unscaled overlay (never squashed, never shrinks with the rift) ────
        if (_style.drawGroundScorch)
            _scorch = MakeChild(_fx, "GroundPool", EnemyPortalSprites.Disc, new Color(0f, 0f, 0.02f, 0.5f), 1f);

        if (_style.arrivalFlashStrength > 0f)
            _flash = MakeChild(_fx, "ArrivalFlash", EnemyPortalSprites.Disc, Color.white, 1f);

        int sc = Mathf.Max(0, _style.sparkCount);
        _sparks = new SpriteRenderer[sc];
        _sparkDir = new Vector2[sc];
        _sparkSpeed = new float[sc];
        _sparkSize = new float[sc];
        _sparkLife = new float[sc];
        for (int i = 0; i < sc; i++)
        {
            _sparks[i] = MakeChild(_fx, "Ember" + i, EnemyPortalSprites.Disc, cSpark, 1f);
            float a = (360f / Mathf.Max(1, sc)) * i + UnityEngine.Random.Range(-16f, 16f);
            float rad = a * Mathf.Deg2Rad;
            _sparkDir[i] = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad) * _style.verticalSquash).normalized;
            _sparkSpeed[i] = UnityEngine.Random.Range(0.9f, 2.1f);
            _sparkSize[i] = UnityEngine.Random.Range(0.09f, 0.20f);
            _sparkLife[i] = UnityEngine.Random.Range(0.55f, 1f);
        }

        ApplySorting();
        Tick(0f);   // pose everything for frame zero so nothing pops at full size
    }

    private static SpriteRenderer MakeChild(Transform parent, string name, Sprite sprite, Color color, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        return sr;
    }

    /// The largest sprite on the enemy — that is the body, not a shadow blob or a
    /// status pip, so it is the right thing to sort against.
    private static SpriteRenderer FindBestRenderer(GameObject enemy)
    {
        SpriteRenderer best = null;
        float bestArea = -1f;
        foreach (var sr in enemy.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null) continue;
            float area = sr.sprite != null
                ? sr.sprite.bounds.size.x * sr.sprite.bounds.size.y
                : 0f;
            if (best == null || area > bestArea) { best = sr; bestArea = area; }
        }
        return best;
    }

    private static Color Jitter(Color c, float amount, float seed01)
    {
        if (amount <= 0f) return c;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        h = Mathf.Repeat(h + (seed01 - 0.5f) * 2f * amount, 1f);
        Color o = Color.HSVToRGB(h, s, v);
        o.a = c.a;
        return o;
    }

    //  DRIVE
    // LateUpdate, not a coroutine: the sorting order is copied off the enemy's own
    // renderer, which is rewritten every frame by YSortEntity. Reading it late means
    // we are at worst one frame stale on a ±7 offset, which is invisible.
    private void LateUpdate()
    {
        float dt = _style.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _t += dt;

        // Litter guard — see RealtimeWatchdogSeconds.
        if (Time.realtimeSinceStartup - _realStart > RealtimeWatchdogSeconds)
        {
            Destroy(gameObject);
            return;
        }

        if (_t >= TotalLife)
        {
            Destroy(gameObject);
            return;
        }

        ApplySorting();
        Tick(dt);
    }

    private void ApplySorting()
    {
        if (_enemySR != null)
        {
            _sortLayerId = _enemySR.sortingLayerID;
            _baseOrder = _enemySR.sortingOrder;
        }
        // else: the enemy died mid-effect. Keep the last values so the rift does not
        // suddenly jump layer while it is closing.

        SetOrder(_scorch, -7);
        SetOrder(_glow, -6);
        SetOrder(_void, -5);
        SetOrder(_swirlB, -4);
        SetOrder(_swirlA, -3);
        SetOrder(_rimSoft, -2);
        SetOrder(_rim, -1);
        // ...enemy renders here...
        SetOrder(_frontArc, +2);
        SetOrder(_flash, +3);
        if (_sparks != null)
            for (int i = 0; i < _sparks.Length; i++) SetOrder(_sparks[i], +4);
        if (_crackles != null)
            for (int i = 0; i < _crackles.Length; i++) SetOrder(_crackles[i], -1);
    }

    private void SetOrder(SpriteRenderer sr, int offset)
    {
        if (sr == null) return;
        sr.sortingLayerID = _sortLayerId;
        sr.sortingOrder = _baseOrder + offset;
    }

    private void Tick(float dt)
    {
        float open = Mathf.Max(0.0001f, _style.openDuration);
        float hold = Mathf.Max(0f, _style.holdDuration);
        float close = Mathf.Max(0.0001f, _style.closeDuration);

        float openP = Mathf.Clamp01(_t / open);
        float closeP = Mathf.Clamp01((_t - open - hold) / close);
        float lifeP = Mathf.Clamp01(_t / TotalLife);

        // The enemy's sprites usually resolve a frame or two AFTER it spawns, so keep
        // refining the size for the length of the open phase and take the largest
        // reading. The rift is still growing then, so a changing target is invisible.
        if (_t <= _openMeasureWindow) RefineFromEnemy();

        // ── Rift scale ────────────────────────────────────────────────────────
        // Open: width snaps wide first (a slit), then height irises open.
        // Close: height collapses first, so it flattens back to a slit and blinks out.
        float wOpen = EaseOutBack(Mathf.Clamp01(openP / 0.6f));
        float hOpen = Mathf.Lerp(0.06f, 1f, EaseOutCubic(Mathf.Clamp01((openP - 0.15f) / 0.85f)));
        float wClose = 1f - EaseInCubic(closeP);
        float hClose = 1f - EaseInQuint(Mathf.Clamp01(closeP * 1.35f));

        float pulse = 1f + 0.045f * Mathf.Sin(_t * 17f + _pulsePhase);
        float diameter = _radius * 2f;
        float w = diameter * wOpen * wClose * pulse;
        float h = diameter * hOpen * hClose * _style.verticalSquash * pulse;
        _disk.localScale = new Vector3(w, Mathf.Max(h, 0.0001f), 1f);

        // Global fade so nothing hard-cuts at the end of the close.
        float bodyAlpha = Mathf.Clamp01(openP / 0.35f) * (1f - EaseInCubic(closeP));

        SetAlpha(_glow, _style.innerGlow.a * bodyAlpha * (0.75f + 0.25f * Mathf.Sin(_t * 11f + _pulsePhase)));
        SetAlpha(_void, _style.voidColor.a * bodyAlpha);
        SetAlpha(_rim, bodyAlpha);
        SetAlpha(_rimSoft, 0.38f * bodyAlpha);
        SetAlpha(_frontArc, 0.80f * bodyAlpha);

        // ── Vortex ────────────────────────────────────────────────────────────
        // The arms spin fastest as the rift tears open and again as it snaps shut.
        float spinBoost = 1f + 2.2f * (1f - openP) + 2.6f * closeP;
        float spin = _style.swirlSpeed * _swirlDir * spinBoost * dt;
        if (_swirlA != null)
        {
            _swirlA.transform.Rotate(0f, 0f, spin, Space.Self);
            SetAlpha(_swirlA, 0.72f * bodyAlpha);
        }
        if (_swirlB != null)
        {
            _swirlB.transform.Rotate(0f, 0f, -spin * 0.62f, Space.Self);
            SetAlpha(_swirlB, 0.50f * bodyAlpha);
        }

        // ── Edge crackles ─────────────────────────────────────────────────────
        if (_crackles != null)
        {
            for (int i = 0; i < _crackles.Length; i++)
            {
                var c = _crackles[i];
                if (c == null) continue;

                float ang = _crackleAngle[i] + _swirlDir * _t * 24f;
                float rad = ang * Mathf.Deg2Rad;
                // Sprites are unit-sized, so the rim sits at 0.5 in disk space.
                c.transform.localPosition = new Vector3(Mathf.Cos(rad) * 0.5f, Mathf.Sin(rad) * 0.5f, 0f);
                c.transform.localRotation = Quaternion.Euler(0f, 0f, ang);

                // Cheap deterministic flicker — no Perlin lookup, no allocation.
                float flick = 0.5f + 0.5f * Mathf.Sin(_t * 26f + _cracklePhase[i])
                                   * Mathf.Sin(_t * 9.3f + _cracklePhase[i] * 1.7f);
                float len = _crackleLen[i] * (0.55f + 0.45f * flick);
                c.transform.localScale = new Vector3(len, len * 0.16f, 1f);
                SetAlpha(c, bodyAlpha * flick * 0.9f);
            }
        }

        // ── Arrival flash 
        // Peaks a hair after the slit opens: it covers the frame the enemy pops in,
        // which is what makes it read as having stepped through.
        if (_flash != null)
        {
            const float flashPeak = 0.06f;
            const float flashLife = 0.20f;
            float fp = Mathf.Clamp01(_t / flashPeak);
            float fq = Mathf.Clamp01((_t - flashPeak) / flashLife);
            float a = (_t < flashPeak ? fp : 1f - EaseOutCubic(fq)) * _style.arrivalFlashStrength;
            float s = diameter * Mathf.Lerp(0.45f, 1.6f, Mathf.Clamp01(_t / (flashPeak + flashLife)));
            _flash.transform.localScale = new Vector3(s, s * _style.verticalSquash, 1f);
            SetAlpha(_flash, Mathf.Max(0f, a));
        }

        // ── Ground pool 
        if (_scorch != null)
        {
            float s = diameter * 1.35f * Mathf.Clamp01(openP / 0.5f);
            _scorch.transform.localScale = new Vector3(s, s * _style.verticalSquash * 0.85f, 1f);
            // Outlives the rift slightly, so the ground settles instead of blinking.
            SetAlpha(_scorch, 0.5f * Mathf.Clamp01(openP / 0.4f) * (1f - EaseInCubic(Mathf.Clamp01(lifeP * 1.12f))));
        }

        // ── Embers 
        // Thrown outward on opening, then dragged back into the centre as the rift
        // shuts — the suck-in is what makes the close read as deliberate.
        if (_sparks != null)
        {
            float outT = Mathf.Clamp01(_t / (open + hold * 0.7f));
            float suck = EaseInCubic(closeP);
            for (int i = 0; i < _sparks.Length; i++)
            {
                var sp = _sparks[i];
                if (sp == null) continue;

                float travel = _radius * _sparkSpeed[i] * EaseOutCubic(outT) * _sparkLife[i];
                Vector2 p = _sparkDir[i] * travel * (1f - suck);
                sp.transform.localPosition = new Vector3(p.x, p.y, 0f);

                float s = _radius * _sparkSize[i] * (1f - 0.55f * outT) * (1f - suck * 0.8f);
                sp.transform.localScale = new Vector3(s, s, 1f);

                float a = Mathf.Clamp01(_t / 0.05f)                       // instant appear
                        * (1f - Mathf.Clamp01(outT / _sparkLife[i]) * 0.85f)  // natural burn-out
                        * (1f - EaseInCubic(closeP));                     // and gone with the rift
                SetAlpha(sp, a * _style.sparkColor.a);
            }
        }
    }

    /// Re-reads the enemy's live sprites during the open phase.

    private void RefineFromEnemy()
    {
        if (_enemyGO == null) return;
        if (!TryMeasureSprites(_enemyGO, out Vector2 half, out Vector3 worldCenter)) return;

        float wanted = Mathf.Clamp(RadiusFromHalfExtents(half) * Mathf.Max(0.01f, _style.radiusMultiplier),
                                   Mathf.Max(0.01f, _style.minRadius),
                                   Mathf.Max(_style.minRadius, _style.maxRadius));
        if (wanted > _radius) _radius = wanted;

        if (_anchorPending)
        {
            _anchorPending = false;
            Vector3 p = transform.position;
            p.y = worldCenter.y + Mathf.Lerp(-half.y, half.y, _style.verticalAnchor) + _style.verticalOffset;
            transform.position = p;
        }
    }

    private static void SetAlpha(SpriteRenderer sr, float a)
    {
        if (sr == null) return;
        Color c = sr.color;
        c.a = Mathf.Clamp01(a);
        sr.color = c;
    }

    private void OnDestroy()
    {
        _live = Mathf.Max(0, _live - 1);
    }

    // ── Easing 
    private static float EaseOutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
    private static float EaseInCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
    private static float EaseInQuint(float t) { t = Mathf.Clamp01(t); return t * t * t * t * t; }
    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}


//  PROCEDURAL SPRITES
//  Generated once, cached, and flagged HideAndDontSave so a scene load cannot
//  destroy the textures out from under the static cache (which would otherwise
//  show up as invisible portals on the second run of a session).
//  Total cost: five textures, ~1 MB of RGBA32, ~10 ms of generation. Call
//  Prewarm() from a loading screen to pay it there instead of on the first spawn.
public static class EnemyPortalSprites
{
    private const int DiscSize = 128;
    private const int RingSize = 192;
    private const int StreakW = 64;
    private const int StreakH = 16;

    private static Sprite _disc, _discHard, _ringSharp, _ringSoft, _halfRing, _swirl, _streak;

    /// Soft radial falloff. Doubles as the glow, the arrival flash and the embers.
    public static Sprite Disc => _disc != null ? _disc : (_disc = BuildDisc(0f));

    /// Solid out to ~3/4 of the radius, then a short feather. Used for the void: a fully
    /// soft disc reads as a smudge, and the portal needs to read as an actual hole.
    public static Sprite DiscHard => _discHard != null ? _discHard : (_discHard = BuildDisc(0.72f));

    /// Thin bright ring — the rift's hard edge.
    public static Sprite RingSharp => _ringSharp != null ? _ringSharp : (_ringSharp = BuildRing(0.045f, 0.02f));

    /// Wide blurry ring — the bloom around the edge.
    public static Sprite RingSoft => _ringSoft != null ? _ringSoft : (_ringSoft = BuildRing(0.10f, 0.16f));

    /// Lower half of the sharp ring, drawn in front of the enemy so it looks like it
    /// is standing inside the portal rather than pasted on top of it.
    public static Sprite HalfRing => _halfRing != null ? _halfRing : (_halfRing = BuildHalfRing());

    /// Log-ish spiral arms winding into the centre.
    public static Sprite Swirl => _swirl != null ? _swirl : (_swirl = BuildSwirl());

    /// Tapered horizontal bar, used for the edge crackles.
    public static Sprite Streak => _streak != null ? _streak : (_streak = BuildStreak());

    /// Generates everything up front. Safe to call repeatedly — it is a no-op once warm.
    public static void Prewarm()
    {
        _ = Disc; _ = DiscHard; _ = RingSharp; _ = RingSoft; _ = HalfRing; _ = Swirl; _ = Streak;
    }

    private static Texture2D NewTex(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        return tex;
    }

    // pixelsPerUnit == texture width makes every sprite exactly 1 world unit across,
    // so a parent's localScale IS the diameter. Do not change this without revisiting
    // every scale calculation in EnemySpawnPortal.
    private static Sprite Finish(Texture2D tex, Color[] px)
    {
        tex.SetPixels(px);
        tex.Apply(false, false);
        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                               new Vector2(0.5f, 0.5f), tex.width);
        sp.hideFlags = HideFlags.HideAndDontSave;
        return sp;
    }

    /// solidFraction 0 = pure soft glow; 0.72 = opaque core with a short feathered edge.
    private static Sprite BuildDisc(float solidFraction)
    {
        const int S = DiscSize;
        float solid = Mathf.Clamp01(solidFraction);
        float fall = Mathf.Max(0.02f, 1f - solid);

        var tex = NewTex(S, S);
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = d <= solid ? 1f : Mathf.Clamp01(1f - (d - solid) / fall);
                a = a * a * (3f - 2f * a);          // smoothstep falloff
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        return Finish(tex, px);
    }

    private static Sprite BuildRing(float strokeFraction, float softness)
    {
        const int S = RingSize;
        var tex = NewTex(S, S);
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;
        float outerR = c - 1f;
        float half = outerR * strokeFraction;
        float ringR = outerR - half;
        float feather = Mathf.Max(1.2f, outerR * softness);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float off = Mathf.Abs(d - ringR);
                float a;
                if (off <= half) a = 1f;
                else a = Mathf.Clamp01(1f - (off - half) / feather);
                a = a * a * (3f - 2f * a);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        return Finish(tex, px);
    }

    private static Sprite BuildHalfRing()
    {
        const int S = RingSize;
        var tex = NewTex(S, S);
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;
        float outerR = c - 1f;
        float half = outerR * 0.045f;
        float ringR = outerR - half;
        float feather = Mathf.Max(1.2f, outerR * 0.02f);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float off = Mathf.Abs(d - ringR);
                float a = off <= half ? 1f : Mathf.Clamp01(1f - (off - half) / feather);
                a = a * a * (3f - 2f * a);

                // Keep the bottom arc, fade smoothly to nothing across the middle so
                // the front piece blends into the back ring instead of ending abruptly.
                float t = Mathf.Clamp01((-dy) / (outerR * 0.45f));
                a *= t * t * (3f - 2f * t);

                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        return Finish(tex, px);
    }

    // LOGARITHMIC spiral, not a radial one. The winding term is Twist * ln(d): because
    // the phase advances evenly in LOG radius, the arms curve at a constant visual rate
    // all the way out. The obvious-looking alternative, Twist / d, only bends near the
    // very centre — which is exactly the part the inner hole masks off — so it renders
    // as a three-blade fan rather than a vortex. Do not "simplify" it back.
    private static Sprite BuildSwirl()
    {
        const int S = RingSize;
        const int Arms = 4;
        const float Twist = 4.5f;
        const float Sharpen = 2.2f;     // >1 thins the arms and widens the gaps

        var tex = NewTex(S, S);
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 1f) { px[y * S + x] = Color.clear; continue; }

                float ang = Mathf.Atan2(dy, dx);
                float s = Mathf.Sin(Arms * ang - Twist * Mathf.Log(Mathf.Max(d, 0.02f)));
                float a = Mathf.Pow(Mathf.Clamp01(s * 0.5f + 0.5f), Sharpen);

                // Small hollow at the very centre so the arms converge on a dark eye,
                // and a fade before the rim so the ring owns the edge.
                float inner = Mathf.Clamp01((d - 0.03f) / 0.10f);
                float outer = 1f - Mathf.Clamp01((d - 0.70f) / 0.30f);
                a *= inner * inner * (3f - 2f * inner);
                a *= outer * outer * (3f - 2f * outer);

                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        return Finish(tex, px);
    }

    private static Sprite BuildStreak()
    {
        var tex = NewTex(StreakW, StreakH);
        var px = new Color[StreakW * StreakH];
        float cx = (StreakW - 1) * 0.5f, cy = (StreakH - 1) * 0.5f;
        for (int y = 0; y < StreakH; y++)
            for (int x = 0; x < StreakW; x++)
            {
                float u = Mathf.Abs(x - cx) / cx;
                float v = Mathf.Abs(y - cy) / cy;
                float a = Mathf.Clamp01(1f - u) * Mathf.Clamp01(1f - v * v);
                a = a * a * (3f - 2f * a);
                px[y * StreakW + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(px);
        tex.Apply(false, false);
        // Same pixels-per-unit convention as the square sprites: 1 unit across.
        var sp = Sprite.Create(tex, new Rect(0, 0, StreakW, StreakH),
                               new Vector2(0.5f, 0.5f), StreakW);
        sp.hideFlags = HideFlags.HideAndDontSave;
        return sp;
    }
}
