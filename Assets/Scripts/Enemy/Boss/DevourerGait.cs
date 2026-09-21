using System.Collections.Generic;
using UnityEngine;

//  BOSS 4 — "THE DEVOURER" : LEG-PUSH GAIT


[DefaultExecutionOrder(10001)]
[RequireComponent(typeof(Boss4))]
[DisallowMultipleComponent]
public class DevourerGait : MonoBehaviour
{
    //  PUSH CADENCE

    [System.Serializable]
    public class GaitPush
    {
        [Tooltip("Frame of the WALK clip, 0-based, where this leg plants and shoves. " +
                 "Scrub the clip (or use Debug ▸ Log Walk Frames) and pick the frame " +
                 "where the foot is flat on the ground and the body starts to move " +
                 "forward — not the frame where the leg is furthest back.")]
        public int frame = 0;

        [Tooltip("Relative power of this shove. Only ratios matter: the whole set is " +
                 "normalised so average speed stays equal to MoveSpeed. Two entries at " +
                 "1.0 and 0.35 = a strong leg and a weak drag step, i.e. a limp.")]
        [Range(0.05f, 1f)] public float strength = 1f;

        [Tooltip("Sideways bias of this particular shove, as a fraction of speed. " +
                 "Positive pushes the body to its LEFT, negative to its right. A real " +
                 "leg pushes off-centre, so giving two pushes opposite signs is what " +
                 "produces a natural waddle rather than a straight-line pulse.")]
        [Range(-1f, 1f)] public float lateralBias = 0f;
    }

    [Header("Push Cadence")]
    [Tooltip("One entry per footfall in the walk clip. The defaults are read off the " +
             "Devourer's own 40-frame Walk cycle: the leg plants at frame 12 (body at " +
             "minimum height, contact furthest back) and the jaw drags the body " +
             "forward around frame 36.\n\n" +
             "Clear the list entirely to auto-space Auto Pushes Per Stride evenly " +
             "instead — use that only for a DIFFERENT clip.")]
    [SerializeField]
    private List<GaitPush> pushes = new List<GaitPush>
    {
        // Frame 12 — the leg. Body is at its most compressed here and the ground
        // contact has swung furthest back, which is what a loaded leg looks like.
        new GaitPush { frame = 12, strength = 1.00f, lateralBias =  0.20f },

        // Frame 36 — the jaw. The underside is carrying the weight forward through
        // the back third of the cycle, so the boss gets a second, weaker haul rather
        // than coasting for a full 2.2s between shoves. Opposite lateral bias so the
        // two alternate instead of pulsing along one line.
        new GaitPush { frame = 36, strength = 0.45f, lateralBias = -0.15f },
    };

    [Tooltip("Only used when Pushes is empty.")]
    [SerializeField, Range(1, 6)] private int autoPushesPerStride = 2;

    //  THE SHOVE ITSELF

    [Header("Shove")]
    [Tooltip("How much of the motion is gait rather than the old constant slide.\n\n" +
             "0 = original sliding. 1 = fully gait-driven, i.e. the boss is nearly " +
             "stationary between shoves. 0.7 is a heavy, clearly-legged walk that " +
             "still reads as continuous movement.\n\n" +
             "This is also the floor: at 0.7 the slowest point of the coast is 30% " +
             "of MoveSpeed, never zero, so the boss can't look frozen.")]
    [SerializeField, Range(0f, 1f)] private float gaitStrength = 0.8f;

    [Tooltip("Ground friction, in 'e-foldings per stride'. Higher = the shove dies " +
             "faster and the boss spends more of the cycle coasting to a halt, which " +
             "reads as heavier and more effortful. 1 = barely any decay (almost the " +
             "old slide), 5 = it stops dead between steps.\n\n" +
             "Note this does NOT change average speed — it changes the shape only. " +
             "The normalisation divides it straight back out.")]
    [SerializeField, Range(0.5f, 6f)] private float decayPerStride = 3.5f;

    [Tooltip("Extra kick given when the boss starts moving from a standstill, as a " +
             "fraction of a normal shove. Without it the first step waits for the next " +
             "plant frame and the boss looks like it hesitated.")]
    [SerializeField, Range(0f, 2f)] private float standingStartKick = 0.8f;

    [Header("Weight Transfer")]
    [Tooltip("Side-to-side weave as a fraction of speed — the body swinging over the " +
             "planted leg. Small: 0.1 is a readable lumber, 0.3 is drunk. Set 0 for a " +
             "dead-straight path.")]
    [SerializeField, Range(0f, 0.5f)] private float swayFraction = 0.12f;

    [Tooltip("Where in the stride the weave peaks. Shift this until the body leans " +
             "over the leg that is actually planted instead of the one in the air.")]
    [SerializeField, Range(0f, 1f)] private float swayPhaseOffset = 0f;

    [Header("Turning")]
    [Tooltip("ON: the boss can only really change heading while a foot is planted, " +
             "and barely steers mid-coast. This is the single biggest contributor to " +
             "'it has weight' — a sliding enemy turns on the spot, a legged one has to " +
             "commit to where the last push aimed it.\n\n" +
             "Steering itself is untouched: EnemyController and Boss4's obstacle " +
             "avoidance still choose the goal, we only rate-limit how fast the body " +
             "swings onto it.")]
    [SerializeField] private bool turnOnlyWhilePushing = true;

    [Tooltip("Maximum heading change per STRIDE, at the peak of a shove — not per " +
             "second. A legged creature can only redirect itself as often as it plants " +
             "a foot, so expressing it this way means a slower boss also turns more " +
             "ponderously, automatically, with no second value to keep in sync.\n\n" +
             "Lower = wider, more committed arcs. Too low and it will orbit a target it " +
             "can't turn tightly enough to reach, so watch it hunt a minion after " +
             "changing this.")]
    [SerializeField, Range(60f, 1200f)] private float turnDegreesPerStride = 420f;

    [Tooltip("Turn rate mid-coast, as a fraction of the above. 0.15 = almost locked in.")]
    [SerializeField, Range(0.05f, 1f)] private float coastTurnFraction = 0.2f;

    //  BODY

    [Header("Body — squash, stretch, lean")]
    [Tooltip("Master switch for the visual half. The physics half above works with " +
             "this off if you only want the motion, not the animation on top of it.")]
    [SerializeField] private bool animateBody = true;

    [Tooltip("How far the body compresses in the moment BEFORE the push, as a " +
             "fraction of height. This is the anticipation — the reason a push reads " +
             "as deliberate rather than as a sudden speed change.\n\n" +
             "Kept low on purpose: the sprite pivot is centred, so half of this shows " +
             "up as the feet leaving the floor. Above ~0.06 the boss starts to bounce.")]
    [SerializeField, Range(0f, 0.3f)] private float loadCrouch = 0.035f;

    [Tooltip("Seconds of crouch before the plant frame. Roughly 2–4 walk frames.")]
    [SerializeField, Range(0.02f, 0.5f)] private float loadTime = 0.16f;

    [Tooltip("How far the body extends at the instant of the shove.")]
    [SerializeField, Range(0f, 0.4f)] private float pushStretch = 0.05f;

    [Tooltip("How fast the stretch springs back. Higher = snappier, more muscular; " +
             "lower = a long lazy heave.")]
    [SerializeField, Range(2f, 30f)] private float springDamping = 9f;

    [Tooltip("Wobbles per second as it settles. 2–4 reads as flesh and weight; above " +
             "~8 it starts to look like jelly.")]
    [SerializeField, Range(0.5f, 12f)] private float springFrequency = 3.2f;

    [Tooltip("Degrees the body leans INTO each shove. The lean is applied on top of " +
             "whatever EnemyAnimationController's walk-lean already does, and is " +
             "mirrored automatically when the boss faces left.")]
    [SerializeField, Range(0f, 15f)] private float pushLeanDegrees = 4f;

    //  FOOTFALLS

    [Header("Footfall Impact")]
    [Tooltip("Dust and grit thrown BACKWARD out of each plant — the visible proof " +
             "that the boss is pushing against the ground rather than floating over it.")]
    [SerializeField] private bool footfallDust = true;

    [Tooltip("Puffs per plant. 2–3 is plenty; this fires every stride for the whole " +
             "fight, so it is worth keeping cheap.")]
    [SerializeField, Range(1, 6)] private int puffsPerStep = 3;

    [Tooltip("Grit chunks flung back off the foot. 0 disables them.")]
    [SerializeField, Range(0, 6)] private int gritPerStep = 2;

    [SerializeField] private Color dustColor = new Color(0.50f, 0.44f, 0.38f, 0.45f);

    [Tooltip("Where the ground line sits inside the sprite, as a fraction of its " +
             "height up from the bottom edge. 0 = the very lowest pixel. The Devourer's " +
             "lowest pixels ARE its contact points (jaw and leg both touch), so a hair " +
             "above zero puts the dust right at the seam instead of under it.")]
    [SerializeField, Range(0f, 0.25f)] private float footLineOffset = 0.02f;

    [Tooltip("How wide the dust spreads along that contact line, as a fraction of the " +
             "sprite's half-width. The whole underside of this boss touches down, so a " +
             "single point-puff reads as a much smaller creature than it is.")]
    [SerializeField, Range(0.1f, 1f)] private float contactSpread = 0.55f;

    [Tooltip("Shifts the dust toward the BACK of the body (away from travel), as a " +
             "fraction of half-width. The leg is behind the jaw, so a small positive " +
             "value puts the heaviest dust where the shove comes from.")]
    [SerializeField, Range(-0.5f, 0.5f)] private float footBackBias = 0.2f;

    [Tooltip("Turns the existing distance-based DevourerDustEmitter down when this " +
             "component is running, so dust comes from FOOTFALLS rather than from a " +
             "steady trail. Leave ON — a continuous haze under a stepping creature is " +
             "exactly the cue that made it read as hovering.")]
    [SerializeField] private bool suppressContinuousDust = true;

    [Tooltip("Camera shake amplitude per plant. Tiny — this fires every stride. " +
             "0 disables. It scales with how hard the boss is actually pushing, so a " +
             "Brute-grown, enraged Devourer thumps harder for free.")]
    [SerializeField, Range(0f, 0.15f)] private float stepShakeAmplitude = 0.035f;

    [SerializeField, Range(0.02f, 0.3f)] private float stepShakeDuration = 0.07f;

    [Tooltip("Beyond this distance from the camera a step doesn't shake it. Stops an " +
             "off-screen boss from rattling the whole screen for the entire fight.")]
    [SerializeField] private float stepShakeMaxDistance = 18f;

    [Tooltip("Optional footstep event. Leave empty for silence.")]
    [SerializeField] private FMODUnity.EventReference footstepSound;

    //  STRIDE / SPEED MATCH

    [Header("Stride")]
    [Tooltip("THE NATURALNESS SWITCH. ON: the walk clip is advanced by how far the " +
             "boss has actually TRAVELLED, not by a timer. A foot planted on the " +
             "ground then stays planted — the clip physically cannot run faster or " +
             "slower than the body moves, so foot-slide is impossible at any speed.\n\n" +
             "This is what makes a slow walk read as slow rather than as the same walk " +
             "played at the wrong speed. It also means you tune speed in ONE place " +
             "(Boss4 ▸ Boss Move Speed) and the cadence follows automatically.\n\n" +
             "While it is on, Boss4's Walk Frame Time no longer controls the walk — " +
             "Stride Length does. The bite clip is untouched.")]
    [SerializeField] private bool driveWalkFromDistance = true;

    [Tooltip("World units covered by ONE full 40-frame cycle. This is the single knob " +
             "that decides how the walk reads:\n\n" +
             "  too SHORT → the boss scurries, tiny frantic steps\n" +
             "  too LONG  → it glides, feet dragging across the ground\n\n" +
             "Tune it by watching one foot: pick a value where it stays stuck to the " +
             "same patch of ground through the whole stance. ~1.2x the body width is " +
             "usually close. Leave at 0 to auto-derive from the sprite width.")]
    [SerializeField] private float strideLength = 1.6f;

    [Tooltip("How fast the clip still ticks over when the boss is stationary or " +
             "pressed against something, as a fraction of normal cadence. Zero would " +
             "freeze it into a statue the instant it stopped; a slow creep reads as a " +
             "living thing shifting its weight. Also covers the 'walking into a wall' " +
             "case, where actual distance travelled is zero.")]
    [SerializeField, Range(0f, 0.5f)] private float idleCreepFraction = 0.12f;

    [Tooltip("Per-step randomness in shove strength and timing. Real legs are not a " +
             "metronome, and an identical push every single stride is the thing that " +
             "makes even good procedural motion read as machinery. 0.1-0.15 is enough " +
             "to break the pattern without looking drunk.")]
    [SerializeField, Range(0f, 0.35f)] private float strideVariation = 0.12f;

    [Tooltip("Fallback only, used when Drive Walk From Distance is OFF: re-times the " +
             "clip when MoveSpeed changes by more than 5% and restarts the walk loop.")]
    [SerializeField] private bool matchStrideToSpeed = true;

    [Header("Debug")]
    [Tooltip("Logs the walk frame index each time it changes. Use it once to find " +
             "your plant frames, then turn it off.")]
    [SerializeField] private bool logWalkFrames = false;

    //  RUNTIME

    private EnemyStats stats;
    private EnemyController controller;
    private EnemyAnimationController anim;
    private SpriteRenderer sr;
    private Rigidbody2D rb;
    private SmoothSpriteFlip flip;
    private DevourerDustEmitter continuousDust;

    // Frame → index lookup into the walk range of the flat sprite array. Rebuilt if
    // the controller ever swaps its sprite set (SetSpritesDirectly).
    private readonly Dictionary<Sprite, int> walkLookup = new Dictionary<Sprite, int>();
    private Sprite[] lookupSource;
    private int walkCount;
    private int walkStartIndex;

    // Distance-driven stride bookkeeping.
    private Vector3 lastSamplePos;
    private bool sampleSeeded;
    private float[] pushJitter;

    private int lastFrameIndex = -1;
    private float timeInFrame;
    private float phase;          // 0..1 through the stride

    // Phase advances in Update, pushes are consumed in FixedUpdate, and the two do
    // NOT run at the same rate. So we keep a cursor rather than a "previous value":
    // Update never rewinds it, FixedUpdate moves it forward once it has processed the
    // interval. At 200fps/50Hz physics three quarters of the footfalls were otherwise
    // silently dropped; at 30fps they fired twice.
    private float unconsumedFrom;
    private bool phaseValid;

    private float gait;           // the raw push/decay accumulator
    private float gaitOutput = 1f; // normalised, mean 1.0
    private Vector2 heading;
    private Vector2 lastOutputVelocity;
    private bool wasMoving;

    private float lastPushTime = -99f;
    private float lastPushStrength = 1f;
    private float visualWeight;   // eases the body animation in/out

    private Quaternion lastOutputRotation = Quaternion.identity;
    private Quaternion rotationBase = Quaternion.identity;

    // Same trick as the rotation base, for scale: if localScale.y still equals what we
    // wrote last frame, nobody re-asserted it and `scaleBaseY` is still the true base.
    // If it changed, Boss4 (or the Brute growth, or a lunge crouch) wrote a new base
    // and we adopt it. That keeps us composable with all three instead of accumulating
    // our own squash frame after frame.
    private float lastOutputScaleY = float.NegativeInfinity;
    private float scaleBaseY = 1f;

    private float syncedSpeedReference = -1f;   // last speed we re-timed the clip for
    private float baseSpeedReference = -1f;     // the speed baseWalkFrameTime was authored against
    private float baseWalkFrameTime;

    /// The live speed multiplier the gait is applying, averaged 1.0 over a stride.
    /// Exposed so other systems (screen shake, footstep audio, a debug overlay) can read
    /// how hard the boss is currently shoving without recomputing any of this.
    public float GaitSpeedMultiplier => gaitOutput;

    /// Seconds since the last leg plant. -1 until the boss has taken a step.
    public float TimeSinceFootfall => lastPushTime < 0f ? -1f : Time.time - lastPushTime;

    //  LIFECYCLE

    private void Awake()
    {
        stats = GetComponent<EnemyStats>();
        controller = GetComponent<EnemyController>();
        anim = GetComponent<EnemyAnimationController>();
        sr = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        flip = GetComponent<SmoothSpriteFlip>();
    }

    private void Start()
    {
        // Boss4.Start builds the dust emitter, so we can only find it after it has run.
        // Our execution order guarantees that, but the null check keeps this honest if
        // the boss is ever built a different way.
        continuousDust = GetComponent<DevourerDustEmitter>();
        if (continuousDust != null && suppressContinuousDust)
            continuousDust.distancePerPuff = 999f;   // effectively off; footfalls own it now

        if (stats != null && stats.enemyData != null)
        {
            baseWalkFrameTime = stats.enemyData.GetAnimSpeed(stats.enemyData.idle);
            baseSpeedReference = stats.MoveSpeed;
            syncedSpeedReference = stats.MoveSpeed;
        }

        if (pushes == null || pushes.Count == 0) BuildAutoPushes();
    }

    private void OnDisable()
    {
        // Hand the body back exactly as we found it. Without this, disabling the
        // component mid-stride would leave the boss permanently squashed.
        RestoreBody();
    }

    private void BuildAutoPushes()
    {
        pushes = new List<GaitPush>();
        int count = Mathf.Max(1, autoPushesPerStride);
        int frames = Mathf.Max(1, WalkFrameCount);
        for (int i = 0; i < count; i++)
        {
            pushes.Add(new GaitPush
            {
                frame = Mathf.RoundToInt(frames * (i / (float)count)),
                strength = 1f,
                // Alternate the off-centre bias so the auto gait already waddles
                // instead of pulsing in a straight line.
                lateralBias = (i % 2 == 0) ? 0.25f : -0.25f
            });
        }
    }

    //  PHASE — driven by the sprite that is actually on screen

    private int WalkFrameCount =>
        (stats != null && stats.enemyData != null) ? Mathf.Max(1, stats.enemyData.idle.frameCount) : 1;

    // World units one full walk cycle covers. Auto-derived from the sprite when the
    // field is left at 0, so a resized or Brute-grown boss still lands on a sane value.
    private float StrideLengthWorld
    {
        get
        {
            if (strideLength > 0.01f) return strideLength;
            if (sr != null && sr.sprite != null) return Mathf.Max(0.2f, sr.bounds.size.x * 0.45f);
            return 1.6f;
        }
    }

    // Seconds per stride. When the walk is distance-driven this is EMERGENT — stride
    // length divided by how fast the boss is travelling — which is why halving
    // MoveSpeed automatically halves the cadence instead of leaving the same footfalls
    // playing over slower movement. Everything timed off a stride (friction decay, the
    // squash spring, the turn rate) therefore slows down with it, for free.
    private float StrideDuration
    {
        get
        {
            if (driveWalkFromDistance && stats != null)
            {
                float sp = Mathf.Max(0.05f, stats.MoveSpeed);
                return Mathf.Clamp(StrideLengthWorld / sp, 0.25f, 10f);
            }

            if (stats == null || stats.enemyData == null) return 1f;
            var d = stats.enemyData;
            return Mathf.Max(0.05f, d.GetAnimSpeed(d.idle) * Mathf.Max(1, d.idle.frameCount));
        }
    }

    private void EnsureLookup()
    {
        if (anim == null || stats == null || stats.enemyData == null) return;

        Sprite[] src = anim.Sprites;
        if (src == null || src.Length == 0) return;
        if (ReferenceEquals(src, lookupSource) && walkLookup.Count > 0) return;

        lookupSource = src;
        walkLookup.Clear();

        int start = stats.enemyData.idle.startFrame;
        walkStartIndex = start;
        walkCount = Mathf.Max(1, stats.enemyData.idle.frameCount);

        for (int i = 0; i < walkCount; i++)
        {
            int idx = start + i;
            if (idx < 0 || idx >= src.Length || src[idx] == null) continue;
            // First occurrence wins: a clip that reuses a sprite would otherwise
            // report the later index and jump the phase backwards.
            if (!walkLookup.ContainsKey(src[idx])) walkLookup[src[idx]] = i;
        }
    }

    // Advances `phase` from the live sprite where possible, and from a clock of the
    // same period otherwise (during the bite clip, or if the art was injected by a
    // system we can't see into).
    private void TickPhase(float dt)
    {
        EnsureLookup();

        if (walkCount <= 0) walkCount = WalkFrameCount;

        if (driveWalkFromDistance && CanDriveWalk())
        {
            TickPhaseFromDistance(dt);
            return;
        }

        float frameTime = Mathf.Max(0.005f, StrideDuration / Mathf.Max(1, walkCount));

        int index = -1;
        if (sr != null && sr.sprite != null && walkLookup.TryGetValue(sr.sprite, out int found))
            index = found;

        if (index >= 0)
        {
            if (index != lastFrameIndex)
            {
                if (logWalkFrames)
                    Debug.Log($"[DevourerGait] walk frame {index} / {walkCount}");

                lastFrameIndex = index;
                timeInFrame = 0f;
            }
            else
            {
                timeInFrame += dt;
            }

            // Sub-frame interpolation, so the push lands on the exact instant the
            // frame is reached rather than being quantised to whole frames.
            float within = Mathf.Clamp01(timeInFrame / frameTime);
            phase = (index + within) / walkCount;
            phaseValid = true;
        }
        else
        {
            // Free-run at the same period. Keeps the cadence alive through the bite so
            // the boss doesn't restart its stride from a dead stop on every attack.
            phase = Mathf.Repeat(phase + dt / StrideDuration, 1f);
            lastFrameIndex = -1;
        }
    }

    private bool CanDriveWalk()
    {
        if (sr == null || anim == null || lookupSource == null || walkCount <= 0) return false;
        if (anim.IsDying || anim.IsAnimationFrozen) return false;
        if (!anim.IsPlayingIdleLoop) return false;                  // bite / death owns the sprite
        if (controller != null && controller.IsAttacking) return false;
        return true;
    }

    // Advances the stride by GROUND COVERED rather than by a clock.
    //
    // This is what removes foot-slide: the clip and the body are driven by the same
    // number, so a planted foot cannot drift. It also means the animation pulses with
    // the gait — during a shove the clip races, during the coast it nearly holds,
    // which is exactly what a leg pushing and then riding out the glide looks like.
    private void TickPhaseFromDistance(float dt)
    {
        if (!sampleSeeded) { lastSamplePos = transform.position; sampleSeeded = true; }

        float moved = Vector2.Distance(transform.position, lastSamplePos);
        lastSamplePos = transform.position;

        // Teleport / respawn guard, same reasoning as DevourerDustEmitter's: a single
        // huge jump must not spin the stride through ten cycles and fire every push.
        if (moved > 3f) moved = 0f;

        // Floor it with a slow creep so a stopped — or wall-jammed — boss keeps
        // shifting its weight instead of freezing mid-step.
        float intended = (stats != null ? stats.MoveSpeed : 1f) * dt;
        moved = Mathf.Max(moved, intended * idleCreepFraction);

        phase = Mathf.Repeat(phase + moved / Mathf.Max(0.05f, StrideLengthWorld), 1f);
        phaseValid = true;
    }

    // Writes the walk frame ourselves, from the stride phase.
    //
    // EnemyAnimationController's Utilities.AnimateSprite coroutine is still running and
    // still assigning sprites on its own timer; coroutines resume between Update and
    // LateUpdate, so writing here simply wins every frame. That is deliberately gentler
    // than stopping its coroutine: nothing about its state machine changes, so the bite,
    // the death clip, parry freezes and Boss4's own facing logic all keep working
    // untouched, and turning this component off hands the walk straight back.
    private void DriveWalkSprite()
    {
        if (!driveWalkFromDistance || !CanDriveWalk()) return;

        int idx = Mathf.Clamp(Mathf.FloorToInt(phase * walkCount), 0, walkCount - 1);
        int flat = walkStartIndex + idx;
        if (flat < 0 || flat >= lookupSource.Length) return;

        Sprite frame = lookupSource[flat];
        if (frame != null && sr.sprite != frame)
        {
            sr.sprite = frame;
            if (logWalkFrames) Debug.Log($"[DevourerGait] walk frame {idx} / {walkCount}");
        }
    }

    private bool CrossedPush(float from, float to, float pushPhase)
    {
        if (Mathf.Approximately(from, to)) return false;
        if (to >= from) return pushPhase > from && pushPhase <= to;
        return pushPhase > from || pushPhase <= to;   // wrapped past 1.0
    }

    private void EnsureJitter()
    {
        if (pushJitter == null || pushJitter.Length != pushes.Count)
            pushJitter = new float[pushes.Count];
    }

    private float TotalKick
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < pushes.Count; i++) sum += Mathf.Max(0.01f, pushes[i].strength);
            return Mathf.Max(0.01f, sum);
        }
    }

    //  UPDATE — phase, stride matching, visual weight

    private void Update()
    {
        float dt = Time.deltaTime;
        TickPhase(dt);

        // The two stride systems are alternatives, never both: re-timing the clip on
        // top of driving it by distance would fight itself.
        if (matchStrideToSpeed && !driveWalkFromDistance) TickStrideMatch();

        // Ease the body animation in and out so a boss that stops mid-stride settles
        // instead of snapping to its resting shape.
        bool driving = GaitActive && wasMoving;
        visualWeight = Mathf.MoveTowards(visualWeight, driving ? 1f : 0f, dt * 4f);
    }

    // Keeps footfall cadence honest when MoveSpeed changes (enrage, Feathers trait).
    // Utilities.AnimateSprite captures its frame time when the coroutine starts, so
    // the only way to apply a new speed is to restart the loop — which is safe here
    // because we only do it when the walk loop is the clip currently playing.
    private void TickStrideMatch()
    {
        if (stats == null || stats.enemyData == null || anim == null) return;
        if (syncedSpeedReference <= 0f || baseWalkFrameTime <= 0f) return;

        float speed = stats.MoveSpeed;
        if (speed <= 0.01f) return;
        if (Mathf.Abs(speed - syncedSpeedReference) / syncedSpeedReference < 0.05f) return;

        if (!anim.IsPlayingIdleLoop || anim.IsAnimationFrozen) return;
        if (controller != null && controller.IsAttacking) return;

        // Faster boss → shorter time per frame → same stride LENGTH, more strides.
        // Scaled from the ORIGINAL authored pair, never from the last synced one: chaining
        // ratios drifts, so a speed buff followed by its removal would not return the
        // clip to the frame time you typed into Boss4.
        float scaled = baseWalkFrameTime * (baseSpeedReference / speed);

        var idle = stats.enemyData.idle;
        idle.speedOverride = Mathf.Clamp(scaled, 0.01f, 0.5f);
        stats.enemyData.idle = idle;

        syncedSpeedReference = speed;
        anim.PlayLoopingIdleAnimation();
    }

    //  FIXED UPDATE — the actual locomotion
    //
    //  Ordering, which is the whole reason this works:
    //      EnemyController (order 0)     writes velocity = dir * MoveSpeed
    //      Boss4           (order 10000) re-steers that velocity around obstacles
    //      this            (order 10001) reshapes its MAGNITUDE into a gait
    //  We never pick the direction. Everything upstream keeps deciding where to go.

    private bool GaitActive
    {
        get
        {
            if (rb == null || controller == null || stats == null) return false;
            if (stats.currentHealth <= 0f) return false;
            if (anim != null && anim.IsDying) return false;
            // The bite lunge and the devour pounce both take ExternalMovementControl
            // and drive position directly. Touching velocity during either would fight
            // a scripted move — and those already have their own weight to them.
            if (controller.ExternalMovementControl) return false;
            if (controller.IsBeingGrappled()) return false;
            return true;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Idle / scripted-move steps still keep the cursor current. Letting it fall
        // behind while the boss stands in attack range would make the whole backlog of
        // plant frames fire on the single step it starts walking again.
        if (!GaitActive) { gait = 0f; wasMoving = false; unconsumedFrom = phase; return; }

        Vector2 v = rb.linearVelocity;

        // Did anything upstream actually write velocity this step? If the vector is
        // still bit-for-bit our own output, the controller returned early (knockback,
        // attack-range stop, game over) and re-shaping our own result would compound
        // into a feedback loop.
        if ((v - lastOutputVelocity).sqrMagnitude < 1e-8f && wasMoving) return;

        float baseSpeed = v.magnitude;
        float moveSpeed = Mathf.Max(0.01f, stats.MoveSpeed);

        // Standing still (in attack range, frozen, stunned): let the gait bleed off so
        // the next departure starts with a real push rather than mid-coast.
        if (baseSpeed < 0.05f)
        {
            gait = 0f;
            wasMoving = false;
            heading = Vector2.zero;
            lastOutputVelocity = v;
            unconsumedFrom = phase;
            return;
        }

        // Anything much faster than MoveSpeed is not locomotion — it's knockback or a
        // grapple pull. Leave those exactly as they are; they read as force already.
        if (baseSpeed > moveSpeed * 1.15f) { lastOutputVelocity = v; return; }

        Vector2 desired = v / baseSpeed;

        //  1. Integrate the push / friction model.
        float decayRate = decayPerStride / StrideDuration;        // e-foldings per second
        gait *= Mathf.Exp(-decayRate * dt);

        if (!wasMoving && standingStartKick > 0f)
        {
            // Step off from a standstill instead of waiting for the next plant frame.
            gait += standingStartKick * (TotalKick / Mathf.Max(1, pushes.Count));
            FireFootfall(desired, standingStartKick * 0.7f);
        }

        float lateral = 0f;
        float from = unconsumedFrom;
        unconsumedFrom = phase;          // consume the interval exactly once

        EnsureJitter();

        for (int i = 0; i < pushes.Count; i++)
        {
            var p = pushes[i];
            float pushPhase = Mathf.Repeat(p.frame / (float)Mathf.Max(1, walkCount), 1f);
            if (!phaseValid) pushPhase = i / (float)Mathf.Max(1, pushes.Count);

            // Timing jitter, re-rolled after every shove. A push that lands on the exact
            // same phase every single stride is what reads as a machine rather than a
            // creature; a fraction of a frame of slop is enough to break that without
            // ever desyncing the shove from the plant frame.
            pushPhase = Mathf.Repeat(pushPhase + pushJitter[i], 1f);

            if (CrossedPush(from, phase, pushPhase))
            {
                // Strength varies too — some steps are heavier than others. The variation
                // is symmetric, so the stride average (and therefore MoveSpeed) is
                // unchanged over any reasonable number of steps.
                float k = Mathf.Max(0.01f, p.strength);
                if (strideVariation > 0f)
                    k *= 1f + Random.Range(-strideVariation, strideVariation);

                gait += k;
                lastPushTime = Time.time;
                lastPushStrength = k;
                lateral += p.lateralBias;
                FireFootfall(desired, k);

                pushJitter[i] = Random.Range(-strideVariation, strideVariation) * 0.02f;
            }
        }

        //  2. Normalise. mean(gait) over a stride == TotalKick / decayPerStride, so
        //     this multiplier averages exactly 1.0 and MoveSpeed still means MoveSpeed.
        float normalised = gait * decayPerStride / TotalKick;
        gaitOutput = Mathf.Lerp(1f, normalised, gaitStrength);

        //  3. Turning. Commit to the heading the last shove aimed us at; steer hardest
        //     while a foot is planted.
        if (heading.sqrMagnitude < 0.0001f) heading = desired;

        float turnScale = turnOnlyWhilePushing
            ? Mathf.Lerp(coastTurnFraction, 1f, Mathf.Clamp01(gaitOutput))
            : 1f;
        float turnPerSecond = turnDegreesPerStride / StrideDuration;
        float maxTurn = turnPerSecond * turnScale * Mathf.Deg2Rad * dt;
        heading = Vector3.RotateTowards(heading, desired, maxTurn, 0f);
        heading.Normalize();

        //  4. Weight transfer: the body swings over whichever leg is planted, plus a
        //     one-off shove off-centre on the frame a leg actually pushes.
        Vector2 perp = new Vector2(-heading.y, heading.x);
        float sway = Mathf.Sin((phase + swayPhaseOffset) * Mathf.PI * 2f) * swayFraction;
        sway += lateral * 0.5f;
        // Scaled by how hard we're moving, so a nearly-stopped boss doesn't crab
        // sideways with no forward motion to justify it.
        sway *= Mathf.Clamp01(0.35f + 0.65f * gaitOutput);

        Vector2 result = heading * (baseSpeed * gaitOutput) + perp * (moveSpeed * sway);

        rb.linearVelocity = result;
        lastOutputVelocity = result;
        wasMoving = true;
    }

    //  LATE UPDATE — squash, stretch, lean
    //
    //  Runs after Boss4.LateUpdate (order 10000), which asserts localScale.y from its
    //  own displayScale every frame to survive YSortEntity. We therefore read the Y it
    //  just wrote as our base and multiply on top — the bite lunge's crouch, the Brute
    //  trait's growth and our gait all compose instead of overwriting each other.
    //
    //  X IS NEVER TOUCHED. SmoothSpriteFlip animates X down through zero to mirror the
    //  boss, and writing X here would break the flip exactly as Boss4's own comment
    //  warns. Y-only squash still reads correctly: shorter = loading, taller = pushing.

    private void LateUpdate()
    {
        // Sprite first, then the body shape that goes on top of it.
        DriveWalkSprite();

        if (!animateBody) return;
        if (stats != null && stats.currentHealth <= 0f) return;
        if (anim != null && anim.IsDying) return;

        // Adopt the base scale before writing: if localScale.y is still exactly what we
        // left, nobody re-asserted it this frame and the base is unchanged; if it moved,
        // Boss4 / the Brute growth / a lunge crouch wrote a new one and that is now the
        // base we multiply on top of.
        var current = transform.localScale;
        if (!Mathf.Approximately(current.y, lastOutputScaleY)) scaleBaseY = current.y;

        float weight = visualWeight;
        if (weight <= 0.001f)
        {
            transform.localScale = new Vector3(current.x, scaleBaseY, current.z);
            lastOutputScaleY = scaleBaseY;
            SyncRotationBase(0f);
            return;
        }

        //  Vertical: anticipation crouch, then a damped spring off the push.
        float sincePush = Time.time - lastPushTime;
        float spring = pushStretch * lastPushStrength
                     * Mathf.Exp(-springDamping * sincePush)
                     * Mathf.Cos(springFrequency * Mathf.PI * 2f * sincePush);

        float toNext = TimeToNextPush();
        float load = loadCrouch * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(toNext / Mathf.Max(0.01f, loadTime))));

        float yMul = 1f + (spring - load) * weight;
        yMul = Mathf.Clamp(yMul, 0.75f, 1.3f);

        float outY = scaleBaseY * yMul;
        transform.localScale = new Vector3(current.x, outY, current.z);
        lastOutputScaleY = outY;

        //  Lean: into the shove, mirrored with facing.
        float lean = pushLeanDegrees * Mathf.Clamp(spring / Mathf.Max(0.0001f, pushStretch), -1f, 1f) * weight;
        if (flip != null && flip.IsFacingLeft) lean = -lean;
        SyncRotationBase(lean);
    }

    // Applies `lean` on top of whatever rotation the animation controller wrote this
    // frame, without accumulating when it DIDN'T write one (which happens while Boss4
    // drives its own facing during a hunt and turns orientation driving off).
    private void SyncRotationBase(float lean)
    {
        if (Quaternion.Angle(transform.rotation, lastOutputRotation) > 0.01f)
            rotationBase = transform.rotation;   // somebody else wrote it: that's the new base

        Quaternion result = rotationBase * Quaternion.Euler(0f, 0f, lean);
        transform.rotation = result;
        lastOutputRotation = result;
    }

    private float TimeToNextPush()
    {
        if (pushes.Count == 0) return 999f;

        float best = 999f;
        float stride = StrideDuration;
        for (int i = 0; i < pushes.Count; i++)
        {
            float pushPhase = phaseValid
                ? Mathf.Repeat(pushes[i].frame / (float)Mathf.Max(1, walkCount), 1f)
                : i / (float)pushes.Count;

            float d = Mathf.Repeat(pushPhase - phase, 1f) * stride;
            if (d < best) best = d;
        }
        return best;
    }

    private void RestoreBody()
    {
        var ls = transform.localScale;
        if (Mathf.Approximately(ls.y, lastOutputScaleY) && scaleBaseY > 0.01f)
            transform.localScale = new Vector3(ls.x, scaleBaseY, ls.z);

        lastOutputScaleY = float.NegativeInfinity;
        transform.rotation = rotationBase;
        lastOutputRotation = rotationBase;
    }

    //  FOOTFALL IMPACT

    private void FireFootfall(Vector2 travelDir, float strength)
    {
        if (!isActiveAndEnabled) return;

        // The ground line comes from the SPRITE, not the collider.
        //
        // The collider is authored for gameplay reach and on this boss is much
        // smaller than the art (that mismatch is the exact bug Boss4's
        // useColliderFromPrefab comment describes), so deriving the foot height from
        // it put the dust halfway up the body. sr.bounds is the live world AABB of
        // the frame currently on screen, so it tracks sizeMultiplier, the Brute
        // growth and the gait squash for free.
        float groundY, halfWidth;
        if (sr != null && sr.sprite != null)
        {
            var b = sr.bounds;
            groundY = b.min.y + b.size.y * footLineOffset;
            halfWidth = b.size.x * 0.5f;
        }
        else
        {
            float r = BodyRadius();
            groundY = transform.position.y - r * 0.55f;
            halfWidth = r;
        }

        // The whole underside touches down — jaw at the front, leg at the back — so
        // the dust is spread along the contact line instead of puffed from a point,
        // biased toward whichever end is doing the pushing.
        float spread = halfWidth * contactSpread;
        Vector3 centre = new Vector3(
            transform.position.x - travelDir.x * halfWidth * footBackBias, groundY, 0f);

        float scale = Mathf.Max(0.15f, halfWidth);
        string layer = sr != null ? sr.sortingLayerName : "Default";

        if (footfallDust)
        {
            for (int i = 0; i < puffsPerStep; i++)
            {
                Vector3 at = centre + new Vector3(Random.Range(-spread, spread),
                                                  Random.Range(-0.04f, 0.06f) * scale, 0f);
                var go = new GameObject("Devourer_StepDust");
                go.transform.position = at;

                var dsr = DevourerSprites.NewSprite(go.transform, "Sprite", DevourerSprites.SoftDisc,
                                                    layer, DevourerFXOrder.GroundAt(at.y), dustColor);

                // Thrown BACKWARD out from under the body. A puff that drifts straight
                // up reads as landing; one that sprays behind reads as pushing off,
                // which is the whole point of this component.
                Vector2 back = -travelDir * Random.Range(0.5f, 1.4f) * strength;
                go.AddComponent<DevourerFadeSprite>().Play(
                    dsr, Random.Range(0.4f, 0.7f),
                    0.35f * scale * strength, 1.1f * scale * strength,
                    drift: new Vector2(back.x, back.y * 0.4f + Random.Range(0.1f, 0.35f)));
            }
        }

        for (int i = 0; i < gritPerStep; i++)
        {
            Vector3 at = centre + new Vector3(Random.Range(-spread, spread) * 0.7f, 0f, 0f);
            var gGO = new GameObject("Devourer_StepGrit");
            gGO.transform.position = at;

            var gsr = DevourerSprites.NewSprite(gGO.transform, "Sprite", Boss2VFXSprites.GetRockChunk(),
                                                layer, DevourerFXOrder.GroundAt(at.y) + 1,
                                                new Color(dustColor.r * 0.8f, dustColor.g * 0.8f,
                                                          dustColor.b * 0.8f, 0.9f));

            Vector2 kick = -travelDir * Random.Range(1.2f, 2.6f) * strength;
            gGO.AddComponent<DevourerFadeSprite>().Play(
                gsr, Random.Range(0.3f, 0.6f),
                0.14f * scale, 0.09f * scale,
                drift: new Vector2(kick.x, Mathf.Abs(kick.y) * 0.3f + Random.Range(0.8f, 1.8f)),
                spinDegPerSec: Random.Range(-500f, 500f));
        }

        if (stepShakeAmplitude > 0f && CameraShake.Instance != null && NearCamera())
            CameraShake.Instance.Shake(stepShakeAmplitude * strength, stepShakeDuration);

        PlayFootstep(centre);
    }

    private bool NearCamera()
    {
        var cam = Camera.main;
        if (cam == null) return true;   // no camera to reason about: don't silently drop the cue
        return Vector2.Distance(cam.transform.position, transform.position) <= stepShakeMaxDistance;
    }

    private void PlayFootstep(Vector3 at)
    {
        if (footstepSound.IsNull) return;
        if (AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShot(footstepSound, at);
    }

    // Live body radius, so a Brute-grown Devourer kicks up proportionally bigger dust
    // without any extra wiring — the same approach Boss4.BodyRadius takes.
    private float BodyRadius()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col != null) return Mathf.Max(0.1f, col.radius * Mathf.Abs(transform.lossyScale.x));
        return Mathf.Max(0.1f, Mathf.Abs(transform.lossyScale.x));
    }
}


