using System.Collections.Generic;
using UnityEngine;

//  BufferVisual — fully procedural body for the Buffer enemy.
//  • The root SpriteRenderer is kept alive but disabled. EnemyStats writes the
//    damage-flash and freeze tint into spriteRenderer.color, so we read that
//    colour every frame and multiply it through the whole rig. Hit flashes and
//    Ice Armor cyan therefore work with zero changes to EnemyStats.
//  • Sorting order is inherited from that same SpriteRenderer each frame, so
//    whatever YSortEntity does to the Buffer, this rig follows it.
//  • On death the rig detaches from the enemy and hands itself to
//    BufferDeathCollapse, so the collapse plays out even when EnemyStats
//    destroys the GameObject on the same frame.
//  • Requires the small BufferController additions (FogCharge01,
//    OnFogSpawned, fogEmitPointProvider, CurrentTarget, FogRadius).

[DisallowMultipleComponent]
public class BufferVisual : MonoBehaviour
{
    //  Palette 
    // Channel note: the shared material is Sprites/Default, whose fragment
    // stage premultiplies (c.rgb *= c.a). That means a colour with rgb ABOVE
    // 1.0 blows out past the background instead of clipping — which is how the
    // "glow" colours below fake additive blending without a second shader. If
    // you have bloom on, these are the values it will pick up.

    [Header("Palette — Robe")]
    [Tooltip("Outer silhouette colour. Keep this the darkest value in the palette; " +
             "it is what separates the Buffer from the ground at a glance.")]
    [SerializeField] private Color robeEdge = new Color(0.09f, 0.03f, 0.17f, 1f);

    [Tooltip("Main cloth body colour.")]
    [SerializeField] private Color robeMid = new Color(0.21f, 0.08f, 0.36f, 1f);

    [Tooltip("Lit side of the cloth. Sits on the facing side of the spine, so " +
             "the rim light flips automatically when he turns around.")]
    [SerializeField] private Color robeLit = new Color(0.42f, 0.21f, 0.66f, 1f);

    [Header("Palette — Light")]
    [Tooltip("Interior of the hood. Near-black, fully opaque — this is a hole, not a shadow.")]
    [SerializeField] private Color voidColor = new Color(0.02f, 0.01f, 0.05f, 1f);

    [Tooltip("Eye glow. Values >1 are intentional (see palette note).")]
    [SerializeField] private Color eyeColor = new Color(1.35f, 0.70f, 1.60f, 1f);

    [Tooltip("Shared glow for shards, chest sigil and the censer core.")]
    [SerializeField] private Color glowColor = new Color(0.95f, 0.52f, 1.45f, 1f);

    [Tooltip("Colour of the tether along its length. Deep violet, so the arc " +
             "sits in the same family as the robe and the fog rather than the " +
             "cold cyan it used to be — that cyan read as a separate effect " +
             "that happened to be attached to him.")]
    [SerializeField] private Color arcColor = new Color(0.46f, 0.18f, 0.82f, 1f);

    [Tooltip("Colour of the pulse that travels along the tether. Pale lilac — " +
             "the only bright value in the effect, and only ever bright at one " +
             "point at a time.")]
    [SerializeField] private Color arcPulseColor = new Color(0.88f, 0.64f, 1.30f, 1f);

    [Tooltip("Hem mist and censer smoke.")]
    [SerializeField] private Color mistColor = new Color(0.44f, 0.17f, 0.70f, 1f);

    [Tooltip("Contact shadow. Alpha is the real control here.")]
    [SerializeField] private Color shadowColor = new Color(0.02f, 0.00f, 0.05f, 0.42f);

    // ── Rig ───────────────────────────────────────────────────────────────

    [Header("Rig")]
    [Tooltip("Uniform multiplier applied to EVERY world-space dimension below " +
             "(body, hover, censer, shards, mist, tether width) exactly once, in " +
             "Awake. Added as a separate knob rather than by editing the defaults " +
             "because Unity serialises the old values into the prefab — bumping a " +
             "default would not have changed an already-placed Buffer.\n\n" +
             "Deliberately does NOT scale Ground Offset Y (that depends on the " +
             "prefab's pivot, not on how big he is), speeds, or durations.")]
    [Range(0.25f, 10f)][SerializeField] private float rigScale = 3f;

    [Tooltip("Local Y of the ground plane relative to this transform. If your " +
             "Buffer prefab has a centred pivot on a 1-unit sprite this is -0.5. " +
             "If the pivot is already at the feet, set it to 0. Everything else " +
             "in the rig is measured up from here, so this is the first thing to " +
             "fix if he floats too high or sinks into the floor.")]
    [SerializeField] private float groundOffsetY = -0.5f;

    [Tooltip("Height of the robe from hem to crown, in world units.")]
    [SerializeField] private float bodyHeight = 1.05f;

    [Tooltip("Full width of the robe at its widest (the hem), in world units.")]
    [SerializeField] private float bodyWidth = 0.64f;

    [Tooltip("How far off the ground he floats at rest.")]
    [SerializeField] private float hoverHeight = 0.16f;

    [Tooltip("Vertical bob distance added to hoverHeight.")]
    [SerializeField] private float hoverAmplitude = 0.052f;

    [SerializeField] private float hoverSpeed = 1.7f;

    [Tooltip("Sideways drift of the cloth. Anchored at the shoulders and " +
             "strongest at the hem, so the body sways like something hanging.")]
    [SerializeField] private float swayAmplitude = 0.036f;

    [SerializeField] private float swaySpeed = 1.25f;

    [Tooltip("Vertical ripple travelling around the hem. This is what sells " +
             "'cloth' rather than 'cone'.")]
    [SerializeField] private float hemWaveAmplitude = 0.032f;

    [SerializeField] private float hemWaveSpeed = 2.6f;

    [Tooltip("How far the crown of the hood curls forward. 0 = a plain cone.")]
    [SerializeField] private float hoodCurl = 0.10f;

    [Tooltip("How much the upper body trails behind when moving. Cloth lag — " +
             "the hem leads, the hood follows.")]
    [SerializeField] private float moveLean = 0.09f;

    [Tooltip("Seconds-ish for a full turn-around. Higher = snappier.")]
    [SerializeField] private float facingLerpSpeed = 9f;

    [Tooltip("Rows in the robe mesh. 16 is smooth; below 10 the hem ripple " +
             "starts to look faceted.")]
    [Range(6, 40)][SerializeField] private int robeRows = 16;

    // ── Subsystem toggles ─────────────────────────────────────────────────

    [Header("Subsystems")]
    [SerializeField] private bool enableShadow = true;
    [SerializeField] private bool enableRobe = true;

    [SerializeField] private bool enableFace = true;
    [SerializeField] private bool enableSigil = true;
    [SerializeField] private bool enableCenser = true;
    [SerializeField] private bool enableCenserSmoke = true;
    [SerializeField] private bool enableShards = true;
    [SerializeField] private bool enableHemMist = true;
    [SerializeField] private bool enableBuffTethers = true;
    [SerializeField] private bool enableDropPulse = true;
    [SerializeField] private bool enableDeathCollapse = true;

    // ── Face ──────────────────────────────────────────────────────────────

    [Header("Face")]
    [Tooltip("Radius of each eye glow at rest.")]
    [SerializeField] private float eyeRadius = 0.048f;

    [Tooltip("Horizontal gap between the two eyes.")]
    [SerializeField] private float eyeSeparation = 0.103f;

    [Tooltip("Average seconds between blinks. Blinks are a squash of the eye " +
             "glow, not an eyelid — there is no face to put one on.")]
    [SerializeField] private float blinkInterval = 3.2f;

    // ── Chest sigil ───────────────────────────────────────────────────────

    [Header("Chest Sigil")]
    [Tooltip("Radius of the rotating ring on the chest.")]
    [SerializeField] private float sigilRadius = 0.088f;

    [Tooltip("Degrees/sec at rest. Scales up with fog charge.")]
    [SerializeField] private float sigilSpinSpeed = 40f;

    // ── Censer ────────────────────────────────────────────────────────────

    [Header("Censer")]
    [Tooltip("Chain length as a FRACTION OF BODY HEIGHT.\n\n" +
             "A fraction, and a brand-new field, on purpose. The old absolute " +
             "value hung the thurible at 18% of body height — where the robe is " +
             "close to its widest — so it dangled INSIDE the silhouette with " +
             "nothing to read against. At 0.27 it sits about a third of the way " +
             "up, where the robe has narrowed enough for the swing to cross the " +
             "edge and actually be seen.")]
    [Range(0.08f, 0.6f)][SerializeField] private float censerChainLengthFraction = 0.27f;

    [Tooltip("Resting angle of the chain in degrees away from straight down. He " +
             "holds the censer out in front of himself rather than letting it " +
             "hang against his leg; this is the other half of getting it clear " +
             "of the robe.")]
    [Range(0f, 60f)][SerializeField] private float censerRestAngle = 20f;

    [Tooltip("Amplitude of the constant idle swing, in degrees.\n\n" +
             "AUTHORED motion layered on top of the pendulum, not a force fed " +
             "into it. Driving the sim off-resonance the way the previous version " +
             "did produced about 2 degrees of travel — technically a swing, " +
             "visually nothing. The physics still owns everything reactive " +
             "(walking, knockback, the kick on each fog drop); this only " +
             "guarantees he is never completely still.")]
    [Range(0f, 45f)][SerializeField] private float censerIdleSwingAngle = 16f;

    [SerializeField] private float censerIdleSwingSpeed = 1.9f;

    [Tooltip("Points in the chain LineRenderer. More = smoother sag.")]
    [Range(2, 16)][SerializeField] private int censerChainPoints = 7;

    [Tooltip("Chain width as a fraction of body width.")]
    [SerializeField] private float censerChainWidthFraction = 0.020f;

    [Tooltip("Thurible radius as a fraction of body width.")]
    [SerializeField] private float censerRadiusFraction = 0.115f;

    [Tooltip("Pendulum damping. Lower = swings for longer after he stops.")]
    [SerializeField] private float censerDamping = 0.9f;

    [Tooltip("Smoke puffs alive at once above the censer.")]
    [Range(0, 32)][SerializeField] private int censerSmokeCount = 10;

    [SerializeField] private float censerSmokeRate = 5f;
    [SerializeField] private float censerSmokeLifetime = 1.1f;
    [SerializeField] private float censerSmokeRise = 0.55f;

    // ── Rune shards ───────────────────────────────────────────────────────

    [Header("Rune Shards")]
    [Range(0, 12)][SerializeField] private int shardCount = 3;
    [SerializeField] private float shardSize = 0.072f;

    [Tooltip("Horizontal radius of the orbit ellipse.")]
    [SerializeField] private float shardOrbitX = 0.40f;

    [Tooltip("Vertical radius. Much smaller than X — that squash is what makes " +
             "a flat 2D orbit read as a ring going around him in 3D.")]
    [SerializeField] private float shardOrbitY = 0.115f;

    [SerializeField] private float shardOrbitSpeed = 1.15f;

    [Tooltip("Fraction of the orbit radius the shards pull in by at full charge.")]
    [Range(0f, 0.9f)][SerializeField] private float shardChargePull = 0.42f;

    //  Hem mist 

    [Header("Hem Mist")]
    [Range(0, 12)][SerializeField] private int hemMistCount = 4;
    [SerializeField] private float hemMistSize = 0.28f;

    //  Buff tethers 

    [Header("Buff Tethers")]
    [Tooltip("How far a buffed ally can be and still show a tether. Should be " +
             "comfortably larger than the fog radius, because allies keep the " +
             "buff while they stand in a cloud he dropped and then walked away from.")]
    [SerializeField] private float tetherRange = 6.5f;

    [Range(1, 16)][SerializeField] private int maxTethers = 6;

    [Tooltip("Seconds between re-scans for buffed allies. This is a physics " +
             "query, so don't run it every frame.")]
    [SerializeField] private float tetherRefreshInterval = 0.18f;

    [Tooltip("Points per tether. Floored at 16 in code — the arc is a smooth " +
             "curve now rather than a jagged one, and a curve needs resolution " +
             "or it shows its corners.")]
    [Range(3, 40)][SerializeField] private int tetherSegments = 18;

    [Tooltip("Line width as a fraction of body width.\n\n" +
             "A fraction, and a new field, because the old absolute 0.035 became " +
             "0.105 world units at 3x scale — a rope, not a filament.")]
    [SerializeField] private float tetherWidthFraction = 0.011f;

    [Tooltip("How far the arc bows sideways, as a fraction of its own length. " +
             "Scaling with length stops a short tether looking like a tangle " +
             "and a long one looking dead straight.")]
    [Range(0f, 0.3f)][SerializeField] private float tetherWaveAmplitude = 0.075f;

    [Tooltip("Speed the wave travels along the arc, from the Buffer to the ally.")]
    [SerializeField] private float tetherWaveSpeed = 1.1f;

    [Tooltip("Seconds for a tether to fade in when an ally gains the buff and " +
             "out when they lose it. Without this they popped on and off at the " +
             "rescan rate, which was most of the flicker.")]
    [SerializeField] private float tetherFadeTime = 0.35f;

    [Tooltip("How fast the bright pulse travels from the Buffer to the ally. " +
             "The direction of travel is the whole point — it reads as him " +
             "feeding them power rather than them feeding him.")]
    [SerializeField] private float tetherFlowSpeed = 0.75f;

    [Tooltip("World-space Y offset on the ally end, so the arc lands on their " +
             "body rather than their feet.")]
    [SerializeField] private float tetherTargetLift = 0.35f;

    // ── Drop pulse ────────────────────────────────────────────────────────

    [Header("Drop Pulse")]
    [Tooltip("Seconds for the ring to expand to the fog radius.")]
    [SerializeField] private float pulseDuration = 0.55f;

    [Tooltip("Used when BufferController does not report a fog radius.")]
    [SerializeField] private float pulseFallbackRadius = 2.5f;

    // ── Death ─────────────────────────────────────────────────────────────

    [Header("Death")]
    [Tooltip("Seconds for the collapse. The rig detaches from the enemy first, " +
             "so this is independent of when EnemyStats destroys the GameObject.")]
    [SerializeField] private float deathCollapseDuration = 0.85f;

    [Tooltip("Force EnemyStats' sprite-shatter death VFX off.\n\n" +
             "MUST stay ON for a procedural Buffer. EnemyDeathVFX builds its " +
             "debris out of the enemy's OWN sprite, on renderers it creates " +
             "itself — so disabling our SpriteRenderer does not hide it, and the " +
             "placeholder frame pops back onto the screen for the length of the " +
             "effect. BufferVisual owns the death visual now, so it calls " +
             "ConfigureDeathVfx(0) in Start (after every Awake, which is where " +
             "BufferController sets it to 0.7) and takes the field back.")]
    [SerializeField] private bool suppressSpriteDeathVfx = true;

    // ── Rendering ─────────────────────────────────────────────────────────

    [Header("Collider")]
    [Tooltip("How the enemy's Collider2D is resized to the procedural body.\n\n" +
             "Leave         — don't touch it. Safest; the hitbox stays whatever the\n" +
             "                prefab had, which may be much smaller than he looks.\n" +
             "CenteredOnPivot — size it to the body but keep it CENTERED ON THE\n" +
             "                TRANSFORM. Default, and the only option that keeps\n" +
             "                navigation coherent.\n" +
             "MatchVisual   — align it with the drawn body. Looks right, but on a\n" +
             "                tall rig this pushes the collider well off the\n" +
             "                transform origin. READ THE WARNING BELOW.\n\n" +
             "WHY THIS MATTERS: every steering, stuck-detection and targeting\n" +
             "routine in this project uses transform.position as 'where I am'.\n" +
             "MatchVisual on a 3x Buffer puts the collider at Y +1.56 with the\n" +
             "transform at 0 — the physics body ends up entirely outside itself,\n" +
             "so the AI steers from a point 1.5 units away from what is actually\n" +
             "touching the wall.")]
    [SerializeField] private ColliderFitMode colliderFit = ColliderFitMode.CenteredOnPivot;

    public enum ColliderFitMode { Leave = 0, CenteredOnPivot = 1, MatchVisual = 2 }

    [Tooltip("Collider width as a fraction of body width. Below 1 so the hitbox " +
             "tracks the solid torso rather than the widest flare of the hem.")]
    [Range(0.2f, 1.4f)][SerializeField] private float colliderWidthFraction = 0.74f;

    [Tooltip("Collider height as a fraction of body height. Below 1 to skip the " +
             "wispy dissolving hem and the crown tip.")]
    [Range(0.2f, 1.4f)][SerializeField] private float colliderHeightFraction = 0.88f;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";

    [Tooltip("Used only when Follow Sprite Sorting Order is off or there is no " +
             "SpriteRenderer to follow. Characters live around 3000 in this " +
             "project (fog is 2000, grass 1000-1600).")]
    [SerializeField] private int sortingOrderBase = 3000;

    [Tooltip("Inherit sorting order from the root SpriteRenderer every frame. " +
             "Leave ON — this is what keeps the rig in sync with YSortEntity.")]
    [SerializeField] private bool followSpriteSortingOrder = true;

    [Tooltip("Push the whole rig this far toward the camera. Belt and braces " +
             "against anything that sorts by Z; sortingOrder does the real work.")]
    [SerializeField] private float zOffset = -0.02f;

    [Tooltip("Disable the placeholder SpriteRenderer on Awake. The component " +
             "itself is deliberately KEPT — EnemyStats writes hit-flash and " +
             "freeze tints into its .color, which we read back every frame.")]
    [SerializeField] private bool hideSourceSpriteRenderer = true;

    // ══════════════════════════════════════════════════════════════════════
    //  Runtime state
    // ══════════════════════════════════════════════════════════════════════

    private EnemyStats stats;
    private BufferController buffer;
    private Rigidbody2D rb;
    private SpriteRenderer sourceSprite;

    private Transform root;   // detachable; holds everything, never flips
    private Transform pivot;  // holds the body; flips on X and bobs on Y

    private Material mat;
    private MaterialPropertyBlock mpb;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly List<Renderer> sortedRenderers = new List<Renderer>();
    private readonly List<int> sortOffsets = new List<int>();
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private int lastAppliedSortBase = int.MinValue;

    // Whatever the rig should currently sort against. Follows the placeholder
    // SpriteRenderer (and therefore YSortEntity) unless told otherwise.
    private int SortBase =>
        (followSpriteSortingOrder && sourceSprite != null) ? sourceSprite.sortingOrder : sortingOrderBase;

    private float animTime;
    private float facing = 1f;
    private float charge01;
    private float dropFlash;    // 1 on drop, decays
    private float shardKick;    // 1 on drop, decays
    private bool dying;
    private bool rigDetached;

    // Robe
    private Mesh robeMesh;
    private Vector3[] robeVerts;
    private Color[] robeCols;
    private Part robePart;
    private const int RobeColumns = 5;

    // Face
    private Part hoodVoid;
    private Part eyeL, eyeR;
    private float blinkTimer;
    private float blinkPhase = -1f; // <0 = not blinking

    // Sigil
    private Part sigil;
    private float sigilAngle;

    // Censer
    private Transform censerAnchor;
    private Transform censerBody;
    private LineRenderer censerChain;
    private Part censerShell, censerCore, censerRim;
    private float censerAngle, censerAngVel;
    private float lastVelX;

    private struct SmokePuff
    {
        public Part part;
        public Vector2 pos, vel;
        public float life, maxLife, size, spin;
    }
    private SmokePuff[] smoke;
    private float smokeSpawnAccum;

    // Shards
    private struct Shard
    {
        public Transform tr;
        public MeshRenderer mr;
        public float phase, spin, spinPhase, sizeScale;
    }
    private Shard[] shards;

    // Hem mist
    private Part[] hemMist;
    private float[] hemMistPhase;

    // Tethers
    private class Tether
    {
        public GameObject go;
        public LineRenderer lr;
        public Transform target;
        public float phase;
        public float phaseA, phaseB;   // detuned wave phases, so no two arcs match
        public float bow;              // small constant lateral offset
        public float visible;          // 0-1 fade, so tethers never pop on or off
        public Vector3 lastEnd;        // held so a lost target can fade out in place
        public Gradient gradient;
        public GradientColorKey[] colorKeys;
        public GradientAlphaKey[] alphaKeys;
    }
    private Tether[] tethers;
    private float tetherRefreshTimer;
    private readonly List<Transform> _tetherScratch = new List<Transform>();
    private static readonly Collider2D[] TetherScanBuffer = new Collider2D[64];
    private static ContactFilter2D tetherScanFilter = new ContactFilter2D().NoFilter();

    // Drop pulse
    private struct Pulse
    {
        public Part part;
        public bool active;
        public float t;
        public float maxRadius;
    }
    private Pulse[] pulses;

    // A single renderable piece of the rig. `color` is the authored tint; the
    // per-frame product of colour x global tint x alpha goes into the property
    // block, so the mesh vertex colours only ever carry the SHAPE gradient.
    private class Part
    {
        public Transform tr;
        public MeshRenderer mr;
        public Color color;
        public float alpha = 1f;
    }


    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        buffer = GetComponent<BufferController>();
        rb = GetComponent<Rigidbody2D>();

        sourceSprite = GetComponent<SpriteRenderer>();
        if (sourceSprite == null) sourceSprite = GetComponentInChildren<SpriteRenderer>();

        // Keep the component (it is our tint channel) but stop it drawing the
        // placeholder art on top of the rig.
        if (hideSourceSpriteRenderer && sourceSprite != null)
            sourceSprite.enabled = false;

        // If a frame animator is present it will keep writing sprites and, more
        // importantly, keep driving transform orientation — which would fight
        // our own facing. The renderer is off so the sprites are invisible; we
        // only need to take the orientation away from it.
        var animController = GetComponent<EnemyAnimationController>();
        if (animController != null) animController.SetOrientationDrivingEnabled(false);

        mpb = new MaterialPropertyBlock();

        // Must run before anything reads a dimension.
        ApplyRigScale();
        FitCollider();

        // Seeded before BuildRig so the very first robe pose is already
        // desynced — a wave of Buffers must not breathe in lockstep.
        animTime = Random.Range(0f, 100f);

        BuildMaterial();
        BuildRig();

        if (buffer != null)
        {
            // Fog spawns from the thurible rather than from the Buffer's origin.
            // Gameplay-neutral (the offset is a fraction of the fog radius) but
            // it makes cause and effect legible.
            buffer.fogEmitPointProvider = GetCenserWorldPosition;
            buffer.OnFogSpawned += HandleFogSpawned;
        }

        blinkTimer = Random.Range(0.5f, blinkInterval);

        // DEATH DETECTION. Polling IsDead() from Update is not enough and this
        // is why the collapse never played: Destroy() is deferred to the end of
        // the frame, so if our Update had already run this frame when the
        // killing blow landed, it simply never runs again and the rig is
        // destroyed as a child of the enemy without ever noticing he died.
        //
        // CharacterStats.TakeDamage raises OnHealthChanged synchronously, one
        // line BEFORE it calls Die(), so this hook always fires while the
        // GameObject is still whole. The Update/LateUpdate polls below stay as
        // a backstop for the paths that call Die() directly without damage.
        if (stats != null) stats.OnHealthChanged += HandleHealthChanged;
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (!dying && current <= 0f) BeginDeath();
    }

    private void Start()
    {
        // Start, not Awake: BufferController.Awake calls ConfigureDeathVfx with
        // its own serialized duration, and whichever Awake ran last would win.
        // Start runs after every Awake, so this is the one that sticks.
        if (suppressSpriteDeathVfx && stats != null)
            stats.ConfigureDeathVfx(0f, destroyHealthBarBeforeVfx: true);
    }

    private void OnDestroy()
    {
        if (stats != null) stats.OnHealthChanged -= HandleHealthChanged;

        if (buffer != null)
        {
            buffer.OnFogSpawned -= HandleFogSpawned;
            if (buffer.fogEmitPointProvider == (System.Func<Vector3>)GetCenserWorldPosition)
                buffer.fogEmitPointProvider = null;
        }

        // If the rig was handed to BufferDeathCollapse it now owns the material
        // and the meshes, and will clean them up when the collapse finishes.
        if (rigDetached) return;

        for (int i = 0; i < ownedMeshes.Count; i++)
            if (ownedMeshes[i] != null) Destroy(ownedMeshes[i]);
        ownedMeshes.Clear();

        if (mat != null) Destroy(mat);
        mat = null;
    }

    private void Update()
    {
        if (root == null) return;

        if (!dying && stats != null && stats.IsDead())
        {
            BeginDeath();
            return;
        }
        if (dying) return;

        // A frozen Buffer freezes visually too — same rule his FixedUpdate uses
        // for movement. Time stops for the rig, so the hem, the pendulum and
        // the orbit all hold their pose instead of animating inside the ice.
        bool frozen = buffer != null && buffer.IsFrozen();
        float dt = frozen ? 0f : Time.deltaTime;
        animTime += dt;

        charge01 = buffer != null ? Mathf.Clamp01(buffer.FogCharge01) : 0f;
        dropFlash = Mathf.Max(0f, dropFlash - Time.deltaTime * 3.5f);
        shardKick = Mathf.Max(0f, shardKick - Time.deltaTime * 2.2f);

        Color tint = sourceSprite != null ? sourceSprite.color : Color.white;
        tint.a = 1f; // alpha on the source sprite is not a fade signal for us

        UpdateFacing(dt);
        UpdateHover();

        if (enableRobe) UpdateRobe(tint);
        if (enableFace) UpdateFace(dt, tint);
        if (enableSigil) UpdateSigil(dt, tint);
        if (enableCenser) UpdateCenser(dt, tint);
        if (enableCenserSmoke) UpdateCenserSmoke(dt, tint);
        if (enableShards) UpdateShards(tint);
        if (enableHemMist) UpdateHemMist(tint);
        if (enableShadow) UpdateShadow(tint);
        if (enableDropPulse) UpdatePulses(tint);
        if (enableBuffTethers) UpdateTethers(dt);
    }

    private void LateUpdate()
    {
        if (root == null) return;

        // Backstop poll. LateUpdate runs after every Update but before Unity
        // processes deferred destruction, so this catches a Die() called from
        // any script's Update in this frame that the event hook missed.
        if (!dying && stats != null && stats.IsDead())
        {
            BeginDeath();
            return;
        }
        if (dying) return;

        // Stay upright no matter what physics or knockback does to the body,
        // and keep the Z lift.
        root.rotation = Quaternion.identity;
        root.localPosition = new Vector3(0f, 0f, zOffset);

        ApplySortingOrder();
    }

    //  Construction
    // Multiplies every world-space dimension by rigScale, exactly once. These
    // are runtime copies of the serialized values, so the prefab asset on disk
    // is untouched — but it does mean tweaking a field in the inspector while
    // playing gives you the UNSCALED number. Adjust rigScale instead.
    private void ApplyRigScale()
    {
        float k = Mathf.Max(0.01f, rigScale);
        if (Mathf.Approximately(k, 1f)) return;

        bodyHeight *= k;
        bodyWidth *= k;

        hoverHeight *= k;
        hoverAmplitude *= k;
        swayAmplitude *= k;
        hemWaveAmplitude *= k;
        moveLean *= k;

        eyeRadius *= k;
        eyeSeparation *= k;
        sigilRadius *= k;

        censerSmokeRise *= k;
        // censerChainLengthFraction / censerRadiusFraction / censerChainWidthFraction
        // are fractions of bodyHeight and bodyWidth, both already scaled above —
        // multiplying them here would apply rigScale twice.

        shardSize *= k;
        shardOrbitX *= k;
        shardOrbitY *= k;

        hemMistSize *= k;

        tetherTargetLift *= k;
        // tetherWidthFraction is a fraction of bodyWidth and tetherWaveAmplitude
        // a fraction of the arc's own length, so both already scale.

        // hoodCurl is already expressed as a fraction of bodyWidth, and every
        // speed / duration / angle is scale-invariant, so none of those are
        // touched here. tetherRange and pulseFallbackRadius are gameplay
        // distances (reach, fog radius) rather than body dimensions — they
        // belong to the ability, not to how big he is drawn.
    }

    // Aligns the physics hitbox with the body that is now actually on screen.
    // Without this the collider is still sized and centred for the 1-unit
    // placeholder sprite, so shots pass through his chest and connect with
    // empty air near his feet.
    // Aligns the physics hitbox with the body that is now on screen — while
    // keeping it somewhere the navigation code can reason about.
    //
    // The first version of this centred the collider on the VISUAL, which on a
    // 3x rig meant offset (0, +1.56) with a 1.42 x 2.77 box: the transform
    // origin was not even inside its own collider. Since every steering and
    // stuck-check in this project probes from transform.position, the Buffer
    // was navigating from a point a metre and a half below the thing that was
    // actually hitting the wall.
    private void FitCollider()
    {
        if (colliderFit == ColliderFitMode.Leave) return;

        var col = GetComponent<Collider2D>();
        if (col == null) return;

        float w = bodyWidth * colliderWidthFraction;
        float h = bodyHeight * colliderHeightFraction;

        float centerY = 0f;
        if (colliderFit == ColliderFitMode.MatchVisual)
        {
            centerY = groundOffsetY + hoverHeight + bodyHeight * 0.5f;

            if (Mathf.Abs(centerY) > 0.5f)
            {
                Debug.LogWarning(
                    $"[BufferVisual] {name}: Collider Fit is MatchVisual, which puts the " +
                    $"collider at Y {centerY:+0.00;-0.00} while the transform stays at 0. " +
                    "Steering and stuck-detection probe from transform.position, so the " +
                    "enemy will navigate from a point that is not where its body is — " +
                    "expect it to jam against walls. Use CenteredOnPivot unless you have " +
                    "moved the navigation code onto the collider.");
            }
        }

        if (col is CapsuleCollider2D capsule)
        {
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(w, h);
            capsule.offset = new Vector2(0f, centerY);
        }
        else if (col is BoxCollider2D box)
        {
            box.size = new Vector2(w, h);
            box.offset = new Vector2(0f, centerY);
        }
        else if (col is CircleCollider2D circle)
        {
            // A circle cannot express a tall body, so split the difference
            // rather than letting one axis be badly wrong.
            circle.radius = (w + h) * 0.25f;
            circle.offset = new Vector2(0f, centerY);
        }
        else
        {
            Debug.LogWarning(
                $"[BufferVisual] {name} has a {col.GetType().Name}, which this " +
                "component cannot resize automatically. Fit it by hand, swap it " +
                "for a CapsuleCollider2D, or set Collider Fit to Leave.");
        }
    }

    private void BuildMaterial()
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");

        mat = new Material(sh) { name = "BufferVisualShared" };
        mat.mainTexture = Texture2D.whiteTexture;

        // HARDENING: Sprites/Default multiplies by two uniforms that only a
        // SpriteRenderer normally supplies — _RendererColor and _Flip. Drawn
        // through a MeshRenderer they are whatever happens to be left in the
        // constant buffer, and if that is zero the mesh renders black or
        // collapses to a point. Writing them explicitly costs nothing and
        // removes the entire class of bug. (Worth adding the same two lines to
        // BufferFogVisual.BuildSharedMaterial if it ever misbehaves on a
        // different Unity version or render pipeline.)
        mat.SetColor("_RendererColor", Color.white);
        mat.SetVector("_Flip", new Vector4(1f, 1f, 1f, 1f));
        mat.SetFloat("_EnableExternalAlpha", 0f);
    }

    private void BuildRig()
    {
        var rootGO = new GameObject("BufferVisualRig");
        rootGO.transform.SetParent(transform, false);
        rootGO.transform.localPosition = new Vector3(0f, 0f, zOffset);
        root = rootGO.transform;

        var pivotGO = new GameObject("Body");
        pivotGO.transform.SetParent(root, false);
        pivot = pivotGO.transform;

        if (enableShadow) BuildShadow();
        if (enableRobe) BuildRobe();
        if (enableFace) BuildFace();
        if (enableSigil) BuildSigil();
        if (enableCenser) BuildCenser();
        if (enableCenserSmoke) BuildCenserSmoke();
        if (enableShards) BuildShards();
        if (enableHemMist) BuildHemMist();
        if (enableBuffTethers) BuildTethers();
        if (enableDropPulse) BuildPulses();
    }

    //  Shadow 

    private Part shadow;

    private void BuildShadow()
    {
        // Deliberately parented to root, not pivot: the contact shadow must not
        // bob with him or mirror when he turns.
        Mesh disc = BuildDisc(20, new[] { 0f, 0.34f, 0.5f }, new[] { 1f, 0.85f, 0f }, "BufferShadow");
        shadow = CreateMeshPart("Shadow", root, disc, shadowColor, -6);
        shadow.tr.localPosition = new Vector3(0f, groundOffsetY, 0f);
    }

    private void UpdateShadow(Color tint)
    {
        if (shadow == null) return;

        // Tighter and darker when he is low, wider and fainter when he rises.
        float lift = (pivot.localPosition.y - hoverHeight) / Mathf.Max(0.0001f, hoverAmplitude);
        float t = Mathf.Clamp01((lift + 1f) * 0.5f);
        float w = bodyWidth * Mathf.Lerp(1.30f, 1.02f, t);
        shadow.tr.localScale = new Vector3(w, w * 0.42f, 1f);
        shadow.alpha = Mathf.Lerp(1f, 0.62f, t);

        // The shadow is an occlusion cue, not part of the character, so it is
        // the one piece that must NOT take the hit-flash tint — a white flash
        // would turn it into a bright puddle.
        ApplyPart(shadow, Color.white);
    }

    // ── Robe ──────────────────────────────────────────────────────────────

    // Silhouette control points, hem (t=0) to crown (t=1), as (t, halfWidth)
    // where halfWidth is a fraction of bodyWidth. Hand-authored rather than a
    // formula because the two pinches — the neck at 0.60 and the shoulder
    // flare at 0.52 — are what stop it reading as a traffic cone.
    private static readonly Vector2[] RobeProfile =
    {
        new Vector2(0.000f, 0.470f), // hem — leaner than a cone, still grounded
        new Vector2(0.080f, 0.452f),
        new Vector2(0.180f, 0.418f),
        new Vector2(0.290f, 0.375f),
        new Vector2(0.400f, 0.338f),
        new Vector2(0.500f, 0.312f),
        new Vector2(0.570f, 0.296f), // shoulders
        new Vector2(0.645f, 0.246f), // neck — a nip, NOT the wasp waist I used
                                     // when a collar was going to cover it up
        new Vector2(0.710f, 0.272f),
        new Vector2(0.800f, 0.288f), // hood, reading clearly wider than the neck
        new Vector2(0.890f, 0.238f),
        new Vector2(0.950f, 0.140f),
        new Vector2(1.000f, 0.030f), // crown comes to a point
    };

    private static float ProfileHalfWidth(float t)
    {
        t = Mathf.Clamp01(t);
        for (int i = 1; i < RobeProfile.Length; i++)
        {
            if (t <= RobeProfile[i].x)
            {
                float u = Mathf.InverseLerp(RobeProfile[i - 1].x, RobeProfile[i].x, t);
                return Mathf.Lerp(RobeProfile[i - 1].y, RobeProfile[i].y, u);
            }
        }
        return RobeProfile[RobeProfile.Length - 1].y;
    }

    private void BuildRobe()
    {
        int rows = Mathf.Max(6, robeRows);
        int vertCount = rows * RobeColumns;

        robeVerts = new Vector3[vertCount];
        robeCols = new Color[vertCount];

        var tris = new int[(rows - 1) * (RobeColumns - 1) * 6];
        int ti = 0;
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < RobeColumns - 1; c++)
            {
                int i0 = r * RobeColumns + c;
                int i1 = i0 + 1;
                int i2 = i0 + RobeColumns;
                int i3 = i2 + 1;
                tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
            }
        }

        // Colours are baked once: they describe the cloth's own shading, and
        // never change. Every per-frame colour change (hit flash, freeze,
        // charge glow) rides the property block instead.
        for (int r = 0; r < rows; r++)
        {
            float t = r / (float)(rows - 1);

            // The hem does not end — it dissolves. Fading alpha out over the
            // bottom fifth is what lets the hem mist blend into the body
            // instead of sitting in front of a hard edge.
            float hemFade = Mathf.SmoothStep(0.18f, 1f, Mathf.InverseLerp(0f, 0.22f, t));

            // Slight ambient-occlusion darkening low on the robe.
            float vertShade = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(t * 1.4f));

            for (int c = 0; c < RobeColumns; c++)
            {
                float u = (c / (float)(RobeColumns - 1)) * 2f - 1f; // -1..1

                // Rim light biased to +u (the facing side). Because the whole
                // pivot mirrors on X when he turns, this automatically becomes
                // the correct side without any extra work.
                float lit = Mathf.Clamp01(0.5f + u * 0.62f);
                float edge = Mathf.Abs(u);

                Color c1 = Color.Lerp(robeMid, robeLit, lit * lit);
                Color c2 = Color.Lerp(c1, robeEdge, edge * edge);
                c2 = new Color(c2.r * vertShade, c2.g * vertShade, c2.b * vertShade, hemFade);

                robeCols[r * RobeColumns + c] = c2;
            }
        }

        robeMesh = new Mesh { name = "BufferRobe" };
        robeMesh.MarkDynamic();
        WriteRobeVertices(0f);
        robeMesh.vertices = robeVerts;
        robeMesh.colors = robeCols;
        robeMesh.triangles = tris;
        ownedMeshes.Add(robeMesh);

        robePart = CreateMeshPart("Robe", pivot, robeMesh, Color.white, 0);
    }

    // Writes the deformed silhouette into robeVerts. Split out from
    // UpdateRobe so BuildRobe can produce a valid first frame before anything
    // is visible — otherwise the mesh spends one frame collapsed at the origin.
    private void WriteRobeVertices(float velX)
    {
        int rows = robeVerts.Length / RobeColumns;

        for (int r = 0; r < rows; r++)
        {
            float t = r / (float)(rows - 1);
            float y = groundOffsetY + t * bodyHeight;
            float halfW = ProfileHalfWidth(t) * bodyWidth;

            // Cloth sway: pinned at the shoulders, free at the hem.
            float hang = (1f - t);
            float sway = Mathf.Sin(animTime * swaySpeed + t * 2.6f) * swayAmplitude * hang * hang;

            // Movement lag: the hood trails, so lean grows with t.
            float lean = -velX * moveLean * t * t;

            // The hood curls forward. Quartic so it only kicks in at the very top.
            float curl = hoodCurl * bodyWidth * t * t * t * t * 6f;

            for (int c = 0; c < RobeColumns; c++)
            {
                float u = (c / (float)(RobeColumns - 1)) * 2f - 1f;

                // Hem ripple: a wave travelling around the silhouette. Phase is
                // offset by u so the left and right edges are never in sync,
                // which is what makes it look like fabric instead of a bounce.
                float ripple = Mathf.Sin(animTime * hemWaveSpeed + u * 2.9f + t * 3.4f)
                             * hemWaveAmplitude * hang * hang;

                robeVerts[r * RobeColumns + c] = new Vector3(
                    u * halfW + sway + lean + curl,
                    y + ripple,
                    0f);
            }
        }
    }

    private void UpdateRobe(Color tint)
    {
        if (robePart == null || robeMesh == null) return;

        float velX = 0f;
        if (rb != null)
        {
            // Into the pivot's mirrored space, so lean always trails correctly.
            velX = rb.linearVelocity.x * (facing >= 0f ? 1f : -1f);
            velX = Mathf.Clamp(velX, -6f, 6f);
        }

        WriteRobeVertices(velX);
        robeMesh.vertices = robeVerts;

        // Charge bleeds a faint violet heat up through the cloth, so the body
        // itself participates in the telegraph rather than only the trinkets.
        float heat = charge01 * charge01 * 0.22f + dropFlash * 0.35f;
        Color c = Color.Lerp(Color.white, glowColor, heat);
        ApplyPart(robePart, tint * c);
    }

    // ── Face ──────────────────────────────────────────────────────────────

    private float HoodCenterY => groundOffsetY + bodyHeight * 0.775f;
    private float HoodCenterX => hoodCurl * bodyWidth * 3.1f;

    private void BuildFace()
    {
        // The void is a hard-edged hole, so its alpha profile stays at 1 almost
        // all the way out — a soft one would look like a smudge, not a cavity.
        Mesh voidMesh = BuildDisc(18, new[] { 0f, 0.40f, 0.5f }, new[] { 1f, 1f, 0f }, "BufferHoodVoid");
        hoodVoid = CreateMeshPart("HoodVoid", pivot, voidMesh, voidColor, 3);
        hoodVoid.tr.localPosition = new Vector3(HoodCenterX, HoodCenterY, 0f);
        hoodVoid.tr.localScale = new Vector3(bodyWidth * 0.48f, bodyWidth * 0.40f, 1f);

        Mesh eyeMesh = BuildDisc(14, new[] { 0f, 0.22f, 0.5f }, new[] { 1f, 0.55f, 0f }, "BufferEye");
        eyeL = CreateMeshPart("EyeL", pivot, eyeMesh, eyeColor, 4);
        eyeR = CreateMeshPart("EyeR", pivot, eyeMesh, eyeColor, 4);

        // Narrow slits tilted so their inner ends drop toward each other — the
        // universal shorthand for a scowl, and far more legible at a distance
        // than two round dots, which read as surprise or as nothing at all.
        // The pivot mirrors these along with everything else when he turns.
        eyeL.tr.localRotation = Quaternion.Euler(0f, 0f, -17f);
        eyeR.tr.localRotation = Quaternion.Euler(0f, 0f, 17f);
    }

    private void UpdateFace(float dt, Color tint)
    {
        if (hoodVoid == null) return;

        ApplyPart(hoodVoid, Color.white); // a hole is a hole; never tinted

        // Blink scheduling. A blink is a fast vertical squash of the glow.
        if (blinkPhase < 0f)
        {
            blinkTimer -= dt;
            if (blinkTimer <= 0f)
            {
                blinkPhase = 0f;
                blinkTimer = blinkInterval * Random.Range(0.6f, 1.5f);
            }
        }
        else
        {
            blinkPhase += dt / 0.13f;
            if (blinkPhase >= 1f) blinkPhase = -1f;
        }

        float squash = blinkPhase < 0f ? 1f : 1f - Mathf.Sin(blinkPhase * Mathf.PI) * 0.92f;

        // Eyes are the primary charge readout: they widen and brighten as the
        // drop nears, then blow out on the drop itself.
        float glow = 1f + charge01 * 0.85f + dropFlash * 1.6f;
        float rad = eyeRadius * (1f + charge01 * 0.22f + dropFlash * 0.5f);

        // Independent slow drift so the stare never looks frozen.
        float driftY = Mathf.Sin(animTime * 1.4f) * eyeRadius * 0.12f;

        float baseX = HoodCenterX + bodyWidth * 0.045f; // sits slightly forward in the hood
        float baseY = HoodCenterY + driftY;

        eyeL.tr.localPosition = new Vector3(baseX - eyeSeparation * 0.5f, baseY, 0f);
        eyeR.tr.localPosition = new Vector3(baseX + eyeSeparation * 0.5f, baseY, 0f);

        // Tall and narrow. The far eye is slighter — cheap head-turn perspective.
        eyeL.tr.localScale = new Vector3(rad * 0.80f, rad * 2.30f * squash, 1f);
        eyeR.tr.localScale = new Vector3(rad * 0.95f, rad * 2.65f * squash, 1f);

        eyeL.alpha = 0.82f;
        eyeR.alpha = 1f;
        ApplyPart(eyeL, tint * glow);
        ApplyPart(eyeR, tint * glow);
    }

    // ── Chest sigil 

    private void BuildSigil()
    {
        Mesh ring = BuildRingMesh(24, 0.34f, 0.5f, "BufferSigil");
        sigil = CreateMeshPart("Sigil", pivot, ring, glowColor, 4);
        sigil.tr.localPosition = new Vector3(HoodCenterX * 0.4f, groundOffsetY + bodyHeight * 0.435f, 0f);
    }

    private void UpdateSigil(float dt, Color tint)
    {
        if (sigil == null) return;

        sigilAngle += dt * sigilSpinSpeed * (1f + charge01 * 3f);
        sigil.tr.localRotation = Quaternion.Euler(0f, 0f, sigilAngle);

        float s = sigilRadius * 2f * (1f + charge01 * 0.18f + dropFlash * 0.4f);
        // Squashed on Y so it reads as a ring lying at an angle on his chest.
        sigil.tr.localScale = new Vector3(s, s * 0.62f, 1f);
        sigil.alpha = 0.30f + charge01 * 0.55f + dropFlash * 0.6f;

        ApplyPart(sigil, tint);
    }

    // ── Censer 

    private void BuildCenser()
    {
        var anchorGO = new GameObject("CenserAnchor");
        anchorGO.transform.SetParent(pivot, false);
        censerAnchor = anchorGO.transform;
        // At the sleeve edge (the robe's own half-width up here is ~0.29 of
        // bodyWidth) and at shoulder height, so the chain reads as leaving his
        // hand instead of sprouting out of his stomach.
        censerAnchor.localPosition = new Vector3(
            bodyWidth * 0.29f, groundOffsetY + bodyHeight * 0.58f, 0f);

        var chainGO = new GameObject("CenserChain");
        chainGO.transform.SetParent(pivot, false);
        censerChain = chainGO.AddComponent<LineRenderer>();
        censerChain.useWorldSpace = false;
        censerChain.sharedMaterial = mat;
        censerChain.positionCount = Mathf.Max(2, censerChainPoints);
        censerChain.startWidth = bodyWidth * censerChainWidthFraction;
        censerChain.endWidth = bodyWidth * censerChainWidthFraction * 0.85f;
        censerChain.numCornerVertices = 1;
        censerChain.numCapVertices = 0;
        censerChain.textureMode = LineTextureMode.Stretch;
        censerChain.startColor = Color.clear;
        censerChain.endColor = Color.clear;
        Register(censerChain, 2);

        var bodyGO = new GameObject("CenserBody");
        bodyGO.transform.SetParent(pivot, false);
        censerBody = bodyGO.transform;

        Mesh core = BuildDisc(16, new[] { 0f, 0.20f, 0.5f }, new[] { 1f, 0.6f, 0f }, "BufferCenserCore");
        Mesh shell = BuildDisc(16, new[] { 0f, 0.38f, 0.5f }, new[] { 1f, 0.95f, 0f }, "BufferCenserShell");
        Mesh rim = BuildRingMesh(18, 0.36f, 0.5f, "BufferCenserRim");

        censerShell = CreateMeshPart("Shell", censerBody, shell, robeEdge, 4);
        censerRim = CreateMeshPart("Rim", censerBody, rim, glowColor, 5);
        censerCore = CreateMeshPart("Core", censerBody, core, glowColor, 6);
    }

    private void UpdateCenser(float dt, Color tint)
    {
        if (censerAnchor == null) return;

        float L = CenserChainLength;

        // Pendulum. Movement drives it through the horizontal acceleration of
        // the rigidbody, so he swings the censer by walking rather than by an
        // authored animation — start moving and it lags behind, stop and it
        // overshoots forward.
        float vLocalX = 0f;
        if (rb != null) vLocalX = rb.linearVelocity.x * (facing >= 0f ? 1f : -1f);

        float accelX = (vLocalX - lastVelX) / Mathf.Max(dt, 0.0001f);
        lastVelX = vLocalX;

        // Reactive forcing ONLY. The idle swing is deliberately not fed in here:
        // pushing a pendulum well off its natural frequency barely moves it,
        // which is exactly how the last version ended up with a censer that
        // technically swung and visibly did not.
        float drive =
            -Mathf.Clamp(accelX, -40f, 40f) * 0.012f
            - vLocalX * 0.55f;

        const float gravity = 16f;
        float angAcc = -(gravity / L) * Mathf.Sin(censerAngle) - censerDamping * censerAngVel + drive;

        censerAngVel += angAcc * dt;
        censerAngVel = Mathf.Clamp(censerAngVel, -12f, 12f);
        censerAngle += censerAngVel * dt;
        censerAngle = Mathf.Clamp(censerAngle, -1.15f, 1.15f);

        // Authored idle swing, layered on top of the reactive angle. Two
        // detuned sines so it never settles into a metronome.
        float idle = Mathf.Sin(animTime * censerIdleSwingSpeed)
                   + Mathf.Sin(animTime * censerIdleSwingSpeed * 0.37f + 0.8f) * 0.3f;
        idle *= censerIdleSwingAngle * Mathf.Deg2Rad;

        float swing = censerRestAngle * Mathf.Deg2Rad + censerAngle + idle;

        Vector3 anchor = censerAnchor.localPosition;
        Vector3 bob = anchor + new Vector3(Mathf.Sin(swing) * L, -Mathf.Cos(swing) * L, 0f);
        censerBody.localPosition = bob;

        // Chain with a little slack, bulging away from the swing direction.
        int n = censerChain.positionCount;
        float sag = 0.05f * L * Mathf.Clamp01(1f - Mathf.Abs(censerAngVel) * 0.15f);
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(anchor, bob, u);
            p.y -= Mathf.Sin(u * Mathf.PI) * sag;
            censerChain.SetPosition(i, p);
        }

        Color chain = robeEdge * tint;
        chain.a = 0.9f;
        Color chainEnd = Color.Lerp(robeEdge, glowColor, 0.25f) * tint;
        chainEnd.a = 0.9f;
        censerChain.startColor = chain;
        censerChain.endColor = chainEnd;

        // Thurible. The core is the charge meter: it inflates and brightens all
        // the way to the drop, then punches on release.
        float pulse = 0.5f + 0.5f * Mathf.Sin(animTime * (3f + charge01 * 9f));
        float cr = CenserRadius;
        float coreScale = cr * (1.15f + charge01 * 0.75f + dropFlash * 1.4f + pulse * 0.12f);

        censerShell.tr.localScale = new Vector3(cr * 2f, cr * 1.85f, 1f);
        censerShell.alpha = 1f;
        ApplyPart(censerShell, tint);

        censerRim.tr.localScale = new Vector3(cr * 2.25f, cr * 2.1f, 1f);
        // Tracks the FULL swing, so the thurible hangs square to its chain
        // rather than rotating independently of it.
        censerRim.tr.localRotation = Quaternion.Euler(0f, 0f, -swing * Mathf.Rad2Deg);
        censerRim.alpha = 0.35f + charge01 * 0.5f + dropFlash * 0.5f;
        ApplyPart(censerRim, tint);

        censerCore.tr.localScale = new Vector3(coreScale * 2f, coreScale * 2f, 1f);
        censerCore.alpha = 0.55f + charge01 * 0.45f + dropFlash * 0.8f;
        ApplyPart(censerCore, tint * (1f + charge01 * 0.6f + dropFlash));
    }

    private float CenserChainLength => Mathf.Max(0.02f, bodyHeight * censerChainLengthFraction);
    private float CenserRadius => Mathf.Max(0.01f, bodyWidth * censerRadiusFraction);

    private Vector3 GetCenserWorldPosition()
    {
        if (censerBody != null) return censerBody.position;
        return transform.position;
    }

    // ── Censer smoke 

    private void BuildCenserSmoke()
    {
        if (censerSmokeCount <= 0) return;

        Mesh puff = BuildDisc(14, new[] { 0f, 0.26f, 0.5f }, new[] { 1f, 0.42f, 0f }, "BufferSmokePuff");
        smoke = new SmokePuff[censerSmokeCount];
        for (int i = 0; i < smoke.Length; i++)
        {
            // Parented to root, not pivot: once a puff has left the censer it
            // is in the world, and should not mirror when he turns around.
            var p = CreateMeshPart("Smoke_" + i, root, puff, mistColor, 5);
            p.tr.localScale = Vector3.zero;
            smoke[i] = new SmokePuff { part = p, life = -1f };
        }
    }

    private void UpdateCenserSmoke(float dt, Color tint)
    {
        if (smoke == null) return;

        smokeSpawnAccum += dt * censerSmokeRate * (0.55f + charge01 * 1.2f + dropFlash * 3f);

        Vector3 originLocal = root.InverseTransformPoint(GetCenserWorldPosition());

        for (int i = 0; i < smoke.Length; i++)
        {
            var s = smoke[i];

            if (s.life < 0f)
            {
                if (smokeSpawnAccum >= 1f)
                {
                    smokeSpawnAccum -= 1f;
                    s.pos = new Vector2(originLocal.x, originLocal.y);
                    s.vel = new Vector2(Random.Range(-0.12f, 0.12f), censerSmokeRise * Random.Range(0.7f, 1.3f));
                    s.maxLife = censerSmokeLifetime * Random.Range(0.75f, 1.25f);
                    s.life = 0f;
                    s.size = Random.Range(0.55f, 1f);
                    s.spin = Random.Range(0f, Mathf.PI * 2f);
                }
                else
                {
                    s.part.tr.localScale = Vector3.zero;
                    smoke[i] = s;
                    continue;
                }
            }

            s.life += dt;
            if (s.life >= s.maxLife)
            {
                s.life = -1f;
                s.part.tr.localScale = Vector3.zero;
                smoke[i] = s;
                continue;
            }

            float u = s.life / s.maxLife;

            // Curl sideways as it rises and slows — the classic incense drift.
            s.vel.y *= 0.985f;
            s.pos += s.vel * dt;
            s.pos.x += Mathf.Sin(animTime * 1.9f + s.spin) * 0.18f * dt;

            s.part.tr.localPosition = new Vector3(s.pos.x, s.pos.y, 0f);

            float scale = CenserRadius * 2.4f * s.size * Mathf.Lerp(0.4f, 1.9f, u);
            s.part.tr.localScale = new Vector3(scale, scale, 1f);

            // Fade in fast, out slow.
            s.part.alpha = (u < 0.18f ? u / 0.18f : 1f - (u - 0.18f) / 0.82f) * 0.5f;
            ApplyPart(s.part, tint);

            smoke[i] = s;
        }
    }

    // ── Rune shards ───────────────────────────────────────────────────────

    private void BuildShards()
    {
        if (shardCount <= 0) return;

        Mesh diamond = BuildDiamondMesh("BufferShard");
        shards = new Shard[shardCount];

        for (int i = 0; i < shardCount; i++)
        {
            var go = new GameObject("Shard_" + i);
            go.transform.SetParent(pivot, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = diamond;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingLayerName = sortingLayerName;
            // Deliberately NOT registered in the shared sorted list: a shard's
            // order changes twice per orbit, and routing that through the
            // shared list would re-apply every renderer in the rig every frame.

            shards[i] = new Shard
            {
                tr = go.transform,
                mr = mr,
                phase = (i / (float)shardCount) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f),
                spin = Random.Range(50f, 140f) * (Random.value < 0.5f ? -1f : 1f),
                spinPhase = Random.Range(0f, 360f),
                sizeScale = Random.Range(0.8f, 1.2f),
            };
        }
    }

    private void UpdateShards(Color tint)
    {
        if (shards == null) return;

        float centerY = groundOffsetY + bodyHeight * 0.55f;

        // Charge sucks the ring inward; the drop throws it back out. The two
        // are separate terms so a drop can overshoot past the resting radius.
        float pull = 1f - shardChargePull * charge01;
        float kick = 1f + shardKick * 0.55f;

        for (int i = 0; i < shards.Length; i++)
        {
            var s = shards[i];
            if (s.tr == null) continue;

            float a = animTime * shardOrbitSpeed * (1f + charge01 * 1.6f) + s.phase;
            float sn = Mathf.Sin(a);

            float x = Mathf.Cos(a) * shardOrbitX * pull * kick;
            float y = centerY + sn * shardOrbitY * pull * kick;

            s.tr.localPosition = new Vector3(x, y, 0f);
            s.tr.localRotation = Quaternion.Euler(0f, 0f, s.spinPhase + animTime * s.spin);

            // Pseudo-3D: the near half of the orbit (sin < 0) draws in front of
            // the robe and slightly larger. This one trick is what makes four
            // flat quads read as a ring circling a body.
            bool near = sn < 0f;
            float depth = Mathf.Abs(sn);
            float size = shardSize * s.sizeScale * Mathf.Lerp(1f, near ? 1.18f : 0.78f, depth);
            s.tr.localScale = new Vector3(size, size, 1f);

            s.mr.sortingOrder = SortBase + (near ? 7 : -1);

            float alpha = Mathf.Lerp(1f, near ? 1f : 0.55f, depth)
                        * (0.65f + charge01 * 0.35f + dropFlash * 0.5f);

            mpb.SetColor(ColorId, MulAlpha(glowColor * tint, alpha));
            s.mr.SetPropertyBlock(mpb);
        }
    }

    // ── Hem mist ──────────────────────────────────────────────────────────

    private void BuildHemMist()
    {
        if (hemMistCount <= 0) return;

        Mesh puff = BuildDisc(14, new[] { 0f, 0.24f, 0.5f }, new[] { 1f, 0.38f, 0f }, "BufferHemMist");
        hemMist = new Part[hemMistCount];
        hemMistPhase = new float[hemMistCount];

        for (int i = 0; i < hemMistCount; i++)
        {
            hemMist[i] = CreateMeshPart("HemMist_" + i, pivot, puff, mistColor, 5);
            hemMistPhase[i] = Random.Range(0f, Mathf.PI * 2f);
        }
    }

    private void UpdateHemMist(Color tint)
    {
        if (hemMist == null) return;

        for (int i = 0; i < hemMist.Length; i++)
        {
            float ph = hemMistPhase[i];
            float spread = (i / (float)Mathf.Max(1, hemMist.Length - 1)) * 2f - 1f;

            float x = spread * bodyWidth * 0.42f + Mathf.Sin(animTime * 0.9f + ph) * bodyWidth * 0.12f;
            float y = groundOffsetY + Mathf.Abs(Mathf.Sin(animTime * 0.7f + ph * 1.3f)) * bodyHeight * 0.10f;

            hemMist[i].tr.localPosition = new Vector3(x, y, 0f);

            float breathe = 0.85f + 0.15f * Mathf.Sin(animTime * 1.3f + ph);
            float sx = hemMistSize * breathe * (1f - Mathf.Abs(spread) * 0.25f);
            hemMist[i].tr.localScale = new Vector3(sx, sx * 0.62f, 1f);

            hemMist[i].alpha = 0.24f + 0.10f * Mathf.Sin(animTime * 1.1f + ph) + charge01 * 0.14f;
            ApplyPart(hemMist[i], tint);
        }
    }

    // ── Buff tethers ──────────────────────────────────────────────────────

    private void BuildTethers()
    {
        tethers = new Tether[Mathf.Max(1, maxTethers)];
        // Floored so an un-Reset component still gets a smooth curve.
        int segs = Mathf.Max(16, tetherSegments);

        for (int i = 0; i < tethers.Length; i++)
        {
            var go = new GameObject("BuffTether_" + i);
            // World space, parented to root so it is carried along on death
            // detach but never inherits the body's mirror or bob.
            go.transform.SetParent(root, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = mat;
            lr.positionCount = segs;
            float w = bodyWidth * tetherWidthFraction;
            lr.startWidth = w;
            lr.endWidth = w * 0.75f;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;   // rounded ends, no chopped-off tips
            lr.textureMode = LineTextureMode.Stretch;
            lr.enabled = false;
            Register(lr, 5);

            tethers[i] = new Tether
            {
                go = go,
                lr = lr,
                phase = Random.Range(0f, 1f),
                phaseA = Random.Range(0f, Mathf.PI * 2f),
                phaseB = Random.Range(0f, Mathf.PI * 2f),
                bow = Random.Range(-1f, 1f),
                visible = 0f,
                gradient = new Gradient(),
                colorKeys = new GradientColorKey[3],
                alphaKeys = new GradientAlphaKey[7],
            };
        }
    }

    private void UpdateTethers(float dt)
    {
        if (tethers == null) return;

        tetherRefreshTimer -= dt;
        if (tetherRefreshTimer <= 0f)
        {
            tetherRefreshTimer = tetherRefreshInterval;
            RefreshTetherTargets();
        }

        Vector3 origin = GetCenserWorldPosition();
        float fadeRate = 1f / Mathf.Max(0.05f, tetherFadeTime);

        for (int i = 0; i < tethers.Length; i++)
        {
            var t = tethers[i];

            // Fade rather than pop. An ally drifting in and out of a cloud used
            // to snap its beam on and off at the rescan rate; now the beam
            // arrives and leaves, and a lost target keeps its last endpoint so
            // the arc has somewhere to retract to.
            Vector3 end;
            if (t.target != null)
            {
                end = t.target.position + new Vector3(0f, tetherTargetLift, 0f);
                t.lastEnd = end;
                t.visible = Mathf.Min(1f, t.visible + dt * fadeRate);
            }
            else
            {
                end = t.lastEnd;
                t.visible = Mathf.Max(0f, t.visible - dt * fadeRate);
            }

            if (t.visible <= 0.001f)
            {
                if (t.lr.enabled) t.lr.enabled = false;
                continue;
            }

            Vector3 delta = end - origin;
            float len = Mathf.Max(0.0001f, delta.magnitude);
            Vector3 perp = new Vector3(-delta.y, delta.x, 0f) * (1f / len);

            // Amplitude scales with length so a short tether stays taut and a
            // long one has room to breathe — and it is capped, so an ally across
            // the arena doesn't produce one enormous sine wave.
            float amp = Mathf.Min(len * tetherWaveAmplitude, bodyWidth * 0.45f) * t.visible;

            int segs = t.lr.positionCount;
            for (int sIdx = 0; sIdx < segs; sIdx++)
            {
                float u = sIdx / (float)(segs - 1);

                // Two detuned harmonics travelling toward the ally. This is the
                // whole change: the old version rolled fresh random offsets 20
                // times a second across 28% of his body width, which is noise,
                // not motion. A continuous wave carries the same sense of live
                // energy with none of the strobing — and costs less, because
                // nothing is being randomised per frame.
                float wave = Mathf.Sin(u * 6.0f - animTime * tetherWaveSpeed * 6.0f + t.phaseA)
                           + Mathf.Sin(u * 11.0f - animTime * tetherWaveSpeed * 3.7f + t.phaseB) * 0.32f;

                // Envelope pins both ends to zero, so the arc attaches cleanly
                // instead of wagging where it meets him or the ally.
                float env = Mathf.Sin(u * Mathf.PI);

                // The constant bow term separates two arcs heading to allies
                // standing near each other, instead of merging them into one line.
                float lateral = (wave * 0.42f + t.bow * 0.9f) * amp * env;

                Vector3 p = Vector3.Lerp(origin, end, u) + perp * lateral;
                p.z = origin.z;
                t.lr.SetPosition(sIdx, p);
            }

            // One soft pulse drifts from him to the ally, brightening as it
            // leaves and dissolving as it arrives, then a new one starts. Fading
            // at both extremes is what stops it teleporting back to the start —
            // it reads as a comet, not a looping ticker.
            float flow = Mathf.Repeat(animTime * tetherFlowSpeed + t.phase, 1f);
            float head = Mathf.Lerp(0.30f, 0.76f, flow);
            float headStrength = Mathf.Sin(flow * Mathf.PI);

            float baseA = (0.20f + charge01 * 0.12f) * t.visible;
            float peakA = Mathf.Clamp01(baseA + headStrength * (0.34f + charge01 * 0.16f) * t.visible);

            // Seven keys, strictly ascending by construction: head is clamped to
            // 0.30-0.76 and the shoulders sit at 0.09 and 0.91, so the moving
            // keys can never cross the fixed ones. Both ends land on zero, so
            // there is no hard tip at the censer or at the ally.
            t.alphaKeys[0] = new GradientAlphaKey(0f, 0f);
            t.alphaKeys[1] = new GradientAlphaKey(baseA, 0.09f);
            t.alphaKeys[2] = new GradientAlphaKey(baseA, head - 0.13f);
            t.alphaKeys[3] = new GradientAlphaKey(peakA, head);
            t.alphaKeys[4] = new GradientAlphaKey(baseA, head + 0.13f);
            t.alphaKeys[5] = new GradientAlphaKey(baseA, 0.91f);
            t.alphaKeys[6] = new GradientAlphaKey(0f, 1f);

            // Colour follows the pulse, so the pale value exists only at the one
            // point that is currently bright.
            t.colorKeys[0] = new GradientColorKey(arcColor, 0f);
            t.colorKeys[1] = new GradientColorKey(Color.Lerp(arcColor, arcPulseColor, headStrength), head);
            t.colorKeys[2] = new GradientColorKey(arcColor, 1f);

            t.gradient.SetKeys(t.colorKeys, t.alphaKeys);
            t.lr.colorGradient = t.gradient;

            if (!t.lr.enabled) t.lr.enabled = true;
        }
    }

    private void RefreshTetherTargets()
    {
        _tetherScratch.Clear();

        int hits = Physics2D.OverlapCircle(transform.position, tetherRange, tetherScanFilter, TetherScanBuffer);
        for (int i = 0; i < hits && _tetherScratch.Count < tethers.Length; i++)
        {
            var col = TetherScanBuffer[i];
            if (col == null) continue;

            var es = col.GetComponentInParent<EnemyStats>();
            if (es == null) continue;
            if (es == stats) continue;
            if (es.IsDead()) continue;

            // The tag is the ground truth: BufferFog adds it to anyone standing
            // in a cloud and strips it when they leave. Reading the tag rather
            // than re-deriving "who is in my fog" means the beam is on screen
            // exactly as long as the buff is actually applied, including for
            // clouds this Buffer dropped and then walked away from.
            if (es.GetComponent<ScarecrowBuffTag>() == null) continue;

            // An enemy can carry several colliders; only tether it once.
            if (_tetherScratch.Contains(es.transform)) continue;

            _tetherScratch.Add(es.transform);
        }

        // PASS 1 — keep every tether already pointed at someone still on the
        // list. Without this the whole set was rebuilt in raw physics-scan
        // order every 0.18s, so a beam could jump between two enemies from one
        // rescan to the next and whip across the screen. Sticky assignment
        // keeps a tether on its ally for as long as that ally stays buffed.
        for (int i = 0; i < tethers.Length; i++)
        {
            var t = tethers[i];
            if (t.target == null) continue;

            int idx = _tetherScratch.IndexOf(t.target);
            if (idx >= 0) _tetherScratch.RemoveAt(idx);   // claimed, keep it
            else t.target = null;                          // buff gone: fade out
        }

        // PASS 2 — hand whoever is left to the free tethers.
        for (int i = 0; i < tethers.Length && _tetherScratch.Count > 0; i++)
        {
            if (tethers[i].target != null) continue;

            tethers[i].target = _tetherScratch[_tetherScratch.Count - 1];
            _tetherScratch.RemoveAt(_tetherScratch.Count - 1);

            // Re-roll the look so a recycled slot doesn't inherit the previous
            // ally's wave phase and appear to snap as it fades in.
            tethers[i].phase = Random.Range(0f, 1f);
            tethers[i].phaseA = Random.Range(0f, Mathf.PI * 2f);
            tethers[i].phaseB = Random.Range(0f, Mathf.PI * 2f);
            tethers[i].bow = Random.Range(-1f, 1f);
        }
    }

    private void BuildPulses()
    {
        Mesh ring = BuildRingMesh(30, 0.40f, 0.5f, "BufferDropPulse");
        pulses = new Pulse[3];
        for (int i = 0; i < pulses.Length; i++)
        {
            var p = CreateMeshPart("DropPulse_" + i, root, ring, glowColor, -2);
            p.tr.localScale = Vector3.zero;
            pulses[i] = new Pulse { part = p, active = false };
        }
    }

    private void UpdatePulses(Color tint)
    {
        if (pulses == null) return;

        for (int i = 0; i < pulses.Length; i++)
        {
            var p = pulses[i];
            if (!p.active) continue;

            p.t += Time.deltaTime / Mathf.Max(0.05f, pulseDuration);
            if (p.t >= 1f)
            {
                p.active = false;
                p.part.tr.localScale = Vector3.zero;
                pulses[i] = p;
                continue;
            }

            // Ease-out so it snaps out of the censer and settles on the fog's
            // real radius rather than crawling there linearly.
            float e = 1f - (1f - p.t) * (1f - p.t);
            float r = p.maxRadius * 2f * e;

            // Squashed to the same 0.42 ratio as the shadow — the ring is
            // reading as a circle on the ground, not a bubble in the air.
            p.part.tr.localScale = new Vector3(r, r * 0.42f, 1f);
            p.part.alpha = (1f - p.t) * (1f - p.t) * 0.85f;
            ApplyPart(p.part, tint);

            pulses[i] = p;
        }
    }

    private void HandleFogSpawned(Vector3 worldPos)
    {
        dropFlash = 1f;
        shardKick = 1f;

        // Kick the pendulum — the drop looks like it costs him something.
        censerAngVel += Random.Range(2.5f, 4.5f) * (Random.value < 0.5f ? -1f : 1f);

        if (!enableDropPulse || pulses == null || root == null) return;

        float radius = buffer != null && buffer.FogRadius > 0f ? buffer.FogRadius : pulseFallbackRadius;

        for (int i = 0; i < pulses.Length; i++)
        {
            if (pulses[i].active) continue;

            var p = pulses[i];
            p.active = true;
            p.t = 0f;
            p.maxRadius = radius;

            Vector3 local = root.InverseTransformPoint(worldPos);
            // Rings sit on the ground under the drop point, not in the air.
            p.part.tr.localPosition = new Vector3(local.x, groundOffsetY, 0f);
            pulses[i] = p;
            return;
        }
    }

    // ── Facing & hover ────────────────────────────────────────────────────

    private void UpdateFacing(float dt)
    {
        float desired = facing;

        if (rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.05f)
        {
            desired = Mathf.Sign(rb.linearVelocity.x);
        }
        else if (buffer != null && buffer.CurrentTarget != null)
        {
            // Standing still next to the ally he is supporting: keep looking at
            // them rather than freezing mid-turn.
            float dx = buffer.CurrentTarget.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.08f) desired = Mathf.Sign(dx);
        }

        facing = Mathf.MoveTowards(facing, desired, dt * facingLerpSpeed);
        if (Mathf.Abs(facing) < 0.06f) facing = 0.06f * Mathf.Sign(desired == 0f ? 1f : desired);
    }

    private void UpdateHover()
    {
        float bob = Mathf.Sin(animTime * hoverSpeed) * hoverAmplitude;

        // A second, slower wave keeps the float from looking metronomic.
        bob += Mathf.Sin(animTime * hoverSpeed * 0.43f + 1.1f) * hoverAmplitude * 0.35f;

        // Robe vertices are already authored from groundOffsetY upward, so the
        // pivot only ever carries the LIFT above that base — not the base too.
        pivot.localPosition = new Vector3(0f, hoverHeight + bob, 0f);

        // Scale.x carries the mirror. The tiny |x| floor set in UpdateFacing
        // stops a degenerate zero-width matrix mid-turn.
        pivot.localScale = new Vector3(facing, 1f, 1f);
    }

    // ── Death ─────────────────────────────────────────────────────────────

    private void BeginDeath()
    {
        dying = true;

        if (!enableDeathCollapse || root == null)
        {
            if (root != null) Destroy(root.gameObject);
            return;
        }

        // Detach so the collapse survives EnemyStats destroying the enemy on
        // this very frame (which it does whenever there is no death animation).
        root.SetParent(null, true);
        rigDetached = true;

        // Tethers snap the instant he dies — that is the readable beat: the
        // pack visibly loses its support.
        if (tethers != null)
        {
            for (int i = 0; i < tethers.Length; i++)
            {
                if (tethers[i].lr != null) tethers[i].lr.enabled = false;
                tethers[i].target = null;
            }
        }

        var debris = new List<Transform>();
        if (shards != null)
            for (int i = 0; i < shards.Length; i++)
                if (shards[i].tr != null) debris.Add(shards[i].tr);
        if (censerBody != null) debris.Add(censerBody);

        var collapse = root.gameObject.AddComponent<BufferDeathCollapse>();
        collapse.Play(
            duration: deathCollapseDuration,
            pivot: pivot,
            shadow: shadow != null ? shadow.tr : null,
            debris: debris,
            ownedMaterial: mat,
            ownedMeshes: new List<Mesh>(ownedMeshes),
            burstColor: glowColor,
            groundY: groundOffsetY,
            bodyCenterY: groundOffsetY + hoverHeight + bodyHeight * 0.5f,
            scale: Mathf.Max(0.01f, rigScale));
    }

    //  Rendering plumbing

    private Part CreateMeshPart(string partName, Transform parent, Mesh mesh, Color color, int sortOffset)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        Register(mr, sortOffset);

        return new Part { tr = go.transform, mr = mr, color = color, alpha = 1f };
    }

    private void Register(Renderer r, int offset)
    {
        r.sortingLayerName = sortingLayerName;
        sortedRenderers.Add(r);
        sortOffsets.Add(offset);
        lastAppliedSortBase = int.MinValue; // force a re-apply next LateUpdate
    }

    private void ApplySortingOrder()
    {
        int baseOrder = SortBase;
        if (baseOrder == lastAppliedSortBase) return;
        lastAppliedSortBase = baseOrder;

        for (int i = 0; i < sortedRenderers.Count; i++)
        {
            var r = sortedRenderers[i];
            if (r == null) continue;
            r.sortingOrder = baseOrder + sortOffsets[i];
        }
    }

    private void ApplyPart(Part p, Color tint)
    {
        if (p == null || p.mr == null) return;
        mpb.SetColor(ColorId, MulAlpha(p.color * tint, p.alpha));
        p.mr.SetPropertyBlock(mpb);
    }

    private static Color MulAlpha(Color c, float a)
    {
        c.a = Mathf.Clamp01(c.a * a);
        return c;
    }

    //  Mesh builders
    //  All of these produce unit-sized shapes (radius 0.5) centred on the
    //  origin, with WHITE vertex colours whose ALPHA carries the falloff.
    //  Actual colour comes from the property block at draw time, which is what
    //  lets one mesh be shared by every part that wants that silhouette.
    // A soft disc: concentric rings at the given radii with the given alphas.
    private Mesh BuildDisc(int segments, float[] radii, float[] alphas, string meshName)
    {
        segments = Mathf.Max(6, segments);
        int rings = radii.Length;

        var verts = new Vector3[1 + segments * (rings - 1)];
        var cols = new Color[verts.Length];

        verts[0] = Vector3.zero;
        cols[0] = new Color(1f, 1f, 1f, alphas[0]);

        for (int ring = 1; ring < rings; ring++)
        {
            for (int s = 0; s < segments; s++)
            {
                float a = (s / (float)segments) * Mathf.PI * 2f;
                int idx = 1 + (ring - 1) * segments + s;
                verts[idx] = new Vector3(Mathf.Cos(a) * radii[ring], Mathf.Sin(a) * radii[ring], 0f);
                cols[idx] = new Color(1f, 1f, 1f, alphas[ring]);
            }
        }

        var tris = new List<int>(segments * (rings - 1) * 6);

        // Inner fan.
        for (int s = 0; s < segments; s++)
        {
            int a = 1 + s;
            int b = 1 + (s + 1) % segments;
            tris.Add(0); tris.Add(a); tris.Add(b);
        }

        // Outer bands.
        for (int ring = 1; ring < rings - 1; ring++)
        {
            int inner = 1 + (ring - 1) * segments;
            int outer = 1 + ring * segments;
            for (int s = 0; s < segments; s++)
            {
                int s2 = (s + 1) % segments;
                tris.Add(inner + s); tris.Add(outer + s); tris.Add(inner + s2);
                tris.Add(inner + s2); tris.Add(outer + s); tris.Add(outer + s2);
            }
        }

        var m = new Mesh { name = meshName };
        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris.ToArray();
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

    // A soft annulus: transparent at innerR, opaque at the midpoint,
    // transparent again at outerR.
    private Mesh BuildRingMesh(int segments, float innerR, float outerR, string meshName)
    {
        segments = Mathf.Max(8, segments);
        float midR = (innerR + outerR) * 0.5f;
        float[] radii = { innerR, midR, outerR };
        float[] alphas = { 0f, 1f, 0f };

        var verts = new Vector3[segments * 3];
        var cols = new Color[verts.Length];

        for (int ring = 0; ring < 3; ring++)
        {
            for (int s = 0; s < segments; s++)
            {
                float a = (s / (float)segments) * Mathf.PI * 2f;
                int idx = ring * segments + s;
                verts[idx] = new Vector3(Mathf.Cos(a) * radii[ring], Mathf.Sin(a) * radii[ring], 0f);
                cols[idx] = new Color(1f, 1f, 1f, alphas[ring]);
            }
        }

        var tris = new List<int>(segments * 12);
        for (int ring = 0; ring < 2; ring++)
        {
            int inner = ring * segments;
            int outer = (ring + 1) * segments;
            for (int s = 0; s < segments; s++)
            {
                int s2 = (s + 1) % segments;
                tris.Add(inner + s); tris.Add(outer + s); tris.Add(inner + s2);
                tris.Add(inner + s2); tris.Add(outer + s); tris.Add(outer + s2);
            }
        }

        var m = new Mesh { name = meshName };
        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris.ToArray();
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

    // A four-pointed rune shard: bright core, faded tips.
    private Mesh BuildDiamondMesh(string meshName)
    {
        var verts = new Vector3[]
        {
            Vector3.zero,
            new Vector3(0f,  0.5f, 0f),
            new Vector3(0.28f, 0f, 0f),
            new Vector3(0f, -0.5f, 0f),
            new Vector3(-0.28f, 0f, 0f),
        };

        var cols = new Color[]
        {
            new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 0.05f),
            new Color(1f, 1f, 1f, 0.45f),
            new Color(1f, 1f, 1f, 0.05f),
            new Color(1f, 1f, 1f, 0.45f),
        };

        var tris = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1 };

        var m = new Mesh { name = meshName };
        m.vertices = verts;
        m.colors = cols;
        m.triangles = tris;
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.65f, 0.85f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, tetherRange);
    }
#endif
}



