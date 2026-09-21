using System.Collections.Generic;
using UnityEngine;

//  BerserkVisual
//  Execution order 20000: its Awake runs AFTER EnemyStats.Awake (so the cloned
//    EnemyData exists) but still in the Awake phase, i.e. BEFORE
//    EnemyAnimationController.Start() consumes the sprites/ranges.
//  It rewrites the cloned EnemyData's idle/attack/death ranges to match the
//    generated sheet, so attack timing / hit frame / parry window all keep working
//    through the existing pipeline.
//  The generated frames are cached statically and shared (read-only) across all
//    Berserk instances and across the run, mirroring EnemyAnimationController's
//    folder cache. Call BerserkVisual.Prewarm() from a loading screen to build
//    them ahead of the first spawn if you want to avoid the one-time hitch.


[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyAnimationController))]
[DefaultExecutionOrder(20000)]
public class BerserkVisual : MonoBehaviour, ISpritePrewarm
{
    //  Sheet layout (kept const so the generator and the ranges never disagree) 
    // 12 idle frames = ONE FULL WALK CYCLE (two footfalls), not a breathing loop.
    // 8 attack frames buys room for real anticipation and follow-through — at 6 the
    // wind-up and the bite were adjacent frames and the strike had no weight.
    private const int IDLE_FRAMES = 12;
    private const int ATTACK_FRAMES = 8;
    private const int HIT_FRAME = 5;         // the "chomp shut" frame (relative to attack)
    // Parry window is serialized below (Combat Timing) rather than fixed here, because
    // it is a balance number you will want to retune without recompiling. Note that the
    // values on the EnemyData ASSET are ignored: Awake overwrites them every spawn.

    private const int TEX_W = 288;   // WIDE aspect — squat, broad beast (trimmed for perf)
    private const int TEX_H = 208;

    // Intrinsic size/pivot of the generated sprite. Because the frames are cached
    // and SHARED across every Berserk (fixed pixels-per-unit), these are constants.
    // this is the size at Transform scale 1. The Berserk prefab is
    // authored at Transform scale 0.20 (which ALSO sizes its CircleCollider2D), so an
    // ~8-unit-tall intrinsic sprite renders at ~1.6 units tall / ~2.24 wide on screen,
    // matching the collider (radius 4 × 0.20 = 0.8 world). We deliberately do NOT
    // override the prefab scale from code (that co-scales the collider and blew up the
    // hitbox). To resize, change the prefab's Transform scale — it moves visual +
    // collider together (0.25 → 0.20 is the "20% smaller" pass).
    private const float SPRITE_WORLD_HEIGHT = 8.0f;                // units, at Transform scale 1
    private const float PIVOT_Y = 0.13f;                           // pivot in the smoky base
    private static readonly float SPRITE_WORLD_WIDTH = SPRITE_WORLD_HEIGHT * (float)TEX_W / TEX_H;

    //  Baked-in palette (shared frames → must be run-global, hence static consts).
    //  Deliberately narrow: ONE cold hue (violet) and ONE hot hue (ember), plus cyan
    //  reserved exclusively for digital corruption. The old pass had violet rim + red
    //  aura + orange maw + red ichor all fighting each other, which is what made him
    //  read as muddy rather than menacing.
    private static readonly Color VOID_CORE = new Color(0.012f, 0.011f, 0.020f, 1f); // deep interior
    private static readonly Color VOID_EDGE = new Color(0.060f, 0.050f, 0.082f, 1f); // slightly lifted skin
    private static readonly Color RIM_HOT = new Color(1.00f, 0.34f, 0.10f, 1f);      // up-facing rim (ember)
    private static readonly Color RIM_COOL = new Color(0.44f, 0.11f, 0.60f, 1f);     // down-facing rim (violet)
    private static readonly Color DATA_CYAN = new Color(0.40f, 0.96f, 1.00f, 1f);    // corruption ONLY
    private static readonly Color CRACK = new Color(1.00f, 0.30f, 0.07f, 1f);        // internal fissures
    private static readonly Color EYE = new Color(1f, 0.16f, 0.03f, 1f);             // fierce fiery red
    private static readonly Color EYE_CORE = new Color(1f, 0.94f, 0.70f, 1f);        // white-hot centre
    private static readonly Color VOID_HALO = new Color(0.020f, 0.012f, 0.032f, 1f); // dark backing halo
    private static readonly Color BLOOM = new Color(0.85f, 0.16f, 0.10f, 1f);        // hot backing bloom

    //  Violet striation ramp, sampled straight off the reference crystal. These are the
    //  seams of light running through the black shell — the amethyst reads as the stuff
    //  he's actually made of, with the black being only the crust over it.
    private static readonly Color VEIN_DEEP = new Color(0.110f, 0.000f, 0.137f, 1f);  // #1c0023
    private static readonly Color VEIN_MID = new Color(0.420f, 0.000f, 0.510f, 1f);   // #6b0082
    private static readonly Color VEIN_HOT = new Color(0.671f, 0.275f, 0.761f, 1f);   // #ab46c2
    // Highlight colour. Pale lilac rather than saturated violet — a saturated specular
    // reads as paint, a desaturated one reads as light.
    private static readonly Color SPEC = new Color(0.78f, 0.68f, 0.88f, 1f);

    [Header("Animation Speeds")]
    [Tooltip("Seconds for ONE COMPLETE walk cycle. Driving the loop by total duration " +
             "rather than seconds-per-frame means changing the frame count can never " +
             "silently change how fast he moves. Set 0 to use Idle Speed instead.")]
    [SerializeField] private float idleCycleDuration = 1.05f;
    [Tooltip("Seconds for ONE COMPLETE attack, wind-up through recovery. This is the " +
             "number that matters for gameplay — hit frame and parry window are derived " +
             "from it. Set 0 to use Attack Speed instead.")]
    [SerializeField] private float attackTotalDuration = 0.34f;

    [Tooltip("Fallback seconds-per-frame, used only when the durations above are 0.")]
    [SerializeField] private float idleSpeed = 0.09f;
    [SerializeField] private float attackSpeed = 0.055f;

    [Header("Combat Timing")]
    [Tooltip("First attack frame (0-based) on which a parry connects. THIS IS THE ONLY " +
             "FRAME FIELD THAT WIDENS THE WINDOW - see Parry Frame End. Frames 2-4 are " +
             "the wide-maw telegraph, so 2 opens the window exactly as the gape becomes " +
             "readable. Avoid 0: EnemyController clamps the 'Longer Parry Window' augment " +
             "at frame 0, so a start of 0 leaves that upgrade with nothing to give.")]
    [Range(0, HIT_FRAME - 1)][SerializeField] private int parryFrameStart = 1;
    [Tooltip("Last parryable attack frame, inclusive. RAISING THIS DOES NOTHING. " +
             "EnemyController adjudicates the parry AT the hit frame, comparing when the " +
             "shield was pressed against the window - so any part of the window after the " +
             "hit is in the future and can never be matched. The usable window is always " +
             "(hitFrame - parryFrameStart) x seconds-per-frame. To actually lengthen it, " +
             "lower Parry Frame Start or raise Attack Total Duration.")]
    [Range(0, HIT_FRAME - 1)][SerializeField] private int parryFrameEnd = 4;

    [Tooltip("Guaranteed MINIMUM usable parry window, in seconds. If the attack is too " +
             "fast to give this much, the attack animation is automatically slowed until " +
             "it does. This exists because the parry indicator turns on exactly when the " +
             "window opens and off exactly when it closes - it gives the player ZERO lead " +
             "time, so if the window is shorter than reaction time (~0.25s) it is " +
             "literally impossible to parry by reacting to the indicator, and every " +
             "attempt lands late and registers as a block. Set 0 to disable and drive " +
             "the timing purely from Attack Total Duration.")]
    [Range(0f, 0.6f)][SerializeField] private float minParryWindow = 0.28f;

    [Header("Size")]
    [Tooltip("If > 0, forces this enemy's Transform scale. This co-scales the " +
             "CircleCollider2D too, so the hitbox stays matched to the visual. " +
             "0.16 is ~20% smaller than the old 0.20. Set 0 to leave the prefab's " +
             "own Transform scale alone.")]
    [SerializeField] private float rootScaleOverride = 0.16f;

    [Header("Health Bar")]
    [Tooltip("Raises ONLY this enemy's health bar so it clears the tall silhouette, " +
             "without touching the shared EnemyHpCanvas 'offset' that every other " +
             "enemy uses. Value is extra world-units above the top of the sprite.")]
    [SerializeField] private float healthBarMargin = 0.3f;

    [Header("Glitch")]
    [Tooltip("Baseline chromatic RGB-split distance, as a fraction of sprite height.")]
    [SerializeField] private float chromaticBase = 0.018f;
    [Tooltip("Extra chromatic split at the peak of a glitch burst.")]
    [SerializeField] private float chromaticBurst = 0.10f;
    [Tooltip("Average seconds between random glitch bursts.")]
    [SerializeField] private float glitchInterval = 0.65f;
    [Tooltip("Alpha of the two chromatic ghost layers at rest.")]
    [Range(0f, 1f)][SerializeField] private float ghostBaseAlpha = 0.30f;

    [Header("Glitch Slices")]
    [Tooltip("Horizontal bands of the CURRENT frame, torn sideways during a glitch " +
             "burst — the datamosh 'sliced VHS' read. These are real cut-outs of the " +
             "live sprite, so they always match whatever pose he's in. 0 to disable.")]
    [Range(0, 6)][SerializeField] private int sliceCount = 3;
    [Tooltip("How far a slice can tear sideways, as a fraction of sprite width.")]
    [SerializeField] private float sliceThrow = 0.11f;
    [Tooltip("Seconds between slice re-rolls. Small = frantic strobing; " +
             "large = slower, heavier tearing.")]
    [SerializeField] private float sliceRerollInterval = 0.035f;

    [Header("Floating Threads")]
    [Tooltip("Wispy shadow filaments that trail off the body, sway, and glitch-jump — " +
             "the stringy, unravelling look. 0 to disable.")]
    [Range(0, 20)][SerializeField] private int threadCount = 10;
    [Tooltip("Thread length as a fraction of sprite height.")]
    [SerializeField] private float threadLength = 0.55f;

    [Header("Floating Parts")]
    [Tooltip("Number of detached shadow shards that orbit / float around the body.")]
    [Range(0, 16)][SerializeField] private int shardCount = 10;
    [Tooltip("How far the shards drift out, as a fraction of sprite height.")]
    [SerializeField] private float shardOrbit = 0.34f;

    [Header("Visibility / Aura")]
    [Tooltip("Soft pulsing halo behind the body so the shadow reads on ANY biome. " +
             "This layer is DARK (it deepens him against bright snow/desert); the " +
             "hot bloom below is what carries him on dark biomes. 0 to disable.")]
    [Range(0f, 1f)][SerializeField] private float auraAlpha = 0.42f;
    [Tooltip("Aura size as a fraction of sprite height.")]
    [SerializeField] private float auraSizeFrac = 0.9f;
    [Tooltip("Hot ember bloom sitting inside the dark aura. Keeps him visible at " +
             "night without washing the silhouette out into a red blob.")]
    [Range(0f, 1f)][SerializeField] private float bloomAlpha = 0.26f;

    [Header("Ground Shadow")]
    [Tooltip("Soft contact ellipse under the feet. Without this he reads as floating; " +
             "it also sells the size increase as he grows. 0 to disable.")]
    [Range(0f, 1f)][SerializeField] private float groundShadowAlpha = 0.45f;

    [Header("Eyes")]
    [Tooltip("Base alpha of the additive eye-glow halo that pulses on top of the baked eyes.")]
    [Range(0f, 1f)][SerializeField] private float eyeGlowAlpha = 0.72f;
    [Tooltip("Eye-glow halo size, as a fraction of sprite height.")]
    [SerializeField] private float eyeGlowSizeFrac = 0.16f;
    [Tooltip("Extra eyes that open across his hide as he eats — one per kill, up to " +
             "this cap. Reads as him wearing what he's swallowed. 0 to disable.")]
    [Range(0, 8)][SerializeField] private int maxExtraEyes = 6;

    [Header("Embers")]
    [Tooltip("Average seconds between rising ember wisps (0 to disable).")]
    [SerializeField] private float emberInterval = 0.18f;
    [Tooltip("Average seconds between falling ash flakes shedding off the body " +
             "(0 to disable). Counter-motion to the embers; makes him feel like " +
             "he's burning down rather than just burning.")]
    [SerializeField] private float ashInterval = 0.42f;

    [Header("Death")]
    [Tooltip("Duration handed to EnemyDeathVFX. ≥1.0 shatters the shadow silhouette " +
             "into glowing red chunks (recommended). Set 0 to leave death handling alone.")]
    [SerializeField] private float deathVfxDuration = 1.15f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    // ── Cached silhouette anchor fractions (also used to place the eye-glow) ──
    private const float EYE_Y_FRAC = 0.532f;  // set into the brow of the wedge skull
    private const float EYE_DX_FRAC = 0.058f; // close-set — wide-set eyes read as cute
    private const float MAW_Y_FRAC = 0.405f;  // in the jaw, not across the whole torso

    // ── Runtime refs ──
    private EnemyStats stats;
    private EnemyAnimationController animController;
    private BerserkController berserk;
    private SpriteRenderer mainRenderer;
    private Transform spriteTf;   // the transform BerserkController scales/flips

    // Chromatic ghosts + glows (parented to the sprite so they inherit scale/flip)
    private SpriteRenderer ghostR, ghostC;
    private SpriteRenderer eyeGlowL, eyeGlowR;
    private SpriteRenderer aura;          // dark backing halo
    private SpriteRenderer bloom;         // hot backing bloom inside the dark halo
    private SpriteRenderer groundShadow;  // contact ellipse

    // Torn horizontal bands of the live frame (the datamosh slice look).
    private SpriteRenderer[] _slices;
    private int[] _sliceBand;
    private float[] _sliceOffset;
    private float _sliceTimer;

    // Extra eyes that open as he eats.
    private readonly List<SpriteRenderer> _extraEyes = new List<SpriteRenderer>(8);
    private readonly List<float> _extraEyePhase = new List<float>(8);

    // Floating detached shadow shards (parented to the sprite).
    private SpriteRenderer[] _shards;
    private float[] _shardAngle, _shardRadius, _shardSpin, _shardBob, _shardSize;

    // Floating shadow threads / filaments (parented to the sprite).
    private SpriteRenderer[] _threads;
    private Vector3[] _threadAnchor;
    private float[] _threadBaseAng, _threadSwayAmp, _threadSwayPhase, _threadSwaySpeed,
                    _threadLen, _threadWidth, _threadDrift;
    private bool[] _threadFree;

    // On-screen height right now (intrinsic × the live Transform scale). Used to
    // size WORLD-space FX (embers, devour burst) so they track the actual size.
    private float OnScreenHeight =>
        SPRITE_WORLD_HEIGHT * (spriteTf != null ? Mathf.Abs(spriteTf.lossyScale.y) : 1f);

    // ── Shared 0..1 intensity drivers ──
    private float glitchTimer;
    private float glitchSpike;        // random burst intensity, decaying
    private float eyeFlare;           // devour eye flare, decaying
    private float _gulp;              // swallow throb (drives maw glow), decaying
    private float _hitFlash;          // took damage, decaying
    private float _surge;             // devour data-surge (drives slice storm), decaying
    private float _frailty;           // 0 at full HP → 1 at death's door

    private SpriteRenderer mawGlow;   // runtime glow at the maw
    private bool _awaitingRealFrames; // true while showing the placeholder sheet
    private Sprite[] _liveSheet;      // the injected array; contents swapped in place
    private int _frameIdx;            // index of the frame currently on screen

    // World-space FX (embers, ash, devour burst) — plain motes ticked in Update.
    private float emberTimer, ashTimer;
    private readonly List<Mote> _fx = new List<Mote>(64);
    private Transform _fxHost;        // follows the enemy, holds ember/burst motes
    private int _fxSortOrder;
    private int _fxSortLayerID;

    // ── Static generated-sheet cache (shared, read-only) ──
    private static Sprite[] _cachedFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _cachedFrames = null;
        _softDot = null;
        _streak = null;
        _threadSprite = null;
        _shardSprites = null;
        _sliceSprites = null;
        _nf1 = null;
        _nf2 = null;
        _nfVein = null;
        _nfBlotch = null;
        _nfFine = null;
        _frameData = null;
        _computeStarted = false;
        _genThread = null;
        _placeholderSprite = null;
    }

    /// PERF hook — kicks the background build of the shared sprite sheet. Safe to call
    /// repeatedly (idempotent). Called automatically by GameOrchestrator's stage
    /// prewarm via ISpritePrewarm, so the generation runs on a worker thread behind the
    /// black stage-intro and the first spawn just creates the (already-computed) textures.
    public static void Prewarm() => KickBackgroundCompute();

    // Also kick the background build the instant the game starts — BEFORE the first
    // scene loads — regardless of entry point. This matters when you Play directly into
    // the GameScene in the editor (bypassing the menu's prewarm): it keeps the heavy
    // sprite math off the main thread during startup, so it can't slow the boot enough
    // to trip StageTransitionOverlay's reveal failsafe (which would briefly uncover the
    // scene's default biome before the black intro). Harmless if no Berserk ever spawns.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoWarmAtStartup() => KickBackgroundCompute();

    // ISpritePrewarm — GameOrchestrator finds this on the Berserk prefab and calls it
    // (on the PREFAB, no instance) to warm the sheet ahead of time. Only kicks the
    // static background build — no scene work.
    public void PrewarmSpriteFolders() => KickBackgroundCompute();

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        animController = GetComponent<EnemyAnimationController>();
        berserk = GetComponent<BerserkController>();

        // Force the enemy size (co-scales the CircleCollider2D, so the hitbox stays
        // matched). Done in Awake so BerserkController.Start() captures THIS as its
        // base scale and grows from it. Setting 0 disables the override.
        if (rootScaleOverride > 0f)
            transform.localScale = new Vector3(rootScaleOverride, rootScaleOverride, rootScaleOverride);

        if (stats == null || stats.enemyData == null)
        {
            Debug.LogError("[BerserkVisual] Missing EnemyStats/EnemyData — cannot build the " +
                           "procedural Berserk. Assign an EnemyData asset on the prefab.");
            enabled = false;
            return;
        }

        // Inject sprites WITHOUT ever blocking the main thread. We inject ONE
        // persistent array (_liveSheet). If the background build is already done we
        // fill it with the real frames now; otherwise we fill it with a placeholder
        // and overwrite its CONTENTS in place the instant the real frames are ready
        // (see LateUpdate). Because the animation loops read _liveSheet[index] live
        // each tick, swapping the contents is picked up seamlessly — no flicker, no
        // restart, and the Berserk can never cause a boot/first-spawn freeze.
        KickBackgroundCompute();
        _liveSheet = new Sprite[FRAME_COUNT];
        Sprite[] ready = GetFramesIfReady();
        if (ready != null)
        {
            System.Array.Copy(ready, _liveSheet, FRAME_COUNT);
        }
        else
        {
            Sprite ph = GetPlaceholderSprite();
            for (int i = 0; i < FRAME_COUNT; i++) _liveSheet[i] = ph;
            _awaitingRealFrames = true;
        }
        animController.SetSpritesDirectly(_liveSheet);

        // Rewrite the CLONED EnemyData ranges to match the generated sheet:
        //   [ idle (0..9) | attack (10..15) ]   — no death frames (VFX handles death).
        EnemyData data = stats.enemyData;
        data.spriteFacesLeft = false;

        float idleSpf = (idleCycleDuration > 0f) ? idleCycleDuration / IDLE_FRAMES : idleSpeed;
        float atkSpf = (attackTotalDuration > 0f) ? attackTotalDuration / ATTACK_FRAMES : attackSpeed;

        // NOTE: pStart/pEnd are resolved below, but the parry floor needs pStart first.
        int pStart0 = Mathf.Clamp(parryFrameStart, 0, HIT_FRAME - 1);
        if (minParryWindow > 0f)
        {
            // Usable window = (hitFrame - parryStart) x seconds-per-frame. Everything
            // after the bite is unreachable, because EnemyController judges the parry AT
            // the hit. So the only way to buy more window is more time per frame.
            int usableFrames = Mathf.Max(1, HIT_FRAME - pStart0);
            float needSpf = minParryWindow / usableFrames;
            if (needSpf > atkSpf)
            {
                if (debugLogs)
                    Debug.Log($"[BerserkVisual] Attack slowed from {atkSpf * ATTACK_FRAMES:F3}s " +
                              $"to {needSpf * ATTACK_FRAMES:F3}s to guarantee a {minParryWindow:F3}s " +
                              $"parry window (Min Parry Window).");
                atkSpf = needSpf;
            }
        }
        var idle = new AnimationFrameRange(0, IDLE_FRAMES) { speedOverride = idleSpf };
        var attack = new AnimationFrameRange(IDLE_FRAMES, ATTACK_FRAMES) { speedOverride = atkSpf };
        var death = new AnimationFrameRange(IDLE_FRAMES + ATTACK_FRAMES, 0);
        data.idle = idle;
        data.attack = attack;
        data.death = death;
        data.laserAttack = new AnimationFrameRange(0, 1); // defensive: never runs, keep in-bounds

        // Clamp to a window that is ordered, in range, and fully resolved before the bite.
        int pStart = pStart0;
        int pEnd = Mathf.Clamp(parryFrameEnd, pStart, HIT_FRAME - 1);
        if (pStart != parryFrameStart || pEnd != parryFrameEnd)
        {
            Debug.LogWarning($"[BerserkVisual] Parry window {parryFrameStart}..{parryFrameEnd} " +
                             $"clamped to {pStart}..{pEnd}. Valid range is 0..{HIT_FRAME - 1} " +
                             $"(the bite lands on frame {HIT_FRAME}).", this);
        }

        data.hitFrame = HIT_FRAME;
        data.parryFrameStart = pStart;
        data.parryFrameEnd = pEnd;

        // Death → reuse the existing disintegration VFX (shadow shatters into red chunks).
        if (deathVfxDuration > 0f)
            stats.ConfigureDeathVfx(deathVfxDuration, destroyHealthBarBeforeVfx: true);

        // NOTE: we intentionally do NOT touch the Transform scale beyond the override
        // above. The prefab's scale also sizes the CircleCollider2D, so overriding it
        // per-frame blows up the hitbox. The sprite is authored ~8 units tall
        // intrinsically so it renders at the right size at the prefab's scale.

        // The window the player can actually use runs from when it opens to the moment the
        // bite lands - everything after that is unreachable, because the parry is judged
        // at the hit. Sub-250ms is at or under human visual reaction time, so warn.
        float usableParry = atkSpf * (HIT_FRAME - pStart);
        if (usableParry < 0.25f)
        {
            Debug.LogWarning($"[BerserkVisual] Usable parry window is only {usableParry:F3}s " +
                             $"(opens {atkSpf * pStart:F3}s in, bite lands {atkSpf * HIT_FRAME:F3}s in). " +
                             $"That is at or below human reaction time. Raise Attack Total Duration " +
                             $"(currently {atkSpf * ATTACK_FRAMES:F2}s) or lower Parry Frame Start " +
                             $"(currently {pStart}). Raising Parry Frame End will NOT help.", this);
        }

        if (debugLogs)
            Debug.Log($"[BerserkVisual] Injected {(_awaitingRealFrames ? "placeholder (real pending)" : "ready")} frames " +
                      $"(idle 0..{IDLE_FRAMES - 1}, attack {IDLE_FRAMES}..{IDLE_FRAMES + ATTACK_FRAMES - 1}). " +
                      $"Attack {atkSpf * ATTACK_FRAMES:F3}s @ {atkSpf:F4}s/frame; " +
                      $"parry frames {pStart}..{pEnd}, usable {usableParry:F3}s, " +
                      $"opening {atkSpf * pStart:F3}s in, hit at {atkSpf * HIT_FRAME:F3}s.");
    }

    private void OnEnable()
    {
        if (berserk != null) berserk.OnAteEnemy += PlayDevour;
        // Every damage source that actually removes HP funnels through
        // CharacterStats.OnDamaged, so this is the one hook needed for a hit reaction.
        if (stats != null) stats.OnDamaged += OnDamagedFlash;
    }

    private void OnDisable()
    {
        if (berserk != null) berserk.OnAteEnemy -= PlayDevour;
        if (stats != null) stats.OnDamaged -= OnDamagedFlash;
    }

    private void Start()
    {
        mainRenderer = GetComponentInChildren<SpriteRenderer>();
        if (mainRenderer == null)
        {
            enabled = false;
            return;
        }
        spriteTf = mainRenderer.transform;

        BuildGroundShadow();
        BuildAura();
        BuildGlitchGhosts();
        BuildSlices();
        BuildEyeGlow();
        BuildMawGlow();
        BuildShards();
        BuildThreads();

        // World-space FX host that follows the enemy.
        _fxHost = new GameObject("[BerserkFX]").transform;
        _fxHost.position = transform.position;
        _fxSortOrder = mainRenderer.sortingOrder + 3;
        _fxSortLayerID = mainRenderer.sortingLayerID;

        glitchTimer = Random.Range(0.2f, glitchInterval);
        emberTimer = Random.Range(0f, emberInterval);
        ashTimer = Random.Range(0f, Mathf.Max(0.01f, ashInterval));
    }

    // ── Health-bar raise (this enemy only) ──
    // The bar is Instantiate()d (unparented, world-space) by EnemyStats and a follower
    // on the SHARED EnemyHpCanvas keeps it at (enemy.position + offset). Rather than
    // fight that follower's internals (which vary), we simply re-drive THIS bar's world
    // position every LateUpdate — after the follower has run — so it sits above the
    // silhouette and rises as the Berserk grows. Other enemies are untouched.
    private Transform _barTf;

    private void UpdateHealthBarOffset()
    {
        // Lazily grab this enemy's own bar instance (it may spawn a frame after us).
        if (_barTf == null)
        {
            var bar = (stats != null) ? stats.GetHealthBar() : null;
            if (bar == null) return;
            _barTf = bar.transform;
        }

        float sc = Mathf.Abs((spriteTf != null ? spriteTf : transform).lossyScale.y);
        float top = (1f - PIVOT_Y) * SPRITE_WORLD_HEIGHT * sc;   // top of the silhouette
        // +margin, then +15% lift; scales with the enemy so it stays clear as it grows.
        float y = (top + healthBarMargin) * 1.15f;
        _barTf.position = transform.position + new Vector3(0f, y, 0f);
    }

    //  Runtime glitch / eyes / embers
    private void Update()
    {
        float dt = Time.deltaTime;

        // How close to death he is — drives instability everywhere below.
        if (stats != null && stats.maxHealth > 0f)
            _frailty = 1f - Mathf.Clamp01(stats.currentHealth / stats.maxHealth);

        // Decay bursts.
        glitchSpike = Mathf.Max(0f, glitchSpike - dt * 3.2f);
        eyeFlare = Mathf.Max(0f, eyeFlare - dt * 2.2f);
        _gulp = Mathf.Max(0f, _gulp - dt * 2.6f);
        _hitFlash = Mathf.Max(0f, _hitFlash - dt * 5.5f);
        _surge = Mathf.Max(0f, _surge - dt * 3.0f);

        // Random glitch bursts — twice as frequent, and harder, when he's nearly dead.
        glitchTimer -= dt;
        if (glitchTimer <= 0f)
        {
            float interval = glitchInterval * Mathf.Lerp(1f, 0.45f, _frailty);
            glitchTimer = interval * Random.Range(0.55f, 1.6f);
            glitchSpike = Mathf.Max(glitchSpike, Random.Range(0.6f, 1f) * (0.8f + 0.35f * _frailty));
        }

        // NOTE: the tear/jitter is rendered purely on the chromatic GHOST and SLICE
        // layers (see LateUpdate). We deliberately NEVER write to the sprite transform's
        // position here — for the Berserk the SpriteRenderer sits on the ROOT, so
        // moving it would teleport the whole enemy (that was the "spawns far away /
        // won't advance" bug).

        if (_fxHost != null)
        {
            _fxHost.position = transform.position;

            // Ember wisps rising off the body.
            if (emberInterval > 0f)
            {
                emberTimer -= dt;
                if (emberTimer <= 0f)
                {
                    emberTimer = emberInterval * Random.Range(0.6f, 1.5f);
                    SpawnEmber();
                }
            }

            // Ash flaking off and falling — counter-motion to the embers. Sheds
            // faster the more damaged he is.
            if (ashInterval > 0f)
            {
                ashTimer -= dt;
                if (ashTimer <= 0f)
                {
                    ashTimer = ashInterval * Random.Range(0.6f, 1.5f) * Mathf.Lerp(1f, 0.5f, _frailty);
                    SpawnAsh();
                }
            }
        }

        TickFx(dt);
    }

    // Sync the ghost/glow layers AFTER BerserkController (order 10000) has written
    // the sprite's scale/flip for this frame.
    // NOTE ON SORTING (biome / grass interaction)
    // Every child layer copies the main renderer's sorting LAYER each frame and offsets
    // its ORDER within a deliberately tight -3..+2 window. That window matters: if the
    // scene Y-sorts foliage into the same range as enemies, a wide spread lets a grass
    // sprite land BETWEEN the body and its own backing layers, so the aura and bloom
    // disappear behind the biome while the body still draws in front of it — which looks
    // exactly like "some of the visuals aren't rendering". Keep new layers inside this
    // window rather than adding further out.
    private void LateUpdate()
    {
        if (mainRenderer == null) return;
        Sprite frame = mainRenderer.sprite;
        int baseOrder = mainRenderer.sortingOrder;
        int layer = mainRenderer.sortingLayerID;
        float dt = Time.deltaTime;

        ResolveFrameIndex();

        float t = Time.time;
        // Total instability this frame — bursts, damage reactions and the devour surge
        // all feed the same number so every layer agrees about how broken he looks.
        float chaos = Mathf.Clamp01(glitchSpike + _surge * 0.8f + _hitFlash * 0.7f
                                    + _frailty * 0.25f);
        float chroma = (chromaticBase + chromaticBurst * chaos) * SPRITE_WORLD_HEIGHT;
        float wobble = Mathf.Sin(t * 37f) * 0.35f + 0.65f;

        // Extra random "tear" jitter on the ghost layers when a burst is active.
        // This lives entirely on the child ghosts, so it never moves the enemy.
        Vector3 tear = Vector3.zero;
        if (chaos > 0.25f)
        {
            float j = chromaticBurst * SPRITE_WORLD_HEIGHT * chaos;
            tear = new Vector3(Random.Range(-j, j), Random.Range(-j, j) * 0.4f, 0f);
        }

        float ghostA = ghostBaseAlpha + 0.32f * chaos;
        SyncGhost(ghostR, frame, baseOrder - 1, layer,
                  new Vector3(-chroma, -chroma * 0.35f, 0f) * wobble + tear, ghostA);
        SyncGhost(ghostC, frame, baseOrder - 2, layer,
                  new Vector3(chroma, chroma * 0.35f, 0f) * wobble - tear, ghostA);

        // Eye glow pulse (+ devour flare). The occasional hard blink-out is the
        // cheapest scare in the whole file: eyes that stutter read as *wrong*.
        float pulse = 0.6f + 0.4f * Mathf.Sin(t * 6.3f);
        float blink = (Random.value < 0.05f + 0.08f * _frailty) ? 0.12f : 1f;
        float a = eyeGlowAlpha * pulse * blink + eyeFlare * 0.9f;
        float sizeMul = 1f + eyeFlare * 1.4f + chaos * 0.22f;
        SyncEye(eyeGlowL, baseOrder + 2, layer, a, sizeMul);
        SyncEye(eyeGlowR, baseOrder + 2, layer, a, sizeMul);
        SyncExtraEyes(baseOrder + 2, layer, t, a);

        // Maw glow: a low idle ember that flares bright during the swallow (_gulp).
        if (mawGlow != null)
        {
            float mg = 0.12f + 0.06f * Mathf.Sin(t * 4.7f) + _gulp * 0.95f;
            mawGlow.color = new Color(1f, 0.42f, 0.1f, Mathf.Clamp01(mg));
            mawGlow.sortingOrder = baseOrder + 1;
            mawGlow.sortingLayerID = layer;
            float ms = 0.22f * SPRITE_WORLD_HEIGHT * (1f + _gulp * 0.6f);
            mawGlow.transform.localScale = new Vector3(ms * 1.3f, ms, 1f);
        }

        // Backing halo. Two layers doing different jobs: a DARK one that deepens the
        // silhouette against bright snow/desert, and a small hot bloom inside it that
        // carries him on dark biomes. Keeping them separate is why he no longer reads
        // as a red smudge on every background.
        if (aura != null)
        {
            float ap = 0.75f + 0.25f * Mathf.Sin(t * 2.3f);
            Color ac = VOID_HALO;
            ac.a = Mathf.Clamp01(auraAlpha * ap);
            aura.color = ac;
            aura.sortingOrder = baseOrder - 3;
            aura.sortingLayerID = layer;
            float aw = 1f + 0.05f * Mathf.Sin(t * 3.1f) + chaos * 0.12f;
            aura.transform.localScale = new Vector3(auraSizeFrac * SPRITE_WORLD_WIDTH * aw,
                                                    auraSizeFrac * SPRITE_WORLD_HEIGHT * aw, 1f);
        }
        if (bloom != null)
        {
            float bp = 0.7f + 0.3f * Mathf.Sin(t * 3.7f + 1.1f);
            Color bc = BLOOM;
            bc.a = Mathf.Clamp01(bloomAlpha * bp + eyeFlare * 0.45f + _hitFlash * 0.3f);
            bloom.color = bc;
            bloom.sortingOrder = baseOrder - 2;
            bloom.sortingLayerID = layer;
            float bw = 0.62f * (1f + 0.08f * Mathf.Sin(t * 4.3f) + eyeFlare * 0.55f);
            bloom.transform.localScale = new Vector3(auraSizeFrac * SPRITE_WORLD_WIDTH * bw,
                                                     auraSizeFrac * SPRITE_WORLD_HEIGHT * bw, 1f);
        }

        // Contact shadow — squashes slightly on the gulp so the weight reads.
        if (groundShadow != null)
        {
            groundShadow.sortingOrder = baseOrder - 3;
            groundShadow.sortingLayerID = layer;
            Color gc = new Color(0.01f, 0.008f, 0.02f,
                                 Mathf.Clamp01(groundShadowAlpha * (0.88f + 0.12f * Mathf.Sin(t * 2.9f))));
            groundShadow.color = gc;
            float gw = 0.66f * (1f + 0.05f * Mathf.Sin(t * 2.9f) + _gulp * 0.10f);
            groundShadow.transform.localScale = new Vector3(SPRITE_WORLD_WIDTH * gw,
                                                            SPRITE_WORLD_HEIGHT * 0.15f, 1f);
        }

        TickShards(dt, baseOrder, layer, t);
        TickThreads(dt, baseOrder, layer, t);
        TickSlices(dt, baseOrder, layer, chaos);

        // Keep this Berserk's bar above the silhouette and rising as it grows.
        UpdateHealthBarOffset();

        // If we started on the placeholder, overwrite the live sheet's CONTENTS with
        // the real frames the moment the background build finishes. The running idle/
        // attack loops read _liveSheet[index] live, so the beast appears seamlessly.
        if (_awaitingRealFrames)
        {
            Sprite[] real = GetFramesIfReady();
            if (real != null)
            {
                System.Array.Copy(real, _liveSheet, FRAME_COUNT);
                _awaitingRealFrames = false;
            }
        }
    }

    private void OnDamagedFlash(float amount)
    {
        _hitFlash = 1f;
        glitchSpike = Mathf.Max(glitchSpike, 0.85f);
    }

    // Which sheet index is on screen right now? The slice layer needs it to cut bands
    // out of the CORRECT frame. Cheap: almost always a single reference compare, and
    // the fallback scan is over 16 entries.
    private void ResolveFrameIndex()
    {
        if (_liveSheet == null || mainRenderer == null) return;
        Sprite s = mainRenderer.sprite;
        if (s == null) return;
        if (_frameIdx >= 0 && _frameIdx < _liveSheet.Length && _liveSheet[_frameIdx] == s) return;
        for (int i = 0; i < _liveSheet.Length; i++)
        {
            if (_liveSheet[i] == s) { _frameIdx = i; return; }
        }
    }

    private void SyncGhost(SpriteRenderer g, Sprite frame, int order, int layer,
                           Vector3 localOffset, float alpha)
    {
        if (g == null) return;
        g.sprite = frame;
        g.enabled = frame != null && alpha > 0.02f;
        g.sortingOrder = order;
        g.sortingLayerID = layer;
        g.transform.localPosition = localOffset;
        Color c = g.color; c.a = Mathf.Clamp01(alpha); g.color = c;
    }

    private void SyncEye(SpriteRenderer e, int order, int layer, float alpha, float sizeMul)
    {
        if (e == null) return;
        e.sortingOrder = order;
        e.sortingLayerID = layer;
        e.transform.localScale = Vector3.one * (eyeGlowSizeFrac * SPRITE_WORLD_HEIGHT * sizeMul);
        Color c = EYE; c.a = Mathf.Clamp01(alpha); e.color = c;
    }

    // ── Extra eyes ──
    // One opens per enemy eaten, up to the cap. They're deliberately smaller, dimmer
    // and blink on their own offset phases, so they read as *passengers* rather than
    // as a second pair of his own eyes.
    private void SyncExtraEyes(int order, int layer, float t, float mainAlpha)
    {
        if (maxExtraEyes <= 0) return;

        int kills = (berserk != null) ? berserk.Kills : 0;
        int want = Mathf.Clamp(kills, 0, maxExtraEyes);

        while (_extraEyes.Count < want)
        {
            int i = _extraEyes.Count;
            // Alternate sides and creep outward/downward as more of them open.
            float side = (i % 2 == 0) ? -1f : 1f;
            float spread = 0.10f + 0.055f * (i / 2);
            float x = side * spread * SPRITE_WORLD_WIDTH + Random.Range(-0.02f, 0.02f) * SPRITE_WORLD_WIDTH;
            float yf = Mathf.Lerp(0.68f, 0.40f, Mathf.Clamp01(i / (float)Mathf.Max(1, maxExtraEyes - 1)));
            float y = (yf - PIVOT_Y) * SPRITE_WORLD_HEIGHT + Random.Range(-0.03f, 0.03f) * SPRITE_WORLD_HEIGHT;

            var go = new GameObject("ExtraEye" + i);
            go.transform.SetParent(spriteTf, false);
            go.transform.localPosition = new Vector3(x, y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetSoftDot();
            sr.color = EYE;
            _extraEyes.Add(sr);
            _extraEyePhase.Add(Random.Range(0f, Mathf.PI * 2f));
        }

        for (int i = 0; i < _extraEyes.Count; i++)
        {
            var sr = _extraEyes[i];
            if (sr == null) continue;
            bool on = i < want;
            sr.enabled = on;
            if (!on) continue;

            float ph = _extraEyePhase[i];
            float p = 0.55f + 0.45f * Mathf.Sin(t * (3.1f + i * 0.7f) + ph);
            // Independent, more frequent blinking than the main pair.
            float bl = (Random.value < 0.09f) ? 0.05f : 1f;
            float al = Mathf.Clamp01(mainAlpha * 0.55f * p * bl + eyeFlare * 0.5f);

            Color c = EYE; c.a = al; sr.color = c;
            sr.sortingOrder = order;
            sr.sortingLayerID = layer;
            float s = eyeGlowSizeFrac * SPRITE_WORLD_HEIGHT * (0.42f + 0.10f * Mathf.Sin(t * 5f + ph))
                      * (1f + eyeFlare * 0.8f);
            sr.transform.localScale = Vector3.one * s;
        }
    }

    private void BuildAura()
    {
        if (auraAlpha <= 0f && bloomAlpha <= 0f) return;
        float cy = (0.45f - PIVOT_Y) * SPRITE_WORLD_HEIGHT;

        if (auraAlpha > 0f)
        {
            var go = new GameObject("Aura");
            go.transform.SetParent(spriteTf, false);
            go.transform.localPosition = new Vector3(0f, cy, 0f);
            go.transform.localScale = new Vector3(auraSizeFrac * SPRITE_WORLD_WIDTH,
                                                  auraSizeFrac * SPRITE_WORLD_HEIGHT, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetSoftDot();
            Color c = VOID_HALO; c.a = auraAlpha; sr.color = c;
            aura = sr;
        }

        if (bloomAlpha > 0f)
        {
            var go = new GameObject("Bloom");
            go.transform.SetParent(spriteTf, false);
            go.transform.localPosition = new Vector3(0f, cy, 0f);
            go.transform.localScale = new Vector3(auraSizeFrac * SPRITE_WORLD_WIDTH * 0.62f,
                                                  auraSizeFrac * SPRITE_WORLD_HEIGHT * 0.62f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetSoftDot();
            Color c = BLOOM; c.a = bloomAlpha; sr.color = c;
            bloom = sr;
        }
    }

    private void BuildGroundShadow()
    {
        if (groundShadowAlpha <= 0f) return;
        var go = new GameObject("GroundShadow");
        go.transform.SetParent(spriteTf, false);
        // The pivot already sits at the ground line; drop a hair below it so the
        // ellipse peeks out from under the claws.
        go.transform.localPosition = new Vector3(0f, -0.012f * SPRITE_WORLD_HEIGHT, 0f);
        go.transform.localScale = new Vector3(SPRITE_WORLD_WIDTH * 0.66f,
                                              SPRITE_WORLD_HEIGHT * 0.15f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSoftDot();
        sr.color = new Color(0.01f, 0.008f, 0.02f, groundShadowAlpha);
        groundShadow = sr;
    }

    // World position of the maw (where devouring visually happens).
    private Vector3 MouthWorld()
    {
        Vector3 local = new Vector3(0f, (MAW_Y_FRAC - PIVOT_Y) * SPRITE_WORLD_HEIGHT, 0f);
        return (spriteTf != null) ? spriteTf.TransformPoint(local) : transform.position + local;
    }

    private void BuildMawGlow()
    {
        var go = new GameObject("MawGlow");
        go.transform.SetParent(spriteTf, false);
        go.transform.localPosition = new Vector3(0f, (MAW_Y_FRAC - PIVOT_Y) * SPRITE_WORLD_HEIGHT, 0f);
        float s = 0.22f * SPRITE_WORLD_HEIGHT;
        go.transform.localScale = new Vector3(s * 1.3f, s, 1f);  // wide like the maw
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSoftDot();
        sr.color = new Color(1f, 0.4f, 0.1f, 0f);
        mawGlow = sr;
    }

    private void BuildGlitchGhosts()
    {
        // Pure red / pure cyan rather than the old washed pastels — the split reads
        // as a real RGB tear instead of a soft double-image.
        ghostR = MakeChild("GlitchGhostR", new Color(1f, 0.06f, 0.10f, ghostBaseAlpha));
        ghostC = MakeChild("GlitchGhostC", new Color(0.15f, 0.90f, 1f, ghostBaseAlpha));
    }

    private SpriteRenderer MakeChild(string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(spriteTf, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = mainRenderer.sprite;
        sr.color = col;
        return sr;
    }

    private void BuildEyeGlow()
    {
        float ex = EYE_DX_FRAC * SPRITE_WORLD_WIDTH;                 // ± from centre
        float ey = (EYE_Y_FRAC - PIVOT_Y) * SPRITE_WORLD_HEIGHT;     // relative to pivot

        eyeGlowL = MakeEyeGlow("EyeGlowL", new Vector3(-ex, ey, 0f));
        eyeGlowR = MakeEyeGlow("EyeGlowR", new Vector3(ex, ey, 0f));
    }

    private SpriteRenderer MakeEyeGlow(string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(spriteTf, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * (eyeGlowSizeFrac * SPRITE_WORLD_HEIGHT);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSoftDot();
        sr.color = EYE;
        return sr;
    }

    // ── Glitch slices ──
    // Each slice is a SpriteRenderer showing one horizontal band cut straight out of
    // the CURRENT frame's texture (sub-rect Sprites over the same Texture2D — no extra
    // pixel work, no extra memory beyond the tiny Sprite objects). Displacing them
    // sideways gives the torn-VHS read, and because they're cut from the live frame
    // they track the pose exactly instead of smearing a stale silhouette.
    private void BuildSlices()
    {
        if (sliceCount <= 0) return;
        _slices = new SpriteRenderer[sliceCount];
        _sliceBand = new int[sliceCount];
        _sliceOffset = new float[sliceCount];

        for (int i = 0; i < sliceCount; i++)
        {
            var go = new GameObject("GlitchSlice" + i);
            go.transform.SetParent(spriteTf, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.enabled = false;
            _slices[i] = sr;
            _sliceBand[i] = Random.Range(0, SLICE_BANDS);
        }
    }

    private void TickSlices(float dt, int baseOrder, int layer, float chaos)
    {
        if (_slices == null) return;

        // Below the threshold, or while the placeholder is up, stay completely off —
        // a slice of the placeholder blob would look like a bug, not a glitch.
        if (chaos < 0.18f || _awaitingRealFrames || _cachedFrames == null)
        {
            for (int i = 0; i < _slices.Length; i++)
                if (_slices[i] != null) _slices[i].enabled = false;
            return;
        }

        _sliceTimer -= dt;
        bool reroll = _sliceTimer <= 0f;
        if (reroll) _sliceTimer = Mathf.Max(0.01f, sliceRerollInterval);

        float bandH = SPRITE_WORLD_HEIGHT / SLICE_BANDS;

        for (int i = 0; i < _slices.Length; i++)
        {
            var sr = _slices[i];
            if (sr == null) continue;

            if (reroll)
            {
                _sliceBand[i] = Random.Range(0, SLICE_BANDS);
                _sliceOffset[i] = Random.Range(-1f, 1f) * SPRITE_WORLD_WIDTH * sliceThrow * chaos;
            }

            Sprite sp = GetSliceSprite(_frameIdx, _sliceBand[i]);
            if (sp == null) { sr.enabled = false; continue; }

            sr.sprite = sp;
            sr.enabled = true;
            sr.sortingOrder = baseOrder + 2;
            sr.sortingLayerID = layer;

            // The slice sprite is pivoted at its own centre, so we place it back at the
            // band's own height and only displace it sideways.
            float bandCentreY = (_sliceBand[i] + 0.5f) * bandH - PIVOT_Y * SPRITE_WORLD_HEIGHT;
            sr.transform.localPosition = new Vector3(_sliceOffset[i], bandCentreY, 0f);

            // Rotate tints so consecutive slices separate visually: a plain displaced
            // copy, a cyan one and a red one is the classic channel-tear signature.
            Color tint = (i % 3 == 0) ? DATA_CYAN
                       : (i % 3 == 1) ? new Color(1f, 0.30f, 0.42f, 1f)
                                      : new Color(1f, 1f, 1f, 1f);
            tint.a = Mathf.Clamp01(0.5f + 0.5f * chaos);
            sr.color = tint;
        }
    }

    // ── Floating detached shadow shards ──
    // Small dark smoky blobs that orbit the torso, bob, slowly rotate, and glitch-
    // jump — selling the "coming apart / parts floating" look. Parented to the
    // sprite transform so they inherit its scale and flip.
    private void BuildShards()
    {
        if (shardCount <= 0) return;
        _shards = new SpriteRenderer[shardCount];
        _shardAngle = new float[shardCount];
        _shardRadius = new float[shardCount];
        _shardSpin = new float[shardCount];
        _shardBob = new float[shardCount];
        _shardSize = new float[shardCount];

        for (int i = 0; i < shardCount; i++)
        {
            var go = new GameObject("Shard" + i);
            go.transform.SetParent(spriteTf, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetShardSprite(i);
            // Mostly dark, a couple with a faint ember edge.
            sr.color = (i % 3 == 0)
                ? new Color(0.30f, 0.07f, 0.36f, 0.5f)   // violet, matching the veins
                : new Color(0.02f, 0.02f, 0.04f, 0.72f);
            _shards[i] = sr;

            _shardAngle[i] = Random.Range(0f, Mathf.PI * 2f);
            _shardRadius[i] = shardOrbit * SPRITE_WORLD_HEIGHT * Random.Range(0.55f, 1.15f);
            _shardSpin[i] = Random.Range(-1.1f, 1.1f) * (0.4f + Random.value);
            _shardBob[i] = Random.Range(0f, Mathf.PI * 2f);
            _shardSize[i] = SPRITE_WORLD_HEIGHT * Random.Range(0.05f, 0.11f);
        }
    }

    private void TickShards(float dt, int baseOrder, int layer, float t)
    {
        if (_shards == null) return;
        // Torso centre in local space (relative to the bottom-ish pivot).
        float cyLocal = (0.5f - PIVOT_Y) * SPRITE_WORLD_HEIGHT;

        for (int i = 0; i < _shards.Length; i++)
        {
            var sr = _shards[i];
            if (sr == null) continue;

            _shardAngle[i] += _shardSpin[i] * dt;
            _shardBob[i] += dt * (1.5f + i * 0.2f);

            // Occasional glitch-jump to a new orbit angle.
            if (Random.value < 0.008f + 0.05f * glitchSpike)
                _shardAngle[i] = Random.Range(0f, Mathf.PI * 2f);

            float r = _shardRadius[i] * (1f + 0.12f * Mathf.Sin(_shardBob[i])
                                          + 0.4f * glitchSpike + 0.25f * _frailty);
            float x = Mathf.Cos(_shardAngle[i]) * r;
            float y = cyLocal + Mathf.Sin(_shardAngle[i]) * r * 0.75f
                      + Mathf.Sin(_shardBob[i]) * SPRITE_WORLD_HEIGHT * 0.02f;

            sr.transform.localPosition = new Vector3(x, y, 0f);
            sr.transform.localScale = Vector3.one * _shardSize[i] * (1f + 0.25f * glitchSpike);
            sr.transform.Rotate(0f, 0f, _shardSpin[i] * 40f * dt);
            // Shards behind the body for depth; in front briefly during a glitch.
            sr.sortingOrder = baseOrder + (glitchSpike > 0.5f && (i & 1) == 0 ? 1 : -1);
            sr.sortingLayerID = layer;

            Color c = sr.color;
            c.a = Mathf.Clamp01((0.5f + 0.25f * Mathf.Sin(_shardBob[i] * 1.3f)) * (0.7f + 0.3f * glitchSpike));
            sr.color = c;
        }
    }

    // ── Floating shadow threads / filaments ──
    // Thin wispy strands that trail off the crown, back and arms, swaying like they're
    // suspended in the air and glitch-snapping to new angles — the "unravelling shadow"
    // look. Each is a single tapered sprite pivoted at its anchor end (pendulum sway).
    private void BuildThreads()
    {
        if (threadCount <= 0) return;
        _threads = new SpriteRenderer[threadCount];
        _threadAnchor = new Vector3[threadCount];
        _threadBaseAng = new float[threadCount];
        _threadSwayAmp = new float[threadCount];
        _threadSwayPhase = new float[threadCount];
        _threadSwaySpeed = new float[threadCount];
        _threadLen = new float[threadCount];
        _threadWidth = new float[threadCount];
        _threadDrift = new float[threadCount];
        _threadFree = new bool[threadCount];

        float w = SPRITE_WORLD_WIDTH;

        for (int i = 0; i < threadCount; i++)
        {
            var go = new GameObject("Thread" + i);
            go.transform.SetParent(spriteTf, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetThreadSprite();
            // Very dark with a faint ember bleed; a few hotter for menace.
            sr.color = (i % 4 == 0) ? new Color(0.38f, 0.08f, 0.45f, 0.5f)
                                    : new Color(0.03f, 0.02f, 0.04f, 0.7f);
            _threads[i] = sr;

            bool free = (i % 3 == 0);           // ~1/3 drift freely around the body
            _threadFree[i] = free;

            // Anchor around the upper body: crown, back ridge, shoulders, arms.
            float ax = Random.Range(-0.26f, 0.26f) * w;
            float ay = (Random.Range(0.45f, 0.72f) - PIVOT_Y) * SPRITE_WORLD_HEIGHT;
            _threadAnchor[i] = new Vector3(ax, ay, 0f);

            // Mostly point up/out; free ones can point anywhere.
            _threadBaseAng[i] = free ? Random.Range(0f, 360f)
                                     : 180f - (ax / w) * 70f + Random.Range(-40f, 40f);
            _threadSwayAmp[i] = Random.Range(10f, 32f);
            _threadSwayPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            _threadSwaySpeed[i] = Random.Range(0.8f, 2.2f) * (free ? 0.5f : 1f);
            _threadLen[i] = threadLength * SPRITE_WORLD_HEIGHT * Random.Range(0.6f, 1.25f);
            _threadWidth[i] = SPRITE_WORLD_HEIGHT * Random.Range(0.010f, 0.022f);
            _threadDrift[i] = Random.Range(0f, Mathf.PI * 2f);
        }
    }

    private void TickThreads(float dt, int baseOrder, int layer, float t)
    {
        if (_threads == null) return;
        float cyLocal = (0.5f - PIVOT_Y) * SPRITE_WORLD_HEIGHT;

        for (int i = 0; i < _threads.Length; i++)
        {
            var sr = _threads[i];
            if (sr == null) continue;

            // Thrash harder the closer he is to death.
            _threadSwayPhase[i] += dt * _threadSwaySpeed[i] * (1f + 0.8f * _frailty);
            _threadDrift[i] += dt * 0.6f;

            // Glitch-snap the base angle occasionally (more often mid-burst).
            if (Random.value < 0.006f + 0.06f * glitchSpike)
                _threadBaseAng[i] += Random.Range(-40f, 40f);

            float sway = Mathf.Sin(_threadSwayPhase[i]) * _threadSwayAmp[i];
            float jitter = glitchSpike > 0.2f ? Random.Range(-1f, 1f) * 12f * glitchSpike : 0f;
            float ang;
            Vector3 anchor;

            if (_threadFree[i])
            {
                // Drifts slowly around the body on a lazy orbit.
                _threadBaseAng[i] += dt * 14f;
                float orbit = (shardOrbit + 0.12f) * SPRITE_WORLD_HEIGHT;
                anchor = new Vector3(Mathf.Cos(_threadDrift[i]) * orbit,
                                     cyLocal + Mathf.Sin(_threadDrift[i]) * orbit * 0.7f, 0f);
                ang = _threadBaseAng[i] + sway * 0.5f + jitter;
            }
            else
            {
                anchor = _threadAnchor[i];
                ang = _threadBaseAng[i] + sway + jitter;
            }

            sr.transform.localPosition = anchor;
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
            float lenPulse = 1f + 0.10f * Mathf.Sin(_threadSwayPhase[i] * 1.7f) + 0.25f * glitchSpike;
            sr.transform.localScale = new Vector3(_threadWidth[i], _threadLen[i] * lenPulse, 1f);

            // Behind the body normally; a couple flick in front during a glitch.
            sr.sortingOrder = baseOrder + (glitchSpike > 0.55f && (i % 4 == 0) ? 1 : -2);
            sr.sortingLayerID = layer;

            Color c = sr.color;
            float baseA = _threadFree[i] ? 0.45f : 0.6f;
            c.a = Mathf.Clamp01(baseA * (0.55f + 0.45f * Mathf.Sin(_threadSwayPhase[i] * 1.3f + i))
                                * (0.7f + 0.5f * glitchSpike));
            // Flash hotter during a glitch burst.
            c = Color.Lerp(c, new Color(0.62f, 0.14f, 0.72f, c.a), glitchSpike * 0.5f);
            sr.color = c;
        }
    }

    //  Devour effect (raised by BerserkController.OnAteEnemy)
    private void PlayDevour()
    {
        if (!isActiveAndEnabled || _fxHost == null) return;

        // Punch the live layers (eyes + bloom flare, glitch spike, slice storm).
        eyeFlare = 1f;
        glitchSpike = 1f;
        _gulp = 1f;                    // drives a throat/eye "swallow" throb (Update)
        _surge = 1f;                   // drives the slice storm (TickSlices)

        float scale = Mathf.Max(0.4f, OnScreenHeight * 0.5f);
        Vector3 body = transform.position;
        Vector3 mouth = MouthWorld();  // the maw — where the meal is drawn in

        // 1) A hot flare AT THE MAW that blooms then collapses inward (the bite lights up).
        SpawnFx(mouth, Vector2.zero, life: 0.28f,
                startScale: scale * 1.3f, endScale: scale * 0.15f,
                c0: new Color(1f, 0.55f, 0.12f, 0.95f),
                c1: new Color(0.9f, 0.05f, 0.02f, 0f),
                sprite: GetSoftDot());

        // 2) Essence of the prey SUCKED INTO THE MOUTH from a wide ring — curving in.
        int suck = 16;
        for (int i = 0; i < suck; i++)
        {
            float ang = (i / (float)suck) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float rad = scale * Random.Range(1.6f, 3.0f);
            Vector3 from = mouth + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f, 0f) * rad;
            Vector2 vel = ((Vector2)(mouth - from)).normalized * Random.Range(5f, 9f) * Mathf.Max(0.5f, scale);
            var m = SpawnFx(from, vel, life: Random.Range(0.22f, 0.38f),
                            startScale: scale * Random.Range(0.14f, 0.26f), endScale: 0f,
                            c0: new Color(1f, 0.5f, 0.18f, 0.95f),
                            c1: new Color(0.8f, 0.05f, 0.03f, 0f),
                            sprite: GetSoftDot());
            m.homing = mouth;          // curve toward the maw as it travels
            m.homingPull = 22f;
            Replace(m);
        }

        // 3) A few shadow SHARDS get yanked off the body into the maw (it feeds on itself).
        int chunks = 5;
        for (int i = 0; i < chunks; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 from = body + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * scale * Random.Range(0.6f, 1.3f);
            var m = SpawnFx(from, Vector2.zero, life: Random.Range(0.18f, 0.3f),
                            startScale: scale * Random.Range(0.12f, 0.22f), endScale: 0f,
                            c0: new Color(0.04f, 0.02f, 0.05f, 0.9f),
                            c1: new Color(0.5f, 0.06f, 0.05f, 0f),
                            sprite: GetShardSprite(i));
            m.homing = mouth;
            m.homingPull = 26f;
            m.spin = Random.Range(-360f, 360f);
            Replace(m);
        }

        // 4) A dark-red shockwave ring off the body — the gulp's impact.
        SpawnFx(body, Vector2.zero, life: 0.34f,
                startScale: scale * 0.5f, endScale: scale * 2.8f,
                c0: new Color(0.85f, 0.10f, 0.05f, 0.6f),
                c1: new Color(0.15f, 0.02f, 0.02f, 0f),
                sprite: GetSoftDot());

        // 5) Lashing tendrils whipping out from the body.
        int lash = 10;
        for (int i = 0; i < lash; i++)
        {
            float ang = (i / (float)lash) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float speed = Random.Range(3f, 6.5f) * Mathf.Clamp(scale, 0.6f, 3f);
            var m = SpawnFx(body, dir * speed, life: Random.Range(0.22f, 0.34f),
                            startScale: scale * 0.9f, endScale: scale * 0.12f,
                            c0: new Color(0.05f, 0.02f, 0.06f, 0.9f),
                            c1: new Color(0.6f, 0.08f, 0.05f, 0f),
                            sprite: GetStreak());
            m.stretchDir = dir;
            m.stretch = Random.Range(2.5f, 4.5f);
            m.grav = -3f;
            Replace(m);
        }

        // 6) Horizontal CYAN data-streaks shearing off sideways — the one moment cyan
        //    is allowed on the body, so the devour reads as him corrupting rather than
        //    just burning. Pairs with the slice storm _surge kicked off above.
        for (int i = 0; i < 4; i++)
        {
            float dirX = (i % 2 == 0) ? 1f : -1f;
            Vector3 from = body + new Vector3(0f, Random.Range(0.1f, 0.75f) * OnScreenHeight, 0f);
            Vector2 dir = new Vector2(dirX, 0f);
            var m = SpawnFx(from, dir * Random.Range(6f, 11f) * Mathf.Max(0.5f, scale),
                            life: Random.Range(0.12f, 0.2f),
                            startScale: scale * Random.Range(0.18f, 0.34f), endScale: 0f,
                            c0: new Color(0.5f, 0.98f, 1f, 0.8f),
                            c1: new Color(0.1f, 0.5f, 0.7f, 0f),
                            sprite: GetStreak());
            m.stretchDir = dir;
            m.stretch = Random.Range(5f, 9f);
            Replace(m);
        }

        if (debugLogs) Debug.Log("[BerserkVisual] Devour effect played.");
    }

    // ── Lightweight world-space mote system (embers, ash, devour burst) ──
    private struct Mote
    {
        public Transform tr;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float life, maxLife, grav, spin;
        public float s0, s1;
        public Color c0, c1;
        public Vector2 stretchDir;
        public float stretch;   // 0 = uniform; >0 = stretch along stretchDir
        public Vector3 homing;  // world point to curve toward (devour)
        public float homingPull;// steering acceleration toward `homing` (0 = none)
    }

    private void SpawnEmber()
    {
        // Position is picked in the sprite's LOCAL space then mapped to world (so it
        // inherits the Transform scale); SIZE/velocity are in WORLD units based on the
        // actual on-screen height.
        float x = Random.Range(-0.26f, 0.26f) * SPRITE_WORLD_WIDTH;
        float y = Random.Range(0.28f, 0.60f) * SPRITE_WORLD_HEIGHT;
        Vector3 local = new Vector3(x, y, 0f);
        Vector3 world = (spriteTf != null) ? spriteTf.TransformPoint(local)
                                           : transform.position + local;
        float on = OnScreenHeight;
        Vector2 vel = new Vector2(Random.Range(-0.15f, 0.15f) * on, Random.Range(0.35f, 0.8f) * on);
        SpawnFx(world, vel, life: Random.Range(0.5f, 1.0f),
                startScale: Random.Range(0.05f, 0.11f) * on,
                endScale: 0f,
                c0: new Color(1f, 0.32f, 0.06f, 0.85f),
                c1: new Color(0.7f, 0.05f, 0.02f, 0f),
                sprite: GetSoftDot());
    }

    // Ash: dark flakes shedding off the underside and sinking. Slower and heavier than
    // the embers, and they never glow — the contrast is what makes the embers read hot.
    private void SpawnAsh()
    {
        float x = Random.Range(-0.30f, 0.30f) * SPRITE_WORLD_WIDTH;
        float y = Random.Range(0.10f, 0.45f) * SPRITE_WORLD_HEIGHT;
        Vector3 local = new Vector3(x, y, 0f);
        Vector3 world = (spriteTf != null) ? spriteTf.TransformPoint(local)
                                           : transform.position + local;
        float on = OnScreenHeight;
        Vector2 vel = new Vector2(Random.Range(-0.10f, 0.10f) * on, Random.Range(-0.30f, -0.08f) * on);
        var m = SpawnFx(world, vel, life: Random.Range(0.7f, 1.4f),
                        startScale: Random.Range(0.04f, 0.09f) * on,
                        endScale: 0f,
                        c0: new Color(0.05f, 0.04f, 0.07f, 0.75f),
                        c1: new Color(0.10f, 0.03f, 0.04f, 0f),
                        sprite: GetShardSprite(Random.Range(0, 3)));
        m.grav = -0.6f * on;
        m.spin = Random.Range(-140f, 140f);
        Replace(m);
    }

    private Mote SpawnFx(Vector3 world, Vector2 vel, float life, float startScale,
                         float endScale, Color c0, Color c1, Sprite sprite)
    {
        var go = new GameObject("fx");
        go.transform.SetParent(_fxHost, true);
        go.transform.position = world;
        go.transform.localScale = Vector3.one * startScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c0;
        sr.sortingOrder = _fxSortOrder;
        sr.sortingLayerID = _fxSortLayerID;

        var m = new Mote
        {
            tr = go.transform,
            sr = sr,
            vel = vel,
            life = 0f,
            maxLife = life,
            grav = 0f,
            spin = Random.Range(-180f, 180f),
            s0 = startScale,
            s1 = endScale,
            c0 = c0,
            c1 = c1,
            stretchDir = Vector2.zero,
            stretch = 0f,
            homing = Vector3.zero,
            homingPull = 0f
        };
        _fx.Add(m);
        return m;
    }

    // Overwrite the last-added mote with a modified copy (used to tweak lash motes).
    private void Replace(Mote m)
    {
        if (_fx.Count == 0) return;
        _fx[_fx.Count - 1] = m;
    }

    private void TickFx(float dt)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var m = _fx[i];
            if (m.tr == null) { _fx.RemoveAt(i); continue; }

            m.life += dt;
            float t = Mathf.Clamp01(m.life / m.maxLife);

            // Homing: accelerate toward the maw so essence/shards curve in and vanish.
            if (m.homingPull > 0f)
            {
                Vector2 to = (Vector2)(m.homing - m.tr.position);
                m.vel += to.normalized * m.homingPull * dt;
            }

            m.vel.y += m.grav * dt;
            m.vel *= 0.93f;
            m.tr.position += (Vector3)m.vel * dt;

            float ease = 1f - (1f - t) * (1f - t);
            float s = Mathf.Lerp(m.s0, m.s1, ease);
            if (m.stretch > 0f && m.stretchDir.sqrMagnitude > 0.0001f)
            {
                float ang = Mathf.Atan2(m.stretchDir.y, m.stretchDir.x) * Mathf.Rad2Deg;
                m.tr.rotation = Quaternion.Euler(0, 0, ang);
                m.tr.localScale = new Vector3(s * m.stretch, s, 1f);
            }
            else
            {
                m.tr.Rotate(0, 0, m.spin * dt);
                m.tr.localScale = Vector3.one * Mathf.Max(0.0001f, s);
            }

            m.sr.color = Color.Lerp(m.c0, m.c1, t);

            if (m.life >= m.maxLife) { Destroy(m.tr.gameObject); _fx.RemoveAt(i); continue; }
            _fx[i] = m;
        }
    }

    private void OnDestroy()
    {
        if (_fxHost != null) Destroy(_fxHost.gameObject);
    }

    //  PROCEDURAL SPRITE GENERATION
    //  The heavy per-pixel work (coverage + smoke + compose → Color32[] arrays) is
    //  pure math, so it runs on a BACKGROUND THREAD kicked at app startup and by the
    //  stage prewarm. The main thread only ever creates the 16 Texture2D/Sprite objects
    //  from the finished arrays — cheap. It NEVER blocks/joins the worker: if a Berserk
    //  spawns before the build is done, GetFramesIfReady() returns null and the enemy
    //  shows a placeholder for a fraction of a second, then swaps in the real frames
    //  (LateUpdate) the instant they're ready. On WebGL (no threads) KickBackground
    //  Compute() computes inline, so the frames are ready immediately.

    private static volatile Color32[][] _frameData;   // computed off-thread
    private static bool _computeStarted;
    private static System.Threading.Thread _genThread;
    private static readonly object _genLock = new object();

    private const int FRAME_COUNT = IDLE_FRAMES + ATTACK_FRAMES;

    // Kicks the background compute (idempotent). Called from Prewarm/PrewarmSpriteFolders.
    private static void KickBackgroundCompute()
    {
        lock (_genLock)
        {
            if (_computeStarted) return;
            _computeStarted = true;
        }
        try
        {
            _genThread = new System.Threading.Thread(ComputeAllFrames)
            { IsBackground = true, Name = "BerserkGen", Priority = System.Threading.ThreadPriority.BelowNormal };
            _genThread.Start();
        }
        catch
        {
            // Platforms without threads (e.g. WebGL) → compute inline now.
            ComputeAllFrames();
        }
    }

    // Thread body (or synchronous fallback). Builds every frame's Color32[] array.
    private static void ComputeAllFrames()
    {
        BuildNoiseFields();
        var data = new Color32[FRAME_COUNT][];
        float[] cov = new float[TEX_W * TEX_H];       // thread-local scratch
        float[] hgt = new float[TEX_W * TEX_H];       // surface height field (for shading)
        float[] hblur = new float[TEX_W * TEX_H];     // blurred height → cavity/AO term
        float[] htmp = new float[TEX_W * TEX_H];      // separable-blur intermediate
        float[] vfield = new float[TEX_W * TEX_H];    // warped vein field (colour + groove)
        float[] ffield = new float[TEX_W * TEX_H];    // body-anchored surface grain
        float[] crk = new float[TEX_W * TEX_H];       // internal-fissure mask
        Color[] scratch = new Color[TEX_W * TEX_H];
        Color[] rowTmp = new Color[TEX_W];            // one row, for the datamosh pass
        for (int i = 0; i < FRAME_COUNT; i++)
            data[i] = BuildSpritePixels(MakeFrameParams(i), cov, hgt, hblur, htmp, vfield, ffield, crk, scratch, rowTmp);
        _frameData = data;                            // publish last (volatile)
    }

    // Deterministic per-frame animation parameters (shared by thread + fallback).
    private static FrameParams MakeFrameParams(int index)
    {
        if (index < IDLE_FRAMES)
        {
            // ── Walk cycle ──
            // He is drawn front-on, so a side-view leg cycle would read as nothing. What
            // sells a front-facing walk is the WEIGHT: the body drops onto each footfall
            // (twice per cycle), sways toward the planted side (once per cycle), and the
            // head lags behind both. The limbs then lift on alternating halves.
            float u = index / (float)IDLE_FRAMES;          // 0..1 through the cycle
            float ph = u * Mathf.PI * 2f;

            return new FrameParams
            {
                // Two dips per cycle, one per footfall, biased so he hangs at the top.
                bodyY = -0.013f + 0.017f * Mathf.Cos(ph * 2f),
                roll = 0.024f * Mathf.Sin(ph),
                legPhase = u,
                stepAmp = 1f,
                // Arms swing gently out of phase with the legs, and the leg splay
                // breathes with the body drop so he sinks onto his stance each step.
                armReach = 0.16f * Mathf.Sin(ph - 0.9f),
                splay = 0.50f + 0.16f * Mathf.Cos(ph * 2f),
                breath = 0.007f * Mathf.Sin(ph),
                // Head lags the body by about an eighth of a cycle — the follow-through
                // that stops a walk looking like a rigid puppet bouncing on a stick.
                headBob = -0.011f * Mathf.Cos(ph * 2f - 0.8f),
                armSwing = Mathf.Sin(ph),
                limbFloat = 0.5f + 0.5f * Mathf.Sin(ph * 0.7f),
                // Mane and horns lag further still.
                tendrilPhase = ph - 1.15f,
                smokeScroll = index * 7f,
                leanTop = 0.012f * Mathf.Sin(ph * 0.5f),
                forwardShift = 0f,
                mawOpen = 0.26f + 0.08f * Mathf.Sin(ph * 2f - 0.5f),
                eyeBright = 0.82f + 0.18f * (0.5f + 0.5f * Mathf.Sin(ph * 1.3f)),
                seed = index,
                // Corruption is now a SPIKE PATTERN, not a constant hum. Low-level noise
                // on every frame is indistinguishable from a badly compressed sprite;
                // clean frames punctuated by two hard glitch frames reads as deliberate.
                corrupt = IDLE_CORRUPT[index % IDLE_FRAMES],
                // These two used to swing 0.35→1.75 and 0.80→1.90 between frames, which
                // by itself made him visibly change colour mid-animation. Markings on a
                // body should breathe, not flash.
                crackGlow = 0.55f + 0.10f * Mathf.Sin(ph * 2f),
                veinScroll = 0f,
                veinGlow = 0.98f + 0.07f * Mathf.Sin(ph * 2f + 1.1f)
            };
        }

        // ── Attack ──
        // 0-3 anticipation: he coils DOWN and BACK while the maw opens and the fissures
        //     light up, so the player has three frames of unmistakable telegraph.
        // 4-5 strike: snaps forward and up, then the jaw slams shut on frame 5.
        // 6-7 recovery: overshoots low, then settles — without this the attack ends on a
        //     hard cut and reads as a pose change rather than a bite.
        int a = index - IDLE_FRAMES;
        float[] lean = { -0.05f, -0.10f, -0.15f, -0.17f, 0.13f, 0.21f, 0.12f, 0.03f };
        float[] fwd = { -0.02f, -0.04f, -0.055f, -0.065f, 0.045f, 0.085f, 0.050f, 0.010f };
        float[] bodyY = { -0.006f, -0.020f, -0.034f, -0.042f, 0.010f, 0.022f, -0.016f, -0.005f };
        float[] maw = { 0.30f, 0.64f, 0.86f, 1.00f, 1.00f, 0.08f, 0.24f, 0.32f };
        float[] eye = { 0.95f, 1.05f, 1.22f, 1.45f, 1.62f, 1.95f, 1.18f, 1.00f };
        float[] arm = { -0.7f, -1.0f, -1.15f, -0.95f, 0.7f, 1.15f, 0.8f, 0.25f };
        float[] hbob = { 0.004f, 0.013f, 0.023f, 0.029f, -0.010f, -0.027f, -0.018f, -0.004f };
        // The fissures brighten right through the wind-up and blow out on the bite,
        // so the attack telegraphs as "something inside him is building up".
        // The fissures still surge through the wind-up — that's the telegraph — but over
        // a far tighter range, and the veins barely move at all. A 2.4x swing in vein
        // brightness between idle and attack was reading as the creature changing colour.
        float[] crack = { 0.60f, 0.75f, 0.95f, 1.15f, 1.30f, 1.35f, 0.80f, 0.62f };
        float[] corr = { 0.00f, 0.22f, 0.00f, 0.60f, 0.78f, 0.42f, 0.00f, 0.12f };
        float[] vein = { 1.00f, 1.06f, 1.12f, 1.20f, 1.24f, 1.10f, 1.02f, 0.99f };
        // The limbs carry the attack as much as the jaw does. The arms cock right back
        // through the wind-up and lash forward on the strike, while the walking legs
        // brace wide and low to plant him, then drive up as he lunges.
        float[] reachA = { -0.45f, -0.85f, -1.00f, -0.90f, 0.65f, 1.00f, 0.55f, 0.12f };
        float[] splayA = { 0.60f, 0.80f, 0.98f, 1.00f, 0.72f, 0.40f, 0.62f, 0.55f };

        return new FrameParams
        {
            breath = 0.006f,
            bodyY = bodyY[a],
            roll = 0f,
            legPhase = 0.25f,
            // A trace of step so the braced legs still shift rather than freezing solid.
            stepAmp = 0.18f,
            armReach = reachA[a],
            splay = splayA[a],
            headBob = hbob[a],
            armSwing = arm[a],
            limbFloat = 0.3f,
            tendrilPhase = 3.0f + a * 0.7f,
            smokeScroll = 40f + a * 9f,
            leanTop = lean[a],
            forwardShift = fwd[a],
            mawOpen = maw[a],
            eyeBright = eye[a],
            seed = index,
            corrupt = corr[a],
            crackGlow = crack[a],
            veinScroll = 0f,
            veinGlow = vein[a]
        };
    }

    // MAIN THREAD ONLY. Returns the finished Sprite frames if the background pixel
    // build is done, else null — it NEVER blocks/joins the worker thread. Creating the
    // 16 textures from the finished arrays is cheap (no per-pixel math). On WebGL,
    // KickBackgroundCompute() computes inline, so _frameData is already set here.
    private static Sprite[] GetFramesIfReady()
    {
        if (_cachedFrames != null) return _cachedFrames;

        Color32[][] data = _frameData;
        if (data == null) return null;   // still computing on the worker thread

        var frames = new Sprite[data.Length];
        Vector2 pivot = new Vector2(0.5f, PIVOT_Y);
        float ppu = TEX_H / SPRITE_WORLD_HEIGHT;
        for (int i = 0; i < data.Length; i++)
        {
            var tex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(data[i]);
            tex.Apply(false);   // keep readable so EnemyDeathVFX can shatter it
            frames[i] = Sprite.Create(tex, new Rect(0, 0, TEX_W, TEX_H), pivot, ppu, 0, SpriteMeshType.FullRect);
        }

        _cachedFrames = frames;
        _frameData = null;   // textures own the data now — free the CPU arrays
        return frames;
    }

    // A cheap stand-in shown for the ~fraction of a second before the real frames
    // finish (only ever needed if a Berserk spawns during a cold first-Play compile).
    // Correct pivot/size so it sits exactly where the real beast will, and its
    // contents are swapped out in place the moment the real sheet is ready.
    private static Sprite _placeholderSprite;
    private static Sprite GetPlaceholderSprite()
    {
        if (_placeholderSprite != null) return _placeholderSprite;

        const int PW = 48, PH = 40;
        var tex = new Texture2D(PW, PH, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[PW * PH];
        float cx = (PW - 1) * 0.5f, cy = (PH - 1) * 0.42f;
        for (int y = 0; y < PH; y++)
            for (int x = 0; x < PW; x++)
            {
                float nx = (x - cx) / (PW * 0.42f), ny = (y - cy) / (PH * 0.44f);
                float d = nx * nx + ny * ny;
                float a = d < 1f ? Mathf.Clamp01((1f - d) * 2f) : 0f;
                px[y * PW + x] = new Color32(7, 7, 12, (byte)(a * 235f));
            }
        tex.SetPixels32(px); tex.Apply(false);
        // Match the real sheet's world size so nothing jumps on swap.
        float ppu = PH / SPRITE_WORLD_HEIGHT;
        _placeholderSprite = Sprite.Create(tex, new Rect(0, 0, PW, PH),
                                           new Vector2(0.5f, PIVOT_Y), ppu, 0, SpriteMeshType.FullRect);
        return _placeholderSprite;
    }

    private struct FrameParams
    {
        public float breath;         // shoulder/chest heave (fraction of H)
        public float headBob;        // head vertical bob (fraction of H)
        public float armSwing;       // -1..1 arm sway/reach
        public float limbFloat;      // 0..1 how detached the lower limbs float
        public float tendrilPhase;   // spine/mane ripple phase
        public float smokeScroll;    // smoke drift (px)
        public float leanTop;        // forward shear of the top (fraction of W)
        public float forwardShift;   // whole-body x shift (fraction of W)
        public float mawOpen;        // 0..1 maw open (fanged even at rest)
        public float eyeBright;      // eye brightness multiplier
        public int seed;             // frame index — drives per-frame glitch hashing
        public float corrupt;        // 0..1 how heavily the datamosh pass tears this frame
        public float crackGlow;      // 0..~1.6 brightness of the internal fissures
        public float veinScroll;     // striation drift (band-space units)
        public float veinGlow;       // 0..~1.9 brightness of the violet striations
        public float bodyY;          // whole-body vertical bob (fraction of H)
        public float roll;           // side-to-side sway, applied as a shear like leanTop
        public float legPhase;       // 0..1 position in the walk cycle
        public float stepAmp;        // 0..1 scales walk-cycle limb lift (0 = planted)
        public float armReach;       // -1 arms cocked back … +1 thrown forward to grab
        public float splay;          // 0 legs tucked … 1 braced wide and low
    }

    // Thread-SAFE pixel builder: computes one frame into a Color32[] using only pure
    // math (no Unity Texture/Sprite API), so it can run on a background thread. The
    // caller supplies reusable coverage + fissure + colour + row scratch buffers.
    private static Color32[] BuildSpritePixels(FrameParams p, float[] cov, float[] hgt,
                                               float[] hblur, float[] htmp, float[] vfield,
                                               float[] ffield, float[] crk, Color[] outPx,
                                               Color[] rowTmp)
    {
        int W = TEX_W, H = TEX_H;
        System.Array.Clear(cov, 0, cov.Length);
        System.Array.Clear(hgt, 0, hgt.Length);
        System.Array.Clear(crk, 0, crk.Length);

        float cx = W * 0.5f + p.forwardShift * W;
        // bodyY rides in on `bob`, which is added to the torso/head/shoulders but NOT to
        // the feet — so the mass drops onto planted limbs instead of the whole sprite
        // sliding up and down, which is the difference between a walk and a hover.
        float bob = (p.breath + p.bodyY) * H;
        float headB = p.headBob * H;
        float armSw = p.armSwing;

        // Shear helper: the higher up a shape sits, the more it leans forward.
        float baseY = H * 0.05f;
        // Forward lean and walk sway are both horizontal shear on a front-facing sprite,
        // so they share one term. Shear scales with height, so the feet stay put while
        // the upper body rocks over them.
        float shear = p.leanTop + p.roll;
        System.Func<float, float, float> shx = (x, y) =>
            x + shear * W * ((y - baseY) / H);


        var pxs = new float[8];
        var pys = new float[8];

        // ── Abdomen: a heavy sac slung low behind the thorax ──
        // Splitting the body into cephalothorax + abdomen is what makes the arachnid
        // read land. It also gives the lower half real mass again now that the walking
        // legs have moved out to the sides, where a spider actually carries them.
        float abdY = H * 0.335f + bob * 0.7f;
        AddDisc(cov, hgt, W, H, shx(cx + W * 0.012f, abdY), abdY, W * 0.128f, W * 0.055f);
        AddDisc(cov, hgt, W, H, shx(cx - W * 0.030f, abdY + H * 0.055f), abdY + H * 0.055f,
                W * 0.098f, W * 0.048f);
        AddDisc(cov, hgt, W, H, shx(cx + W * 0.048f, abdY - H * 0.038f), abdY - H * 0.038f,
                W * 0.078f, W * 0.045f);

        // Pedicel — the narrow waist joining abdomen to thorax. Spiders are defined by
        // this pinch; without it the two masses merge back into one oval.
        float waistY = H * 0.470f + bob;
        AddCapsule(cov, hgt, W, H, shx(cx, waistY + H * 0.030f), waistY + H * 0.030f,
                   shx(cx + W * 0.010f, waistY - H * 0.030f), waistY - H * 0.030f,
                   W * 0.052f, W * 0.058f, W * 0.032f);

        // ── Four arched walking legs ──
        // The knee sits ABOVE the body and the shin drops away outside it. That inverted
        // "Λ" is the single most recognisable arachnid cue there is — far more than leg
        // count — and it fills the space either side of the torso with structure.
        //
        // Gait: adjacent legs are half a cycle apart, and the two sides are opposed, so
        // at any instant a tripod is planted while the rest swing. Same principle real
        // spiders walk on, and it stops the legs pulsing in unison like a jellyfish.
        float[] legOff = { 0.00f, 0.50f, 0.55f, 0.05f };   // midR, midL, rearR, rearL
        for (int pair = 0; pair < 2; pair++)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                int li = pair * 2 + (s > 0 ? 0 : 1);
                float ph = p.legPhase + legOff[li];
                float lift = StepLift(ph) * p.stepAmp;
                float reach = Mathf.Sin(ph * Mathf.PI * 2f);
                float jitter = Hash(li * 37, 3, NOISE_SEED + 53);   // per-leg asymmetry

                bool mid = pair == 0;
                // The KNEE must be the outermost and highest point, with the shin coming
                // back INWARD and down to the foot. Splaying continuously outward from
                // root to foot — which is what this did at first — just makes straight
                // diagonal sticks and loses the arch completely.
                float rootF = mid ? 0.112f : 0.088f;
                float rootY = H * (mid ? 0.615f : 0.520f) + bob;
                float kneeF = (mid ? 0.368f : 0.310f) + p.splay * 0.040f + jitter * 0.016f;
                // Kept clear of the shoulder line on purpose. When the knees peaked at
                // the same height as the shoulder plates and the crown, the entire top of
                // the silhouette became one cluttered band with no hierarchy — crown
                // highest, then shoulders, then knees, reads far cleaner.
                float kneeY = H * ((mid ? 0.726f : 0.640f) - p.splay * 0.032f)
                              + bob * 0.6f + lift * H * 0.038f;
                float footF = (mid ? 0.300f : 0.248f) + p.splay * 0.036f + reach * 0.026f;
                float footY = H * (mid ? 0.090f : 0.068f) + lift * H * 0.105f;

                float rootX = cx + s * W * rootF;
                float kneeX = cx + s * W * kneeF;
                float footX = cx + s * W * footF;

                // Femur up to the knee, then the long shin falling away outside it.
                AddCapsule(cov, hgt, W, H, shx(rootX, rootY), rootY, shx(kneeX, kneeY), kneeY,
                           W * (mid ? 0.052f : 0.044f), W * (mid ? 0.032f : 0.027f), W * 0.024f);
                AddDisc(cov, hgt, W, H, shx(kneeX, kneeY), kneeY, W * (mid ? 0.038f : 0.032f), W * 0.020f);
                AddCapsule(cov, hgt, W, H, shx(kneeX, kneeY), kneeY, shx(footX, footY), footY,
                           W * (mid ? 0.032f : 0.027f), W * 0.011f, W * 0.020f);

                // Tarsal claw, hooked inward where the leg meets the ground.
                float tipX = footX - s * W * 0.030f;
                float tipY = footY - H * 0.030f;
                AddCapsule(cov, hgt, W, H, shx(footX, footY), footY, shx(tipX, tipY), tipY,
                           W * 0.010f, W * 0.0025f, W * 0.010f);
            }
        }

        // ── Ribcage: a hard angular plate, not a ball ──
        // ── Ribcage: a hard angular plate, not a ball ──
        pxs[0] = shx(cx - W * 0.095f, H * 0.48f + bob); pys[0] = H * 0.48f + bob;
        pxs[1] = shx(cx - W * 0.170f, H * 0.60f + bob); pys[1] = H * 0.60f + bob;
        pxs[2] = shx(cx - W * 0.135f, H * 0.725f + bob); pys[2] = H * 0.725f + bob;
        pxs[3] = shx(cx + W * 0.145f, H * 0.745f + bob); pys[3] = H * 0.745f + bob;
        pxs[4] = shx(cx + W * 0.185f, H * 0.605f + bob); pys[4] = H * 0.605f + bob;
        pxs[5] = shx(cx + W * 0.105f, H * 0.475f + bob); pys[5] = H * 0.475f + bob;
        AddPoly(cov, hgt, W, H, pxs, pys, 6, W * 0.030f, 2.6f);

        // Sternum ridge, so the chest has a centre line to catch light.
        AddCapsule(cov, hgt, W, H, shx(cx + W * 0.01f, H * 0.71f + bob), H * 0.71f + bob,
                   shx(cx - W * 0.01f, H * 0.50f + bob), H * 0.50f + bob,
                   W * 0.042f, W * 0.030f, W * 0.030f);

        // ── Shoulder plates: tall, angular, rising ABOVE and BEHIND the skull ──
        // Deliberately mismatched. The right one is taller, heavier and pushed further
        // out; mirrored shoulders were a large part of the "generated" look.
        float shrug = armSw * H * 0.012f;
        pxs[0] = shx(cx - W * 0.120f, H * 0.665f + bob); pys[0] = H * 0.665f + bob;
        pxs[1] = shx(cx - W * 0.232f, H * 0.725f + bob - shrug); pys[1] = H * 0.725f + bob - shrug;
        pxs[2] = shx(cx - W * 0.248f, H * 0.828f + bob - shrug); pys[2] = H * 0.828f + bob - shrug;
        pxs[3] = shx(cx - W * 0.160f, H * 0.808f + bob - shrug); pys[3] = H * 0.808f + bob - shrug;
        pxs[4] = shx(cx - W * 0.098f, H * 0.742f + bob); pys[4] = H * 0.742f + bob;
        AddPoly(cov, hgt, W, H, pxs, pys, 5, W * 0.026f, 2.5f);

        pxs[0] = shx(cx + W * 0.105f, H * 0.660f + bob); pys[0] = H * 0.660f + bob;
        pxs[1] = shx(cx + W * 0.240f, H * 0.740f + bob + shrug); pys[1] = H * 0.740f + bob + shrug;
        pxs[2] = shx(cx + W * 0.282f, H * 0.892f + bob + shrug); pys[2] = H * 0.892f + bob + shrug;
        pxs[3] = shx(cx + W * 0.178f, H * 0.852f + bob + shrug); pys[3] = H * 0.852f + bob + shrug;
        pxs[4] = shx(cx + W * 0.090f, H * 0.748f + bob); pys[4] = H * 0.748f + bob;
        AddPoly(cov, hgt, W, H, pxs, pys, 5, W * 0.026f, 2.5f);

        // Blades erupting from each shoulder plate.
        for (int b = 0; b < 5; b++)
        {
            int side = (b < 3) ? 1 : -1;
            int bi = (b < 3) ? b : b - 3;
            float baseF = (side > 0) ? 0.175f + bi * 0.036f : 0.150f + bi * 0.040f;
            float baseY2 = (side > 0) ? H * (0.845f + bi * 0.012f) : H * (0.800f + bi * 0.010f);
            baseY2 += bob;
            float jag = 0.62f + 0.75f * Hash(b * 31, 5, NOISE_SEED + 11);
            float len = H * 0.10f * jag * ((side > 0) ? 1.15f : 0.85f);
            float sway = Mathf.Sin(p.tendrilPhase * 1.2f + b) * W * 0.012f;
            float halfw = W * (0.020f - bi * 0.003f);
            pxs[0] = shx(cx + side * W * (baseF - 0.018f), baseY2); pys[0] = baseY2;
            pxs[1] = shx(cx + side * W * (baseF + 0.018f), baseY2); pys[1] = baseY2;
            pxs[2] = shx(cx + side * W * (baseF + 0.030f) + sway, baseY2 + len);
            pys[2] = baseY2 + len;
            AddPoly(cov, hgt, W, H, pxs, pys, 3, halfw * 0.55f, 2.0f);
        }

        // ── Neck: thrust forward and DOWN out of the shoulders ──
        float napeY = H * 0.700f + bob;
        float skullY = H * 0.560f + bob + headB;
        AddCapsule(cov, hgt, W, H, shx(cx + W * 0.015f, napeY), napeY,
                   shx(cx - W * 0.005f, skullY), skullY, W * 0.072f, W * 0.088f, W * 0.040f);

        // ── Skull: a downward wedge, tipped off vertical ──
        float tip = W * 0.012f;   // head tilt — never let the face sit dead level
        pxs[0] = shx(cx - W * 0.132f + tip, H * 0.500f + bob + headB); pys[0] = H * 0.500f + bob + headB;
        pxs[1] = shx(cx - W * 0.118f + tip, H * 0.598f + bob + headB); pys[1] = H * 0.598f + bob + headB;
        pxs[2] = shx(cx + W * 0.010f + tip, H * 0.628f + bob + headB); pys[2] = H * 0.628f + bob + headB;
        pxs[3] = shx(cx + W * 0.128f + tip, H * 0.586f + bob + headB); pys[3] = H * 0.586f + bob + headB;
        pxs[4] = shx(cx + W * 0.138f + tip, H * 0.478f + bob + headB); pys[4] = H * 0.478f + bob + headB;
        pxs[5] = shx(cx + W * 0.072f + tip, H * 0.335f + bob + headB); pys[5] = H * 0.335f + bob + headB;
        pxs[6] = shx(cx - W * 0.068f + tip, H * 0.332f + bob + headB); pys[6] = H * 0.332f + bob + headB;
        AddPoly(cov, hgt, W, H, pxs, pys, 7, W * 0.022f, 2.4f);

        // Brow ridge — a hard shelf over the eyes, which the self-shadow pass then drops
        // the sockets underneath.
        AddCapsule(cov, hgt, W, H,
                   shx(cx - W * 0.118f + tip, H * 0.578f + bob + headB), H * 0.578f + bob + headB,
                   shx(cx + W * 0.122f + tip, H * 0.566f + bob + headB), H * 0.566f + bob + headB,
                   W * 0.030f, W * 0.030f, W * 0.020f);
        // Cheek / jaw-hinge masses.
        AddDisc(cov, hgt, W, H, shx(cx - W * 0.100f + tip, H * 0.452f + bob + headB),
                H * 0.452f + bob + headB, W * 0.052f, W * 0.032f);
        AddDisc(cov, hgt, W, H, shx(cx + W * 0.108f + tip, H * 0.446f + bob + headB),
                H * 0.446f + bob + headB, W * 0.056f, W * 0.032f);

        // ── Crown: a row of uneven backswept blades ──
        for (int i = 0; i < 7; i++)
        {
            float f = (i / 6f) * 2f - 1f;
            float rootX = cx + f * W * 0.105f + tip;
            float rootY = H * (0.612f - Mathf.Abs(f) * 0.030f) + bob + headB;
            float jag = 0.45f + 1.05f * Hash(i * 41, 9, NOISE_SEED + 19);
            float len = H * (0.085f + 0.075f * (1f - Mathf.Abs(f))) * jag;
            float sway = Mathf.Sin(p.tendrilPhase * 1.35f + i * 0.9f) * W * 0.016f;
            float halfw = W * (0.019f - 0.005f * Mathf.Abs(f));
            pxs[0] = shx(rootX - halfw, rootY); pys[0] = rootY;
            pxs[1] = shx(rootX + halfw, rootY); pys[1] = rootY;
            pxs[2] = shx(rootX + f * W * 0.060f + sway, rootY + len); pys[2] = rootY + len;
            AddPoly(cov, hgt, W, H, pxs, pys, 3, W * 0.010f, 1.9f);
        }

        // ── Two heavy horns sweeping back off the temples ──
        for (int s = -1; s <= 1; s += 2)
        {
            float hx = cx + s * W * 0.125f + tip, hy2 = H * 0.575f + bob + headB;
            float lenMul = (s > 0) ? 1.0f : 0.70f;      // the left one is broken short
            int seg = 9;
            float ppx = hx, ppy = hy2;
            for (int i = 0; i < seg; i++)
            {
                float st = (i + 1) / (float)seg;
                float outward = 0.075f * st + 0.055f * st * st;
                float nx2 = hx + s * W * (outward + 0.010f * Mathf.Sin(p.tendrilPhase + s));
                float ny2 = hy2 + H * (0.235f * lenMul) * st - H * 0.030f * st * st;
                float rr = Mathf.Lerp(W * 0.042f, W * 0.004f, st * st);
                AddCapsule(cov, hgt, W, H, shx(ppx, ppy), ppy, shx(nx2, ny2), ny2, rr, rr * 0.7f, rr * 0.6f);
                ppx = nx2; ppy = ny2;
            }
        }

        // ── Arms: long, swung wide, knuckles near the floor ──
        // The gaps these leave between forearm and ribcage are the negative space the
        // old silhouette never had. Read at a distance, those two triangles of empty
        // background do more for the shape than any amount of surface detail.
        for (int s = -1; s <= 1; s += 2)
        {
            float swing = s * armSw;
            float bulk = (s > 0) ? 1.16f : 0.90f;
            float legPh = p.legPhase + (s > 0 ? 0f : 0.5f);
            float sw2 = Mathf.Sin(legPh * Mathf.PI * 2f);     // arms counter-swing the legs

            // armReach drives the whole grab. Cocked back (-1) the elbows drop and the
            // hands pull out and up; thrown forward (+1) the elbows hike out and up and
            // the hands sweep inward and down, toward the player. On a front-facing
            // sprite that inward-and-down sweep is what actually reads as "reaching at
            // you" — you cannot lengthen the arm toward camera, so the pose has to say it.
            float rf = p.armReach;
            float shX = cx + s * W * 0.170f, shY = H * 0.705f + bob;
            float elbX = cx + s * W * (0.268f + 0.020f * swing + 0.028f * rf) + sw2 * W * 0.014f;
            float elbY = H * (0.435f + 0.055f * rf) + bob * 0.4f + swing * H * 0.022f
                         + StepLift(legPh) * p.stepAmp * H * 0.022f;
            float wrX = cx + s * W * (0.190f - 0.028f * swing - 0.062f * rf) - sw2 * W * 0.026f;
            float wrY = H * (0.175f - 0.045f * rf) - sw2 * s * H * 0.030f
                        + StepLift(legPh) * p.stepAmp * H * 0.055f;

            AddCapsule(cov, hgt, W, H, shx(shX, shY), shY, shx(elbX, elbY), elbY,
                       W * 0.088f * bulk, W * 0.055f * bulk, W * 0.034f);
            AddCapsule(cov, hgt, W, H, shx(elbX, elbY), elbY, shx(wrX, wrY), wrY,
                       W * 0.058f * bulk, W * 0.040f * bulk, W * 0.030f);
            AddDisc(cov, hgt, W, H, shx(elbX, elbY), elbY, W * 0.060f * bulk, W * 0.026f);
            AddDisc(cov, hgt, W, H, shx(wrX, wrY), wrY, W * 0.048f * bulk, W * 0.024f);

            // Long hooked claws.
            for (int c = 0; c < 4; c++)
            {
                float ca = c - 1.5f;
                float k0x = wrX + ca * W * (0.023f + 0.010f * Mathf.Max(0f, rf));
                float k0y = wrY - H * 0.020f;
                float midX = k0x + s * W * 0.012f;
                float midY = k0y - H * 0.062f;
                float tipX = k0x + s * W * (0.036f + 0.016f * Mathf.Max(0f, rf));
                float tipY = k0y - H * 0.118f - Mathf.Abs(ca) * H * 0.008f;
                AddCapsule(cov, hgt, W, H, shx(wrX, wrY), wrY, shx(midX, midY), midY,
                           W * 0.019f, W * 0.009f, W * 0.012f);
                AddCapsule(cov, hgt, W, H, shx(midX, midY), midY, shx(tipX, tipY), tipY,
                           W * 0.009f, W * 0.002f, W * 0.009f);
            }
        }

        // ── Smoke ──
        // Coverage only, never height: haze that pushes height inflates into balloons.
        // Held tight around the hips and behind the shoulders so it frames the figure
        // instead of filling in the negative space the arms just created.
        // The softness MUST exceed the radius. With soft ≈ r these blobs still reach ~0.97
        // coverage, so they render as solid flat discs — a cluster of dark grapes hanging
        // between his legs. Making soft roughly double r caps them in the feathered band
        // where they can only ever be haze.
        for (int k = 0; k < 6; k++)
        {
            float f = (k / 5f) * 2f - 1f;
            float rx2 = cx + f * W * 0.20f;
            float ry2 = H * 0.740f + bob - Mathf.Abs(f) * H * 0.045f
                        + Mathf.Sin(p.tendrilPhase * 1.1f + k * 0.9f) * H * 0.020f;
            AddDisc(cov, null, W, H, shx(rx2, ry2), ry2, W * (0.030f - 0.008f * Mathf.Abs(f)), W * 0.085f);
        }
        for (int k = 0; k < 8; k++)
        {
            float fx = cx + Mathf.Sin(k * 1.7f + p.tendrilPhase * 0.4f) * W * 0.095f;
            float fy = baseY + H * 0.030f + bob * 0.30f + k * H * 0.028f;
            float r = W * (0.062f - k * 0.004f);
            AddDisc(cov, null, W, H, shx(fx, fy), fy, Mathf.Max(3f, r), r * 2.3f);
        }

        // ── Negative space ──
        // Sharpen the joins the polygons leave: a notch under each jaw hinge so the head
        // reads free of the chest, and a nick where each arm passes the ribcage.
        SubDisc(cov, hgt, W, H, shx(cx - W * 0.150f + tip, H * 0.505f + bob), H * 0.505f + bob,
                W * 0.045f, W * 0.045f, 0.45f);
        SubDisc(cov, hgt, W, H, shx(cx + W * 0.162f + tip, H * 0.512f + bob), H * 0.512f + bob,
                W * 0.048f, W * 0.045f, 0.50f);

        // Per-frame "eaten away" holes. They jump around every frame, so the body looks
        // like it's actively dissolving and re-knitting rather than just being ragged.
        // Each is TWO overlapping carves — a single disc punches a perfect circle, and a
        // row of perfect circles reads as bubbles or bullet holes rather than decay.
        for (int k = 0; k < 3; k++)
        {
            // Re-rolled ONLY on glitch frames. Moving them every frame made the body
            // shimmer with holes constantly, which reads as noise rather than as decay.
            int hseed = (p.corrupt > 0.35f) ? p.seed : 0;
            float hx = Hash(k * 13, hseed, NOISE_SEED + 3);
            float hy3 = Hash(k * 29 + 5, hseed, NOISE_SEED + 9);
            float hr = Hash(k * 7 + 11, hseed, NOISE_SEED + 17);
            float ho = Hash(k * 23 + 3, hseed, NOISE_SEED + 29);
            // Held out to the flanks: a hole punched in the middle of the face reads as
            // damage to the sprite, not as decay of the creature.
            float side2 = (hx < 0.5f) ? -1f : 1f;
            float bxp = cx + side2 * W * (0.135f + 0.115f * ho);
            float byp = H * (0.30f + hy3 * 0.36f);
            float br = W * (0.008f + 0.015f * hr);
            SubDisc(cov, hgt, W, H, shx(bxp, byp), byp, br, W * 0.016f, 0.95f);
            SubDisc(cov, hgt, W, H, shx(bxp + (ho - 0.5f) * br * 1.6f, byp + (hr - 0.5f) * br * 1.4f),
                    byp + (hr - 0.5f) * br * 1.4f, br * 0.75f, W * 0.014f, 0.9f);
        }

        // ── Internal fissures ──
        // Drawn into their own mask so the compose pass can light them from *inside*
        // the body only. Run them down the ribcage, following the new chest plate.
        for (int cI = 0; cI < 3; cI++)
        {
            float rootX = cx + (cI - 1) * W * 0.058f;
            float rootY = H * (0.395f + cI * 0.030f) + bob * 0.7f;
            float ppx = rootX, ppy = rootY;
            int seg = 6;
            for (int s2 = 0; s2 < seg; s2++)
            {
                float st = (s2 + 1) / (float)seg;
                float wob = Mathf.Sin(p.tendrilPhase * 0.9f + cI * 2.1f + s2 * 1.3f) * W * 0.012f;
                float nx2 = rootX + (cI - 1) * W * 0.055f * st + wob;
                float ny2 = rootY + st * H * 0.115f * (cI == 1 ? 1.2f : 1f);
                float rr = Mathf.Lerp(W * 0.0055f, W * 0.0015f, st);
                AddCapsule(crk, null, W, H, shx(ppx, ppy), ppy, shx(nx2, ny2), ny2, rr, rr * 0.6f, W * 0.005f);
                ppx = nx2; ppy = ny2;
            }
        }

        // ── Seat the face features into the surface ──
        // Denting the height field under the eyes and maw makes the brow ridge and lips
        // bulge around them. Without this the face is painted onto a smooth dome and no
        // amount of detail in DrawEye/DrawMaw can make it look like it belongs there.
        float eyeYh = H * EYE_Y_FRAC + bob + headB;
        DentHeight(hgt, W, H, shx(cx - W * EYE_DX_FRAC, eyeYh), eyeYh, W * 0.048f, H * 0.042f, 8f);
        DentHeight(hgt, W, H, shx(cx + W * EYE_DX_FRAC, eyeYh), eyeYh, W * 0.048f, H * 0.042f, 8f);
        float mawYh = H * MAW_Y_FRAC + bob + headB;
        DentHeight(hgt, W, H, shx(cx, mawYh), mawYh, W * 0.118f,
                   H * 0.026f + W * 0.095f * p.mawOpen, 11f);

        // ── Internal fissures ──
        // Drawn into their own mask so the compose pass can light them from *inside*
        // the body only. Three main cracks running up the chest and out along the back.
        for (int cI = 0; cI < 3; cI++)
        {
            float rootX = cx + (cI - 1) * W * 0.11f;
            float rootY = H * (0.26f + cI * 0.050f) + bob;
            float ppx = rootX, ppy = rootY;
            int seg = 6;
            for (int s2 = 0; s2 < seg; s2++)
            {
                float st = (s2 + 1) / (float)seg;
                float wob = Mathf.Sin(p.tendrilPhase * 0.9f + cI * 2.1f + s2 * 1.3f) * W * 0.014f;
                float nx = rootX + (cI - 1) * W * 0.07f * st + wob;
                float ny = rootY + st * H * 0.135f * (cI == 1 ? 1.25f : 1f);
                float rr = Mathf.Lerp(W * 0.0055f, W * 0.0015f, st);
                AddCapsule(crk, null, W, H, shx(ppx, ppy), ppy, shx(nx, ny), ny, rr, rr * 0.6f, W * 0.005f);
                ppx = nx; ppy = ny;
            }
        }
        // (No facial fissures: at gameplay zoom, small hot marks beside the eyes stop
        // reading as cracks and start reading as blemishes. The chest cracks carry it.)

        // ── Pre-pass: cavity term ──
        // Comparing the height field against a blurred copy of itself gives concavity:
        // wherever the surface sits below its own neighbourhood average, it's in a
        // crease. That is the armpits, the joints, under the brow and the jaw — all the
        // places that were previously lit exactly as brightly as the crests.
        BoxBlur(hgt, hblur, htmp, W, H, 6);

        // ── Pre-pass: vein field and surface grain, both BODY-ANCHORED ──
        //
        // Two bugs lived here and they produced the "sometimes purple stripes, sometimes
        // black" flicker:
        //
        //   1. The domain warp was driven by smokeScroll, which runs 0→77 across the idle
        //      loop and jumps to 40→103 for the attack. With a ±22 px warp amplitude the
        //      whole vein network swam by up to 44 px EVERY FRAME, so any given patch of
        //      hide strobed between veined and bare, and the pattern teleported when he
        //      switched animations. Smoke is allowed to drift; markings are not.
        //   2. Even held still, the field was sampled in TEXTURE space while the body
        //      moves underneath it (bob, walk sway, lunge shear), so the markings slid
        //      across him as he walked.
        //
        // Both are fixed by sampling in BODY-LOCAL space: undo the frame's shear, lunge
        // offset and bob, and use a fixed warp. The veins are now painted on his hide and
        // travel with it, which is also why the surface grain is baked here too.
        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            float byl = y - bob;
            float lean = shear * W * ((y - baseY) / H) + p.forwardShift * W;
            for (int x = 0; x < W; x++)
            {
                int idx = row + x;
                if (cov[idx] <= 0.001f) { vfield[idx] = 0f; ffield[idx] = 0.5f; continue; }
                float bxl = x - lean;
                float na = SampleWrap(_nf1, bxl, byl);
                float nb = SampleWrap(_nf2, bxl, byl);
                vfield[idx] = SampleWrap(_nfVein, bxl + (nb - 0.5f) * 22f, byl + (na - 0.5f) * 22f);
                ffield[idx] = SampleWrap(_nfFine, bxl, byl);
            }
        }

        // ── Compose colour with fractal-noise smoke (into the supplied scratch) ──
        // Face centre, used to hold the striations off the head: the eyes and maw are the
        // focal points and anything crossing them competes for the same attention.
        float faceYc = H * 0.52f + bob + headB;
        float faceXc = cx + p.leanTop * W * ((faceYc - baseY) / H);
        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            for (int x = 0; x < W; x++)
            {
                int idx = row + x;
                float c = cov[idx];
                if (c <= 0.001f) { outPx[idx] = new Color(0, 0, 0, 0); continue; }

                // Cheap lookups into the baked fields (scrolled for smoke drift),
                // equivalent to the old per-pixel Fractal() calls but ~15× faster.
                float n = SampleWrap(_nf1, x + p.smokeScroll * 0.3f, y - p.smokeScroll);
                float n2 = SampleWrap(_nf2, x, y - p.smokeScroll * 1.7f);

                // Solid interior vs feathered, ragged edge.
                float solid = SStep(0.42f, 0.62f, c);
                float edge = Mathf.Clamp01(c - solid);
                float raggedEdge = edge * SStep(0.30f, 0.72f, n); // punch smoky holes at the rim
                float alpha = Mathf.Clamp01(solid + raggedEdge);
                alpha *= 0.86f + 0.14f * n2;                       // subtle shimmer

                // Surface normal straight out of the coverage gradient. This is the
                // upgrade that gives him volume: the rim can now know which way a
                // surface faces instead of uniformly outlining the whole silhouette.
                float gx = 0f, gy = 0f;
                if (x > 0 && x < W - 1) gx = cov[idx + 1] - cov[idx - 1];
                if (y > 0 && y < H - 1) gy = cov[idx + W] - cov[idx - W];
                float gl = Mathf.Sqrt(gx * gx + gy * gy);
                float nxN = 0f, nyN = 0f;
                if (gl > 1e-4f) { nxN = -gx / gl; nyN = -gy / gl; }   // outward normal

                // ── Surface shading from the height field ──
                // The coverage field saturates at 1 across the entire interior, so it
                // carries no information about form — every pass that reads only coverage
                // can light the OUTLINE and nothing else, which is exactly why he used to
                // look like a black cut-out with decoration painted on. The primitives now
                // also accumulate a height field, so here we can reconstruct a real
                // surface normal and light it properly.
                float hgx = 0f, hgy = 0f;
                if (x > 0 && x < W - 1)
                {
                    hgx = hgt[idx - 1] - hgt[idx + 1];
                    // Micro-grain, plus the vein network cut IN as a groove (hence the
                    // minus). Perturbing the normal rather than just darkening pixels is
                    // what makes the veins catch light along their edges and stop
                    // looking like something painted on the outside.
                    hgx += (ffield[idx - 1] - ffield[idx + 1]) * FINE_BUMP
                         - (vfield[idx - 1] - vfield[idx + 1]) * VEIN_BUMP;
                }
                if (y > 0 && y < H - 1)
                {
                    hgy = hgt[idx - W] - hgt[idx + W];
                    hgy += (ffield[idx - W] - ffield[idx + W]) * FINE_BUMP
                         - (vfield[idx - W] - vfield[idx + W]) * VEIN_BUMP;
                }
                float hinv = 1f / Mathf.Sqrt(hgx * hgx + hgy * hgy + NZ * NZ);
                float Nx = hgx * hinv, Ny = hgy * hinv, Nz = NZ * hinv;

                float ndl = Mathf.Clamp01(Nx * LX + Ny * LY + Nz * LZ);
                float ndh = Mathf.Clamp01(Nx * HVX + Ny * HVY + Nz * HVZ);

                // Two-lobe specular: a broad sheen over the whole form plus a tight
                // highlight on the crests. Obsidian is defined almost entirely by its
                // reflections, so on a body this dark the speculars ARE the anatomy.
                float s2 = ndh * ndh;
                float s4 = s2 * s2;
                float sheen = s4 * s4;                 // ndh^8  — broad
                float s16 = sheen * sheen;
                float tight = s16 * s16;               // ndh^32 — crest highlight only

                float depth = Mathf.Clamp01((c - 0.45f) * 1.9f);
                float bylC = y - bob;
                float bxlC = x - (shear * W * ((y - baseY) / H) + p.forwardShift * W);
                float grain = 0.88f + 0.24f * ffield[idx];

                // ── Violet vein network ──
                // A branching RIDGED-noise field, domain-warped by the smoke noise. The
                // previous version used a periodic triangle wave, and no amount of
                // wobble hid the constant spacing — it always read as painted-on stripes.
                // A ridged field gives filaments that fork, thin out and terminate, which
                // is what actual mineral veining does.
                float vr = vfield[idx];

                float fdx = (x - faceXc) / (W * 0.20f);
                float fdy = (y - faceYc) / (H * 0.20f);
                float faceFade = SStep(0.70f, 1.35f, Mathf.Sqrt(fdx * fdx + fdy * fdy));

                // Intensity varies along the network so stretches of it go dark — a vein
                // that glows evenly end to end is the other big tell of a procedural one.
                float vAmp = Mathf.Clamp01(0.15f + 1.5f * SampleWrap(_nfBlotch, bxlC, bylC));
                float vein = SStep(0.66f, 0.88f, vr) * vAmp * faceFade * solid;
                // The seam sits in a GROOVE. Darkening the shoulders of each vein is what
                // makes it look cut into the shell rather than drawn on the surface.
                float groove = Mathf.Clamp01(SStep(0.36f, 0.56f, vr) - SStep(0.56f, 0.70f, vr))
                               * faceFade * solid;

                // Albedo: near-black, tinted toward the deepest violet so the shell and
                // the veins read as one material rather than two stuck together.
                float grooveDark = 1f - 0.42f * groove;
                float ar = Mathf.Lerp(VOID_EDGE.r, VOID_CORE.r, depth) * grain * grooveDark;
                float ag = Mathf.Lerp(VOID_EDGE.g, VOID_CORE.g, depth) * grain * grooveDark;
                float ab = Mathf.Lerp(VOID_EDGE.b, VOID_CORE.b, depth) * grain * grooveDark;
                ar = Mathf.Lerp(ar, VEIN_DEEP.r, 0.30f * solid);
                ag = Mathf.Lerp(ag, VEIN_DEEP.g, 0.30f * solid);
                ab = Mathf.Lerp(ab, VEIN_DEEP.b, 0.30f * solid);

                // Cavity occlusion.
                float ao = 1f - AO_STRENGTH * Mathf.Clamp01((hblur[idx] - hgt[idx]) / AO_K);

                // Height-field self-shadowing: march toward the light and see whether
                // anything rises above the ray. This is what puts the horns' shadows on
                // his skull and drops the sockets and the underside of the jaw into
                // darkness — the cue that reads as "solid object" more than any other.
                float shadow = 0f;
                float h0 = hgt[idx];
                for (int t = 1; t <= SH_STEPS; t++)
                {
                    int sxi = (int)(x + L2X * SH_STEP * t + 0.5f);
                    int syi = (int)(y + L2Y * SH_STEP * t + 0.5f);
                    if (sxi < 0 || sxi >= W || syi < 0 || syi >= H) break;
                    float occ = hgt[syi * W + sxi] - (h0 + SH_RISE * SH_STEP * t);
                    if (occ > 0f)
                    {
                        float o = Mathf.Clamp01(occ / SH_SOFT);
                        if (o > shadow) shadow = o;
                    }
                }
                float shadowK = 1f - SH_STRENGTH * shadow;

                float lightK = (0.30f + 0.95f * ndl * shadowK) * ao;
                // Kept deliberately low. He has to stay BLACK: at anything like a
                // realistic gloss level the speculars cover the whole body and he turns
                // into a shiny purple balloon. Just enough to catch the crests.
                float specK = (sheen * 0.045f + tight * 0.30f) * (0.35f + 0.65f * solid) * shadowK * ao;

                Color body = new Color(
                    ar * lightK + SPEC.r * specK,
                    ag * lightK + SPEC.g * specK,
                    ab * lightK + SPEC.b * specK, 1f);

                // Veins emit; they are not affected by the key light.
                if (vein > 0.002f)
                {
                    float vg = Mathf.Clamp01(p.veinGlow);
                    Color vc = Color.Lerp(VEIN_MID, VEIN_HOT, vein * vein);
                    float vk = Mathf.Clamp01(vein * 0.55f * vg);
                    body = new Color(body.r + vc.r * vk, body.g + vc.g * vk, body.b + vc.b * vk, 1f);
                }

                // Rim: ember-hot where the surface faces up/left into the key light,
                // cold violet on the undersides. Two-tone is what stops it reading as
                // a flat purple outline sticker.
                float facing = Mathf.Clamp01(nyN * 0.80f - nxN * 0.42f + 0.26f);
                Color rimCol = Color.Lerp(RIM_COOL, RIM_HOT, facing * facing);
                float rimBand = Mathf.Clamp01(edge * 2.6f) * (0.45f + 0.55f * n);
                // The floor term matters: without a minimum rim contribution he vanishes
                // completely against a night biome, because a black shape on black has
                // no silhouette at all.
                body = Color.Lerp(body, rimCol, Mathf.Clamp01(rimBand * (0.55f + 0.80f * facing)));

                // Internal fissures glowing through the shell (interior only).
                float k = crk[idx] * p.crackGlow * solid * faceFade;
                if (k > 0.002f)
                {
                    body = Color.Lerp(body, CRACK, Mathf.Clamp01(k * 1.5f));
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(k) * solid);
                }

                body.a = alpha;
                outPx[idx] = body;
            }
        }

        // ── Eyes: fierce fiery glare with a vertical slit pupil ──
        float eyeY = H * EYE_Y_FRAC + bob + headB;
        DrawEye(outPx, W, H, shx(cx - W * EYE_DX_FRAC, eyeY), eyeY, +1, p.eyeBright);
        DrawEye(outPx, W, H, shx(cx + W * EYE_DX_FRAC, eyeY), eyeY, -1, p.eyeBright);

        // ── Huge central devouring maw (fanged even at rest, gapes on attack) ──
        float mawCy = H * MAW_Y_FRAC + bob + headB;
        DrawMaw(outPx, W, H, shx(cx, mawCy), mawCy, p.mawOpen);

        // ── Dripping ichor from the maw (sways; longer when gaping) ──
        // Only while the jaw is actually open, and hung below the lower lip. Strands
        // drawn INSIDE the mouth just read as two extra stubby tusks.
        if (p.mawOpen > 0.38f)
        {
            float dripFade = Mathf.Clamp01((p.mawOpen - 0.38f) * 2.6f);
            float lipY = mawCy - (H * 0.030f + W * 0.115f * p.mawOpen);
            for (int dI = 0; dI < 2; dI++)
            {
                float dxo = (dI == 0 ? -1f : 1f) * W * 0.055f;
                float dripLen = H * (0.03f + 0.055f * p.mawOpen
                                     + 0.018f * Mathf.Sin(p.tendrilPhase * 2f + dI));
                float topX = shx(cx + dxo, lipY);
                float botX = topX + Mathf.Sin(p.tendrilPhase * 1.7f + dI * 2f) * W * 0.012f;
                DrawDrip(outPx, W, H, topX, lipY, botX, lipY - dripLen, dripFade);
            }
        }

        // ── Datamosh post-pass (runs on the finished colour, so the eyes and maw tear
        //    along with the body instead of floating serenely on top of the corruption).
        ApplyGlitchBands(outPx, W, H, p, rowTmp);

        // Convert to Color32 (SetPixels32 upload is cheaper and this is thread-safe).
        var px32 = new Color32[W * H];
        for (int i = 0; i < px32.Length; i++)
        {
            Color c = outPx[i];
            px32[i] = new Color32(
                (byte)(Mathf.Clamp01(c.r) * 255f),
                (byte)(Mathf.Clamp01(c.g) * 255f),
                (byte)(Mathf.Clamp01(c.b) * 255f),
                (byte)(Mathf.Clamp01(c.a) * 255f));
        }
        return px32;
    }

    // ── Datamosh ──
    // Slices the finished frame into horizontal bands and, for a hashed subset of them:
    //   • shears the band sideways (quantised to 2 px so it reads as digital, not blur)
    //   • occasionally drops the band almost to nothing (a dead scanline block)
    //   • fringes the torn leading edge with cyan (channel misregistration)
    // Plus a rolling scan bar that walks down the body over the animation, and very
    // light scanlines throughout. All of it is BAKED, so the corruption keeps moving
    // even when the enemy is standing perfectly still — the runtime layers on top then
    // add the parts that have to react to gameplay.
    private static void ApplyGlitchBands(Color[] px, int W, int H, FrameParams p, Color[] rowTmp)
    {
        // A clean frame stays COMPLETELY clean. Previously every frame carried scanlines,
        // a rolling bar and a bit of band shift, and permanent low-level corruption is
        // exactly what a badly-compressed sprite looks like. The effect only reads as
        // intentional when most frames are sharp and the glitch frames hit hard.
        if (p.corrupt < 0.02f) return;

        const int BAND = 7;
        int bands = (H + BAND - 1) / BAND;

        for (int b = 0; b < bands; b++)
        {
            float h1 = Hash(b, p.seed * 31, NOISE_SEED + 41);
            if (h1 > 0.07f + 0.24f * p.corrupt) continue;

            float h2 = Hash(b + 91, p.seed * 17, NOISE_SEED + 57);
            float h3 = Hash(b + 313, p.seed * 7, NOISE_SEED + 73);

            // Big, hard, quantised offsets. Timid 2 px nudges read as blur; a clean
            // 20 px step with a hard edge reads as a torn scanline.
            int shift = Mathf.RoundToInt((h2 * 2f - 1f) * W * (0.016f + 0.062f * p.corrupt));
            shift = (shift / 3) * 3;
            float drop = (h3 > 0.93f) ? 0.35f : 1f;

            // Per-band RGB separation. Splitting the channels is the thing that makes a
            // displaced band unmistakably a DIGITAL artifact rather than a drawing error.
            int split = 2 + (int)(h1 * 97f) % 5;
            bool smear = h3 > 0.74f && h3 <= 0.93f;
            int smearX0 = (int)(h2 * (W - 1));
            int smearRun = (int)(W * (0.05f + 0.13f * p.corrupt));

            int y1 = Mathf.Min(H, (b + 1) * BAND);
            for (int y = b * BAND; y < y1; y++)
            {
                int row = y * W;
                System.Array.Copy(px, row, rowTmp, 0, W);
                for (int x = 0; x < W; x++)
                {
                    int sx = x - shift;
                    if (sx < 0 || sx >= W) { px[row + x] = new Color(0f, 0f, 0f, 0f); continue; }

                    Color cg = rowTmp[sx];
                    Color cr = rowTmp[Mathf.Clamp(sx + split, 0, W - 1)];
                    Color cb = rowTmp[Mathf.Clamp(sx - split, 0, W - 1)];

                    // Alpha comes mostly from the green tap, with the offset channels
                    // able to extend it slightly — that's what leaves coloured fringes
                    // hanging off the leading and trailing edges of the tear.
                    float a = Mathf.Max(cg.a, 0.55f * Mathf.Max(cr.a, cb.a)) * drop;
                    if (a <= 0.004f) { px[row + x] = new Color(0f, 0f, 0f, 0f); continue; }

                    px[row + x] = new Color(cr.r * (0.35f + 0.65f * cr.a),
                                            cg.g * (0.35f + 0.65f * cg.a),
                                            cb.b * (0.35f + 0.65f * cb.a), a);
                }

                if (smear)
                {
                    Color src = rowTmp[Mathf.Clamp(smearX0, 0, W - 1)];
                    int xEnd = Mathf.Min(W, smearX0 + smearRun);
                    for (int x = Mathf.Max(0, smearX0); x < xEnd; x++)
                    {
                        float ft = 1f - (x - smearX0) / (float)Mathf.Max(smearRun, 1);
                        px[row + x] = Color.Lerp(px[row + x], src, Mathf.Clamp01(ft * 0.95f));
                    }
                }
            }
        }

        // ── Block mosaic ──
        // Only on the heavier frames, and with chunkier blocks than before. This is the
        // one corruption mode that isn't row-based, so it breaks the horizontal rhythm.
        if (p.corrupt > 0.30f)
        {
            for (int q = 0; q < 3; q++)
            {
                float hA = Hash(q * 57 + 5, p.seed * 13, NOISE_SEED + 101);
                if (hA > 0.34f * p.corrupt) continue;

                float hB = Hash(q * 131 + 9, p.seed * 29, NOISE_SEED + 113);
                float hC = Hash(q * 211 + 3, p.seed * 37, NOISE_SEED + 127);

                int bw = 5 + (int)(hB * 8f);
                int rx0 = (int)(hB * (W - 1));
                int ry0 = (int)(hC * (H - 1));
                int rx1 = Mathf.Min(W, rx0 + (int)(W * (0.10f + 0.18f * p.corrupt)));
                int ry1 = Mathf.Min(H, ry0 + (int)(H * (0.04f + 0.09f * p.corrupt)));

                for (int by = ry0; by < ry1; by += bw)
                    for (int bx = rx0; bx < rx1; bx += bw)
                    {
                        Color src = px[by * W + bx];
                        if (src.a <= 0.02f) continue;
                        int yEnd = Mathf.Min(ry1, by + bw), xEnd = Mathf.Min(rx1, bx + bw);
                        for (int y = by; y < yEnd; y++)
                        {
                            int row = y * W;
                            for (int x = bx; x < xEnd; x++)
                            {
                                // Original alpha is kept so the mosaic can never bleed
                                // the body outward past its own silhouette.
                                Color d = px[row + x];
                                px[row + x] = new Color(src.r, src.g, src.b, d.a);
                            }
                        }
                    }
            }
        }

        // ── Readout bar ──
        // A bright band sweeping the body, only on glitch frames. Gated to SOLID pixels:
        // letting it cross the low-alpha smoke drew a hard line out into the haze that
        // looked like a UI artifact, and it must not tint, or a bar happening to cross
        // his eyes makes him look like he's wearing a visor.
        int scanY = (p.seed * 23) % H;
        const int scanHalf = 3;
        float scanK = 0.55f * p.corrupt;
        for (int y = Mathf.Max(0, scanY - scanHalf); y < Mathf.Min(H, scanY + scanHalf); y++)
        {
            float f = (1f - Mathf.Abs(y - scanY) / (float)scanHalf) * scanK;
            int row = y * W;
            for (int x = 0; x < W; x++)
            {
                Color c = px[row + x];
                if (c.a <= 0.6f) continue;
                float k = 1f + 0.55f * f;
                px[row + x] = new Color(Mathf.Min(1f, c.r * k + 0.07f * f),
                                        Mathf.Min(1f, c.g * k + 0.05f * f),
                                        Mathf.Min(1f, c.b * k + 0.07f * f), c.a);
            }
        }

        // Scanlines are part of the glitch now, not a permanent veil over the sprite.
        // Applied constantly they just softened and dirtied every frame.
        if (p.corrupt > 0.45f)
        {
            for (int y = 0; y < H; y += 3)
            {
                int row = y * W;
                float k = 1f - 0.16f * p.corrupt;
                for (int x = 0; x < W; x++) px[row + x].a *= k;
            }
        }
    }

    // Fierce, tilted, almond eye with a VERTICAL SLIT pupil and a brow shadow above it.
    // The slit is the whole trick: a solid glowing dot reads as a lamp, a slit reads as
    // something looking back at you.
    private static void DrawEye(Color[] px, int W, int H, float ex, float ey, int side, float bright)
    {
        // The halo has to be BOUNDED. eyeBright peaks near 2.0 on the bite frame, and
        // feeding that straight into the glow turned each eye into a featureless red
        // blob exactly when the player most needs to read the attack.
        float hb = Mathf.Min(bright, 1.30f);
        float glowR = W * 0.070f * (0.9f + 0.18f * hb);
        float ax = W * 0.026f;      // almond half-width
        float ay = W * 0.0125f;     // almond half-height
        float ang = side * 20f * Mathf.Deg2Rad;   // angry tilt: inner corner lower
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);

        int minx = Mathf.Max(0, (int)(ex - glowR)), maxx = Mathf.Min(W - 1, (int)(ex + glowR));
        int miny = Mathf.Max(0, (int)(ey - glowR)), maxy = Mathf.Min(H - 1, (int)(ey + glowR * 1.4f));

        for (int y = miny; y <= maxy; y++)
            for (int x = minx; x <= maxx; x++)
            {
                float dx = x - ex, dy = y - ey;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                int idx = y * W + x;

                // Brow shadow: darken the body just ABOVE the eye so the glare sits in
                // a socket instead of floating on the forehead.
                if (dy > 0f)
                {
                    float bs = Mathf.Clamp01(1f - d / (glowR * 1.25f))
                               * Mathf.Clamp01((dy - ay) / (glowR * 0.5f));
                    if (bs > 0.002f)
                    {
                        Color cc = px[idx];
                        if (cc.a > 0.02f)
                        {
                            float k = 1f - 0.65f * bs;
                            px[idx] = new Color(cc.r * k, cc.g * k, cc.b * k, cc.a);
                        }
                    }
                }

                // Halo — quartic falloff so it stays tight and doesn't fog the brow.
                float g = 1f - Mathf.Clamp01(d / glowR);
                g = g * g; g = g * g * hb;

                // Rotated almond body.
                float rx = dx * ca - dy * sa;
                float ry = dx * sa + dy * ca;
                float e = (rx / ax) * (rx / ax) + (ry / ay) * (ry / ay);
                float eyeMask = SStep(0f, 0.18f, 1f - Mathf.Clamp01(e));

                if (g <= 0.001f && eyeMask <= 0.001f) continue;

                Color cur = px[idx];

                // Red halo (max-blend so it reads as glow over the dark body).
                Color halo = EYE; halo.a = Mathf.Clamp01(g * 0.78f);
                cur = MaxBlend(cur, halo);

                if (eyeMask > 0.001f)
                {
                    // Iris built in proper layers rather than as one flat red blob:
                    //   ramp   — molten centre falling to deep blood red at the edge
                    //   limbal — the dark ring every real eye has, which is most of what
                    //            stops it bleeding into the surrounding face
                    //   lid    — shadow across the top, so it sits under a brow
                    //   slit   — vertical pupil with a faint hot bleed either side
                    //   glint  — a hard specular catch, always upper-left with the key
                    float er = Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(e, 0f)));   // 0 centre → 1 rim
                    Color iris = Color.Lerp(new Color(1f, 0.30f, 0.045f, 1f),
                                            new Color(0.40f, 0.014f, 0.006f, 1f), er * er);
                    // Extra brightness goes into a WHITE-HOT CORE rather than into the
                    // halo, so a flaring eye gets more intense instead of just bigger.
                    if (bright > 1.1f)
                        iris = Color.Lerp(iris, EYE_CORE,
                                          Mathf.Clamp01((bright - 1.1f) * 0.55f) * (1f - er) * (1f - er));
                    iris = Color.Lerp(iris, new Color(0.10f, 0.004f, 0.008f, 1f),
                                      SStep(0.70f, 0.99f, er));

                    float lid = Mathf.Clamp01(ry / ay) * 0.55f;               // darker toward the top
                    iris = new Color(iris.r * (1f - lid), iris.g * (1f - lid), iris.b * (1f - lid), 1f);

                    iris.a = 1f;
                    cur = Color.Lerp(cur, iris, Mathf.Clamp01(eyeMask * (0.86f + 0.14f * hb)));

                    // Vertical slit pupil.
                    // Slit measured in UNROTATED space so it stays truly vertical. Letting
                    // it rotate with the almond bent it into a hook that read as a
                    // scribble over the eye rather than as a pupil.
                    float slitX = 1f - Mathf.Clamp01(Mathf.Abs(dx) / (ax * 0.17f));
                    float slitY = 1f - Mathf.Clamp01(Mathf.Abs(dy) / (ay * 1.45f));
                    float slit = SStep(0.15f, 0.75f, slitX * (0.35f + 0.65f * slitY)) * eyeMask;
                    if (slit > 0.002f)
                    {
                        // Hot bleed hugging the pupil, then the black slit itself.
                        cur = Color.Lerp(cur, new Color(1f, 0.34f, 0.06f, 1f),
                                         Mathf.Clamp01((slit - 0.25f) * 0.7f) * 0.45f);
                        cur = Color.Lerp(cur, new Color(0.035f, 0.004f, 0.008f, 1f), slit * 0.94f);
                    }

                    // Specular catch. Placed in UNROTATED space so both eyes are lit from
                    // the same side — mirroring it with the almond tilt is a classic tell
                    // that the two eyes were drawn independently.
                    float gx2 = (dx + ax * 0.42f) / (ax * 0.30f);
                    float gy2 = (dy - ay * 0.55f) / (ay * 0.55f);
                    float glint = Mathf.Clamp01(1f - Mathf.Sqrt(gx2 * gx2 + gy2 * gy2));
                    if (glint > 0.001f)
                        cur = Color.Lerp(cur, EYE_CORE, glint * glint * 0.80f * eyeMask);

                    cur.a = 1f;
                }
                px[idx] = cur;
            }
    }

    // Wide lens-shaped maw. Refinements over a plain toothed slot:
    //   • the corners ride UP into a rictus, so it's a grin rather than a gash
    //   • fangs are irregular (per-tooth hashed lengths) and biggest at the front —
    //     a uniform comb of identical triangles reads as a zip, not a jaw
    //   • fang length is absolute rather than proportional, so the teeth still fully
    //     interlock when the mouth is nearly shut
    //   • a dark lip seat OUTSIDE the opening sits it into the face instead of leaving
    //     a glowing slot cut in a flat wall
    //   • the gullet goes properly black in the middle, so the gape has depth
    private static void DrawMaw(Color[] px, int W, int H, float mx, float my, float open)
    {
        float halfW = W * 0.098f;
        float gape = W * 0.024f + W * 0.105f * open;
        // Fangs are sized off a FLOOR, not off the current gape, so a nearly-shut mouth
        // still shows a full set of interlocking teeth with the throat burning between
        // them, instead of collapsing into a fine zigzag scratch.
        float toothScale = Mathf.Max(gape, W * 0.040f);

        // Vertical reach of the hot spill. The bounding box is derived FROM this and the
        // falloff is normalised to reach exactly zero at its edge — when the spill was
        // still non-zero where the loop stopped, it painted a hard-edged olive rectangle
        // across his chest on the wide-gape frames.
        float spillRy = gape * 1.5f + W * 0.025f;
        // Corners pulled DOWN, not up. Lifting them produced an unmistakable
        // jack-o'-lantern smile; a shallow downturn reads as a snarl.
        float curveUp = -W * 0.014f;

        int minx = Mathf.Max(0, (int)(mx - halfW * 1.22f)), maxx = Mathf.Min(W - 1, (int)(mx + halfW * 1.22f));
        int miny = Mathf.Max(0, (int)(my - spillRy)), maxy = Mathf.Min(H - 1, (int)(my + spillRy + curveUp));

        Color lipHot = new Color(1f, 0.55f, 0.16f, 1f);    // hot lip line
        Color throat = new Color(0.80f, 0.05f, 0.02f, 1f); // red throat
        Color deep = new Color(0.05f, 0f, 0f, 1f);          // black gullet
        // Pale fangs, not black ones. Dark teeth against a dark throat only ever read as
        // notches bitten out of the mouth's outline — the single biggest thing holding
        // the maw back. Bone-white tips shading to a bruised violet at the gum give the
        // teeth their own value range and make them pop out of the red.
        Color fangTip = new Color(0.63f, 0.57f, 0.64f, 1f);
        Color fangBase = new Color(0.14f, 0.10f, 0.18f, 1f);
        const float TEETH = 3f;      // ×2 → six fangs across the full width
        const float FANG_W = 0.70f;  // fraction of each period the fang spans

        for (int y = miny; y <= maxy; y++)
            for (int x = minx; x <= maxx; x++)
            {
                float nx = (x - mx) / halfW;
                if (nx < -1.18f || nx > 1.18f) continue;

                float lens = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx));  // 0 at corners, 1 centre
                float halfOpen = gape * lens;
                float mid = my + curveUp * nx * nx;                     // corners ride up
                float ny = y - mid;
                int idx = y * W + x;

                // Hot spill leaking out past the lips onto the surrounding face. Radial
                // and normalised so it is guaranteed to hit zero inside the loop bounds.
                float ey = ny / Mathf.Max(spillRy, 1f);
                float rd = Mathf.Sqrt(nx * nx + ey * ey);
                float spill = Mathf.Clamp01(1f - rd);
                spill = spill * spill * open * 0.34f;
                if (spill > 0.01f)
                {
                    Color sc = lipHot; sc.a = spill;
                    px[idx] = MaxBlend(px[idx], sc);
                }

                // Outside the opening: darken a narrow ring to seat the mouth in the face.
                float outside = Mathf.Abs(ny) - halfOpen;
                if (outside > 0f)
                {
                    float lipS = Mathf.Clamp01(1f - outside / Mathf.Max(W * 0.022f, 1f)) * lens;
                    if (lipS > 0.01f)
                    {
                        Color cc = px[idx];
                        if (cc.a > 0.02f)
                        {
                            float k = 1f - 0.55f * lipS;
                            px[idx] = new Color(cc.r * k, cc.g * k, cc.b * k, cc.a);
                        }
                    }
                    continue;
                }

                float inside = 1f - Mathf.Clamp01(Mathf.Abs(ny) / Mathf.Max(halfOpen, 0.75f));

                // Throat: hot at the lip line → red → black gullet in the very centre.
                Color c = Color.Lerp(lipHot, throat, Mathf.Clamp01(inside * 1.6f));
                c = Color.Lerp(c, deep, Mathf.Clamp01((inside - 0.34f) * 1.9f) * (0.45f + 0.55f * open));
                c.a = Mathf.Clamp01(Mathf.Clamp01(inside * 1.4f) * (0.42f + 0.58f * open));
                px[idx] = MaxBlend(px[idx], c);

                // Upper fangs. FANG_W is the fraction of each period the tooth actually
                // occupies — at 1.0 the teeth touch at the base and the row degenerates
                // into one continuous cartoon zigzag, so they're kept narrow with clear
                // gaps between them and the lengths vary hard from tooth to tooth.
                float tuU = nx * TEETH;
                int tiU = Mathf.FloorToInt(tuU);
                float lenU = (0.35f + 0.75f * Hash(tiU, 7, NOISE_SEED + 61)) * (1.18f - 0.42f * Mathf.Abs(nx));
                float triU = Mathf.Clamp01(1f - Mathf.Abs((tuU - tiU) - 0.5f) * 2f / FANG_W);
                float upperEdge = halfOpen - toothScale * 0.60f * lenU * triU;

                // Lower fangs, offset half a period so the two rows mesh like a trap.
                float tuL = nx * TEETH + 0.5f;
                int tiL = Mathf.FloorToInt(tuL);
                float lenL = (0.30f + 0.70f * Hash(tiL, 23, NOISE_SEED + 83)) * (1.18f - 0.42f * Mathf.Abs(nx));
                float triL = Mathf.Clamp01(1f - Mathf.Abs((tuL - tiL) - 0.5f) * 2f / FANG_W);
                float lowerEdge = -halfOpen + toothScale * 0.54f * lenL * triL;

                const float soft = 0.85f;
                float tU = SStep(upperEdge - soft, upperEdge + soft, ny);
                float tL = SStep(lowerEdge + soft, lowerEdge - soft, ny);
                float tooth = Mathf.Clamp01(Mathf.Max(tU, tL));

                if (tooth > 0.004f)
                {
                    // Shade along the fang: bright at the tip where the throat light
                    // catches it, dark at the gum. This is what makes them read as solid
                    // cones rather than flat cut-out triangles.
                    float shade = (tU >= tL)
                        ? Mathf.Clamp01((ny - upperEdge) / Mathf.Max(halfOpen - upperEdge, 1f))
                        : Mathf.Clamp01((lowerEdge - ny) / Mathf.Max(lowerEdge + halfOpen, 1f));

                    // Per-tooth value jitter, so a row of fangs isn't a row of clones.
                    float tv = 0.82f + 0.28f * Hash((tU >= tL) ? tiU : tiL, 41, NOISE_SEED + 97);
                    Color cc = px[idx];
                    Color f = Color.Lerp(fangTip, fangBase, Mathf.Pow(shade, 0.8f));
                    f = new Color(f.r * tv, f.g * tv, f.b * tv, 1f);
                    f.a = Mathf.Max(cc.a, 0.96f);
                    cc = Color.Lerp(cc, f, tooth * 0.94f);
                    px[idx] = cc;
                }
            }
    }

    // A thin tapering strand of glowing ichor hanging from the maw.
    private static void DrawDrip(Color[] px, int W, int H, float ax, float ay, float bx, float by, float fade)
    {
        Color ichor = new Color(0.95f, 0.22f, 0.05f, 1f);
        // Kept thin and low-alpha on purpose: fatter strands stopped reading as drool
        // and started reading as a second pair of tusks.
        float r0 = W * 0.0050f, r1 = W * 0.0015f;
        float soft = W * 0.009f;
        float maxR = r0 + soft;
        int minx = Mathf.Max(0, (int)(Mathf.Min(ax, bx) - maxR)), maxx = Mathf.Min(W - 1, (int)(Mathf.Max(ax, bx) + maxR));
        int miny = Mathf.Max(0, (int)(Mathf.Min(ay, by) - maxR)), maxy = Mathf.Min(H - 1, (int)(Mathf.Max(ay, by) + maxR));
        Vector2 a = new Vector2(ax, ay), b = new Vector2(bx, by);
        Vector2 ab = b - a; float ab2 = Mathf.Max(Vector2.Dot(ab, ab), 1e-5f);
        for (int y = miny; y <= maxy; y++)
            for (int x = minx; x <= maxx; x++)
            {
                Vector2 pt = new Vector2(x, y);
                float t = Mathf.Clamp01(Vector2.Dot(pt - a, ab) / ab2);
                float dist = Vector2.Distance(pt, a + ab * t);
                float r = Mathf.Lerp(r0, r1, t);
                float cov = 1f - SStep(r, r + soft, dist);
                if (cov <= 0.001f) continue;
                Color c = ichor; c.a = cov * 0.62f * fade * (0.45f + 0.55f * (1f - t));
                int idx = y * W + x;
                px[idx] = MaxBlend(px[idx], c);
            }
    }

    private static Color MaxBlend(Color a, Color b)
    {
        float outA = Mathf.Max(a.a, b.a);
        return new Color(
            Mathf.Max(a.r * a.a, b.r * b.a) / Mathf.Max(outA, 1e-4f),
            Mathf.Max(a.g * a.a, b.g * b.a) / Mathf.Max(outA, 1e-4f),
            Mathf.Max(a.b * a.a, b.b * b.a) / Mathf.Max(outA, 1e-4f),
            outA);
    }

    // ── Coverage primitives ──
    private static void AddDisc(float[] cov, float[] hgt, int W, int H, float cx, float cy, float r, float soft)
    {
        soft = Mathf.Max(0.5f, soft);
        // Coverage is 0 beyond r and 1 inside (r-soft), so only the thin annulus
        // needs a sqrt. Tighten the bbox to r and early-out the interior/exterior.
        float rIn = r - soft; float rIn2 = rIn > 0f ? rIn * rIn : -1f;
        float r2 = r * r;
        int minx = Mathf.Max(0, (int)(cx - r)), maxx = Mathf.Min(W - 1, (int)(cx + r + 1f));
        int miny = Mathf.Max(0, (int)(cy - r)), maxy = Mathf.Min(H - 1, (int)(cy + r + 1f));
        for (int y = miny; y <= maxy; y++)
        {
            int row = y * W;
            float dy = y - cy; float dy2 = dy * dy;
            for (int x = minx; x <= maxx; x++)
            {
                float dx = x - cx;
                float dsq = dx * dx + dy2;
                if (dsq >= r2) continue;                       // outside → 0
                int idx = row + x;
                if (hgt != null)
                    BlendHeight(hgt, idx, Mathf.Sqrt(Mathf.Max(0f, 1f - dsq / r2)) * r * DOME);
                if (dsq <= rIn2) { if (cov[idx] < 1f) cov[idx] = 1f; continue; } // interior → 1, no sqrt
                float d = Mathf.Sqrt(dsq);
                float c = 1f - SStep(r - soft, r, d);
                if (c > cov[idx]) cov[idx] = c;
            }
        }
    }

    private static void AddCapsule(float[] cov, float[] hgt, int W, int H,
                                   float ax, float ay, float bx, float by,
                                   float r0, float r1, float soft)
    {
        soft = Mathf.Max(0.5f, soft);
        float maxR = Mathf.Max(r0, r1);
        int minx = Mathf.Max(0, (int)(Mathf.Min(ax, bx) - maxR));
        int maxx = Mathf.Min(W - 1, (int)(Mathf.Max(ax, bx) + maxR + 1f));
        int miny = Mathf.Max(0, (int)(Mathf.Min(ay, by) - maxR));
        int maxy = Mathf.Min(H - 1, (int)(Mathf.Max(ay, by) + maxR + 1f));

        float abx = bx - ax, aby = by - ay;
        float abLen2 = Mathf.Max(abx * abx + aby * aby, 1e-5f);

        for (int y = miny; y <= maxy; y++)
        {
            int row = y * W;
            float py = y - ay;
            for (int x = minx; x <= maxx; x++)
            {
                float px = x - ax;
                float t = (px * abx + py * aby) / abLen2;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                float dxp = px - abx * t, dyp = py - aby * t;
                float dsq = dxp * dxp + dyp * dyp;
                float r = r0 + (r1 - r0) * t;
                if (dsq >= r * r) continue;                    // outside → 0
                int idx = row + x;
                if (hgt != null)
                    BlendHeight(hgt, idx, Mathf.Sqrt(Mathf.Max(0f, 1f - dsq / (r * r))) * r * DOME);
                float rIn = r - soft;
                if (rIn > 0f && dsq <= rIn * rIn) { if (cov[idx] < 1f) cov[idx] = 1f; continue; }
                float d = Mathf.Sqrt(dsq);
                float c = 1f - SStep(r - soft, r, d);
                if (c > cov[idx]) cov[idx] = c;
            }
        }
    }

    // ── Convex polygon primitive ──
    // Everything in this creature used to be built from discs and capsules, which have
    // round caps — so every form ended up pillowy and every corner ended up soft. That
    // is most of why he read as a plush toy. This gives hard edges, flat planes and
    // actual points: shoulder plates, the wedge skull, and bladed spikes.
    //
    // For a CONVEX polygon the max of the per-edge signed distances is the true signed
    // distance inside the shape, and close enough just outside it for a soft edge.
    // Normals are flipped against the centroid so winding order doesn't matter.
    private static void AddPoly(float[] cov, float[] hgt, int W, int H,
                                float[] vx, float[] vy, int n, float soft, float domeScale)
    {
        if (n < 3) return;
        soft = Mathf.Max(0.5f, soft);

        float ccx = 0f, ccy = 0f;
        float minxf = 1e9f, maxxf = -1e9f, minyf = 1e9f, maxyf = -1e9f;
        for (int i = 0; i < n; i++)
        {
            ccx += vx[i]; ccy += vy[i];
            if (vx[i] < minxf) minxf = vx[i];
            if (vx[i] > maxxf) maxxf = vx[i];
            if (vy[i] < minyf) minyf = vy[i];
            if (vy[i] > maxyf) maxyf = vy[i];
        }
        ccx /= n; ccy /= n;

        // Outward unit normal and offset per edge.
        var enx = new float[n]; var eny = new float[n]; var eoff = new float[n];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float dx = vx[j] - vx[i], dy = vy[j] - vy[i];
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-5f) { enx[i] = 1f; eny[i] = 0f; eoff[i] = -1e9f; continue; }
            float nx = dy / len, ny = -dx / len;
            if (nx * (ccx - vx[i]) + ny * (ccy - vy[i]) > 0f) { nx = -nx; ny = -ny; }
            enx[i] = nx; eny[i] = ny; eoff[i] = nx * vx[i] + ny * vy[i];
        }

        int minx = Mathf.Max(0, (int)(minxf - soft - 1f)), maxx = Mathf.Min(W - 1, (int)(maxxf + soft + 1f));
        int miny = Mathf.Max(0, (int)(minyf - soft - 1f)), maxy = Mathf.Min(H - 1, (int)(maxyf + soft + 1f));

        for (int y = miny; y <= maxy; y++)
        {
            int row = y * W;
            for (int x = minx; x <= maxx; x++)
            {
                float d = -1e9f;
                for (int i = 0; i < n; i++)
                {
                    float de = enx[i] * x + eny[i] * y - eoff[i];
                    if (de > d) d = de;
                }
                if (d >= soft) continue;
                int idx = row + x;
                float c = 1f - SStep(-soft, soft, d);
                if (c > cov[idx]) cov[idx] = c;
                if (hgt != null && d < 0f)
                    BlendHeight(hgt, idx, Mathf.Sqrt(-d) * domeScale);
            }
        }
    }

    // ── Carving primitives (negative space) ──
    // Same falloff as the Add* pair, but they REMOVE coverage. `strength` is how much
    // of the coverage a fully-inside pixel loses (1 = punch a clean hole, 0.7 = thin it
    // out so the smoke still reads through the gap).
    private static void SubDisc(float[] cov, float[] hgt, int W, int H, float cx, float cy,
                                float r, float soft, float strength)
    {
        soft = Mathf.Max(0.5f, soft);
        float r2 = r * r;
        int minx = Mathf.Max(0, (int)(cx - r)), maxx = Mathf.Min(W - 1, (int)(cx + r + 1f));
        int miny = Mathf.Max(0, (int)(cy - r)), maxy = Mathf.Min(H - 1, (int)(cy + r + 1f));
        for (int y = miny; y <= maxy; y++)
        {
            int row = y * W;
            float dy = y - cy; float dy2 = dy * dy;
            for (int x = minx; x <= maxx; x++)
            {
                float dx = x - cx;
                float dsq = dx * dx + dy2;
                if (dsq >= r2) continue;
                int idx = row + x;
                if (cov[idx] <= 0f) continue;
                float d = Mathf.Sqrt(dsq);
                float bite = (1f - SStep(r - soft, r, d)) * strength;
                cov[idx] *= (1f - bite);
                if (hgt != null) hgt[idx] *= (1f - bite);
            }
        }
    }

    private static void SubCapsule(float[] cov, float[] hgt, int W, int H,
                                   float ax, float ay, float bx, float by,
                                   float r0, float r1, float soft, float strength)
    {
        soft = Mathf.Max(0.5f, soft);
        float maxR = Mathf.Max(r0, r1);
        int minx = Mathf.Max(0, (int)(Mathf.Min(ax, bx) - maxR));
        int maxx = Mathf.Min(W - 1, (int)(Mathf.Max(ax, bx) + maxR + 1f));
        int miny = Mathf.Max(0, (int)(Mathf.Min(ay, by) - maxR));
        int maxy = Mathf.Min(H - 1, (int)(Mathf.Max(ay, by) + maxR + 1f));

        float abx = bx - ax, aby = by - ay;
        float abLen2 = Mathf.Max(abx * abx + aby * aby, 1e-5f);

        for (int y = miny; y <= maxy; y++)
        {
            int row = y * W;
            float py = y - ay;
            for (int x = minx; x <= maxx; x++)
            {
                float px = x - ax;
                float t = (px * abx + py * aby) / abLen2;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                float dxp = px - abx * t, dyp = py - aby * t;
                float dsq = dxp * dxp + dyp * dyp;
                float r = r0 + (r1 - r0) * t;
                if (dsq >= r * r) continue;
                int idx = row + x;
                if (cov[idx] <= 0f) continue;
                float d = Mathf.Sqrt(dsq);
                float bite = (1f - SStep(r - soft, r, d)) * strength;
                cov[idx] *= (1f - bite);
                if (hgt != null) hgt[idx] *= (1f - bite);
            }
        }
    }

    // Polynomial smooth union. The empty-cell case is handled separately so the very
    // first primitive to touch a pixel doesn't get a fillet against the zero baseline,
    // which would puff up the whole outer silhouette.
    private static void BlendHeight(float[] hgt, int idx, float hh)
    {
        float a = hgt[idx];
        if (a <= 0f) { hgt[idx] = hh; return; }
        float t = Mathf.Clamp01(0.5f + 0.5f * (hh - a) / SMAX_K);
        hgt[idx] = Mathf.Lerp(a, hh, t) + SMAX_K * t * (1f - t);
    }

    // Presses a smooth elliptical dent into the height field without touching coverage.
    private static void DentHeight(float[] hgt, int W, int H, float cx, float cy,
                                   float rx, float ry, float depth)
    {
        int minx = Mathf.Max(0, (int)(cx - rx)), maxx = Mathf.Min(W - 1, (int)(cx + rx + 1f));
        int miny = Mathf.Max(0, (int)(cy - ry)), maxy = Mathf.Min(H - 1, (int)(cy + ry + 1f));
        for (int y = miny; y <= maxy; y++)
        {
            int row = y * W;
            float v = (y - cy) / Mathf.Max(ry, 1f);
            float v2 = v * v;
            for (int x = minx; x <= maxx; x++)
            {
                float u = (x - cx) / Mathf.Max(rx, 1f);
                float d2 = u * u + v2;
                if (d2 >= 1f) continue;
                float f = 1f - d2;
                int idx = row + x;
                hgt[idx] -= depth * f * f;
                if (hgt[idx] < 0f) hgt[idx] = 0f;
            }
        }
    }

    // Separable box blur with a running sum — O(1) per pixel regardless of radius.
    private static void BoxBlur(float[] src, float[] dst, float[] tmp, int W, int H, int R)
    {
        float inv = 1f / (R * 2 + 1);
        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            float sum = 0f;
            for (int i = -R; i <= R; i++) sum += src[row + Mathf.Clamp(i, 0, W - 1)];
            for (int x = 0; x < W; x++)
            {
                tmp[row + x] = sum * inv;
                sum -= src[row + Mathf.Clamp(x - R, 0, W - 1)];
                sum += src[row + Mathf.Clamp(x + R + 1, 0, W - 1)];
            }
        }
        for (int x = 0; x < W; x++)
        {
            float sum = 0f;
            for (int i = -R; i <= R; i++) sum += tmp[Mathf.Clamp(i, 0, H - 1) * W + x];
            for (int y = 0; y < H; y++)
            {
                dst[y * W + x] = sum * inv;
                sum -= tmp[Mathf.Clamp(y - R, 0, H - 1) * W + x];
                sum += tmp[Mathf.Clamp(y + R + 1, 0, H - 1) * W + x];
            }
        }
    }

    // Squared half-sine: the limb snaps up, hangs, then plants. A plain sine spends
    // half the cycle airborne and reads as wading rather than walking.
    private static float StepLift(float phase)
    {
        float v = Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f));
        return v * v;
    }

    private static float SStep(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0 + 1e-6f));
        return t * t * (3f - 2f * t);
    }

    //  Precomputed noise fields (PERF) 
    // The compose pass used to call Fractal() twice per pixel per frame (~72M hash
    // ops for the whole 16-frame sheet — the bulk of the multi-second first-spawn
    // freeze). Instead we bake two fractal fields at texture resolution ONCE and, per
    // frame, sample them at a scrolled offset with wrap. Sampling is a couple of
    // array reads + lerps, so the compose pass becomes memory-bound and cheap.
    // ── Surface shading constants ──
    // DOME  : how tall each primitive's dome is relative to its radius. Higher = more
    //         bulbous; too high and neighbouring blobs read as separate balloons.
    // NZ    : flattening of the reconstructed normal. This is the main "how much
    //         3D" knob — lower values exaggerate the form, higher values flatten it.
    // L*/HV*: key light direction and its half-vector against a head-on viewer.
    private const float DOME = 0.42f;
    // Smooth-union radius for the height field, in pixels. Plain max() leaves a hard
    // crease everywhere two primitives meet, so the body reads as a bunch of separate
    // spheres — each dome catching its own highlight like a grape. Blending the union
    // fuses them into one continuous surface with fillets in the joints.
    private const float SMAX_K = 15f;
    private const float NZ = 4.2f;
    private const float LX = -0.4243f, LY = 0.7276f, LZ = 0.5395f;   // normalised
    private const float HVX = -0.2554f, HVY = 0.4380f, HVZ = 0.8618f; // normalize(L + view)

    // ── Occlusion / shadowing ──
    // AO_*   : cavity darkening, from comparing height against a blurred height.
    // FINE_* : micro-surface bump strength (grain catching the specular).
    // VEIN_* : how deeply the vein network is pressed INTO the surface.
    // SH_*   : height-field self-shadowing. L2 is the light direction projected to 2D;
    //          SH_RISE is how fast the shadow ray climbs per pixel travelled.
    private const float AO_K = 5.5f;
    private const float AO_STRENGTH = 0.80f;
    private const float FINE_BUMP = 2.2f;
    private const float VEIN_BUMP = 10f;
    private const int SH_STEPS = 10;
    private const float SH_STEP = 2.6f;
    private const float SH_RISE = 0.64f;
    private const float SH_SOFT = 6f;
    private const float SH_STRENGTH = 0.70f;
    private const float L2X = -0.5039f, L2Y = 0.8641f;   // normalize(LX, LY)

    // Which idle frames glitch. Mostly clean, with two hard hits per cycle — the
    // contrast is the whole effect. A little corruption on every frame just looks like a
    // low-quality sprite; nothing then a violent tear looks like the creature breaking up.
    private static readonly float[] IDLE_CORRUPT =
        { 0f, 0f, 0f, 0.62f, 0.30f, 0f, 0f, 0f, 0f, 0.75f, 0.36f, 0f };

    private const int NOISE_SEED = 1000;
    private static float[] _nf1;     // Fractal(x*0.045, y*0.045, seed, 4)
    private static float[] _nf2;     // Fractal(x*0.11 + 13, y*0.11, seed+7, 3)
    private static float[] _nfVein;  // RIDGED noise — branching filament network
    private static float[] _nfBlotch;// very low frequency — varies vein intensity
    private static float[] _nfFine;  // high frequency — fine surface grain

    private static void BuildNoiseFields()
    {
        if (_nf1 != null) return;
        int W = TEX_W, H = TEX_H;
        var f1 = new float[W * H];
        var f2 = new float[W * H];
        var fv = new float[W * H];
        var fb = new float[W * H];
        var ff = new float[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int idx = y * W + x;
                f1[idx] = Fractal(x * 0.045f, y * 0.045f, NOISE_SEED, 4);
                f2[idx] = Fractal(x * 0.11f + 13f, y * 0.11f, NOISE_SEED + 7, 3);
                fv[idx] = Ridged(x * 0.026f, y * 0.026f, NOISE_SEED + 23, 4);
                fb[idx] = Fractal(x * 0.013f + 71f, y * 0.013f, NOISE_SEED + 31, 2);
                ff[idx] = Fractal(x * 0.15f, y * 0.15f + 29f, NOISE_SEED + 47, 3);
            }
        _nf1 = f1;
        _nf2 = f2;
        _nfVein = fv;
        _nfBlotch = fb;
        _nfFine = ff;
    }

    // Bilinear sample with integer wrap — lets us scroll the field arbitrarily.
    private static float SampleWrap(float[] field, float fx, float fy)
    {
        int W = TEX_W, H = TEX_H;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        int xa = ((x0 % W) + W) % W, ya = ((y0 % H) + H) % H;
        int xb = (xa + 1) % W, yb = (ya + 1) % H;
        float a = field[ya * W + xa], b = field[ya * W + xb];
        float c = field[yb * W + xa], d = field[yb * W + xb];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    // Ridged fractal: folding each octave about its midpoint turns smooth blobs into
    // sharp CRESTS, and summing octaves makes those crests branch and terminate. That
    // non-periodic, branching structure is the whole point — a sine or triangle wave
    // banding the body always reads as fake no matter how much noise is added on top,
    // because the eye picks up the constant spacing immediately.
    private static float Ridged(float x, float y, int seed, int octaves)
    {
        float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            float n = ValueNoise(x * freq, y * freq, seed + i * 197);
            float r = 1f - Mathf.Abs(n * 2f - 1f);
            r *= r;
            sum += r * amp;
            norm += amp;
            amp *= 0.55f; freq *= 2.1f;
        }
        return Mathf.Clamp01(sum / Mathf.Max(norm, 1e-4f));
    }

    // ── Value-noise fractal (deterministic) — used ONLY to bake the fields above. ──
    private static float Fractal(float x, float y, int seed, int octaves)
    {
        float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, seed + i * 131);
            norm += amp;
            amp *= 0.5f; freq *= 2f;
        }
        return sum / Mathf.Max(norm, 1e-4f);
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float u = fx * fx * (3f - 2f * fx);
        float v = fy * fy * (3f - 2f * fy);
        float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
        float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    // ── Shared soft sprites for FX ──
    private static Sprite _softDot, _streak, _threadSprite;
    private static Sprite[] _shardSprites;

    // ── Slice sprites ──
    // Sub-rect Sprites over the SAME Texture2D as the full frame, so a slice costs one
    // small managed object and zero extra pixels. Built lazily: only the (frame, band)
    // pairs actually torn during play ever get created. Pivoted at the band's own
    // centre, so TickSlices just places it at that band's height and shoves it sideways.
    private const int SLICE_BANDS = 8;
    private static Sprite[,] _sliceSprites;

    private static Sprite GetSliceSprite(int frame, int band)
    {
        if (_cachedFrames == null) return null;
        if (frame < 0 || frame >= _cachedFrames.Length) return null;
        if (band < 0 || band >= SLICE_BANDS) return null;

        if (_sliceSprites == null) _sliceSprites = new Sprite[_cachedFrames.Length, SLICE_BANDS];
        Sprite s = _sliceSprites[frame, band];
        if (s != null) return s;

        Sprite src = _cachedFrames[frame];
        if (src == null || src.texture == null) return null;

        int bh = TEX_H / SLICE_BANDS;
        int y0 = band * bh;
        float ppu = TEX_H / SPRITE_WORLD_HEIGHT;

        s = Sprite.Create(src.texture, new Rect(0, y0, TEX_W, bh),
                          new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
        _sliceSprites[frame, band] = s;
        return s;
    }

    private static Sprite GetShardSprite(int i)
    {
        if (_shardSprites == null)
        {
            _shardSprites = new Sprite[3];
            for (int v = 0; v < 3; v++) _shardSprites[v] = MakeBlobSprite(v);
        }
        return _shardSprites[((i % 3) + 3) % 3];
    }

    // A small, irregular, soft-edged shadow blob (tinted at runtime).
    private static Sprite MakeBlobSprite(int variant)
    {
        const int S = 40;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        float cx = S * 0.5f, cy = S * 0.5f;
        int lobes = 2 + variant;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float rEdge = (S * 0.42f) * (0.72f + 0.22f * Mathf.Sin(ang * lobes + variant)
                                                     + 0.10f * Mathf.Sin(ang * 5f + variant * 2f));
                float a = Mathf.Clamp01(1f - d / Mathf.Max(rEdge, 1f));
                a *= a;
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, 40f);
    }

    private static Sprite GetSoftDot()
    {
        if (_softDot != null) return _softDot;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float v = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), c) / (S * 0.5f));
                px[y * S + x] = new Color(1f, 1f, 1f, v * v);
            }
        tex.SetPixels(px); tex.Apply(false);
        _softDot = Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * 0.5f, 32f);
        return _softDot;
    }

    private static Sprite GetStreak()
    {
        if (_streak != null) return _streak;
        const int SW = 48, SH = 16;
        var tex = new Texture2D(SW, SH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[SW * SH];
        for (int y = 0; y < SH; y++)
            for (int x = 0; x < SW; x++)
            {
                float ny = (y - SH * 0.5f) / (SH * 0.5f);
                float nx = x / (float)SW;                 // 0 at tail → 1 at head
                float body = Mathf.Clamp01(1f - Mathf.Abs(ny));
                float taper = Mathf.Clamp01(nx);          // thin at the tail
                float a = body * body * taper;
                px[y * SW + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply(false);
        _streak = Sprite.Create(tex, new Rect(0, 0, SW, SH), new Vector2(0f, 0.5f), 32f);
        return _streak;
    }

    // A thin, tapering filament that fades to a point at the free (bottom) end. The
    // pivot sits near the TOP so a thread swings from its anchor like a pendulum.
    private static Sprite GetThreadSprite()
    {
        if (_threadSprite != null) return _threadSprite;
        const int TW = 12, TH = 96;
        var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[TW * TH];
        float cx = (TW - 1) * 0.5f;
        for (int y = 0; y < TH; y++)
        {
            float ty = y / (float)(TH - 1);              // 0 = tip (bottom), 1 = anchor (top)
            float halfW = (0.18f + 0.82f * ty) * cx;     // taper to a point at the tip
            // Fade in at the very tip and hold along the length.
            float lenA = Mathf.Clamp01((ty - 0.02f) / 0.12f);
            for (int x = 0; x < TW; x++)
            {
                float nx = (x - cx) / Mathf.Max(halfW, 0.5f);
                float across = 1f - Mathf.Clamp01(Mathf.Abs(nx));
                float a = across * across * lenA;
                px[y * TW + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px); tex.Apply(false);
        _threadSprite = Sprite.Create(tex, new Rect(0, 0, TW, TH), new Vector2(0.5f, 0.9f), 96f);
        return _threadSprite;
    }
}


