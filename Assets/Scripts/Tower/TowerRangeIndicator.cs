using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Subtle ground rings showing what a tower actually reaches.
///
/// One ring per meaningful radius, derived from the SAME fields the combat code
/// checks against, so the circle can never lie about the gameplay:
///
///   Basic / Artillery / Ice / Poison  outer = Tower.ProjectileRange      (the `dist <= ProjectileRange`
///                                                                         gate in Tower.Attack)
///                                     inner = tentacle reach + 0.4       (Tower.IsTargetInMeleeRange)
///   Laser                             outer = ProjectileRange            (targeting reach, not laserMaxLength)
///   Hammer                            hammerAOERadius                    (the OverlapCircleAll in PerformHammerImpact)
///   Heal                              healRange                          (the heal pulse radius)
///   Generator                         generationRange
///
/// STYLE  (the `style` field — this is the switch for turning the dotted ring off)
///   SpriteImage (default) — draws the authored ring artwork, scaled so the ring in
///     the image lands exactly on the tower's range. Used for the outer AND inner
///     ring; the inner one is rotated by innerRingExtraRotation so two concentric
///     copies don't line up dash-for-dash.
///   Dots — procedural ring of small soft dots, one mesh per ring.
///   SolidLine — a continuous thin line.
/// Changing this at runtime is fine; rings rebuild on the next refresh tick.
///
/// READABILITY ACROSS BIOMES
/// Every ring is drawn twice: a larger, dark, low-alpha halo underneath and a
/// smaller light dot on top. The dark halo keeps the ring readable on pale sand
/// and snow; the light core keeps it readable on dark grass and water. A single
/// flat-coloured ring always disappears on one biome or the other.
///
/// SCALE
/// Every radius on Tower is in WORLD units, but tower prefabs are authored at
/// spriteScale 0.25-0.5. The ring container therefore compensates for lossyScale
/// (same trap the range COLLIDER hit -- see the long comment above
/// Tower.SetRangeColliderWorldRadius). Dot size and spacing stay world-constant,
/// so a 12-unit laser ring and a 2-unit hammer ring use identical dots.
///
/// SORTING
/// Rings default to a FIXED sorting order above the grass band. GrassCartoonOverlay
/// y-sorts its blades across roughly 400-1600 (sortOrderBase 1000, spawnRadius 60,
/// sortPrecision 10), and a ground-decal ring near a tower lands right inside that,
/// so on a grass biome the grass draws straight over it. Set useYSort = true for the
/// prettier TowerSlot-style ground decal on sparse biomes.
///
/// ATTACHING
/// Nothing to wire up: a hidden runtime attacher adds this component to every
/// tower in Tower.ActiveTowers. Add it to the tower prefabs instead and set
/// TowerRangeIndicator.AutoAttach = false if you'd rather author the values
/// per tower type.
[DisallowMultipleComponent]
public class TowerRangeIndicator : MonoBehaviour
{
    public enum RingRole { Attack, Melee, Heal, Energy, Impact }
    public enum RingStyle { Dots, SolidLine, SpriteImage }
    public enum ContrastMode { Auto, ForceDarkBackground, ForceBrightBackground }
    public enum RadiusSource { EffectiveIncludingBuffs, BaseIgnoringTetherBuff }

    // ── Style ───────────────────────────────────────────────────────────────

    [Header("Style")]
    [Tooltip("SpriteImage = draw the authored ring artwork, scaled so its ring lands " +
             "exactly on the tower's range. Used for BOTH the outer and the inner ring.\n" +
             "Dots = procedural ring of small soft dots.\n" +
             "SolidLine = a continuous thin line.\n\n" +
             "Switch to Dots or SolidLine to turn the artwork off; switch away from Dots " +
             "to turn the dotted ring off. Rings rebuild themselves on the next refresh " +
             "tick, so this can be changed at runtime.")]
    public RingStyle style = RingStyle.SpriteImage;

    // ── Visibility ──────────────────────────────────────────────────────────

    [Header("Visibility")]
    [Tooltip("Per-tower switch. TowerRangeIndicator.GlobalVisible turns every ring " +
             "off at once (hook it to a settings toggle).")]
    public bool visible = true;

    [Tooltip("Opacity when nobody is near the tower. This is the 'subtle' value — " +
             "keep it low, the halo does the work of staying legible.")]
    [Range(0f, 1f)] public float idleOpacity = 0.38f;

    [Tooltip("Fade the rings up while a player stands near the tower, so they read " +
             "clearly exactly when the player is deciding where to build.")]
    public bool brightenWhenPlayerNear = true;

    [Tooltip("Distance (world units, measured from the ring's own outer radius) at " +
             "which the brighten kicks in.")]
    [Min(0f)] public float playerNearDistance = 7f;

    [Range(0f, 1f)] public float nearOpacity = 0.8f;

    [Tooltip("Opacity units per second for the fade between idle and near.")]
    [Min(0.1f)] public float fadeSpeed = 5f;

    [Tooltip("Opacity multiplier while the tower is depleted / disabled by damage / " +
             "destroyed. The ring dims rather than vanishing, so a dead tower still " +
             "shows the hole it left in your coverage.")]
    [Range(0f, 1f)] public float offlineOpacityScale = 0.35f;

    // ── Dots ────────────────────────────────────────────────────────────────

    [Header("Dots")]
    [Tooltip("Diameter of one dot in WORLD units. 0.09 is a fine speck at the game's " +
             "usual zoom; raise toward 0.15 if the rings read as noise rather than dots.")]
    [Min(0.01f)] public float dotSize = 0.14f;

    [Tooltip("Gap between dot CENTRES along the ring, in world units. Larger = sparser " +
             "and subtler. Constant in world space, so every ring has the same texture " +
             "regardless of radius.")]
    [Min(0.05f)] public float dotSpacing = 0.40f;

    [Tooltip("The dark halo dot underneath is this many times bigger than the core dot. " +
             "This is what makes the ring survive a pale desert or snow biome.")]
    [Min(1f)] public float haloSizeMultiplier = 2.4f;

    [Tooltip("Degrees per second the whole dot ring slowly rotates. 0 = static. " +
             "A very small value (2-5) makes the ring feel alive without drawing attention.")]
    public float dotDriftDegreesPerSecond = 0f;

    [Tooltip("Safety cap on dots per ring, so a huge radius with tiny spacing can't " +
             "build a 5000-quad mesh.")]
    [Min(8)] public int maxDotsPerRing = 400;

    // ── Sprite artwork (used when style == SpriteImage) ─────────────────────

    [Header("Sprite artwork")]
    [Tooltip("The ring artwork. Leave empty to load it from Ring Sprite Resource Path.")]
    public Sprite ringSprite;

    [Tooltip("Resources path for the ring artwork, used when Ring Sprite is empty.\n" +
             "No leading 'Assets/Resources/' and NO file extension.\n\n" +
             "Default matches Assets/Resources/Sprites/towerRange.png.\n\n" +
             "Resources.Load only sees assets under a folder literally named 'Resources' — " +
             "that is why the artwork appeared in the editor but not in a build while the PNG " +
             "sat in Assets/Art/Buildings. The texture also has to be imported as " +
             "'Sprite (2D and UI)'; a Default-imported texture returns null here and looks " +
             "exactly like a missing file.")]
    public string ringSpriteResourcePath = "Sprites/towerRange";

    [Tooltip("What fraction of the sprite's full WIDTH the drawn ring's DIAMETER occupies.\n\n" +
             "Measured from towerRange 1.png: the dashes span 0.843 of the texture width " +
             "(1436 of 1704 px), so 0.84 puts the artwork's ring exactly on the tower's real " +
             "range. Lower this if the artwork reads as too big, raise it if too small.")]
    [Range(0.1f, 1f)] public float spriteRingRadiusFraction = 0.84f;

    [Tooltip("Multiply the artwork by the ring's role colour (attack/melee/heal/...). " +
             "OFF keeps the authored purple, which is usually what you want — tinting a " +
             "coloured sprite muddies it. ON makes the inner melee ring a different hue " +
             "from the outer one.")]
    public bool tintSpriteWithRoleColor = false;

    [Tooltip("OFF. Draws a darkened copy behind the artwork, the same job the dark halo " +
             "does for the dots.\n\n" +
             "It was on at 1.04 scale, and that was wrong for a ring: scaling a RING up by " +
             "4% doesn't thicken it, it moves it outward. At radius 10 that put a dark arc " +
             "0.4 units outside the purple one — a separate grey ring, not a shadow. Leave " +
             "this off unless the artwork genuinely disappears on a biome.")]
    public bool spriteShadow = false;

    [Tooltip("Scale of the shadow copy. Keep at 1.0 so it sits directly behind the artwork " +
             "and darkens the ground under it. Anything above ~1.02 reads as a second ring.")]
    [Range(1f, 1.1f)] public float spriteShadowScale = 1.0f;

    [Tooltip("Multiplies the sprite's opacity, clamped at fully opaque.\n\n" +
             "The idle/near opacities are tuned for thin procedural dots. The artwork " +
             "already carries its own soft alpha, so running it through 0.38 as well left " +
             "the purple washed out to grey on pale ground.")]
    [Range(1f, 4f)] public float spriteOpacityScale = 2.2f;

    [Tooltip("Rotation applied to the artwork, in degrees. The inner ring adds " +
             "Inner Ring Extra Rotation on top so the two aren't visibly identical.")]
    public float spriteRotationDegrees = 0f;

    [Tooltip("Extra rotation for the INNER (melee) ring, so concentric copies of the same " +
             "artwork don't line up dash-for-dash.")]
    public float innerRingExtraRotation = 18f;

    // ── Solid line (used when style == SolidLine) ───────────────────────────

    [Header("Solid line (only used when Style = Solid Line)")]
    [Min(0.005f)] public float lineWidth = 0.05f;
    [Min(1f)] public float haloWidthMultiplier = 3.5f;
    [Min(16)] public int minSegments = 48;
    [Min(16)] public int maxSegments = 160;

    // ── Colours ─────────────────────────────────────────────────────────────

    [Header("Ring colours (alpha is ignored — opacity is driven above)")]
    [Tooltip("Halo / outline colour. Near-black reads as a soft shadow under the dots.")]
    public Color haloColor = new Color(0.02f, 0.03f, 0.05f, 1f);

    [Tooltip("Halo opacity relative to the core dot's current opacity.")]
    [Range(0f, 1f)] public float haloStrength = 0.8f;

    [Header("Biome contrast")]
    [Tooltip("Light dots with a dark rim read well on grass, marsh and night, and " +
             "disappear on snow and desert — a pale dot on pale ground is pale ground.\n\n" +
             "With this on, the palette INVERTS on bright biomes: the dot core goes dark " +
             "and the halo goes light, so the ring is dark-on-light there and " +
             "light-on-dark everywhere else.")]
    public bool autoContrastToBiome = true;

    [Tooltip("Biome names treated as bright. Matched case-insensitively against " +
             "BiomeManager.activeBiome, as a substring, so 'Snow' also catches " +
             "'SnowNight' if you add one later.")]
    public string[] brightBiomes = { "Snow", "Desert", "Ice" };

    [Tooltip("How far the core colour is pushed toward black on a bright biome. " +
             "0.7 keeps enough hue to tell the rings apart.")]
    [Range(0f, 1f)] public float brightBiomeCoreDarkening = 0.7f;

    [Tooltip("Halo colour used on bright biomes.\n\n" +
             "This was white, which was the wrong idea: a white halo behind a dark dot on " +
             "SNOW is invisible, so all that remained was one 0.14-unit dark speck per dot. " +
             "On a bright background you want dark-on-light for BOTH layers — the halo then " +
             "reads as a soft shadow that makes each dot bigger and far easier to see.")]
    public Color brightBiomeHaloColor = new Color(0.04f, 0.05f, 0.08f, 1f);

    [Tooltip("Override the automatic detection. Auto = read BiomeManager. " +
             "Use the forced values to check both palettes without changing biome.")]
    public ContrastMode contrastOverride = ContrastMode.Auto;

    [Tooltip("Outer ring: projectile / beam reach.")]
    public Color attackColor = new Color(0.90f, 0.95f, 1.00f, 1f);

    [Tooltip("Inner ring: tentacle swipe reach, where the tower switches to melee.")]
    public Color meleeColor = new Color(1.00f, 0.72f, 0.42f, 1f);

    [Tooltip("Heal tower: the radius players are healed in.")]
    public Color healColor = new Color(0.45f, 1.00f, 0.60f, 1f);

    [Tooltip("Generator: the radius the generation effect covers.")]
    public Color energyColor = new Color(0.40f, 0.76f, 1.00f, 1f);

    [Tooltip("Hammer: the ground-slam AOE.")]
    public Color impactColor = new Color(1.00f, 0.55f, 0.35f, 1f);

    // ── Sorting (ground decal — mirrors TowerSlot) ──────────────────────────

    [Header("Sorting")]
    [Tooltip("OFF by default, and this is the setting to check first if you can't see the rings.\n\n" +
             "GrassCartoonOverlay y-sorts its blades over sortOrderBase 1000 +/- " +
             "spawnRadius(60) x sortPrecision(10) — i.e. roughly 400 to 1600. A ground-decal " +
             "ring near the tower lands around 950-1050, which is INSIDE that band, so on a " +
             "grass biome the grass simply draws over it and the ring disappears.\n\n" +
             "OFF = Fixed Sorting Order below (above all grass, always visible).\n" +
             "ON  = y-sort like TowerSlot: grass in front covers the ring. Prettier, but only " +
             "usable on sparse biomes.")]
    public bool useYSort = false;

    [Tooltip("Sorting layer the rings render on. The project keeps gameplay sprites, " +
             "grass and the laser beam on Default; change this only if yours differ, " +
             "because a wrong layer beats any sorting order and hides the rings entirely.")]
    public string sortingLayerName = "Default";

    [Tooltip("Must match GrassCartoonOverlay.sortPrecision.")]
    public float sortPrecision = 10f;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase.")]
    public int sortOrderBase = 1000;

    [Tooltip("Positive = the ring sits further back in the y-sort, i.e. more grass " +
             "draws in front of it. 3 matches TowerSlot.")]
    public float sortYOffset = 3f;

    [Tooltip("Used when Use Y Sort is off.\n\n" +
             "3500 clears the grass band (~400-1600) AND whatever sub-layers the snow / " +
             "desert overlays stack on their base, while staying under the balloons (4000), " +
             "fog (5000) and night (6000) overlays. The laser beam sits at 32000 if you ever " +
             "need a reference for 'above everything'.")]
    public int fixedSortingOrder = 3500;

    // ── Refresh ─────────────────────────────────────────────────────────────

    [Header("Radius changes")]
    [Tooltip("EffectiveIncludingBuffs (default) = draw what the tower can ACTUALLY reach " +
             "right now, tether buff included. The ring therefore grows and shrinks as you " +
             "walk, because the tether's FAR-zone buff is literally changing the tower's " +
             "range — that is the buff working, not a glitch.\n\n" +
             "BaseIgnoringTetherBuff = draw the tower's own unbuffed range, so the ring " +
             "never moves. The tether chain colour still shows when the buff is active.")]
    public RadiusSource radiusSource = RadiusSource.EffectiveIncludingBuffs;

    [Tooltip("Log every radius change for THIS tower to the console, with what caused it. " +
             "TowerRangeIndicator.LogRadiusChanges turns it on for every tower at once.")]
    public bool logRadiusChanges = false;

    [Tooltip("World units per second the ring grows or shrinks when the tower's range " +
             "changes. The tether's FAR-zone buff scales with how many towers are " +
             "tethered, so ranges move constantly while you walk; snapping the ring to " +
             "each new value made it look like the range was glitching. 0 = snap.")]
    [Min(0f)] public float radiusChangeSpeed = 6f;

    [Tooltip("Geometry is rebuilt only when the ring has drifted this far from the size " +
             "it was built at (as a fraction). Below the threshold the existing mesh is " +
             "just scaled, which is free. 0.25 = rebuild after a 25% change.")]
    [Range(0.05f, 1f)] public float rebuildDriftThreshold = 0.25f;

    [Header("Refresh")]
    [Tooltip("How often the radii are re-read from the Tower (seconds). Cheap; this " +
             "is what makes augment / upgrade range changes show up.")]
    [Min(0.05f)] public float refreshInterval = 0.5f;

    [Header("Debug")]
    public bool verboseLogging = false;

    // ── Statics ─────────────────────────────────────────────────────────────

    /// Master switch for every ring in the scene.
    public static bool GlobalVisible = true;

    /// Global switch for the radius change log (see logRadiusChanges).
    /// OFF. Press F10 in play mode to turn it (and the telemetry) back on.
    public static bool LogRadiusChanges = false;

    /// Live telemetry: prints one block per LiveLogInterval with everything needed to
    /// explain a moving ring.
    ///
    /// OFF by default now that the range bugs are fixed. Press F10 in play mode to
    /// turn it back on, or set this true here.
    ///
    /// NOTE: ResetStatics below re-applies these values at the start of every Play
    /// session (domain reload may be disabled), so both places have to agree — editing
    /// only the initializer would have looked like the setting was being ignored.
    public static bool LiveLogging = false;

    /// Seconds between telemetry blocks.
    public static float LiveLogInterval = 1.0f;

    /// Only the N towers nearest the player are logged, so the console stays readable.
    public static int LiveLogNearestTowers = 4;

    /// When true, a hidden runner adds this component to any tower that lacks it.
    /// Set false (before the first tower spawns) if you author it on prefabs.
    public static bool AutoAttach = true;

    private static Material _sharedLineMaterial;
    private static Material _sharedDotMaterial;
    private static Texture2D _dotTexture;
    private static bool _attacherSpawned;
    private static MaterialPropertyBlock _mpb;

    // Statics don't survive a Play session when domain reload is disabled — the
    // Material and Texture are destroyed, the bools keep stale values. Same reset
    // pattern as Tower.ResetStatics / HammerTowerAnimator.ResetStatics.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        GlobalVisible = true;
        AutoAttach = true;
        LogRadiusChanges = false;
        LiveLogging = false;
        LiveLogInterval = 1.0f;
        LiveLogNearestTowers = 4;
        _sharedLineMaterial = null;
        _sharedDotMaterial = null;
        _dotTexture = null;
        _mpb = null;
        _attacherSpawned = false;
        _biome = null;
        _nextBiomeLookup = 0f;
        _cachedBiomeName = "";
        DefaultRingSprite = null;      // destroyed with the session
        _ringSpriteLookupDone = false;
    }

    // Unity's overloaded == catches the destroyed-but-not-null case on all of these.
    private static Material SharedLineMaterial
    {
        get
        {
            if (_sharedLineMaterial == null)
            {
                _sharedLineMaterial = new Material(Shader.Find("Sprites/Default"))
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    color = Color.white,
                };
            }
            return _sharedLineMaterial;
        }
    }

    private static Material SharedDotMaterial
    {
        get
        {
            if (_sharedDotMaterial == null)
            {
                _sharedDotMaterial = new Material(Shader.Find("Sprites/Default"))
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    mainTexture = DotTexture,
                    color = Color.white,
                    enableInstancing = false,
                };

                // Sprites/Default multiplies vertex colour x _Color x _RendererColor, and
                // _Flip scales the vertex positions. A SpriteRenderer fills those two in
                // per-draw; a MeshRenderer does not, so on some Unity versions they arrive
                // as zero and the mesh renders fully transparent — which looks exactly like
                // "the rings aren't being created". Set them explicitly.
                if (_sharedDotMaterial.HasProperty("_RendererColor"))
                    _sharedDotMaterial.SetColor("_RendererColor", Color.white);
                if (_sharedDotMaterial.HasProperty("_Flip"))
                    _sharedDotMaterial.SetVector("_Flip", Vector4.one);
                if (_sharedDotMaterial.HasProperty("_EnableExternalAlpha"))
                    _sharedDotMaterial.SetFloat("_EnableExternalAlpha", 0f);
            }
            return _sharedDotMaterial;
        }
    }

    /// A soft round dot: opaque to 55% of the radius, then a smooth falloff. The soft
    /// edge is what stops the dots aliasing into flickering specks when the camera moves.
    private static Texture2D DotTexture
    {
        get
        {
            if (_dotTexture != null) return _dotTexture;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = 1f - Mathf.SmoothStep(0.55f, 1f, d);
                    px[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _dotTexture = tex;
            return _dotTexture;
        }
    }

    private static MaterialPropertyBlock Mpb => _mpb ?? (_mpb = new MaterialPropertyBlock());

    /// Shared across every tower — loaded once, never per ring.
    public static Sprite DefaultRingSprite;
    private static bool _ringSpriteLookupDone;

    /// Resolve the ring artwork: inspector reference, then the shared static, then
    /// Resources, then (editor only) the raw asset path so it works before the PNG is
    /// moved under a Resources folder.
    private Sprite ResolveRingSprite()
    {
        if (ringSprite != null) return ringSprite;
        if (DefaultRingSprite != null) return DefaultRingSprite;
        if (_ringSpriteLookupDone) return null;
        _ringSpriteLookupDone = true;

        // Resources is the only path that survives into a build, so try it first and try
        // a few likely spellings — a sprite one folder off is the common case.
        TryLoadFromResources(ringSpriteResourcePath);
        TryLoadFromResources("Sprites/towerRange");
        TryLoadFromResources("Sprites/towerRange 1");
        TryLoadFromResources("Art/Buildings/towerRange 1");
        TryLoadFromResources("towerRange");
        TryLoadFromResources("towerRange 1");

        if (DefaultRingSprite != null) return DefaultRingSprite;

#if UNITY_EDITOR
        // EDITOR-ONLY fallback. This is exactly why Play looked fine while Build and Run
        // showed dots: AssetDatabase can reach Assets/Art/Buildings/towerRange 1.png in
        // the editor, and that file does not exist in a player at all. Resources.Load only
        // sees assets under a folder literally named "Resources"; everything else is
        // stripped unless something references it.
        //
        // Kept so the editor still looks right, but it now shouts instead of hiding the
        // problem.
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("towerRange t:Texture2D"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var found = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (found == null) continue;

            DefaultRingSprite = found;
            Debug.LogError(
                "[TowerRangeIndicator] THE RING ARTWORK WILL NOT APPEAR IN A BUILD.\n" +
                $"It was found at '{path}' through AssetDatabase, which is editor-only, while " +
                $"Resources.Load found nothing at 'Resources/{ringSpriteResourcePath}'.\n" +
                "Put the PNG at Assets/Resources/Sprites/towerRange.png (Texture Type = " +
                "'Sprite (2D and UI)'), or set Ring Sprite Resource Path to wherever under " +
                "Resources it actually lives. Until then a player build falls back to dots.");
            return DefaultRingSprite;
        }
#endif

        Debug.LogError("[TowerRangeIndicator] Ring artwork not found — falling back to dots.\n" +
                       $"Looked for Resources/{ringSpriteResourcePath} (i.e. " +
                       $"Assets/Resources/{ringSpriteResourcePath}.png).\n" +
                       "Check that the file is there, that its Texture Type is " +
                       "'Sprite (2D and UI)', and that Ring Sprite Resource Path has no file " +
                       "extension.");
        return null;
    }

    /// Load into DefaultRingSprite if it is still empty and the path resolves.
    /// A Texture2D imported as anything other than "Sprite (2D and UI)" returns null here
    /// even when the file is in the right folder, so a wrong import mode looks identical
    /// to a missing file.
    private static void TryLoadFromResources(string path)
    {
        if (DefaultRingSprite != null || string.IsNullOrWhiteSpace(path)) return;
        DefaultRingSprite = Resources.Load<Sprite>(path);
    }

    /// Turn every ring on the board on or off (settings menu, cutscenes, photo mode).
    public static void SetAllVisible(bool on)
    {
        GlobalVisible = on;
        var towers = Tower.ActiveTowers;
        if (towers == null) return;
        for (int i = 0; i < towers.Count; i++)
        {
            if (towers[i] == null) continue;
            var ind = towers[i].GetComponent<TowerRangeIndicator>();
            if (ind != null) ind.ApplyAlphaImmediate();
        }
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private struct RingSpec
    {
        public float radius;
        public RingRole role;
        public RingSpec(float r, RingRole role) { radius = r; this.role = role; }
    }

    private class Ring
    {
        public GameObject go;
        public RingRole role;

        // radius       = what the mesh/line geometry was actually built at
        // targetRadius = what the tower currently reports
        // displayRadius= what is on screen right now, easing toward target
        public float radius;
        public float targetRadius;
        public float displayRadius;

        // Dots mode
        public MeshRenderer coreMesh, haloMesh;
        public Mesh coreMeshData, haloMeshData;

        // SolidLine mode
        public LineRenderer coreLine, haloLine;

        // SpriteImage mode
        public SpriteRenderer coreSprite, shadowSprite;
    }

    private Tower tower;
    private Transform container;              // scale-compensating parent for the rings
    private readonly List<Ring> rings = new List<Ring>();
    private readonly List<RingSpec> specs = new List<RingSpec>();
    private readonly List<RingSpec> scratch = new List<RingSpec>();

    private RingStyle builtStyle;
    private float nextRefresh;
    private float currentAlpha;               // what is on the renderers right now
    private float appliedAlpha = -1f;
    private float largestRadius;
    private float lastCompensatedScale = -1f;

    private Transform cachedPlayer;           // single-player fallback
    private float nextPlayerLookup;

    private bool onBrightBiome;
    private bool appliedBrightBiome;

    // Previous telemetry sample, for change markers.
    private float tlmProjRange = float.NaN;
    private float tlmCollider = float.NaN;
    private float tlmTetherMult = float.NaN;
    private float tlmTarget = float.NaN;

    private static BiomeManager _biome;
    private static float _nextBiomeLookup;
    private static string _cachedBiomeName = "";

    /// Current biome name, refreshed about once a second (biomes change per stage).
    private static string CurrentBiomeName()
    {
        if (Time.unscaledTime < _nextBiomeLookup && _biome != null) return _cachedBiomeName;
        _nextBiomeLookup = Time.unscaledTime + 1f;

        if (_biome == null) _biome = FindFirstObjectByType<BiomeManager>();
        _cachedBiomeName = _biome != null ? _biome.activeBiome.ToString() : "";
        return _cachedBiomeName;
    }

    private bool ResolveBrightBiome()
    {
        if (contrastOverride == ContrastMode.ForceBrightBackground) return true;
        if (contrastOverride == ContrastMode.ForceDarkBackground) return false;
        if (!autoContrastToBiome || brightBiomes == null) return false;

        string name = CurrentBiomeName();
        if (string.IsNullOrEmpty(name)) return false;

        for (int i = 0; i < brightBiomes.Length; i++)
        {
            if (string.IsNullOrEmpty(brightBiomes[i])) continue;
            if (name.IndexOf(brightBiomes[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    void Awake()
    {
        tower = GetComponent<Tower>();
        if (tower == null)
        {
            Debug.LogWarning($"[TowerRangeIndicator] '{name}' has no Tower component — disabling.");
            enabled = false;
            return;
        }

        // Built on the first refresh instead of here: Tower.ProjectileRange and the
        // final transform scale are both assigned in Tower.Start/SetupTower, which may
        // run after this Awake. Refreshing lazily keeps us order-independent.
        currentAlpha = 0f;
        nextRefresh = 0f;
        builtStyle = style;
    }

    void OnDestroy()
    {
        DestroyRings();
        if (container != null) Destroy(container.gameObject);
    }

    void LateUpdate()
    {
        if (tower == null) return;

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh();
        }

        // A biome change swaps the palette, but alpha may be steady, and ApplyAlpha only
        // runs when alpha moves — so force a colour write on the flip.
        onBrightBiome = ResolveBrightBiome();
        if (onBrightBiome != appliedBrightBiome) appliedAlpha = -1f;

        if (rings.Count == 0) return;

        CompensateScale();
        UpdateRadii();
        UpdateDrift();
        UpdateSorting();
        UpdateAlpha();
    }

    // ── Radius resolution ───────────────────────────────────────────────────

    /// Reads the tower's CURRENT radii. Called on the refresh tick so augments,
    /// upgrades, tether range buffs and the laser's Start() overrides all show up
    /// on their own.
    private void ResolveSpecs(List<RingSpec> into)
    {
        into.Clear();
        if (tower == null) return;

        if (tower.IsGenerator())
        {
            // Was the raw generationRange, so the ring never showed the tether's FAR
            // buff. EffectiveGenerationRange = live generationRange x tether multiplier.
            // Draw the radius GeneratorEnergyNetwork actually links with (it applies
            // linkRangeOverride / linkRangeBonus and the tether multiplier), so the
            // ring and the visible links can never disagree.
            bool includeTether = radiusSource != RadiusSource.BaseIgnoringTetherBuff;
            var net = GeneratorEnergyNetwork.Instance;
            float gen = net != null && net.enableNetwork
                ? net.LinkRangeFor(tower, includeTether)
                : (includeTether ? tower.EffectiveGenerationRange : tower.generationRange);
            AddSpec(into, gen, RingRole.Energy);
            return;
        }

        if (tower.isHealTower || tower.towerType == Tower.TowerType.Heal)
        {
            AddSpec(into, tower.healRange, RingRole.Heal);
            return;
        }

        if (tower.isHammerTower || tower.towerType == Tower.TowerType.Hammer)
        {
            // The hammer has no projectile: hammerAOERadius IS its whole threat range.
            AddSpec(into, tower.hammerAOERadius, RingRole.Impact);
            return;
        }

        // ProjectileRange is what Tower.Attack actually compares against, and it is
        // NOT the inspector `range` field (SetupTower derives it as
        // max(range * 2, tentacleReach * 3.5, 6)). Drawing `range` here would show a
        // circle roughly half the tower's real reach.
        float outer = ResolveOuterRange();

        AddSpec(into, outer, RingRole.Attack);

        // Melee ring: mirror IsTargetInMeleeRange exactly.
        if (tower.useTentacleTurret && !tower.isLaserTower)
        {
            float melee = tower.tentacleConfig.length
                        + tower.tentacleConfig.attachmentOffset.magnitude
                        + 0.4f;

            // Skip it when the two rings would sit on top of each other — two rings
            // 0.1 units apart read as one smudge, not as two ranges.
            if (melee > 0.3f && melee < outer - MinRingSeparation())
                AddSpec(into, melee, RingRole.Melee);
        }
    }

    /// The outer radius to draw. ProjectileRange is what Tower.Attack compares against,
    /// so it is the honest number — but it INCLUDES the tether's FAR-zone buff, which
    /// changes as the player walks. BaseIgnoringTetherBuff divides that back out.
    private float ResolveOuterRange()
    {
        float outer = tower.ProjectileRange;
        if (outer <= 0.01f)
            outer = tower.isLaserTower ? tower.laserMaxLength : Mathf.Max(tower.range * 2f, 6f);

        var boost = tower.GetComponent<TowerTetherBoost>();
        if (boost != null && boost.RangeContributorCount > 0)
        {
            float b = boost.BaseProjectileRange;

            if (radiusSource == RadiusSource.BaseIgnoringTetherBuff)
                return b > 0.01f ? b : outer / Mathf.Max(0.0001f, boost.RangeMultiplier);

            // Read the INTENDED value (base x multiplier) rather than whatever is on the
            // tower this instant.
            //
            // Something in the project still re-derives ProjectileRange periodically —
            // harmless since the setter fix (it now recomputes the correct 10.0 instead
            // of collapsing to 5.0), but it briefly stamps the buff off. The boost puts it
            // back in its own LateUpdate, so the tower is right by the time anything
            // gameplay-relevant reads it. The ring, though, samples on a timer and would
            // catch that one-frame gap and animate a full 14 -> 10 -> 14 dip. Reading the
            // intended value makes the ring immune to the gap.
            if (b > 0.01f) return b * boost.RangeMultiplier;
        }
        return outer;
    }

    private float MinRingSeparation()
    {
        switch (builtStyle)
        {
            // The artwork's dashes are thick, so two sprite rings need real daylight
            // between them or they read as one fat band.
            case RingStyle.SpriteImage: return 1.2f;
            case RingStyle.SolidLine: return lineWidth * haloWidthMultiplier * 2f;
            default: return dotSize * haloSizeMultiplier * 2f;
        }
    }

    private static void AddSpec(List<RingSpec> into, float radius, RingRole role)
    {
        if (radius > 0.05f && !float.IsNaN(radius) && !float.IsInfinity(radius))
            into.Add(new RingSpec(radius, role));
    }

    // ── Build / refresh ─────────────────────────────────────────────────────

    [ContextMenu("Rebuild Rings Now")]
    public void Rebuild()
    {
        DestroyRings();
        specs.Clear();
        Refresh();
    }

    private void Refresh()
    {
        ResolveSpecs(scratch);

        bool changed = scratch.Count != specs.Count || style != builtStyle;
        if (!changed)
        {
            for (int i = 0; i < scratch.Count; i++)
            {
                if (scratch[i].role != specs[i].role ||
                    Mathf.Abs(scratch[i].radius - specs[i].radius) > 0.01f)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            SyncTargetRadii();
            return;
        }

        // Same rings, different radii: retarget instead of rebuilding, and only rebuild
        // the geometry once it has drifted far enough that scaling would distort the
        // dots. This is what stops the ring popping every time a tether forms.
        bool sameShape = scratch.Count == specs.Count && style == builtStyle && rings.Count == scratch.Count;
        if (sameShape)
        {
            for (int i = 0; i < scratch.Count && sameShape; i++)
                if (scratch[i].role != specs[i].role) sameShape = false;
        }

        specs.Clear();
        specs.AddRange(scratch);

        if (sameShape)
        {
            SyncTargetRadii();
            RebuildDriftedRings();
            return;
        }

        builtStyle = style;
        BuildRings();

        if (verboseLogging)
        {
            var msg = $"[TowerRangeIndicator] '{tower.towerName}' rings:";
            for (int i = 0; i < specs.Count; i++) msg += $" {specs[i].role}={specs[i].radius:F2}";
            Debug.Log(msg);
        }
    }

    /// Point each ring at the radius the tower currently reports.
    private void SyncTargetRadii()
    {
        int n = Mathf.Min(rings.Count, specs.Count);
        for (int i = 0; i < n; i++)
        {
            float was = rings[i].targetRadius;
            float now = specs[i].radius;
            if (was > 0.01f && Mathf.Abs(now - was) > 0.01f)
                LogRadiusChange(rings[i].role, was, now);

            rings[i].targetRadius = now;
            if (rings[i].displayRadius <= 0f) rings[i].displayRadius = specs[i].radius;
            if (rings[i].radius <= 0f) rings[i].radius = specs[i].radius;
        }
        largestRadius = 0f;
        for (int i = 0; i < rings.Count; i++)
            if (rings[i].targetRadius > largestRadius) largestRadius = rings[i].targetRadius;
    }

    /// Rebuild just the rings whose on-screen size has drifted far enough from their
    /// built geometry that the scale trick would visibly fatten or thin the dots.
    private void RebuildDriftedRings()
    {
        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            if (r.radius <= 0.0001f) continue;

            float drift = Mathf.Abs(r.targetRadius / r.radius - 1f);
            if (drift < rebuildDriftThreshold) continue;

            var spec = new RingSpec(r.targetRadius, r.role);
            float display = r.displayRadius;

            if (r.coreMeshData != null) Destroy(r.coreMeshData);
            if (r.haloMeshData != null) Destroy(r.haloMeshData);
            if (r.go != null) Destroy(r.go);

            Ring fresh = CreateRing(spec);
            if (fresh == null) continue;

            fresh.targetRadius = spec.radius;
            fresh.displayRadius = display > 0f ? display : spec.radius;  // keep the eased size
            rings[i] = fresh;
            appliedAlpha = -1f;   // the new renderers have no colour yet
        }
    }

    /// Ease each ring's on-screen radius toward its target by scaling the built
    /// geometry. Cheaper than rebuilding a mesh and it animates for free.
    private void UpdateRadii()
    {
        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            if (r.go == null || r.radius <= 0.0001f) continue;
            if (r.targetRadius <= 0f) r.targetRadius = r.radius;
            if (r.displayRadius <= 0f) r.displayRadius = r.targetRadius;

            if (!Mathf.Approximately(r.displayRadius, r.targetRadius))
            {
                r.displayRadius = radiusChangeSpeed <= 0f
                    ? r.targetRadius
                    : Mathf.MoveTowards(r.displayRadius, r.targetRadius,
                                        radiusChangeSpeed * Time.unscaledDeltaTime);
            }

            float k = r.displayRadius / r.radius;
            var ls = r.go.transform.localScale;
            if (!Mathf.Approximately(ls.x, k)) r.go.transform.localScale = new Vector3(k, k, 1f);
        }
    }

    private void EnsureContainer()
    {
        if (container != null) return;

        var go = new GameObject("RangeRings");
        container = go.transform;
        container.SetParent(transform, false);
        container.localPosition = Vector3.zero;
        container.localRotation = Quaternion.identity;
        lastCompensatedScale = -1f;   // force the first compensation
    }

    private void BuildRings()
    {
        DestroyRings();
        if (specs.Count == 0) return;

        EnsureContainer();
        largestRadius = 0f;

        for (int i = 0; i < specs.Count; i++)
        {
            Ring ring = CreateRing(specs[i]);

            if (ring != null)
            {
                ring.targetRadius = ring.radius;
                ring.displayRadius = ring.radius;
                rings.Add(ring);
                if (ring.radius > largestRadius) largestRadius = ring.radius;
            }
        }

        appliedAlpha = -1f;   // force a colour write on the next LateUpdate
    }

    /// THE ONLY place a RingStyle is turned into geometry.
    ///
    /// This exists because RebuildDriftedRings had its own copy of the dispatch —
    /// `builtStyle == Dots ? CreateDottedRing : CreateSolidRing` — written before the
    /// SpriteImage style existed. So SpriteImage fell into the else branch: any ring that
    /// drifted more than rebuildDriftThreshold (the tether's FAR buff moves a ring 10 ->
    /// 14, a 40% drift) was silently rebuilt as a pale SolidLine. That is the grey line
    /// appearing while you walk around with two towers up.
    private Ring CreateRing(RingSpec spec)
    {
        switch (builtStyle)
        {
            case RingStyle.SpriteImage: return CreateSpriteRing(spec);
            case RingStyle.SolidLine: return CreateSolidRing(spec);
            default: return CreateDottedRing(spec);
        }
    }

    // ── Dotted rings ────────────────────────────────────────────────────────

    private Ring CreateDottedRing(RingSpec spec)
    {
        var go = NewRingRoot(spec);

        // Dot COUNT is derived from circumference / spacing, so spacing stays constant
        // in world units no matter how big the ring is.
        int count = Mathf.Clamp(
            Mathf.RoundToInt(2f * Mathf.PI * spec.radius / Mathf.Max(0.05f, dotSpacing)),
            8, Mathf.Max(8, maxDotsPerRing));

        var ring = new Ring { go = go, role = spec.role, radius = spec.radius };

        ring.haloMeshData = BuildDotRingMesh(spec.radius, dotSize * haloSizeMultiplier, count);
        ring.haloMesh = MakeMeshRenderer(go.transform, "Halo", ring.haloMeshData);

        ring.coreMeshData = BuildDotRingMesh(spec.radius, dotSize, count);
        ring.coreMesh = MakeMeshRenderer(go.transform, "Core", ring.coreMeshData);

        return ring;
    }

    /// One mesh for the whole ring — `count` quads in the XY plane, 4 verts each.
    /// One GameObject per dot would be hundreds of transforms per tower; this is two
    /// draw calls per ring instead.
    private static Mesh BuildDotRingMesh(float radius, float size, int count)
    {
        var verts = new Vector3[count * 4];
        var uvs = new Vector2[count * 4];
        var cols = new Color32[count * 4];   // white: the tint comes from the property block
        var tris = new int[count * 6];

        float h = size * 0.5f;
        var white = new Color32(255, 255, 255, 255);

        for (int i = 0; i < count; i++)
        {
            float a = (float)i / count * Mathf.PI * 2f;
            float cx = Mathf.Cos(a) * radius;
            float cy = Mathf.Sin(a) * radius;

            int v = i * 4;
            verts[v + 0] = new Vector3(cx - h, cy - h, 0f);
            verts[v + 1] = new Vector3(cx + h, cy - h, 0f);
            verts[v + 2] = new Vector3(cx + h, cy + h, 0f);
            verts[v + 3] = new Vector3(cx - h, cy + h, 0f);

            uvs[v + 0] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(1f, 0f);
            uvs[v + 2] = new Vector2(1f, 1f);
            uvs[v + 3] = new Vector2(0f, 1f);

            cols[v + 0] = cols[v + 1] = cols[v + 2] = cols[v + 3] = white;

            int t = i * 6;
            tris[t + 0] = v + 0; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 0; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }

        var mesh = new Mesh { name = $"DotRing_{count}" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors32 = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private MeshRenderer MakeMeshRenderer(Transform parent, string label, Mesh mesh)
    {
        var go = NewChild(parent, label);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SharedDotMaterial;   // one material for every ring in the scene
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.sortingLayerName = sortingLayerName;
        return mr;
    }

    // ── Sprite artwork rings ────────────────────────────────────────────────

    private Ring CreateSpriteRing(RingSpec spec)
    {
        Sprite spr = ResolveRingSprite();
        if (spr == null)
        {
            // No artwork: fall back rather than leaving the player with no range at all.
            // builtStyle is deliberately NOT changed to Dots here — Refresh() compares
            // style against builtStyle to decide whether to rebuild, so flipping it would
            // make them disagree forever and rebuild every ring on every refresh tick.
            return CreateDottedRing(spec);
        }

        var go = NewRingRoot(spec);
        var ring = new Ring { go = go, role = spec.role, radius = spec.radius };

        float rot = spriteRotationDegrees + (spec.role == RingRole.Melee ? innerRingExtraRotation : 0f);

        // Scale so the ARTWORK'S ring lands on the tower's real range. The sprite's world
        // width is bounds.size.x (pixels / PPU), and the drawn ring occupies
        // spriteRingRadiusFraction of it — so the sprite has to be blown up past the
        // target diameter by exactly that factor.
        float spriteWorldWidth = Mathf.Max(0.0001f, spr.bounds.size.x);
        float wantedWidth = (spec.radius * 2f) / Mathf.Clamp(spriteRingRadiusFraction, 0.05f, 1f);
        float k = wantedWidth / spriteWorldWidth;

        if (spriteShadow)
        {
            ring.shadowSprite = MakeSprite(go.transform, "Shadow", spr,
                                           k * Mathf.Max(1f, spriteShadowScale), rot);
        }

        ring.coreSprite = MakeSprite(go.transform, "Art", spr, k, rot);
        return ring;
    }

    private SpriteRenderer MakeSprite(Transform parent, string label, Sprite spr, float scale, float rotation)
    {
        var go = NewChild(parent, label);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spr;
        sr.sortingLayerName = sortingLayerName;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    // ── Solid rings (legacy style) ──────────────────────────────────────────

    private Ring CreateSolidRing(RingSpec spec)
    {
        var go = NewRingRoot(spec);

        int segments = Mathf.Clamp(
            Mathf.RoundToInt(spec.radius * 12f),
            Mathf.Min(minSegments, maxSegments),
            Mathf.Max(minSegments, maxSegments));

        var points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            points[i] = new Vector3(Mathf.Cos(a) * spec.radius, Mathf.Sin(a) * spec.radius, 0f);
        }

        return new Ring
        {
            go = go,
            role = spec.role,
            radius = spec.radius,
            haloLine = MakeLine(go.transform, "Halo", points, lineWidth * haloWidthMultiplier),
            coreLine = MakeLine(go.transform, "Core", points, lineWidth),
        };
    }

    private LineRenderer MakeLine(Transform parent, string label, Vector3[] points, float width)
    {
        var go = NewChild(parent, label);

        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = SharedLineMaterial;
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.alignment = LineAlignment.TransformZ; // flat on the ground plane, not camera-facing
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lr.sortingLayerName = sortingLayerName;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        return lr;
    }

    // ── Shared object plumbing ──────────────────────────────────────────────

    private GameObject NewRingRoot(RingSpec spec)
    {
        EnsureContainer();
        return NewChild(container, $"Ring_{spec.role}_{spec.radius:F1}");
    }

    private static GameObject NewChild(Transform parent, string label)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    private void DestroyRings()
    {
        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            if (r == null) continue;

            // Meshes aren't owned by the GameObject — destroying the object leaks them.
            if (r.coreMeshData != null) Destroy(r.coreMeshData);
            if (r.haloMeshData != null) Destroy(r.haloMeshData);
            if (r.go != null) Destroy(r.go);
        }
        rings.Clear();
        largestRadius = 0f;
    }

    // ── Per-frame upkeep ────────────────────────────────────────────────────

    /// Ring radii are WORLD units but the container inherits the tower's scale
    /// (BasicTower authors 0.25, the Hammer animator writes 0.25 too). Undo it, and
    /// re-check occasionally because the scale is assigned during Tower.Start.
    private void CompensateScale()
    {
        if (container == null) return;

        Vector3 ls = transform.lossyScale;
        float s = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
        if (s < 0.0001f || float.IsNaN(s) || float.IsInfinity(s)) s = 1f;

        if (Mathf.Approximately(s, lastCompensatedScale)) return;
        lastCompensatedScale = s;
        container.localScale = Vector3.one * (1f / s);
    }

    private void UpdateDrift()
    {
        if (container == null || Mathf.Approximately(dotDriftDegreesPerSecond, 0f)) return;
        container.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Repeat(Time.time * dotDriftDegreesPerSecond, 360f));
    }

    private void UpdateSorting()
    {
        int order;
        if (useYSort)
        {
            // Same ground-decal maths as TowerSlot.LateUpdate: a positive Y offset
            // pushes the sort point up so grass rooted below the tower draws in front
            // of the ring, and the ring always lands behind the tower body (which
            // y-sorts with a -0.5 offset).
            float sortY = transform.position.y + sortYOffset;
            order = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);
        }
        else
        {
            order = fixedSortingOrder;
        }

        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            if (r.haloMesh != null) r.haloMesh.sortingOrder = order;
            if (r.coreMesh != null) r.coreMesh.sortingOrder = order + 1;
            if (r.haloLine != null) r.haloLine.sortingOrder = order;
            if (r.coreLine != null) r.coreLine.sortingOrder = order + 1;
            if (r.shadowSprite != null) r.shadowSprite.sortingOrder = order;
            if (r.coreSprite != null) r.coreSprite.sortingOrder = order + 1;
        }
    }

    private float TargetAlpha()
    {
        if (tower == null || !visible || !GlobalVisible) return 0f;

        float a = idleOpacity;

        if (brightenWhenPlayerNear)
        {
            Transform p = ResolveNearestPlayer();
            if (p != null)
            {
                // Measured against the outer ring, not the tower centre, so a big
                // laser tower doesn't need the player standing on its base to light up.
                float d = Vector2.Distance(p.position, transform.position) - largestRadius;
                if (d <= playerNearDistance)
                {
                    float t = playerNearDistance <= 0.001f
                        ? 1f
                        : Mathf.Clamp01(1f - Mathf.Max(0f, d) / playerNearDistance);
                    a = Mathf.Lerp(idleOpacity, nearOpacity, t);
                }
            }
        }

        if (!tower.IsOperational()) a *= offlineOpacityScale;
        return Mathf.Clamp01(a);
    }

    private void UpdateAlpha()
    {
        float target = TargetAlpha();
        currentAlpha = Mathf.MoveTowards(currentAlpha, target, fadeSpeed * Time.unscaledDeltaTime);

        // Writing property blocks every frame for nothing is the kind of thing that
        // shows up at 40 towers, so only push when it actually moved.
        if (Mathf.Abs(currentAlpha - appliedAlpha) < 0.004f) return;
        ApplyAlpha(currentAlpha);
    }

    private void ApplyAlphaImmediate()
    {
        currentAlpha = TargetAlpha();
        ApplyAlpha(currentAlpha);
    }

    private void ApplyAlpha(float a)
    {
        appliedAlpha = a;
        appliedBrightBiome = onBrightBiome;
        bool on = a > 0.002f;

        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];

            Color core = CoreColorFor(r.role); core.a = a;
            Color halo = onBrightBiome ? brightBiomeHaloColor : haloColor;
            halo.a = a * haloStrength;

            if (r.coreMesh != null) SetMeshColor(r.coreMesh, core, on);
            if (r.haloMesh != null) SetMeshColor(r.haloMesh, halo, on);

            if (r.coreLine != null)
            {
                r.coreLine.enabled = on;
                if (on) { r.coreLine.startColor = core; r.coreLine.endColor = core; }
            }
            if (r.haloLine != null)
            {
                r.haloLine.enabled = on;
                if (on) { r.haloLine.startColor = halo; r.haloLine.endColor = halo; }
            }

            if (r.coreSprite != null)
            {
                r.coreSprite.enabled = on;
                if (on)
                {
                    // Untinted by default: the artwork already has its own colour, and
                    // multiplying a coloured sprite by a coloured tint just muddies both.
                    Color c = tintSpriteWithRoleColor ? core : Color.white;
                    c.a = Mathf.Clamp01(a * Mathf.Max(1f, spriteOpacityScale));
                    r.coreSprite.color = c;
                }
            }

            if (r.shadowSprite != null)
            {
                r.shadowSprite.enabled = on;
                if (on)
                {
                    Color sh = halo;
                    sh.a = Mathf.Clamp01(halo.a * Mathf.Max(1f, spriteOpacityScale));
                    r.shadowSprite.color = sh;
                }
            }
        }
    }

    private static void SetMeshColor(MeshRenderer mr, Color c, bool on)
    {
        mr.enabled = on;
        if (!on) return;

        // Property block, not a material instance — otherwise every ring would
        // allocate its own material and break batching.
        var mpb = Mpb;
        mpb.Clear();
        mpb.SetColor("_Color", c);
        mr.SetPropertyBlock(mpb);
    }

    /// The role colour, darkened on bright biomes so the dot reads as a dark speck on
    /// snow or sand instead of white-on-white.
    private Color CoreColorFor(RingRole role)
    {
        Color c = ColorFor(role);
        if (!onBrightBiome) return c;

        Color dark = Color.Lerp(c, Color.black, brightBiomeCoreDarkening);
        dark.a = c.a;
        return dark;
    }

    private Color ColorFor(RingRole role)
    {
        switch (role)
        {
            case RingRole.Melee: return meleeColor;
            case RingRole.Heal: return healColor;
            case RingRole.Energy: return energyColor;
            case RingRole.Impact: return impactColor;
            default: return attackColor;
        }
    }

    /// Nearest living player, co-op aware. Same resolution order TowerSlot uses.
    private Transform ResolveNearestPlayer()
    {
        if (Time.unscaledTime < nextPlayerLookup && cachedPlayer != null) return cachedPlayer;
        nextPlayerLookup = Time.unscaledTime + 0.2f;

        if (PlayerRegistry.Count > 0 && PlayerRegistry.Instance != null)
        {
            PlayerStats near = PlayerRegistry.Instance.NearestAlive(transform.position, Mathf.Infinity, true);
            cachedPlayer = near != null ? near.transform : null;
            return cachedPlayer;
        }

        if (cachedPlayer == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            cachedPlayer = p != null ? p.transform : null;
        }
        return cachedPlayer;
    }

    // ── Diagnostics ─────────────────────────────────────────────────────────
    //
    // If the rings are invisible, run one of these before changing anything else.
    // They separate the three failure modes: not attached, attached but no rings
    // built, or rings built but not reaching the screen.

    /// Print the state of every indicator on the board. Call from a debug key:
    ///     if (Input.GetKeyDown(KeyCode.F9)) TowerRangeIndicator.LogAll();
    public static void LogAll()
    {
        var towers = Tower.ActiveTowers;
        if (towers == null || towers.Count == 0)
        {
            Debug.LogWarning("[TowerRangeIndicator] Tower.ActiveTowers is empty — no towers " +
                             "are registered, so nothing can have rings. Place a tower first.");
            return;
        }

        Debug.Log($"[TowerRangeIndicator] GlobalVisible={GlobalVisible} AutoAttach={AutoAttach} " +
                  $"towers={towers.Count}");

        for (int i = 0; i < towers.Count; i++)
        {
            var t = towers[i];
            if (t == null) continue;
            var ind = t.GetComponent<TowerRangeIndicator>();
            if (ind == null)
            {
                Debug.LogWarning($"[TowerRangeIndicator] '{t.towerName}' has NO indicator " +
                                 "component — the auto-attacher never reached it.");
                continue;
            }
            ind.LogSelf();
        }
    }

    /// One line per radius change, naming the cause. This is the log to copy out of the
    /// console when a ring moves and you want to know why.
    private void LogRadiusChange(RingRole role, float from, float to)
    {
        if (!logRadiusChanges && !LogRadiusChanges) return;
        if (tower == null) return;

        float pct = from > 0.0001f ? (to / from - 1f) * 100f : 0f;

        var sb = new System.Text.StringBuilder();
        sb.Append($"[RangeRing] t={Time.time:F2} '{tower.towerName}' {role} ");
        sb.Append($"{from:F2} -> {to:F2} ({(pct >= 0 ? "+" : "")}{pct:F1}%)");
        sb.Append($" | ProjectileRange={tower.ProjectileRange:F2}");
        sb.Append($" colliderReach={tower.RangeColliderWorldRadius:F2}");
        sb.Append($" | source={radiusSource}");
        sb.Append($" | CAUSE: {DescribeRadiusCause()}");

        Debug.Log(sb.ToString());
    }

    /// Best available explanation for the tower's current range, checked against the
    /// systems that are actually allowed to move it.
    private string DescribeRadiusCause()
    {
        var boost = tower.GetComponent<TowerTetherBoost>();
        if (boost != null && boost.RangeContributorCount > 0)
        {
            return $"PlayerTowerTether FAR-zone range buff x{boost.RangeMultiplier:F3} " +
                   $"from {boost.RangeContributorCount} tether(s), base {boost.BaseProjectileRange:F2}. " +
                   "This is the buff working as designed — walking into the FAR band raises " +
                   "the tower's range, walking closer drops it back. Set radiusSource = " +
                   "BaseIgnoringTetherBuff to hold the ring still.";
        }

        if (boost != null)
            return $"a TowerTetherBoost exists but holds no range contribution " +
                   $"(damage x{boost.DamageMultiplier:F3}) — the change came from elsewhere: " +
                   "an augment, an upgrade, or anything writing Tower.range.";

        return "no tether boost on this tower — the change came from Tower itself " +
               "(SetupTower/SetupTower2, the Tower.range setter, an augment, or an upgrade).";
    }

    /// One line per tower for the live telemetry block. Everything needed to decide WHY
    /// a ring moved: the tower's own numbers, the tether multiplier acting on it, the
    /// ring's built/target/displayed radii, and a delta marker on anything that changed
    /// since the previous sample.
    internal void AppendTelemetry(System.Text.StringBuilder sb, Vector2 playerPos, PlayerTowerTether tether)
    {
        if (tower == null) return;

        float proj = tower.ProjectileRange;
        float coll = tower.RangeColliderWorldRadius;
        float dist = Vector2.Distance(playerPos, (Vector2)transform.position);

        var boost = tower.GetComponent<TowerTetherBoost>();
        float mult = boost != null ? boost.RangeMultiplier : 1f;
        float baseR = boost != null ? boost.BaseProjectileRange : -1f;
        int contrib = boost != null ? boost.RangeContributorCount : 0;

        string zone = "-";
        if (tether != null && tether.TryGetTetherState(tower, out string z, out float _, out float _)) zone = z;

        Ring outer = null;
        for (int i = 0; i < rings.Count; i++)
            if (outer == null || rings[i].targetRadius > outer.targetRadius) outer = rings[i];

        float target = outer != null ? outer.targetRadius : -1f;
        float display = outer != null ? outer.displayRadius : -1f;
        float built = outer != null ? outer.radius : -1f;

        sb.Append($"\n  '{tower.towerName}' dist={dist:F2} zone={zone}");
        sb.Append($" | proj={proj:F3}{Delta(proj, tlmProjRange)}");
        sb.Append($" coll={coll:F3}{Delta(coll, tlmCollider)}");
        sb.Append($" range={tower.range:F2}");
        sb.Append($" | tetherX={mult:F4}{Delta(mult, tlmTetherMult)} contrib={contrib} base={baseR:F3}");
        sb.Append($" | ring target={target:F3}{Delta(target, tlmTarget)} shown={display:F3} built={built:F3}");
        sb.Append($" rings={rings.Count} alpha={currentAlpha:F2} src={radiusSource}");

        // Everything needed to explain "I can't see the rings" without another round trip.
        Renderer probe = null;
        if (outer != null)
            probe = outer.coreMesh != null ? (Renderer)outer.coreMesh
                  : outer.coreSprite != null ? (Renderer)outer.coreSprite
                  : (outer.coreLine != null ? outer.coreLine : null);

        if (probe != null)
        {
            Color c = CoreColorFor(outer.role);
            sb.Append($"\n      render: bright={onBrightBiome} enabled={probe.enabled} " +
                      $"onScreen={probe.isVisible} order={probe.sortingOrder} " +
                      $"layer='{probe.sortingLayerName}' " +
                      $"core=({c.r:F2},{c.g:F2},{c.b:F2}) dot={dotSize:F2} halo={dotSize * haloSizeMultiplier:F2}");
        }
        else
        {
            sb.Append("\n      render: NO RENDERER on the outer ring");
        }

        // The whole point of the block: separate "the tether moved it" from "something
        // else moved it". Tower.range's setter, SetupTower2 and any augment all write
        // ProjectileRange without going through the boost.
        bool projMoved = !float.IsNaN(tlmProjRange) && Mathf.Abs(proj - tlmProjRange) > 0.005f;
        bool multMoved = !float.IsNaN(tlmTetherMult) && Mathf.Abs(mult - tlmTetherMult) > 0.0005f;

        // What the boost INTENDS the tower to have right now. If this disagrees with the
        // live value, something overwrote it between the boost's write and this sample.
        if (contrib > 0 && baseR > 0.01f)
        {
            float intended = baseR * mult;
            sb.Append($"\n      intended={intended:F3}");
            if (Mathf.Abs(intended - proj) > 0.01f)
                sb.Append($" *** live value is {proj:F3} — something re-derived ProjectileRange " +
                          "this frame; the boost re-applies it in its own LateUpdate, and the " +
                          "ring reads 'intended' so it no longer dips.");
        }

        if (projMoved && multMoved)
            sb.Append("\n      -> CAUSE: tether FAR-zone range buff (expected: ring follows you as you walk)");
        else if (projMoved && !multMoved)
            sb.Append("\n      -> CAUSE: *** EXTERNAL WRITER *** ProjectileRange moved with NO tether " +
                      "multiplier change. Tower.range setter / SetupTower2 / an augment. NOT the tether.");
        else if (!projMoved && multMoved)
            sb.Append("\n      -> NOTE: tether multiplier moved but ProjectileRange did not. " +
                      "Usually means the captured base is stale: base x newMultiplier happens to " +
                      "equal the current value. Check 'base=' against what the tower should have.");

        // A tower whose ProjectileRange no longer matches what SetupTower would derive has
        // been written by something else — this is the check that caught Tower.range's setter.
        if (!tower.isEnergyGenerator && !tower.isHealTower && !tower.isHammerTower && !tower.isLaserTower)
        {
            float expected = tower.ComputeDerivedAttackRange();
            float unbuffed = mult > 0.0001f ? proj / mult : proj;
            if (Mathf.Abs(unbuffed - expected) > 0.05f)
                sb.Append($"\n      -> MISMATCH: unbuffed range is {unbuffed:F3} but SetupTower's " +
                          $"formula gives {expected:F3} for range={tower.range:F2}. Something wrote " +
                          "ProjectileRange outside SetupTower (see the Tower.range setter patch).");
        }

        tlmProjRange = proj;
        tlmCollider = coll;
        tlmTetherMult = mult;
        tlmTarget = target;
    }

    private static string Delta(float now, float before)
    {
        if (float.IsNaN(before)) return "";
        float d = now - before;
        return Mathf.Abs(d) < 0.005f ? "" : $"(Δ{(d >= 0 ? "+" : "")}{d:F3})";
    }

    [ContextMenu("Log Ring Diagnostics")]
    public void LogSelf()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[TowerRangeIndicator] '{(tower != null ? tower.towerName : name)}' ");
        sb.Append($"style={builtStyle} rings={rings.Count} alpha={currentAlpha:F3} ");
        sb.Append($"(target={TargetAlpha():F3}) visible={visible} global={GlobalVisible} ");
        sb.Append($"containerScale={(container != null ? container.localScale.x : -1f):F3} ");
        sb.Append($"towerScale={transform.lossyScale.x:F3} ");
        sb.Append($"order={(useYSort ? "ysort" : fixedSortingOrder.ToString())} ");
        sb.Append($"biome='{CurrentBiomeName()}' bright={onBrightBiome} source={radiusSource}");

        if (tower != null)
        {
            var boost = tower.GetComponent<TowerTetherBoost>();
            sb.Append($"\n  tower: ProjectileRange={tower.ProjectileRange:F2} " +
                      $"colliderReach={tower.RangeColliderWorldRadius:F2} " +
                      $"range={tower.range:F2} damage={tower.GetDamage():F1} " +
                      $"operational={tower.IsOperational()}");
            if (tower.IsGenerator())
                sb.Append($"\n  generator: generationRange={tower.generationRange:F2} " +
                          $"effective(with tether)={tower.EffectiveGenerationRange:F2}");
            sb.Append(boost != null
                ? $"\n  tetherBoost: range x{boost.RangeMultiplier:F3} ({boost.RangeContributorCount} contrib) " +
                  $"damage x{boost.DamageMultiplier:F3} ({boost.DamageContributorCount} contrib) " +
                  $"baseRange={boost.BaseProjectileRange:F2}"
                : "\n  tetherBoost: none (no tether is buffing this tower right now)");
        }

        if (rings.Count == 0)
        {
            sb.Append("\n  NO RINGS BUILT. ProjectileRange=");
            sb.Append(tower != null ? tower.ProjectileRange.ToString("F2") : "?");
            sb.Append(" — if that's 0, Tower.Start hasn't run yet or this tower type " +
                      "resolved no radius.");
        }

        for (int i = 0; i < rings.Count; i++)
        {
            var r = rings[i];
            // Unity's == is overloaded for destroyed objects but ?? is NOT, so a
            // destroyed MeshRenderer would be picked by ?? and then throw on access.
            Renderer core = r.coreMesh != null ? (Renderer)r.coreMesh
                          : r.coreSprite != null ? (Renderer)r.coreSprite
                          : (r.coreLine != null ? r.coreLine : null);
            sb.Append($"\n  {r.role} r={r.radius:F2} ");
            sb.Append($"style={builtStyle} ");
            sb.Append(core != null
                ? $"enabled={core.enabled} order={core.sortingOrder} layer='{core.sortingLayerName}' " +
                  $"visibleToCam={core.isVisible} mat={(core.sharedMaterial != null ? core.sharedMaterial.shader.name : "NULL")}"
                : "renderer=NULL");
        }

        Debug.Log(sb.ToString());
    }

    /// Last-resort visibility test: opaque, fat, and sorted above everything. If you
    /// still see nothing after this, the rings aren't being built at all (check the
    /// Hierarchy at runtime for a "RangeRings" child under the tower) — it isn't a
    /// sorting or alpha problem.
    [ContextMenu("TEST: Force Max Visibility")]
    public void ForceMaxVisibility()
    {
        visible = true;
        GlobalVisible = true;
        idleOpacity = 1f;
        nearOpacity = 1f;
        haloStrength = 1f;
        dotSize = 0.35f;
        dotSpacing = 0.7f;
        useYSort = false;
        fixedSortingOrder = 32000;   // same order the laser beam uses to beat the grass
        offlineOpacityScale = 1f;
        Rebuild();
        ApplyAlphaImmediate();
        LogSelf();
    }

    /// Apply ForceMaxVisibility to every tower on the board.
    public static void ForceMaxVisibilityAll()
    {
        var towers = Tower.ActiveTowers;
        if (towers == null) return;
        for (int i = 0; i < towers.Count; i++)
        {
            if (towers[i] == null) continue;
            var ind = towers[i].GetComponent<TowerRangeIndicator>();
            if (ind == null) ind = towers[i].gameObject.AddComponent<TowerRangeIndicator>();
            ind.ForceMaxVisibility();
        }
    }

#if UNITY_EDITOR
    /// Fixes the ring PNG's import settings. Menu: Tools > Tower Range > Fix Ring Sprite Import.
    ///
    /// THE BLUE/WHITE FRINGE AROUND THE RING comes from here, not from any code:
    ///
    ///  * Alpha Is Transparency OFF. A PNG's fully transparent pixels still carry a colour,
    ///    usually white. Bilinear filtering blends neighbouring TEXELS before the shader
    ///    ever sees them, so a sample straddling the edge mixes purple with that invisible
    ///    white and comes out as a pale halo. Turning this on makes Unity dilate the real
    ///    colour outward into the transparent region, so there is no white left to blend in.
    ///
    ///  * Mip maps ON. Each mip level averages 4 texels including the transparent white
    ///    ones, so the fringe gets worse the smaller the ring is drawn — which is exactly
    ///    when you see it, since a 1704px texture is drawn a couple hundred pixels wide.
    ///
    ///  * Compression. DXT/BC on a soft purple gradient produces blocky colour shifts that
    ///    read as blue blotches in the glow.
    ///
    /// The artwork does have a genuine soft glow of its own (its alpha extends about 7%
    /// past the solid dashes). If it is still stronger than you want after this, lower
    /// spriteOpacityScale — it multiplies the whole sprite's alpha, glow included.
    [UnityEditor.MenuItem("Tools/Tower Range/Fix Ring Sprite Import")]
    private static void FixRingSpriteImport()
    {
        const string path = "Assets/Resources/Sprites/towerRange.png";

        var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[TowerRangeIndicator] No texture at '{path}'. Update the literal " +
                           "in FixRingSpriteImport if the PNG lives somewhere else.");
            return;
        }

        importer.textureType = UnityEditor.TextureImporterType.Sprite;
        importer.spriteImportMode = UnityEditor.SpriteImportMode.Single;
        importer.alphaIsTransparency = true;                    // dilate colour into transparent pixels
        importer.mipmapEnabled = false;                         // no averaging-in of edge texels
        importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;               // no sampling across the edge
        importer.sRGBTexture = true;
        importer.maxTextureSize = 2048;                          // 1704px source, don't downsample
        importer.SaveAndReimport();

        // The sprite instance is rebuilt by the reimport, so drop the cached one.
        DefaultRingSprite = null;
        _ringSpriteLookupDone = false;

        Debug.Log($"[TowerRangeIndicator] Reimported '{path}': alphaIsTransparency on, mip maps " +
                  "off, uncompressed, clamp. Re-enter Play mode to see it.");
    }
#endif

#if UNITY_EDITOR
    /// Editor-only guard. The artwork-missing failure is invisible until someone runs a
    /// player build, so this reports it while the build is still running. Lives in this
    /// file rather than a separate editor script — UnityEditor types just have to be
    /// fenced out of player compilation, not put in their own file.
    private class BuildArtworkCheck : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            // Matches the default ringSpriteResourcePath. Kept as a literal because this
            // runs before any instance exists.
            const string expected = "Assets/Resources/Sprites/towerRange.png";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(expected) != null) return;

            Debug.LogError(
                "[TowerRangeIndicator] Building WITHOUT the tower range ring artwork.\n" +
                $"No sprite at '{expected}', so Resources.Load returns null in the player and " +
                "the rings fall back to dots.\n" +
                "Either put the PNG there with Texture Type = 'Sprite (2D and UI)', or update " +
                "the literal in BuildArtworkCheck if you moved it somewhere else under " +
                "Resources.");
        }
    }
#endif

    // ── Auto-attach ─────────────────────────────────────────────────────────
    //
    // Towers are spawned from several places (TowerSlot.PlaceTower, the upgrade
    // path, the save-restore path in TowerPlacementManager), so rather than edit
    // each one, a hidden runner polls the registry Tower already maintains.
    // Delete this whole region once every tower prefab carries the component.

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SpawnAttacher()
    {
        // AfterSceneLoad fires once per scene load; without the guard every load
        // would leave another DontDestroyOnLoad runner behind.
        if (!AutoAttach || _attacherSpawned) return;
        _attacherSpawned = true;

        var go = new GameObject("~TowerRangeIndicators") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<Attacher>();
        go.AddComponent<Telemetry>();
    }

    private class Attacher : MonoBehaviour
    {
        private float next;

        void Update()
        {
            HandleDebugKeys();

            if (!AutoAttach) return;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;

            var towers = Tower.ActiveTowers;
            if (towers == null) return;

            for (int i = 0; i < towers.Count; i++)
            {
                var t = towers[i];
                if (t == null) continue;
                if (t.GetComponent<TowerRangeIndicator>() == null)
                    t.gameObject.AddComponent<TowerRangeIndicator>();
            }
        }

        /// F9  = dump a full snapshot of every tower's rings to the console.
        /// F10 = toggle the per-change radius log on every tower.
        ///
        /// Uses Keyboard.current, NOT UnityEngine.Input: the project is on the Input
        /// System package, and legacy Input throws if Active Input Handling is set to
        /// "Input System Package (New)".
        void HandleDebugKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f9Key.wasPressedThisFrame) LogAll();

            if (kb.f10Key.wasPressedThisFrame)
            {
                LogRadiusChanges = !LogRadiusChanges;
                LiveLogging = LogRadiusChanges;
                Debug.Log($"[TowerRangeIndicator] Live logging {(LiveLogging ? "ON" : "OFF")}.");
            }
        }
    }

    /// Prints a telemetry block every LiveLogInterval seconds with no key press, so the
    /// console fills up on its own while you walk around. One block is self-contained:
    /// paste any run of them and the cause of a moving ring is readable from the deltas.
    private class Telemetry : MonoBehaviour
    {
        private float next;
        private PlayerTowerTether tether;
        private float nextTetherLookup;
        private int block;

        void Update()
        {
            if (!LiveLogging) return;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Mathf.Max(0.1f, LiveLogInterval);

            var towers = Tower.ActiveTowers;
            if (towers == null || towers.Count == 0) return;

            if (tether == null && Time.unscaledTime >= nextTetherLookup)
            {
                nextTetherLookup = Time.unscaledTime + 2f;
                tether = FindFirstObjectByType<PlayerTowerTether>();
            }

            Vector2 playerPos = tether != null
                ? (Vector2)tether.transform.position
                : ResolvePlayerPosition();

            var sb = new System.Text.StringBuilder();
            sb.Append($"[RangeLog #{++block}] t={Time.time:F2} player=({playerPos.x:F2},{playerPos.y:F2}) ");
            sb.Append($"biome='{CurrentBiomeName()}' towers={towers.Count}");
            if (tether != null) sb.Append($"\n  {tether.TelemetryLine()}");
            else sb.Append("\n  tether: NO PlayerTowerTether found in the scene");

            // Nearest few only, so a 30-tower board doesn't bury the console.
            _scratch.Clear();
            for (int i = 0; i < towers.Count; i++)
            {
                var t = towers[i];
                if (t == null) continue;
                var ind = t.GetComponent<TowerRangeIndicator>();
                if (ind == null) continue;
                _scratch.Add(ind);
            }
            _scratch.Sort((a, b) =>
                ((Vector2)a.transform.position - playerPos).sqrMagnitude
                .CompareTo(((Vector2)b.transform.position - playerPos).sqrMagnitude));

            int n = Mathf.Min(_scratch.Count, Mathf.Max(1, LiveLogNearestTowers));
            for (int i = 0; i < n; i++) _scratch[i].AppendTelemetry(sb, playerPos, tether);

            if (_scratch.Count == 0)
                sb.Append("\n  NO indicators attached to any tower (AutoAttach=" + AutoAttach + ")");

            Debug.Log(sb.ToString());
        }

        private readonly List<TowerRangeIndicator> _scratch = new List<TowerRangeIndicator>();

        private static Vector2 ResolvePlayerPosition()
        {
            if (PlayerRegistry.Count > 0 && PlayerRegistry.Instance != null)
            {
                var p = PlayerRegistry.Instance.NearestAlive(Vector3.zero, Mathf.Infinity, true);
                if (p != null) return p.transform.position;
            }
            var go = GameObject.FindGameObjectWithTag("Player");
            return go != null ? (Vector2)go.transform.position : Vector2.zero;
        }
    }
}



