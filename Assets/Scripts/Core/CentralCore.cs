using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CentralCore : MonoBehaviour, IEnergyConsumer, IDamageable
{
    [Header("Collision Settings")]
    public SpriteCollisionConfig collisionConfig = new SpriteCollisionConfig()
    {
        enableCollision = true,
        isTrigger = false,
        colliderType = SpriteCollisionConfig.ColliderType.Circle,
        //paddingPercent = 0.1f // 10% padding for Core
        paddingPercent = -0.2f // 10% padding for Core
    };
    private Collider2D spriteCollider;

    [Tooltip("Uniform shrink applied to whatever collider SpriteCollisionManager produces.\n" +
             "0.85 = 15% smaller. This is applied ON TOP of collisionConfig.paddingPercent and is\n" +
             "independent of how the manager interprets that padding, so it stays a true 15%.")]
    [Range(0.1f, 1f)]
    public float colliderSizeMultiplier = 0.85f;

    private bool colliderResized = false;

    #region Enemy Melee Reachability
    // WHY THIS EXISTS
    //
    // An enemy stops where its BODY touches the Core's collider, but
    // EnemyController tests attack range as
    //     Vector2.Distance(enemy.transform.position, target.position)
    // i.e. PIVOT to PIVOT, not surface to surface. For a small target (the
    // player) the two numbers are nearly the same and nobody notices. For a
    // building this big they differ by more than a whole melee reach:
    //
    //   sprite 2048px @ ppu 1200 * coreSize 2      = 3.41 world units
    //   bounds circle * colliderSizeMultiplier .85 = 1.45 radius
    //   Eye body radius (2.005 * prefab scale .25) = 0.50
    //   -> pivot-to-pivot at contact                = 1.88 .. 2.03
    //   Eye attackRange                             = 1.80   NEVER SATISFIED
    //
    // So the Eye walks up, is stopped by physics, and its range gate never
    // opens: attackTimer is reset to 0 every frame and it stands there. The
    // enemy is not broken and its range is not too short — the Core's physical
    // body is simply wider than the gap the range test allows.
    //
    // Fixing it here (rather than lengthening attackRange, which would desync
    // the attack animations) means guaranteeing an invariant the Core owns:
    //
    //   |colliderOffset| + colliderRadius + enemyBodyRadius + margin <= reach
    //
    // Anything that violates it gets clamped at startup, with a log line.

    [Tooltip("Keep the Core's collider small enough that an enemy touching it is still inside its " +
             "own attackRange. Off = raw sprite-bounds collider, and short-reach melee enemies " +
             "(the Eye, attackRange 1.8) will stall against the Core without ever attacking.")]
    public bool enforceMeleeReachability = true;

    [Tooltip("The SHORTEST attackRange of any melee enemy that targets the Core, in world units. " +
             "Read it off the enemy prefab's EnemyController. The Eye is 1.8. Lower this if you " +
             "add a shorter-reach enemy; the collider is sized against whatever is here.")]
    public float meleeReachBudget = 1.8f;

    [Tooltip("Body radius of the WIDEST melee enemy, in world units = its CircleCollider2D.radius " +
             "times the prefab's lossyScale. The Eye is 2.005 * 0.25 = 0.50. Round up — an enemy " +
             "fatter than this value will stop further out than the budget assumes.")]
    public float assumedEnemyBodyRadius = 0.55f;

    [Tooltip("Slack between the contact distance and meleeReachBudget. Without it an enemy that is " +
             "jostled by a neighbour or steering along the surface flickers in and out of range and " +
             "stutters its attack animation.")]
    public float reachSafetyMargin = 0.15f;

    [Tooltip("Size the collider from the mound footprint (groundFootprintFraction) instead of the " +
             "sprite bounds. The bounds circle is drawn around the CANOPY — 1.45 radius versus the " +
             "0.82 the tree actually stands on — so enemies are currently held off by empty air " +
             "above the base. This is the real fix; the clamp below is just the safety net.")]
    public bool useFootprintAsCollider = true;

    [Range(0f, 1f)]
    [Tooltip("How far to slide the collider down toward the Core's ground line. 0 = centred on the " +
             "pivot, 1 = centred on the ground line. Note that offsetting DOWN costs reach on the " +
             "far side: footprint on the ground line puts the south approach at 2.08 pivot-to-pivot, " +
             "back out of the Eye's 1.8. Whatever you set here is clamped to fit the budget.")]
    public float colliderGroundBias = 1f;

    [Tooltip("Log what the reachability pass decided at startup.")]
    public bool logReachabilityFix = true;
    #endregion

    #region Full Outline Collider
    // Traces the tree's own silhouette instead of putting a disc around its base, so enemies
    // are stopped by the trunk and roots rather than walking over them.
    //
    // WHY THE CANOPY IS CLIPPED OFF
    //
    // EnemyController measures attack range pivot-to-pivot. That puts a hard ceiling on how
    // big this collider can be: an Eye (body 0.50, collider offset 0.07, attackRange 1.80)
    // can only reach something whose furthest point is 1.23 from the pivot it aims at.
    // Measured off 00.png, the whole silhouette is 2.66 across, so the smallest circle that
    // could contain it has radius 1.33 - over budget by 0.10 no matter where the pivot sits
    // or what shape the collider is. A full-silhouette body is therefore unattackable from
    // the north (the Eye stops against the canopy at 2.09) and no tuning changes that.
    //
    // There are exactly two configurations, and you have to pick one:
    //
    //   A. outlineClipFraction 1.00 + the EnemyController patch  <-- SHIPPED
    //      Whole silhouette blocks, canopy included. EnemyController subtracts
    //      GetMeleeSurfaceInset() so range is measured to the SURFACE, which makes the
    //      canopy reachable. Also means the tree is a solid 2.11 x 2.60 wall at map centre
    //      that nothing can path behind - check your lanes still work.
    //
    //   B. outlineClipFraction 0.65, keepLargestPathOnly true, EnemyController untouched
    //      Blocks the trunk up to the fork (+0.51; the fork is at +0.54) and everything
    //      below. Worst approach stops at 1.69, inside the Eye's 1.80. 0.70 is already
    //      1.83 and breaks, so 0.65 is a hard ceiling, not a tuning suggestion.
    //
    // Measured stop distance by clip fraction, Eye body 0.50:
    //   0.60 -> 1.62   0.65 -> 1.69   0.70 -> 1.83 X   0.80 -> 1.95 X   1.00 -> 2.22 X

    [Tooltip("Collide with the sprite's traced outline (trunk, roots, mound) instead of a circle " +
             "around the base. On by default - the Core is built from code in " +
             "TowerDefenseMap.CreateCentralCore, so these defaults are the only values that ever " +
             "apply; there is no prefab to override them in the inspector.")]
    public bool useFullOutlineCollider = true;

    [Range(0.1f, 1f)]
    [Tooltip("Keep only the part of the silhouette BELOW this fraction of the frame height, " +
             "measured up from the bottom. The canopy above it is overhead and does not block.\n\n" +
             "1.00 = whole silhouette, canopy included. DEFAULT, and REQUIRES the EnemyController " +
             "GetMeleeSurfaceInset patch - without it the Core is unattackable across a " +
             "129-degree arc.\n" +
             "0.65 = trunk up to the fork, nothing above. The highest clip that stays reachable " +
             "with EnemyController untouched. Set keepLargestPathOnly true if you use this.")]
    public float outlineClipFraction = 1.0f;

    [Tooltip("Keep only the largest path. OFF at clip 1.00 - the canopy is drawn as separate " +
             "clumps and the importer traces each as its own path (your build reported 23), so " +
             "keeping one would collide with a single leaf blob and nothing else. Turn it ON when " +
             "clipping below 1.00, where it discards the crystals the clip severs from the canopy " +
             "they hang off. Either way minOutlinePathArea removes the stray specks.")]
    public bool keepLargestPathOnly = false;

    [Tooltip("Which loaded frame to trace. The canopy sways across the 24-frame cycle; re-tracing " +
             "every frame would rebuild the collider 10x a second, so one frame is picked and kept. " +
             "Below the clip line there is almost no motion, so frame 0 is fine.")]
    public int collisionFrameIndex = 0;

    [Tooltip("Collapse the outline to its convex hull. The raw silhouette is concave - the gaps " +
             "between roots are pockets a steering enemy can wedge into until stuck-detection digs " +
             "it out. The hull trades silhouette accuracy for clean sliding contact.")]
    public bool simplifyToConvexHull = false;

    [Tooltip("Paths smaller than this (world units squared) are discarded, in case a stray speck " +
             "survives the clip. 00.png carries two ~10px dots - the importer's auto-slicer found " +
             "three regions in it.")]
    public float minOutlinePathArea = 0.01f;

    // ── Smoothing ────────────────────────────────────────────────────────────
    // Why enemies wedge on a traced outline. EnemyController drives them by writing
    // rb.linearVelocity straight at the target every FixedUpdate, and the Core is NOT on
    // their obstacleLayer, so they have no avoidance steering for it - they rely entirely
    // on physics contact to deflect them. That works on a smooth surface: the contact
    // normal cancels the inward component and the enemy slides. It fails in two places:
    //
    //   1. A sharp vertex pointing at the enemy has no single tangent, so it jitters
    //      against it instead of picking a side.
    //   2. A narrow concave notch pushes back from two opposing normals at once. They
    //      cancel the drive entirely and the enemy just sits there until stuck-detection
    //      fires 0.5s later. The root gaps and the seams between canopy clumps are these.
    //
    // So: drop micro-detail, fill the narrow notches, round what remains. The large-scale
    // concavity - the bay between canopy and mound - is deliberately kept. It is wide
    // enough to walk out of, and it is what makes the outline read as a tree.

    [Tooltip("Collapse vertices that sit within this distance (world units) of the line between " +
             "their neighbours. The auto-traced shape is full of one-or-two-pixel steps that serve " +
             "no collision purpose and give enemies something to catch on. 0 disables.")]
    public float outlineSimplifyTolerance = 0.03f;

    [Tooltip("Fill concave pockets narrower than notchMouthWidth. This is the main anti-wedge fix - " +
             "a narrow notch traps an enemy between two opposing contact normals that cancel its " +
             "drive. Wide bays keep their shape.")]
    public bool fillNarrowNotches = true;

    [Tooltip("Mouth width (world units) below which a pocket is filled in. A notch traps an enemy " +
             "when it is around its body diameter across - the Eye is 1.00 - so the default sits just " +
             "above that.\n\n" +
             "Measured on this sprite (traps removed / area added):\n" +
             "  0.35 -> 18% / +0.6%    0.70 -> 30% / +1.7%\n" +
             "  0.90 -> 42% / +2.7%    1.20 -> 52% / +3.6%  DEFAULT\n" +
             "  1.60 -> 61% / +4.2%\n" +
             "Raise it if enemies still wedge; the cost is only that shallow detail flattens out.")]
    public float notchMouthWidth = 1.2f;

    [Range(0, 3)]
    [Tooltip("Chaikin corner-cutting passes, applied last. Replaces every sharp vertex with a rounded " +
             "pair so there is always a tangent to slide along. Each pass doubles vertex count, which " +
             "is why simplification runs first. 0 disables.")]
    public int cornerRounding = 2;
    #endregion

    #region Configuration
    [Header("Core Configuration")]
    public float maxEnergy = 100f;
    public float currentEnergy = 100f;
    public float coreSize = 2f;

    [Header("Animation")]
    public bool enableAnimation = true;

    [Tooltip("Seconds each frame is held while the animation is playing.\n" +
             "Overridden at runtime by the energy state (see CalculateAnimationSpeed).")]
    public float animationSpeed = 0.1f;

    [Tooltip("Resources folder holding the loose numbered frames (00.png ... 23.png).\n" +
             "Frames are ordered by the leading number in the file name, not by import order.")]
    public string coreSpriteFolder = "Sprites/Buildings/Towers/Core";

    [Tooltip("How many frames of the loaded sequence to play, starting at spriteStartIndex.\n" +
             "0 (or more than are available) = play every frame that was loaded.")]
    public int animationFrameCount = 0;
    public int spriteStartIndex = 0;

    [Tooltip("Hold the LAST frame of the cycle for a while before restarting from the first frame.")]
    public bool pauseOnLastFrame = true;

    [Tooltip("How long the last frame is held, in seconds. Not affected by the energy-state speed.")]
    public float lastFramePauseDuration = 5f;

    [Header("Visual")]
    public Color normalColor = Color.white;
    public float lowEnergyThreshold = 0.3f;

    [Header("Y-Sort / Ground Footprint")]
    // Both of these are FRACTIONS OF THE SPRITE, not world units, so they stay correct
    // if the frames, the pixels-per-unit or coreSize ever change. Values measured off
    // frame 00's alpha channel: the art occupies rows 85..1647 of a 2048px frame, and
    // the mound is widest at row ~1376, spanning cols 538..1518.
    [Tooltip("Where the Core sorts from, as a fraction of sprite height measured UP from the BOTTOM " +
             "of the sprite's rect. Grass BELOW this line draws in front of the Core; grass above it " +
             "draws behind.\n\n" +
             "Do NOT set this to the art's bottom edge (0.196). Nothing can overlap the Core from the " +
             "front there, because the sprite itself ends at that line — you get a tree floating above " +
             "every blade on the map. It needs to sit INSIDE the mound so there's a band of grass that " +
             "can wash over the base, exactly like the player (sprite bottom -0.5, sort line -0.3).\n\n" +
             "At ppu 1200 / coreSize 2 the sprite is 3.41 units and its art bottoms out at y=-1.04:\n" +
             "  0.21 -> line at -0.99. Below the art entirely. Useless.\n" +
             "  0.30 -> line at -0.68. Grass over the mound's lower 43%. DEFAULT — verified on\n" +
             "          screen. The vertex probe undercounts how this looks, so trust your eyes.\n" +
             "  0.40 -> line at -0.34. 84% of the mound. Proportionally the same as the player\n" +
             "          (grass over its bottom ~25% of artwork), but more grass up the base.\n" +
             "  0.44 -> line at -0.21. Grass to the top of the mound; blades start creeping up\n" +
             "          the trunk, which is the 'grass growing out of the Core' look.")]
    [Range(0f, 1f)]
    public float ySortGroundAnchor = 0.30f;

    [Tooltip("Radius of the mound at its widest, as a fraction of sprite WIDTH. The mound spans ~48% " +
             "of the frame, so its radius is ~0.24. Grass overlays read this to keep from spawning " +
             "inside the Core's base.")]
    [Range(0f, 1f)]
    public float groundFootprintFraction = 0.24f;

    [Tooltip("Where the artwork actually starts, as a fraction of sprite height up from the bottom of " +
             "the frame. Measured off 00.png: the art occupies rows 85..1647 of 2048, so it bottoms out " +
             "at 0.196 and everything below is transparent. Diagnostics only — keeps the probe from " +
             "reporting grass that overlaps empty space as if it overlapped the Core.")]
    [Range(0f, 1f)]
    public float artBottomFraction = 0.196f;

    [Tooltip("Dump the full sorting report to the console a couple of seconds after the Core spawns, " +
             "so it can be copied straight out. OFF by default — the orchestrator spawns the Core twice " +
             "per run, so this is two large dumps every run. The same report is on the component's " +
             "context menu (Probe Grass Sorting / Log Y-Sort Diagnostics) whenever it's needed.")]
    public bool logSortingReportOnStart = false;

    [Tooltip("Log the frame-load result on every spawn. Off by default; genuine problems (zero frames " +
             "loaded, a collider that can't be resized) still log regardless.")]
    public bool verboseLogging = false;

    [Tooltip("Delay before the report runs. Must outlast grass generation — GameOrchestrator bakes " +
             "grass BEFORE it creates the Core, and biome changes rebake it.")]
    public float sortingReportDelay = 2f;

    [Tooltip("Must match GrassCartoonOverlay / YSortEntity on the towers.")]
    public float ySortPrecision = 10f;
    public int ySortOrderBase = 1000;

    public enum YSortMode
    {
        GroundAnchor,   // derive the sort point from the sprite (ySortGroundAnchor above)
        ManualOffset    // ignore the sprite; use ySortWorldOffset verbatim
    }

    [Tooltip("GroundAnchor derives the sort point from the sprite's rect. ManualOffset ignores the " +
             "sprite entirely and uses the world offset below — use it if the sprite's import settings " +
             "make the derived value nonsense, or to just dial it in by eye.")]
    public YSortMode ySortMode = YSortMode.GroundAnchor;

    [Tooltip("World units from the Core's pivot down to its ground line. Only used in ManualOffset mode. " +
             "Negative = sort point below the pivot. Tower.cs uses -0.5 for comparison.")]
    public float ySortWorldOffset = -0.5f;

    [Header("Energy")]
    public bool requiresEnergyToFunction = true;

    [Header("Energy Bar")]
    public EnergyBarSettings energyBarSettings = new EnergyBarSettings();

    [Header("Damage Settings")]
    public float armorReduction = 0f;
    public bool immuneToEnemyDamage = false;
    public float damageFlashDuration = 0.1f;
    public Color damageFlashColor = new Color(2f, 2f, 2f, 1f); // Additive bright flash
    public bool enableDamageEffects = true;
    public float criticalHealthShakeIntensity = 0.1f;

    [System.Serializable]
    public class EnergyBarSettings
    {
        public bool show = true;
        public float height = 0.15f;
        public float width = 1.5f;
        public float offset = 0.45f;
        public bool showText = true;
    }
    #endregion

    #region Core Components
    private SpriteRenderer spriteRenderer;
    private Sprite[] coreSprites;
    private Coroutine animationCoroutine;
    private EnergyBar energyBar;
    private YSortEntity ysortEntity;

    // Static: the Core is respawned per run, so this keeps the stray-sheet warning to once
    // per play session rather than once per spawn.
    private static bool warnedAboutStrayCoreSprites = false;

    // State tracking
    private Vector3 originalScale;
    private Vector3 originalPosition;
    private Color originalColor;
    private float currentAnimationSpeed = -1f;
    private bool isEnergyDepleted, isEnergyLow;
    private bool isDestroyed = false;
    private Coroutine damageFlashCoroutine;
    private Coroutine shakeCoroutine;

    // Highlight system for repair functionality
    private bool isHighlighted = false;
    private Color highlightColor = Color.cyan;
    private bool isRegisteredWithEnergyManager = false;

    // Events
    public System.Action<float> OnEnergyChanged;
    public System.Action OnEnergyDepleted;
    public System.Action OnEnergyRestored;
    public System.Action<float, GameObject> OnDamageTaken;
    public System.Action<GameObject> OnCoreDestroyed;
    public System.Action OnCoreEnteredCriticalState;
    public System.Action OnCoreExitedCriticalState;
    #endregion

    #region Unity Lifecycle
    void Awake() => InitializeComponents();
    void Start() => SetupCore();
    void Update()
    {
        UpdateCoreState();
        RefreshYSort();   // cheap, and self-heals if the sprite or scale changed
    }
    void OnDestroy() => Cleanup();
    #endregion

    #region Initialization
    void InitializeComponents()
    {
        gameObject.tag = "Core";

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        spriteRenderer.sortingOrder = 0;
        spriteRenderer.sortingLayerName = "Default";

        // Y-Sort: dynamically sort against grass based on Y position.
        // Configured below in ConfigureYSort(), AFTER the sprite and scale are set —
        // the offset is derived from the sprite's bounds, which don't exist yet here.
        if (GetComponent<YSortEntity>() == null)
            gameObject.AddComponent<YSortEntity>();

        originalScale = Vector3.one * coreSize;
        originalPosition = transform.position;
        transform.localScale = originalScale;
        originalColor = normalColor;

        LoadCoreSprites();
        ConfigureYSort();
        StartAnimationIfEnabled();
    }

    /// The Core sprite is a tall tree whose pivot sits in the middle of its TRUNK, but
    /// it should sort against grass from the point where it meets the GROUND — same
    /// reason Tower.cs uses sortYOffset = -0.5f. Without this the sort point is halfway
    /// up the trunk, and every blade of grass below that line (including grass on the
    /// mound) gets a higher sortingOrder and draws in front of the whole tree, canopy
    /// and all. Derived from bounds rather than hardcoded because the Core is scaled by
    /// coreSize and its frames are far bigger than a tower's.
    void ConfigureYSort()
    {
        ysortEntity = GetComponent<YSortEntity>();
        if (ysortEntity == null) return;

        ysortEntity.sortPrecision = ySortPrecision;
        ysortEntity.sortOrderBase = ySortOrderBase;
        ysortEntity.sortYOffset = ComputeYSortOffset();
    }

    /// Re-applied every frame (one float, no allocation). The offset depends on
    /// lossyScale and on the sprite, and BOTH can change after Awake — UpdateScaleEffect
    /// rescales the Core as its energy drops, and the frames stream in from Resources.
    /// Computing it once in Awake left it stale, which is a silent way to be wrong.
    void RefreshYSort()
    {
        if (ysortEntity == null) return;

        float offset = ComputeYSortOffset();
        if (!Mathf.Approximately(offset, ysortEntity.sortYOffset))
            ysortEntity.sortYOffset = offset;
    }

    /// World-space offset from transform.position.y down to the Core's ground line.
    ///
    /// Derived from the sprite's RECT + PIVOT + PPU rather than from SpriteRenderer.bounds.
    /// bounds was the wrong tool: with the default Mesh Type = Tight import setting Unity
    /// trims transparent margins, so bounds.min.y lands on the art's bottom edge, not the
    /// frame's — and this frame has ~400px of empty space under the mound. The anchor
    /// fraction then measures against the wrong height. Rect maths gives the same answer
    /// whether the mesh is Tight or Full Rect.
    public float ComputeYSortOffset()
    {
        if (ySortMode == YSortMode.ManualOffset) return ySortWorldOffset;

        var sr = ResolveRenderer();
        var spr = sr != null ? sr.sprite : null;
        if (spr == null) return 0f;

        float ppu = spr.pixelsPerUnit;
        if (ppu <= 0.0001f) return 0f;

        // spr.pivot is in pixels from the rect's bottom-left, so this is where the
        // frame's bottom edge sits relative to the transform origin, in local units.
        float rectBottomLocal = -spr.pivot.y / ppu;
        float rectHeightLocal = spr.rect.height / ppu;
        float groundLocal = rectBottomLocal + rectHeightLocal * Mathf.Clamp01(ySortGroundAnchor);

        return groundLocal * Mathf.Abs(transform.lossyScale.y);
    }

    /// Radius of the Core's base in WORLD units. Grass overlays use this to carve out
    /// the mound instead of relying on a hardcoded radius tuned for the old, much
    /// smaller Core sheet.
    public float GetGroundFootprintRadius()
    {
        var sr = ResolveRenderer();
        var spr = sr != null ? sr.sprite : null;
        if (spr == null) return 0f;

        float ppu = spr.pixelsPerUnit;
        if (ppu <= 0.0001f) return 0f;

        float rectWidthWorld = (spr.rect.width / ppu) * Mathf.Abs(transform.lossyScale.x);
        return rectWidthWorld * Mathf.Clamp01(groundFootprintFraction);
    }

    /// World-space centre of that footprint: the ground line, not the pivot.
    public Vector2 GetGroundFootprintCenter()
    {
        var sr = ResolveRenderer();
        var spr = sr != null ? sr.sprite : null;
        if (spr == null) return transform.position;

        float ppu = spr.pixelsPerUnit;
        if (ppu <= 0.0001f) return transform.position;

        // Horizontal centre of the frame relative to the pivot (0 for a centred pivot).
        float centerOffsetX = ((spr.rect.width * 0.5f) - spr.pivot.x) / ppu * transform.lossyScale.x;

        return new Vector2(transform.position.x + centerOffsetX,
                           transform.position.y + ComputeYSortOffset());
    }

    private SpriteRenderer ResolveRenderer() =>
        spriteRenderer != null ? spriteRenderer : GetComponent<SpriteRenderer>();

    /// Prints every number that feeds the sort order, plus what a grass blade at nearby
    /// Y positions would get, so this stops being guesswork. Right-click the component.
    [ContextMenu("Log Y-Sort Diagnostics")]
    public void LogYSortDiagnostics()
    {
        var sr = ResolveRenderer();
        var spr = sr != null ? sr.sprite : null;
        if (spr == null) { Debug.LogWarning("CentralCore: no sprite yet — enter Play Mode first."); return; }

        float offset = ComputeYSortOffset();
        float groundY = transform.position.y + offset;
        int coreOrder = ySortOrderBase + Mathf.RoundToInt(-groundY * ySortPrecision);

        string grass = "";
        foreach (float dy in new[] { -4f, -2f, 0f, 2f, 4f })
        {
            float gy = groundY + dy;
            int gOrder = ySortOrderBase + Mathf.RoundToInt(-gy * ySortPrecision);
            grass += $"\n    grass at y={gy,7:F2} -> order {gOrder,5}  ({(gOrder > coreOrder ? "IN FRONT of" : "behind")} core)";
        }

        // Sorting is only half of it: grass has to actually EXIST below the sort line.
        // The overlay carves a circle of coreExclusionRadius around the origin, and if that
        // circle reaches past the sort line then every blade that could overlap the Core is
        // deleted before it spawns — no sort order can rescue that.
        var overlay = Object.FindFirstObjectByType<GrassCartoonOverlay>();
        if (overlay != null)
        {
            float reach = Mathf.Abs(offset);   // distance from pivot down to the sort line
            grass += $"\n  grass exclusion: radius {overlay.coreExclusionRadius:F2} around origin " +
                     $"vs sort line {reach:F2} below the pivot";
            if (overlay.coreExclusionRadius >= reach)
                grass += $"\n  *** NO GRASS CAN EVER DRAW IN FRONT: the exclusion circle swallows the " +
                         $"whole overlap band. Set GrassCartoonOverlay.coreExclusionRadius (BiomeManager " +
                         $"sets it — grassCartoonCoreExclusion etc.) below {reach:F2}. ***";
        }

        Debug.Log(
            $"[CentralCore Y-Sort]\n" +
            $"  sprite         : {spr.name}  rect {spr.rect.width}x{spr.rect.height}px  ppu {spr.pixelsPerUnit}\n" +
            $"  pivot          : {spr.pivot} px from rect bottom-left\n" +
            $"  lossyScale     : {transform.lossyScale}   (coreSize = {coreSize})\n" +
            $"  WORLD SIZE     : {(spr.rect.width / spr.pixelsPerUnit) * Mathf.Abs(transform.lossyScale.x):F2} x " +
            $"{(spr.rect.height / spr.pixelsPerUnit) * Mathf.Abs(transform.lossyScale.y):F2} units\n" +
            $"  renderer.bounds: size {sr.bounds.size} — if height differs from WORLD SIZE, Mesh Type is Tight\n" +
            $"  position.y     : {transform.position.y:F2}\n" +
            $"  sortYOffset    : {offset:F2}  (mode: {ySortMode}, anchor {ySortGroundAnchor:F2})\n" +
            $"  ground line at : y = {groundY:F2}  ->  core sortingOrder {coreOrder}\n" +
            $"  footprint      : centre {GetGroundFootprintCenter()}  radius {GetGroundFootprintRadius():F2}" +
            grass);
    }

    /// Reads the LIVE renderer state for the Core and the grass bands and reports who actually
    /// wins, instead of recomputing the formula and trusting it. Every diagnosis in this file
    /// up to now has been arithmetic on assumed values; this one observes.
    ///
    /// Unity's transparent sort key is (renderQueue, sortingLayer, sortingOrder, distance) —
    /// IN THAT ORDER. A queue or layer mismatch makes sortingOrder irrelevant, and no amount
    /// of Y-sort tuning would ever show up on screen. This checks all three.
    [ContextMenu("Probe Grass Sorting (Play Mode)")]
    public void ProbeGrassSorting()
    {
        var sr = ResolveRenderer();
        if (sr == null || sr.sprite == null) { Debug.LogWarning("CentralCore: no sprite — enter Play Mode."); return; }

        int coreOrder = sr.sortingOrder;                 // live, not computed
        int coreLayer = sr.sortingLayerID;
        var coreMat = sr.sharedMaterial;
        int coreQueue = coreMat != null ? coreMat.renderQueue : -1;

        // NOT sr.bounds: the mesh is a full-rect quad, so bounds run to the frame's edge and
        // include ~0.67 units of transparent margin under the mound. Counting verts in there
        // reports grass "in front of the Core" that is in front of nothing. Clip to the art.
        Bounds cb = sr.bounds;
        float artBottom = cb.min.y + cb.size.y * Mathf.Clamp01(artBottomFraction);
        cb.SetMinMax(new Vector3(cb.min.x, artBottom, cb.min.z), cb.max);
        float groundY = transform.position.y + ComputeYSortOffset();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[CentralCore Grass Probe] LIVE values read off the renderers:");
        sb.AppendLine($"  CORE  layer {SortingLayer.IDToName(coreLayer)}({coreLayer})  order {coreOrder}  " +
                      $"queue {coreQueue}  shader {(coreMat != null ? coreMat.shader.name : "none")}");
        sb.AppendLine($"  CORE  ARTWORK spans y {cb.min.y:F2}..{cb.max.y:F2}   x {cb.min.x:F2}..{cb.max.x:F2} " +
                      $"(renderer bounds reach {sr.bounds.min.y:F2} — the gap is transparent margin)");
        sb.AppendLine($"  sort line y={groundY:F2} — grass must sit BELOW this and OVERLAP the art to show in front");

        var ys = GetComponent<YSortEntity>();
        if (ys != null)
            sb.AppendLine($"  YSortEntity  offset {ys.sortYOffset:F3}  precision {ys.sortPrecision}  base {ys.sortOrderBase}");
        sb.AppendLine($"  CORE  pos {transform.position}  lossyScale {transform.lossyScale}");

        var overlay = Object.FindFirstObjectByType<GrassCartoonOverlay>();
        if (overlay != null)
            sb.AppendLine($"  OVERLAY exclusion {overlay.coreExclusionRadius:F2}  base {overlay.sortOrderBase}  " +
                          $"precision {overlay.sortPrecision}  band {overlay.sortBandSize}  scale {overlay.baseScale}");

        var all = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int bandCount = 0, frontBands = 0, frontVerts = 0, mismatches = 0;
        int shown = 0;

        // Bands are ordered; the interesting ones straddle the Core's own order.
        System.Array.Sort(all, (x, y) => x.sortingOrder.CompareTo(y.sortingOrder));

        foreach (var mr in all)
        {
            if (!mr.name.StartsWith("GrassBand_")) continue;
            bandCount++;

            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            // Count blade vertices sitting on top of the Core's artwork.
            int n = 0;
            var verts = mf.sharedMesh.vertices;      // container is at origin, identity transform
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (v.x > cb.min.x && v.x < cb.max.x && v.y > cb.min.y && v.y < cb.max.y) n++;
            }
            if (n == 0) continue;

            var m = mr.sharedMaterial;
            int q = m != null ? m.renderQueue : -1;
            bool sameLayer = mr.sortingLayerID == coreLayer;
            bool sameQueue = q == coreQueue;
            bool ordersAbove = mr.sortingOrder > coreOrder;

            if (ordersAbove) { frontBands++; frontVerts += n; }
            if (!sameLayer || !sameQueue) mismatches++;

            // Print the bands nearest the Core's order — those decide the visible edge.
            if (Mathf.Abs(mr.sortingOrder - coreOrder) <= 8 && shown < 10)
            {
                shown++;
                string flag = !sameQueue ? "  <<< QUEUE MISMATCH — sortingOrder IGNORED"
                            : !sameLayer ? "  <<< LAYER MISMATCH — sortingOrder IGNORED"
                            : ordersAbove ? "  (should draw IN FRONT of core)" : "  (draws behind core)";
                sb.AppendLine($"    {mr.name}: order {mr.sortingOrder} layer " +
                              $"{SortingLayer.IDToName(mr.sortingLayerID)}({mr.sortingLayerID}) queue {q} " +
                              $"shader {(m != null ? m.shader.name : "none")} -> {n} verts over core{flag}");
            }
        }

        sb.AppendLine($"  {bandCount} bands in scene. {frontVerts} verts across {frontBands} band(s) " +
                      $"outrank the Core AND overlap its art. Layer/queue mismatches: {mismatches}");

        if (bandCount == 0)
            sb.AppendLine("  VERDICT: no grass generated yet.");
        else if (mismatches > 0)
            sb.AppendLine("  VERDICT: RENDER QUEUE / SORTING LAYER MISMATCH. Unity's sort key is " +
                          "(renderQueue, sortingLayer, sortingOrder, distance) IN THAT ORDER, so the Core " +
                          "wins on the queue and every sortingOrder above is decorative. Fix the material, " +
                          "not the Y-sort: the Core's SpriteRenderer is created by AddComponent in " +
                          "TowerDefenseMap.CreateCentralCore and gets the default sprite material, while " +
                          "the grass carries your _BackgroundScale shader.");
        else if (frontVerts == 0)
            sb.AppendLine("  VERDICT: no grass outranks the Core where it overlaps — sort line too low. " +
                          "Raise ySortGroundAnchor.");
        else
            sb.AppendLine("  VERDICT: grass outranks the Core by every rule Unity applies. If it still " +
                          "renders behind, something rewrites sortingOrder after LateUpdate.");

        Debug.Log(sb.ToString());
    }

    void OnDrawGizmosSelected()
    {
        var sr = ResolveRenderer();
        if (sr == null || sr.sprite == null) return;

        float groundY = transform.position.y + ComputeYSortOffset();
        float halfW = sr.bounds.size.x * 0.5f;

        // Magenta line = where the Core sorts from. Grass below it draws in front,
        // grass above it draws behind. It should sit on the mound's front edge.
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(new Vector3(transform.position.x - halfW, groundY, 0f),
                        new Vector3(transform.position.x + halfW, groundY, 0f));

        // Yellow circle = the footprint grass is kept out of.
        Gizmos.color = Color.yellow;
        Vector2 c = GetGroundFootprintCenter();
        Gizmos.DrawWireSphere(new Vector3(c.x, c.y, 0f), GetGroundFootprintRadius());
    }

    void SetupCore()
    {
        RegisterWithEnergyManager();
        originalColor = spriteRenderer.color;
        SetupSpriteCollision();
        SetupEnergyBar();
        UpdateVisualState();

        // TODO - remove testing energy resupplying
        //if (currentEnergy >= maxEnergy)
        //{
        //    currentEnergy = maxEnergy * 0.8f;
        //}
        if (transform.position.z != 0f)
        {
            Vector3 fixedPosition = new Vector3(transform.position.x, transform.position.y, 0f);
            transform.position = fixedPosition;
        }

        if (logSortingReportOnStart) StartCoroutine(AutoSortingReport());
    }

    private IEnumerator AutoSortingReport()
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, sortingReportDelay));
        LogYSortDiagnostics();
        ProbeGrassSorting();
    }
    void SetupSpriteCollision()
    {
        if (spriteRenderer?.sprite != null)
        {
            if (useFullOutlineCollider && TryBuildOutlineCollider()) { EnsureCollider(); return; }

            spriteCollider = SpriteCollisionManager.SetupCollision(gameObject, collisionConfig);
            ApplyColliderSizeMultiplier();
            ApplyMeleeReachClamp();
            EnsureCollider();
        }
        else
        {
            // Delay setup if sprite is not ready
            SpriteCollisionManager.SetupCollisionDelayed(this, collisionConfig);
            StartCoroutine(ShrinkColliderWhenReady());
        }
    }

    /// Shrink whatever SpriteCollisionManager produced by colliderSizeMultiplier.
    ///
    /// Done as a post-pass on the finished collider rather than by tweaking
    /// collisionConfig.paddingPercent, because the padding is applied inside
    /// SpriteCollisionManager and Tower.cs's own copy of that maths clamps it
    /// (keep = 1f - Mathf.Clamp01(paddingPercent)), which silently ignores the
    /// negative value this Core is configured with. Scaling the result is a true
    /// 15% either way, and it's idempotent via colliderResized.
    void ApplyColliderSizeMultiplier()
    {
        if (colliderResized) return;

        if (spriteCollider == null) spriteCollider = GetComponent<Collider2D>();
        if (spriteCollider == null) return;

        float m = Mathf.Clamp(colliderSizeMultiplier, 0.01f, 1f);
        if (Mathf.Approximately(m, 1f)) { colliderResized = true; return; }

        switch (spriteCollider)
        {
            case CircleCollider2D circle:
                circle.radius *= m;
                break;

            case BoxCollider2D box:
                box.size *= m;
                break;

            case CapsuleCollider2D capsule:
                capsule.size *= m;
                break;

            case PolygonCollider2D poly:
                for (int p = 0; p < poly.pathCount; p++)
                {
                    var path = poly.GetPath(p);
                    for (int i = 0; i < path.Length; i++) path[i] *= m;
                    poly.SetPath(p, path);
                }
                break;

            default:
                Debug.LogWarning($"CentralCore: colliderSizeMultiplier can't resize a " +
                                 $"{spriteCollider.GetType().Name}; leaving it at full size.");
                return;
        }

        colliderResized = true;
    }

    // The delayed path hands the collider back on a later frame, so wait for it.
    private IEnumerator ShrinkColliderWhenReady()
    {
        float timeout = 5f;
        while (timeout > 0f && GetComponent<Collider2D>() == null)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        spriteCollider = GetComponent<Collider2D>();
        if (spriteCollider != null)
        {
            if (useFullOutlineCollider && TryBuildOutlineCollider()) { EnsureCollider(); yield break; }

            ApplyColliderSizeMultiplier();
            ApplyMeleeReachClamp();
        }

        EnsureCollider();
    }

    /// The outline path is the least-travelled code in this file, and a throw inside it escapes
    /// Start() - which skips the circle fallback AND everything after SetupSpriteCollision, so
    /// the Core ends up with no collision and no energy bar. That happened once, from a bug in
    /// ConvexHull. It is contained now: a cosmetic collider feature must never be able to take
    /// the whole Core down.
    bool TryBuildOutlineCollider()
    {
        try
        {
            return BuildOutlineCollider();
        }
        catch (System.Exception e)
        {
            Debug.LogError("CentralCore: outline collider build threw; falling back to the circle. " +
                           "Set useFullOutlineCollider = false to skip this path entirely.\n" + e);

            var partial = GetComponent<PolygonCollider2D>();
            if (partial != null) { partial.enabled = false; Destroy(partial); }
            return false;
        }
    }

    /// Last line of defence. Whatever happened above, the Core must not finish setup looking
    /// solid and being walk-through.
    void EnsureCollider()
    {
        if (GetComponent<Collider2D>() != null) return;

        var sr = ResolveRenderer();
        var spr = sr != null ? sr.sprite : null;

        var circle = gameObject.AddComponent<CircleCollider2D>();
        circle.isTrigger = collisionConfig != null && collisionConfig.isTrigger;
        circle.radius = (spr != null && spr.pixelsPerUnit > 0.0001f)
            ? (spr.rect.width / spr.pixelsPerUnit) * 0.5f
            : 1f;

        spriteCollider = circle;
        colliderResized = false;
        ApplyColliderSizeMultiplier();
        ApplyMeleeReachClamp();

        Debug.LogError("CentralCore: no collider existed after setup - things would have walked " +
                       "straight through the Core. Added an emergency CircleCollider2D " +
                       $"(world radius {circle.radius * Mathf.Abs(transform.lossyScale.x):F2}).");
    }

    #region Outline collider construction
    /// Replaces the circle body with a PolygonCollider2D traced from the sprite's own
    /// physics shape, so enemies stop against the tree's silhouette instead of a disc
    /// around its base.
    ///
    /// Uses Sprite.GetPhysicsShape, which reads the outline BAKED AT IMPORT. That matters:
    /// it needs neither Read/Write Enabled (00.png has isReadable: 0) nor any per-frame
    /// work, and it respects a hand-authored shape if you draw one in the Sprite Editor.
    /// It does require "Generate Physics Shape" on the importer, which 00_png.meta already
    /// has (spriteGenerateFallbackPhysicsShape: 1).
    ///
    /// Returns false and leaves the circle path to run if anything is missing.
    bool BuildOutlineCollider()
    {
        Sprite src = ResolveCollisionSprite();
        if (src == null) return false;

        int shapes = src.GetPhysicsShapeCount();
        if (shapes == 0)
        {
            Debug.LogWarning($"CentralCore: sprite '{src.name}' has no baked physics shape. Tick " +
                             "'Generate Physics Shape' on the texture importer, or draw one in the " +
                             "Sprite Editor's Custom Physics Shape mode. Falling back to the circle.");
            return false;
        }

        float scale = Mathf.Abs(transform.lossyScale.x);
        if (scale < 0.0001f) scale = 1f;

        // Paths come back in LOCAL units (world units at scale 1), so convert the world-space
        // area threshold into local space rather than the other way round.
        float minLocalArea = minOutlinePathArea / (scale * scale);

        var buffer = new List<Vector2>();
        var paths = new List<Vector2[]>();
        int dropped = 0;

        // Clip line in LOCAL units. Derived from rect + pivot rather than assumed centred,
        // so it stays right if the pivot is ever moved off 0.5.
        float ppu = src.pixelsPerUnit;
        float clipY = float.MaxValue;
        bool clipping = outlineClipFraction < 0.999f && ppu > 0.0001f;
        if (clipping)
            clipY = ((src.rect.height * Mathf.Clamp01(outlineClipFraction)) - src.pivot.y) / ppu;

        for (int i = 0; i < shapes; i++)
        {
            src.GetPhysicsShape(i, buffer);
            if (buffer.Count < 3) { dropped++; continue; }

            var path = clipping ? ClipBelow(buffer, clipY) : new List<Vector2>(buffer);
            if (path.Count < 3) { dropped++; continue; }
            if (Mathf.Abs(SignedArea(path)) < minLocalArea) { dropped++; continue; }

            paths.Add(path.ToArray());
        }

        if (paths.Count == 0)
        {
            Debug.LogWarning($"CentralCore: nothing survived tracing '{src.name}' " +
                             $"(clip {outlineClipFraction:F2}, min area {minOutlinePathArea}). " +
                             "Falling back to the circle.");
            return false;
        }

        if (keepLargestPathOnly && paths.Count > 1)
        {
            int best = 0;
            float bestArea = 0f;
            for (int i = 0; i < paths.Count; i++)
            {
                float area = Mathf.Abs(SignedArea(paths[i]));
                if (area > bestArea) { bestArea = area; best = i; }
            }
            dropped += paths.Count - 1;
            var keep = paths[best];
            paths.Clear();
            paths.Add(keep);
        }

        if (simplifyToConvexHull)
        {
            var all = new List<Vector2>();
            for (int i = 0; i < paths.Count; i++) all.AddRange(paths[i]);
            var hull = ConvexHull(all);
            if (hull.Count >= 3) { paths.Clear(); paths.Add(hull.ToArray()); }
        }

        // ── Smoothing pass. Order matters: thin the vertices out before filling notches
        // (fewer reflex vertices to test) and before rounding (which multiplies them). ──
        int vertsBefore = 0, vertsAfter = 0;
        for (int i = 0; i < paths.Count; i++) vertsBefore += paths[i].Length;

        for (int i = 0; i < paths.Count; i++)
        {
            IList<Vector2> path = paths[i];

            if (outlineSimplifyTolerance > 0f)
                path = Simplify(path, outlineSimplifyTolerance / scale);

            if (fillNarrowNotches && notchMouthWidth > 0f)
                path = FillPockets(path, notchMouthWidth / scale);

            if (cornerRounding > 0)
                path = Chaikin(path, cornerRounding);

            // Never let smoothing destroy a path outright - a degenerate result means the
            // thresholds were too aggressive for that piece, so keep the original.
            if (path.Count >= 3) paths[i] = new List<Vector2>(path).ToArray();
        }

        for (int i = 0; i < paths.Count; i++) vertsAfter += paths[i].Length;

        // Drop whatever SpriteCollisionManager may have built. Two bodies on one object
        // would keep enemies out at the circle's radius and make the outline decorative.
        var circle = GetComponent<CircleCollider2D>();
        if (circle != null) { circle.enabled = false; Destroy(circle); }

        var poly = GetComponent<PolygonCollider2D>();
        if (poly == null) poly = gameObject.AddComponent<PolygonCollider2D>();
        poly.isTrigger = collisionConfig != null && collisionConfig.isTrigger;
        poly.pathCount = paths.Count;
        for (int i = 0; i < paths.Count; i++) poly.SetPath(i, paths[i]);

        spriteCollider = poly;
        colliderResized = true;   // colliderSizeMultiplier does not apply to a traced outline

        if (logReachabilityFix)
        {
            Debug.Log($"[CentralCore] outline collider from '{src.name}': {paths.Count} path(s)" +
                      (dropped > 0 ? $", {dropped} stray path(s) discarded" : "") +
                      (simplifyToConvexHull ? ", collapsed to convex hull" : "") +
                      $", bounds {poly.bounds.size}, clip {outlineClipFraction:F2}, " +
                      $"vertices {vertsBefore} -> {vertsAfter}.");
        }

        return true;
    }

    /// The frame whose outline becomes the collider. Falls back to whatever the renderer
    /// currently shows if the frames haven't streamed in from Resources yet.
    Sprite ResolveCollisionSprite()
    {
        if (coreSprites != null && coreSprites.Length > 0)
            return coreSprites[Mathf.Clamp(collisionFrameIndex, 0, coreSprites.Length - 1)];

        var sr = ResolveRenderer();
        return sr != null ? sr.sprite : null;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    /// Distance from p to the segment ab.
    static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-9f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    /// Douglas-Peucker on a closed path. Split at the vertex furthest from index 0 so the loop
    /// becomes two open chains, simplify each, recombine.
    ///
    /// The important property is that error is measured against the ORIGINAL polyline, not
    /// against current neighbours. A naive "drop any vertex close to the chord between its
    /// neighbours, repeat" version accumulates error every pass and flattens gentle curves
    /// into triangles - on this sprite it collapsed 15908 vertices to 24. This one takes the
    /// same input to 209 with a 0.02% area change.
    static void DPChain(IList<Vector2> pts, int from, int to, float tol, bool[] keep)
    {
        var stack = new List<int> { from, to };

        while (stack.Count >= 2)
        {
            int e = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
            int b = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
            if (e <= b + 1) continue;

            float worst = 0f;
            int idx = -1;
            for (int k = b + 1; k < e; k++)
            {
                float d = PointSegmentDistance(pts[k], pts[b], pts[e]);
                if (d > worst) { worst = d; idx = k; }
            }

            if (worst > tol && idx > 0)
            {
                keep[idx] = true;
                stack.Add(b); stack.Add(idx);
                stack.Add(idx); stack.Add(e);
            }
        }
    }

    static List<Vector2> Simplify(IList<Vector2> poly, float tol)
    {
        int n = poly.Count;
        if (tol <= 0f || n < 4) return new List<Vector2>(poly);

        int far = 0;
        float best = -1f;
        for (int k = 0; k < n; k++)
        {
            float d = (poly[k] - poly[0]).sqrMagnitude;
            if (d > best) { best = d; far = k; }
        }
        if (far == 0) return new List<Vector2>(poly);

        var keep = new bool[n];
        keep[0] = true;
        keep[far] = true;
        DPChain(poly, 0, far, tol, keep);

        var rot = new List<Vector2>(n - far + 1);
        for (int k = far; k < n; k++) rot.Add(poly[k]);
        rot.Add(poly[0]);

        var keep2 = new bool[rot.Count];
        keep2[0] = true;
        keep2[rot.Count - 1] = true;
        DPChain(rot, 0, rot.Count - 1, tol, keep2);

        for (int k = 1; k < rot.Count - 1; k++)
            if (keep2[k]) keep[(far + k) % n] = true;

        var result = new List<Vector2>(n);
        for (int k = 0; k < n; k++) if (keep[k]) result.Add(poly[k]);
        return result.Count >= 3 ? result : new List<Vector2>(poly);
    }

    /// Convex-hull indices, used to locate pockets. Same monotone chain as ConvexHull, but
    /// returning positions in the original path so the vertices between two hull points can
    /// be identified as a pocket.
    static List<int> HullIndices(IList<Vector2> pts)
    {
        var idx = new List<int>(pts.Count);
        for (int i = 0; i < pts.Count; i++) idx.Add(i);
        idx.Sort((p, q) => pts[p].x != pts[q].x ? pts[p].x.CompareTo(pts[q].x)
                                                : pts[p].y.CompareTo(pts[q].y));

        var hull = new List<int>(pts.Count * 2);

        for (int k = 0; k < idx.Count; k++)
        {
            while (hull.Count >= 2 &&
                   Cross(pts[hull[hull.Count - 2]], pts[hull[hull.Count - 1]], pts[idx[k]]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(idx[k]);
        }

        int floor = hull.Count + 1;
        for (int k = idx.Count - 2; k >= 0; k--)
        {
            while (hull.Count >= floor &&
                   Cross(pts[hull[hull.Count - 2]], pts[hull[hull.Count - 1]], pts[idx[k]]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(idx[k]);
        }

        if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// Fills concave pockets whose MOUTH is narrower than `maxMouth`, leaving wide bays alone.
    ///
    /// A pocket is exactly a convex deficiency: a run of vertices lying between two consecutive
    /// hull points. Its mouth is exactly that hull edge. Measuring the real mouth is what makes
    /// this safe - an earlier version tested the chord between a reflex vertex's immediate
    /// NEIGHBOURS, which on a finely tessellated path is tiny everywhere, so iterating ate all
    /// concavity and converged on the convex hull. Here the canopy/mound bay has a long hull
    /// edge and survives untouched.
    static List<Vector2> FillPockets(IList<Vector2> poly, float maxMouth)
    {
        int n = poly.Count;
        if (maxMouth <= 0f || n < 4) return new List<Vector2>(poly);

        var hull = HullIndices(poly);
        if (hull.Count < 3) return new List<Vector2>(poly);

        // Walk the hull in the same rotational direction as the path, or the spans invert.
        if (SignedArea(poly) < 0f) hull.Reverse();

        float maxMouthSq = maxMouth * maxMouth;
        var remove = new HashSet<int>();

        for (int k = 0; k < hull.Count; k++)
        {
            int i = hull[k], j = hull[(k + 1) % hull.Count];
            int span = ((j - i) % n + n) % n;

            if (span <= 1) continue;                                   // no pocket here
            if ((poly[j] - poly[i]).sqrMagnitude >= maxMouthSq) continue;  // a bay, keep it

            for (int t = 1; t < span; t++) remove.Add((i + t) % n);
        }

        if (remove.Count == 0) return new List<Vector2>(poly);

        var result = new List<Vector2>(n);
        for (int k = 0; k < n; k++) if (!remove.Contains(k)) result.Add(poly[k]);
        return result.Count >= 3 ? result : new List<Vector2>(poly);
    }

    /// Chaikin corner cutting on a closed path. Each pass replaces every vertex with two
    /// points at 1/4 and 3/4 along its outgoing edge, so every sharp corner becomes a short
    /// bevel an enemy can slide across instead of stalling on.
    static List<Vector2> Chaikin(IList<Vector2> poly, int passes)
    {
        var cur = new List<Vector2>(poly);

        for (int pass = 0; pass < passes && cur.Count >= 3; pass++)
        {
            var next = new List<Vector2>(cur.Count * 2);
            for (int i = 0; i < cur.Count; i++)
            {
                Vector2 a = cur[i], b = cur[(i + 1) % cur.Count];
                next.Add(Vector2.LerpUnclamped(a, b, 0.25f));
                next.Add(Vector2.LerpUnclamped(a, b, 0.75f));
            }
            cur = next;
        }
        return cur;
    }

    /// Sutherland-Hodgman against the single half-plane y <= clipY. One half-plane keeps this
    /// to a dozen lines and cannot produce self-intersections, which a general polygon clip can.
    static List<Vector2> ClipBelow(List<Vector2> poly, float clipY)
    {
        var result = new List<Vector2>(poly.Count + 4);

        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 cur = poly[i], prev = poly[j];
            bool curIn = cur.y <= clipY, prevIn = prev.y <= clipY;

            if (curIn != prevIn)
            {
                float dy = cur.y - prev.y;
                float t = Mathf.Abs(dy) < 1e-6f ? 0f : (clipY - prev.y) / dy;
                result.Add(new Vector2(Mathf.Lerp(prev.x, cur.x, t), clipY));
            }

            if (curIn) result.Add(cur);
        }

        return result;
    }

    static float SignedArea(IList<Vector2> path)
    {
        float a = 0f;
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
            a += (path[j].x * path[i].y) - (path[i].x * path[j].y);
        return a * 0.5f;
    }

    /// Andrew's monotone chain. NOT used by default - simplifyToConvexHull is off, because
    /// hulling this sprite would fill the whole bay between canopy and mound with invisible
    /// collision. Kept working because the previous version threw ArgumentOutOfRangeException
    /// on any input: it shared one pop-guard between both chains, so the first pass indexed
    /// hull[-1] as soon as the stack held a single point. The two chains need different
    /// guards, hence two explicit loops.
    static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        if (pts == null || pts.Count < 3) return new List<Vector2>(pts ?? new List<Vector2>());

        var sorted = new List<Vector2>(pts);
        sorted.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));

        var hull = new List<Vector2>(sorted.Count * 2);

        for (int i = 0; i < sorted.Count; i++)                       // lower chain
        {
            while (hull.Count >= 2 &&
                   Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(sorted[i]);
        }

        int floor = hull.Count + 1;                                   // upper chain
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            while (hull.Count >= floor &&
                   Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(sorted[i]);
        }

        if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);            // closing duplicate
        return hull;
    }
    #endregion

    /// How much of the pivot-to-pivot distance is swallowed by the Core's own body, as seen
    /// from `fromWorld`. EnemyController subtracts this so attackRange is measured against the
    /// tree's SURFACE instead of a pivot buried inside it.
    ///
    /// This is what makes a full-canopy collider attackable, and it is only consulted for the
    /// Core - every other target keeps the plain pivot-to-pivot test, so no existing
    /// attackRange needs retuning. Returns 0 with no collider, degrading to the old behaviour.
    public float GetMeleeSurfaceInset(Vector2 fromWorld)
    {
        var col = spriteCollider != null ? spriteCollider : GetComponent<Collider2D>();
        if (col == null) return 0f;

        Vector2 pivot = transform.position;
        float pivotDist = Vector2.Distance(pivot, fromWorld);

        // ClosestPoint returns the query point itself when it lies inside the collider, so
        // an enemy that has clipped inside reports the full distance and lands at 0 range.
        Vector2 surface = col.ClosestPoint(fromWorld);
        return Mathf.Clamp(Vector2.Distance(pivot, surface), 0f, pivotDist);
    }

    /// Guarantees that an enemy standing against the Core is inside its own attackRange.
    ///
    /// Works entirely in WORLD units and converts back to local at the end, because
    /// CircleCollider2D.radius/offset are local and the Core is scaled by coreSize.
    /// Local values are the right thing to store: when UpdateScaleEffect shrinks the
    /// Core at low energy the body shrinks with the art, which only ever helps.
    ///
    /// Runs once, after ApplyColliderSizeMultiplier. When useFootprintAsCollider is on
    /// it overwrites the radius outright, so colliderSizeMultiplier stops mattering.
    void ApplyMeleeReachClamp()
    {
        if (!enforceMeleeReachability) return;

        if (spriteCollider == null) spriteCollider = GetComponent<Collider2D>();

        var circle = spriteCollider as CircleCollider2D;
        if (circle == null)
        {
            if (spriteCollider is PolygonCollider2D)
            {
                // Expected when useFullOutlineCollider is on. A traced silhouette cannot be
                // clamped into reach - shrinking it would just un-trace it - so reachability
                // is handled by outlineClipFraction instead. Run "Log Melee Reachability" to
                // confirm the clip line is low enough.
                if (logReachabilityFix)
                    Debug.Log("[CentralCore] outline collider in use; the shrink-to-fit clamp is " +
                              "skipped. Reach is governed by outlineClipFraction.");
                return;
            }

            Debug.LogWarning("CentralCore: the melee-reach clamp needs a CircleCollider2D, but " +
                             $"collisionConfig produced {(spriteCollider == null ? "no collider" : spriteCollider.GetType().Name)}. " +
                             "Set collisionConfig.colliderType = Circle, or short-reach enemies " +
                             "will stall against the Core without attacking.");
            return;
        }

        float scale = Mathf.Abs(transform.lossyScale.x);
        if (scale < 0.0001f) return;

        // ── Desired geometry, in world units ─────────────────────────────────
        float radius = circle.radius * scale;
        if (useFootprintAsCollider)
        {
            float footprint = GetGroundFootprintRadius();
            if (footprint > 0.01f) radius = footprint;
        }

        Vector2 offset = new Vector2(
            GetGroundFootprintCenter().x - transform.position.x,
            ComputeYSortOffset()) * Mathf.Clamp01(colliderGroundBias);

        // ── The invariant ────────────────────────────────────────────────────
        // Worst case, an enemy contacts the far side of the collider, so its
        // pivot sits |offset| + radius + its own body radius from ours.
        float budget = meleeReachBudget - reachSafetyMargin - assumedEnemyBodyRadius;

        if (budget <= 0.05f)
        {
            Debug.LogWarning($"CentralCore: meleeReachBudget {meleeReachBudget:F2} leaves nothing " +
                             $"after margin {reachSafetyMargin:F2} and enemy body {assumedEnemyBodyRadius:F2}. " +
                             "The Core cannot be made reachable by shrinking alone — the enemy's " +
                             "attackRange is shorter than its own body. Leaving the collider as-is.");
            return;
        }

        // Give up the offset before the radius: the radius is the visible footprint
        // enemies read as "the base of the tree", the offset is only polish.
        float clampedRadius = Mathf.Min(radius, budget);
        float maxOffset = Mathf.Max(0f, budget - clampedRadius);
        Vector2 clampedOffset = Vector2.ClampMagnitude(offset, maxOffset);

        circle.radius = clampedRadius / scale;
        circle.offset = clampedOffset / scale;

        if (logReachabilityFix)
        {
            float contact = clampedOffset.magnitude + clampedRadius + assumedEnemyBodyRadius;
            Debug.Log($"[CentralCore] melee reach — radius {radius:F2} -> {clampedRadius:F2}, " +
                      $"offset {offset} -> {clampedOffset}, worst-case pivot-to-pivot at contact " +
                      $"{contact:F2} vs attackRange {meleeReachBudget:F2} " +
                      $"(margin {meleeReachBudget - contact:F2}).");
        }
    }

    /// Right-click the component in Play Mode. Sweeps 24 approach directions with a real
    /// CircleCast, so it reports where an enemy ACTUALLY stops against whatever collider is
    /// live — circle or traced outline — instead of assuming a shape. Then it prints the
    /// surface-corrected distance, which only matters if you ever wire GetMeleeSurfaceInset
    /// into EnemyController to run an unclipped canopy.
    [ContextMenu("Log Melee Reachability")]
    public void LogMeleeReachability()
    {
        var col = spriteCollider != null ? spriteCollider : GetComponent<Collider2D>();
        if (col == null) { Debug.LogWarning("CentralCore: no collider yet - enter Play Mode first."); return; }

        Vector2 pivot = transform.position;
        const float FAR = 12f;
        const int STEPS = 24;
        float body = Mathf.Max(0.01f, assumedEnemyBodyRadius);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== CORE MELEE REACHABILITY ===");
        sb.AppendLine($"  collider {col.GetType().Name}  bounds {col.bounds.size}  lossyScale {transform.lossyScale.x:F2}");
        sb.AppendLine($"  probe body radius {body:F2}, reach budget {meleeReachBudget:F2}");
        sb.AppendLine("  deg |  stops at | surface-corrected | verdict");

        int deadRaw = 0, deadCorrected = 0, missed = 0;
        float worst = 0f, worstDeg = 0f;

        for (int i = 0; i < STEPS; i++)
        {
            float deg = i * (360f / STEPS);
            float rad = deg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            // Fire inward from outside and take OUR hit specifically - other colliders
            // (enemies, decor) sit on the same sweep and would otherwise mask it.
            var hits = Physics2D.CircleCastAll(pivot + dir * FAR, body, -dir, FAR);
            float stop = -1f;
            for (int h = 0; h < hits.Length; h++)
            {
                if (hits[h].collider != col) continue;
                stop = FAR - hits[h].distance;
                break;
            }

            if (stop < 0f) { missed++; continue; }

            Vector2 stopPos = pivot + dir * stop;
            float corrected = stop - GetMeleeSurfaceInset(stopPos);

            bool okRaw = stop <= meleeReachBudget;
            bool okCorrected = corrected <= meleeReachBudget;
            if (!okRaw) deadRaw++;
            if (!okCorrected) deadCorrected++;
            if (stop > worst) { worst = stop; worstDeg = deg; }

            sb.AppendLine($"  {deg,3:F0} | {stop,9:F2} | {corrected,17:F2} | " +
                          (okCorrected ? "OK" : okRaw ? "OK" : "UNREACHABLE"));
        }

        sb.AppendLine($"  worst approach {worstDeg:F0}deg at {worst:F2} from pivot");
        sb.AppendLine($"  pivot-distance metric : {deadRaw} of {STEPS - missed} directions unreachable");
        sb.AppendLine($"  surface-distance metric: {deadCorrected} of {STEPS - missed} directions unreachable");
        if (missed > 0)
            sb.AppendLine($"  ({missed} direction(s) never hit our collider - concave pocket or a gap in the outline)");
        if (deadRaw > 0 && deadCorrected == 0)
            sb.AppendLine("  => lower outlineClipFraction until the pivot-distance row reads 0. " +
                          "That is the metric EnemyController actually uses.");

        Debug.Log(sb.ToString());
    }

    // TODO remove the helper methods
    //public Bounds GetSpriteBounds() => SpriteCollisionManager.GetSpriteBounds(gameObject);
    //public bool IsPointWithinSprite(Vector3 worldPoint) => SpriteCollisionManager.IsPointWithinSprite(gameObject, worldPoint);
    //public float GetCollisionRadius() => SpriteCollisionManager.GetCollisionRadius(gameObject);
    //public void UpdateCollisionSettings() => spriteCollider = SpriteCollisionManager.UpdateCollisionSettings(gameObject, collisionConfig);


    // The Core used to be a single sliced spritesheet. It is now a folder of loose
    // numbered frames (00.png ... 23.png), so load the whole folder and sort it.
    // Resources.LoadAll gives NO ordering guarantee - without the sort the frames
    // play in whatever order the import produced, which looks like random flicker.
    //
    // LoadAll on a FOLDER also returns every sprite in it, and the old
    // 'central_core_sprite' sheet still lives in this folder: its slices would be
    // appended to the animation and the last-frame hold would land on an old sprite.
    // So: if any numbered frames are present, they ARE the animation - everything
    // else in the folder is ignored.
    void LoadCoreSprites()
    {
        //coreSprites = Resources.LoadAll<Sprite>("Sprites/Buildings/central_core_spritesheet2");
        var loaded = Resources.LoadAll<Sprite>(coreSpriteFolder) ?? System.Array.Empty<Sprite>();

        var numbered = loaded.Where(s => HasLeadingFrameNumber(s.name))
                             .OrderBy(s => ParseLeadingFrameInt(s.name, int.MaxValue))
                             .ToArray();

        if (numbered.Length > 0)
        {
            int ignored = loaded.Length - numbered.Length;
            if (ignored > 0 && !warnedAboutStrayCoreSprites)
            {
                // Once per session, not once per spawn. It's a real, actionable asset problem
                // (that old sheet is still compiled into the build), so it isn't gated behind
                // verboseLogging — but the orchestrator respawns the Core, and repeating it
                // every time just trains you to ignore the console.
                warnedAboutStrayCoreSprites = true;
                Debug.LogWarning($"CentralCore: ignoring {ignored} non-numbered sprite(s) in " +
                    $"Resources/'{coreSpriteFolder}' (likely the old '{string.Join(", ", loaded.Where(s => !HasLeadingFrameNumber(s.name)).Select(s => s.name).Take(3))}' " +
                    "sheet). Move that asset out of this folder to silence this.");
            }
            coreSprites = numbered;
        }
        else
        {
            // No numbered frames at all - fall back to whatever is there, unsorted,
            // so an old sliced spritesheet path still animates in slice order.
            coreSprites = loaded;
        }

        if (coreSprites.Length == 0)
        {
            Debug.LogError($"CentralCore: loaded 0 frames from Resources/'{coreSpriteFolder}'. " +
                $"Verify the frames sit in Assets/Resources/{coreSpriteFolder}/ and that each PNG's " +
                "Texture Type is 'Sprite (2D and UI)'.");
            return;
        }

        if (verboseLogging)
            Debug.Log($"CentralCore: loaded {coreSprites.Length} frame(s) from Resources/'{coreSpriteFolder}' " +
                      $"(first='{coreSprites[0].name}', last='{coreSprites[coreSprites.Length - 1].name}').");

        spriteRenderer.sprite = coreSprites[Mathf.Clamp(spriteStartIndex, 0, coreSprites.Length - 1)];
    }

    // True for "00", "07_glow"; false for "central_core_sprite_3".
    static bool HasLeadingFrameNumber(string name) =>
        !string.IsNullOrEmpty(name) && char.IsDigit(name[0]);

    // "07" -> 7, "07_glow" -> 7.
    static int ParseLeadingFrameInt(string name, int fallback)
    {
        if (string.IsNullOrEmpty(name)) return fallback;
        int i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        return i > 0 && int.TryParse(name.Substring(0, i), out int v) ? v : fallback;
    }

    void StartAnimationIfEnabled()
    {
        if (enableAnimation && coreSprites?.Length > 0)
            StartCoreAnimation();
    }

    void RegisterWithEnergyManager()
    {
        if (isRegisteredWithEnergyManager) return;

        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.RegisterEnergyConsumer(this);
            isRegisteredWithEnergyManager = true;
        }
    }

    void SetupEnergyBar()
    {
        if (!energyBarSettings.show) return;

        energyBar = gameObject.AddComponent<EnergyBar>();
        energyBar.showEnergyBar = energyBarSettings.show;
        energyBar.energyBarHeight = energyBarSettings.height;
        energyBar.energyBarWidth = energyBarSettings.width;
        energyBar.energyBarOffset = energyBarSettings.offset;
        energyBar.showEnergyText = energyBarSettings.showText;

        if (EnergyManager.Instance != null)
        {
            energyBar.SetColors(
                EnergyManager.Instance.normalColor,
                EnergyManager.Instance.lowEnergyColor,
                EnergyManager.Instance.criticalEnergyColor,
                EnergyManager.Instance.depletedEnergyColor
            );
        }

        energyBar.Initialize(this, spriteRenderer);
    }
    #endregion

    #region Highlight System for Repair
    public void SetHighlight(bool highlight)
    {
        if (isHighlighted == highlight) return;

        isHighlighted = highlight;

        if (highlight)
        {
            Color currentColor = spriteRenderer.color;
            spriteRenderer.color = Color.Lerp(currentColor, highlightColor, 0.6f);
        }
        else
        {
            UpdateEnergyVisuals();
        }
    }

    public bool IsHighlighted() => isHighlighted;

    private Color GetCurrentEnergyColor()
    {
        if (EnergyManager.Instance != null)
        {
            return EnergyManager.Instance.GetEnergyColor(this);
        }
        return normalColor;
    }
    #endregion

    #region IDamageable Implementation

    // In CentralCore.cs, replace the TakeDamage method with this:
    public bool TakeDamage(float damageAmount, GameObject damageSource = null)
    {
        if (immuneToEnemyDamage || isDestroyed) return false;

        // Check for shield component first
        var shield = GetComponent<CoreShieldMatrix>();
        if (shield != null && shield.IsShieldActive())
        {
            bool allDamageAbsorbed = shield.AbsorbDamage(ref damageAmount);

            if (allDamageAbsorbed)
            {
                // All damage absorbed by shield, no damage to core
                return false;
            }
            // If some damage remains, continue to apply it to core below
        }


        float actualDamage = damageAmount * (1f - armorReduction);
        bool wasCritical = IsInCriticalState();

        ConsumeEnergy(actualDamage);

        // Play damage sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerDamage, transform.position);
        }

        if (enableDamageEffects)
        {
            StartDamageFlash();
            if (IsInCriticalState())
            {
                StartCriticalShake();
            }
        }

        OnDamageTaken?.Invoke(actualDamage, damageSource);

        if (!wasCritical && IsInCriticalState())
        {
            OnCoreEnteredCriticalState?.Invoke();
        }

        if (IsEnergyDepleted())
        {
            DestroyCore(damageSource);
            return true;
        }

        return false;
    }

    public bool CanTakeDamage() => !immuneToEnemyDamage && !isDestroyed;
    public float GetCurrentHealth() => currentEnergy;
    public float GetMaxHealth() => maxEnergy;
    public float GetHealthPercentage() => GetEnergyPercentage();
    public bool IsDestroyed() => isDestroyed;
    public bool IsInCriticalState() => IsEnergyLow() && !IsEnergyDepleted();

    private void DestroyCore(GameObject damageSource)
    {
        if (isDestroyed) return;

        Debug.Log("CentralCore: Destroying core");

        // Play Central Core death sound
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.centralCoreDeath, transform.position);
        }

        isDestroyed = true;

        // Stop all updates
        StopAllEffects();

        // Clean up energy values to prevent NaN issues
        currentEnergy = 0f;

        OnCoreDestroyed?.Invoke(damageSource);

        // Only trigger game over if not already triggered
        if (EnergyManager.Instance != null && !EnergyManager.Instance.IsGameOver())
        {
            EnergyManager.Instance.TriggerGameOver();
        }
    }
    private void StartDamageFlash()
    {
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
        }
        damageFlashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;

        // Double blink with additive bright color for better visibility
        for (int i = 0; i < 2; i++)
        {
            spriteRenderer.color = damageFlashColor; // Bright additive color
            yield return new WaitForSeconds(damageFlashDuration);
            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(0.05f);
        }

        damageFlashCoroutine = null;
    }

    private void StartCriticalShake()
    {
        if (shakeCoroutine != null) return;
        shakeCoroutine = StartCoroutine(CriticalShakeCoroutine());
    }

    private IEnumerator CriticalShakeCoroutine()
    {
        while (IsInCriticalState() && !isDestroyed)
        {
            Vector3 shakeOffset = Random.insideUnitCircle * criticalHealthShakeIntensity;
            transform.position = originalPosition + shakeOffset;
            yield return new WaitForSeconds(0.05f);
        }

        transform.position = originalPosition;
        shakeCoroutine = null;
    }

    private void StopAllEffects()
    {
        if (damageFlashCoroutine != null)
        {
            StopCoroutine(damageFlashCoroutine);
            damageFlashCoroutine = null;
        }

        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }

        transform.position = originalPosition;
    }
    #endregion

    #region Update Logic
    void UpdateCoreState()
    {
        if (isDestroyed) return;

        if (!isRegisteredWithEnergyManager)
        {
            RegisterWithEnergyManager();
            if (!isRegisteredWithEnergyManager) return;
        }

        // Add safety check before updating energy state
        try
        {
            UpdateEnergyState();

            if (CanOperate())
                ProcessCoreOperations();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"CentralCore: Error in UpdateCoreState: {e.Message}");
            // Prevent further updates if we're in a bad state
            if (float.IsNaN(currentEnergy) || float.IsNaN(maxEnergy))
            {
                Debug.LogError("CentralCore: Detected corrupted energy values, destroying core");
                DestroyCore(null);
            }
        }
    }

    void UpdateEnergyState()
    {
        bool wasEnergyDepleted = isEnergyDepleted;
        bool wasEnergyLow = isEnergyLow;
        bool wasCritical = IsInCriticalState();

        isEnergyDepleted = IsEnergyDepleted();
        isEnergyLow = IsEnergyLow();

        if (isEnergyDepleted != wasEnergyDepleted || isEnergyLow != wasEnergyLow)
            UpdateEnergyDependentSystems();

        bool isCritical = IsInCriticalState();
        if (wasCritical && !isCritical)
        {
            OnCoreExitedCriticalState?.Invoke();
        }
    }

    void UpdateEnergyDependentSystems()
    {
        UpdateAnimationSpeed();
        UpdateVisualState();
    }

    void UpdateAnimationSpeed()
    {
        float newSpeed = CalculateAnimationSpeed();

        if (Mathf.Abs(newSpeed - currentAnimationSpeed) > 0.01f)
        {
            animationSpeed = newSpeed;
            currentAnimationSpeed = newSpeed;
            // No StartCoreAnimation() here: AnimateCoreLoop re-reads animationSpeed on
            // every frame, so the new cadence is picked up without restarting the cycle
            // from frame 00 (which used to happen on every energy-state change).
        }
    }

    float CalculateAnimationSpeed()
    {
        if (isEnergyDepleted) return 0.4f;
        if (isEnergyLow) return 0.2f;
        return 0.1f;
    }

    void ProcessCoreOperations()
    {
        // TODO Add Core-specific operations when energy is sufficient
    }

    bool CanOperate() => !requiresEnergyToFunction || !isEnergyDepleted;
    #endregion

    #region Animation System
    // Self-contained instead of Utilities.AnimateSpritePingPong: that helper plays
    // forward-then-backward at a fixed cadence and has nowhere to hang a hold, which
    // is exactly what we need here (play 00 -> 23, sit on 23 for a few seconds, repeat).
    void StartCoreAnimation()
    {
        StopAnimation();

        if (!enableAnimation || coreSprites == null || coreSprites.Length == 0) return;

        animationCoroutine = StartCoroutine(AnimateCoreLoop());
    }

    private IEnumerator AnimateCoreLoop()
    {
        int start = Mathf.Clamp(spriteStartIndex, 0, coreSprites.Length - 1);

        // animationFrameCount <= 0, or larger than what actually loaded, means
        // "use every frame from start onwards" - so dropping in more frames later
        // needs no inspector change.
        int count = animationFrameCount > 0
            ? Mathf.Min(animationFrameCount, coreSprites.Length - start)
            : coreSprites.Length - start;

        // A single frame has nothing to animate: show it and stop, rather than
        // spinning a coroutine forever for nothing.
        if (count < 2)
        {
            if (spriteRenderer != null) spriteRenderer.sprite = coreSprites[start];
            animationCoroutine = null;
            yield break;
        }

        int i = 0;
        while (true)
        {
            if (spriteRenderer == null || isDestroyed) yield break;

            spriteRenderer.sprite = coreSprites[start + i];

            bool isLastFrame = (i == count - 1);

            if (isLastFrame && pauseOnLastFrame)
            {
                // Deliberately NOT scaled by animationSpeed: the pause is a beat in the
                // loop, and a low-energy Core slowing its frames shouldn't stretch it too.
                yield return new WaitForSeconds(Mathf.Max(0f, lastFramePauseDuration));
            }
            else
            {
                // animationSpeed is read fresh every frame, so an energy-state change
                // takes effect on the next frame instead of restarting the cycle
                // (a restart would snap the Core back to frame 00 mid-loop).
                yield return new WaitForSeconds(Mathf.Max(0.01f, animationSpeed));
            }

            i = (i + 1) % count;
        }
    }

    public void SetAnimationSettings(bool enable, float speed, int frameCount, int startIndex)
    {
        enableAnimation = enable;
        animationSpeed = speed;
        animationFrameCount = frameCount;
        spriteStartIndex = startIndex;

        if (enableAnimation && coreSprites?.Length > 0)
            StartCoreAnimation();
        else
            StopAnimation();
    }

    /// Change the hold on the last frame at runtime. Takes effect on the next cycle.
    public void SetLastFramePause(bool enable, float duration)
    {
        pauseOnLastFrame = enable;
        lastFramePauseDuration = Mathf.Max(0f, duration);
    }

    void StopAnimation()
    {
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }
    #endregion

    #region Visual Updates
    void UpdateVisualState()
    {
        UpdateEnergyVisuals();
        UpdateScaleEffect();
    }

    void UpdateEnergyVisuals()
    {
        if (isHighlighted) return;

        if (spriteRenderer != null && EnergyManager.Instance != null)
            EnergyManager.Instance.UpdateConsumerVisuals(this, spriteRenderer);
    }

    void UpdateScaleEffect()
    {
        // Add NaN protection and validation
        if (isDestroyed) return;

        if (isEnergyDepleted)
        {
            float energyPercentage = GetEnergyPercentage();
            float deadThreshold = EnergyManager.Instance?.GetCoreDeadThreshold() ?? 0.1f;

            // Validate all values to prevent NaN
            if (float.IsNaN(energyPercentage) || float.IsInfinity(energyPercentage))
            {
                Debug.LogWarning($"CentralCore: Invalid energyPercentage detected: {energyPercentage}, resetting to 0");
                energyPercentage = 0f;
            }

            if (float.IsNaN(deadThreshold) || float.IsInfinity(deadThreshold) || deadThreshold <= 0f)
            {
                Debug.LogWarning($"CentralCore: Invalid deadThreshold detected: {deadThreshold}, using fallback 0.1f");
                deadThreshold = 0.1f;
            }

            // Safe division with bounds checking
            float ratio = deadThreshold > 0f ? Mathf.Clamp01(energyPercentage / deadThreshold) : 0f;
            float scaleMultiplier = Mathf.Lerp(0.8f, 1f, ratio);

            // Final NaN check before applying scale
            if (float.IsNaN(scaleMultiplier) || float.IsInfinity(scaleMultiplier))
            {
                Debug.LogWarning($"CentralCore: Invalid scaleMultiplier calculated: {scaleMultiplier}, using default 0.8f");
                scaleMultiplier = 0.8f;
            }

            Vector3 newScale = originalScale * scaleMultiplier;

            // Validate the final scale vector
            if (float.IsNaN(newScale.x) || float.IsNaN(newScale.y) || float.IsNaN(newScale.z) ||
                float.IsInfinity(newScale.x) || float.IsInfinity(newScale.y) || float.IsInfinity(newScale.z))
            {
                Debug.LogWarning($"CentralCore: Invalid newScale calculated: {newScale}, using originalScale: {originalScale}");
                newScale = originalScale;
            }

            transform.localScale = newScale;
        }
        else
        {
            // Validate originalScale before using it
            if (float.IsNaN(originalScale.x) || float.IsNaN(originalScale.y) || float.IsNaN(originalScale.z))
            {
                Debug.LogWarning($"CentralCore: originalScale is invalid: {originalScale}, resetting to default");
                originalScale = Vector3.one * coreSize;
            }

            transform.localScale = originalScale;
        }
    }
    #endregion

    #region IEnergyConsumer Implementation
    public void ConsumeEnergy(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            Debug.LogWarning($"CentralCore: Trying to consume invalid energy amount: {amount}, ignoring");
            return;
        }

        if (isDestroyed)
        {
            Debug.LogWarning("CentralCore: Trying to consume energy on destroyed core, ignoring");
            return;
        }

        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Max(0f, currentEnergy - amount);

        // Validate result
        if (float.IsNaN(currentEnergy) || float.IsInfinity(currentEnergy))
        {
            Debug.LogWarning($"CentralCore: Energy became invalid after consumption, resetting to 0");
            currentEnergy = 0f;
        }

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();

            if (currentEnergy <= 0f && previousEnergy > 0f)
                OnEnergyDepleted?.Invoke();
        }
    }

    public void SupplyEnergy(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            Debug.LogWarning($"CentralCore: Trying to supply invalid energy amount: {amount}, ignoring");
            return;
        }

        if (isDestroyed)
        {
            Debug.LogWarning("CentralCore: Trying to supply energy to destroyed core, ignoring");
            return;
        }

        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);

        // Validate result
        if (float.IsNaN(currentEnergy) || float.IsInfinity(currentEnergy))
        {
            Debug.LogWarning($"CentralCore: Energy became invalid after supply, clamping to valid range");
            currentEnergy = Mathf.Clamp(previousEnergy, 0f, maxEnergy);
        }

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();

            if (previousEnergy <= 0f && currentEnergy > 0f)
                OnEnergyRestored?.Invoke();
        }
    }

    public void SetEnergy(float amount)
    {
        float previousEnergy = currentEnergy;
        currentEnergy = Mathf.Clamp(amount, 0f, maxEnergy);

        if (currentEnergy != previousEnergy)
        {
            OnEnergyChanged?.Invoke(currentEnergy);
            UpdateVisualState();
        }
    }

    public void SetMaxEnergy(float amount)
    {
        maxEnergy = amount;
        currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        UpdateVisualState();
    }

    public float GetEnergy() => currentEnergy;
    public float GetMaxEnergy() => maxEnergy;
    public float GetEnergyPercentage()
    {
        // Add validation to prevent NaN
        if (float.IsNaN(currentEnergy) || float.IsInfinity(currentEnergy))
        {
            Debug.LogWarning($"CentralCore: currentEnergy is invalid: {currentEnergy}, resetting to 0");
            currentEnergy = 0f;
        }

        if (float.IsNaN(maxEnergy) || float.IsInfinity(maxEnergy) || maxEnergy <= 0f)
        {
            Debug.LogWarning($"CentralCore: maxEnergy is invalid: {maxEnergy}, resetting to default");
            maxEnergy = 100f;
            currentEnergy = Mathf.Min(currentEnergy, maxEnergy);
        }

        return maxEnergy > 0 ? currentEnergy / maxEnergy : 0f;
    }
    public Vector3 GetPosition() => transform.position;

    public bool IsEnergyDepleted() =>
        EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetCoreDeadThreshold();

    public bool IsEnergyLow() =>
        EnergyManager.Instance != null && GetEnergyPercentage() <= EnergyManager.Instance.GetCoreCriticalThreshold();
    #endregion

    #region Public Methods
    public void RestoreEnergy(float amount) => SupplyEnergy(amount);
    public bool HasEnergy() => currentEnergy > 0f;
    public void SetArmor(float newArmor) => armorReduction = Mathf.Clamp01(newArmor);
    public float GetArmor() => armorReduction;
    #endregion

    #region Cleanup
    void Cleanup()
    {
        EnergyManager.Instance?.UnregisterEnergyConsumer(this);
        StopAnimation();
        StopAllEffects();
        if (energyBar != null) Destroy(energyBar);
    }
    #endregion

#if UNITY_EDITOR
    #region Test Methods
    [ContextMenu("Test Set Energy to 50%")]
    void TestSetEnergyTo50Percent()
    {
        SetEnergy(maxEnergy * 0.5f);
    }

    [ContextMenu("Test Highlight On")]
    void TestHighlightOn()
    {
        SetHighlight(true);
    }

    [ContextMenu("Test Highlight Off")]
    void TestHighlightOff()
    {
        SetHighlight(false);
    }
    #endregion
#endif
}


