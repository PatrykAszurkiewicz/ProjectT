using System.Collections.Generic;
using UnityEngine;

// PROCEDURAL PLAYER FX — breathing, walk bounce, and a swung attack.
//
// The swing's ARC is the hand-drawn image sequence in Assets/Art/Player/BladeSlash,
// played by the "BLADE SLASH" section further down. It replaced a procedurally
// generated ribbon; the body flourish and the cape echoes are unchanged.
//
// The arc is NOT fired from TriggerAttack, because that runs on every click —
// including the ones a melee weapon swallows while it is on cooldown. Weapon.cs calls
// TriggerBladeSlash() from the melee branch of ExecuteWeaponAttack instead, i.e. only
// once a swing has actually passed the cooldown and stamina checks, and it passes that
// weapon's attackCooldown as the window so the arc lasts exactly as long as the
// hitbox does. Which weapons draw an arc is a per-asset flag: WeaponData.useBladeSlashFx.
//
// The frames are read from that folder in the EDITOR and the resulting Sprite
// references are serialised onto the prefab — same model as PlayerMovement's
// walk/attack frames, so builds need no Resources folder, no Animator, no .anim.
//
// WHY THE BODY IS A PROXY ("~Body") — the movement-shake fix.
// The player root carries an INTERPOLATED Rigidbody2D. Interpolation works by drawing
// the transform slightly BEHIND the physics body. Writing ANYTHING to that transform —
// even just scale or rotation for a breath or a lean — tells Physics2D "the transform was
// moved by hand", so on the next step it pulls the body back to the transform's
// (interpolated, lagging) pose. Doing that every frame while walking snaps the body back
// a fraction of a step each frame: that is the shaking. Resizing the root collider every
// frame to "compensate" the squash made it worse by rebuilding the physics shape.
//
// So this component never touches the root transform or its colliders any more. It hides
// the root SpriteRenderer (forceRenderingOff — its enabled flag, sprite, colour, flip and
// sorting stay fully owned by the other scripts) and draws an exact mirror of it on a
// plain child, "~Body", which is the only thing that squashes and tilts. The rigidbody is
// left completely alone, so interpolation is smooth again.

[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]   // mirror AFTER every other script set sprite/colour/sorting
public class PlayerProceduralAnimFx : MonoBehaviour
{
    [Header("Breathing (idle)")]
    public bool enableBreathing = true;

    [Tooltip("Breaths per second while standing still. 0.4-0.6 reads as calm.")]
    [Min(0.05f)] public float idleBreathRate = 0.45f;

    [Tooltip("Vertical scale swing of a breath. 0.03 = ±3%.")]
    [Range(0f, 0.15f)] public float idleBreathAmount = 0.03f;

    [Tooltip("How much of the vertical breath is answered by the opposite horizontal " +
             "squash. 1 = fully volume-preserving, 0 = pure inflate.")]
    [Range(0f, 1f)] public float volumePreservation = 0.65f;

    [Header("Walk bounce")]
    [Tooltip("Footfalls per second. The bounce runs at twice this (one dip per foot).")]
    [Min(0.1f)] public float walkStepRate = 2.2f;

    [Tooltip("Vertical squash per footfall. Keep this SMALL, or leave it at 0 if the walk " +
             "frames already bob — two bounces at slightly different rates beat against " +
             "each other, and that beat is what reads as shaking rather than walking.")]
    [Range(0f, 0.2f)] public float walkBounceAmount = 0.015f;

    [Tooltip("Body lean at full stride.")]
    [Range(0f, 15f)] public float walkSwayDegrees = 1.2f;

    [Tooltip("Sway rate as a fraction of the footfall rate. 0.5 (default) is one lean per " +
             "STRIDE — left foot out, right foot out — which is how a body actually " +
             "shifts its weight. At 1 the lean reverses on every footfall, which at a " +
             "normal walking pace is a 2 Hz wobble and looks like a shiver.")]
    [Range(0.25f, 2f)] public float swayRateMultiplier = 0.5f;

    [Min(1f)] public float locomotionBlendSpeed = 7f;

    [Header("Motion smoothing")]
    [Tooltip("Critically-damped filter on the final squash and tilt, in seconds. Nothing " +
             "the body does can step discontinuously through this, so a retriggered swing " +
             "or a state change eases in instead of snapping. 0 = off.")]
    [Range(0f, 0.25f)] public float motionSmoothing = 0.07f;

    [Tooltip("How fast the body commits to a new side, in sign units per second. The tilt " +
             "reads off this BLENDED value rather than the hard left/right flag, so " +
             "crossing the aim over the player leans through the turn instead of " +
             "mirroring the lean in one frame.")]
    [Range(1f, 20f)] public float facingBlendSpeed = 6f;

    [Tooltip("How far off vertical the aim has to be before the body changes side. The " +
             "old 0.05 meant a cursor held near straight up or straight down flipped the " +
             "lean on tiny mouse jitter, which is most of what read as shaking.")]
    [Range(0.05f, 0.9f)] public float facingFlipThreshold = 0.25f;

    [Tooltip("Ignore a new swing that lands within this fraction of the current flourish. " +
             "PlayerMovement fires one per click, including the clicks a weapon then eats " +
             "on cooldown, so without this a spammed button restarts the wind-up over and " +
             "over and the body never finishes a motion.")]
    [Range(0f, 0.9f)] public float attackRetriggerLockout = 0.35f;

    [Tooltip("Fade the walk sway out by this much during a swing, so the stride and the " +
             "strike aren't rotating the body against each other. 1 = sway fully yields.")]
    [Range(0f, 1f)] public float swayDampWhileAttacking = 0.6f;

    [Header("Attack — body")]
    public bool enableAttackFlourish = true;

    [Tooltip("Fraction of the flourish spent winding up.")]
    [Range(0.05f, 0.5f)] public float anticipationFraction = 0.20f;

    [Tooltip("Fraction of the flourish spent on the swing itself.")]
    [Range(0.1f, 0.6f)] public float strikeFraction = 0.24f;

    [Tooltip("Small dip on the wind-up. Keep this low — the body is not the effect.")]
    [Range(0f, 0.15f)] public float anticipationCrouch = 0.045f;

    [Tooltip("Body tilt through the swing, in degrees. Rotates about the sprite centre, " +
             "so keep it modest: past ~14 it starts to read as toppling over.")]
    [Range(0f, 20f)] public float tiltDegrees = 7f;

    [Tooltip("Flourish length as a multiple of the attack window, so the recovery spring " +
             "can settle after the hit has landed.")]
    [Range(1f, 3f)] public float flourishDurationScale = 1.7f;

    [Header("Attack — blade slash")]
    [Tooltip("Play the hand-drawn slash sequence on each swing.")]
    public bool enableSlash = true;

    [Tooltip("Project-relative folder holding the slash frames (00000..00013). Loaded " +
             "automatically in the editor; the sprites are then saved onto the prefab " +
             "so builds work. Right-click the component ▸ Reload Slash Frames to re-scan.")]
    public string slashFolder = "Assets/Art/Player/BladeSlash";

    [Tooltip("Re-scan the folder when this component loads and the frame list is empty. " +
             "Turn OFF once you're happy, or if you want to hand-pick frames.")]
    public bool autoLoadFramesInEditor = true;

    [Tooltip("Filled in automatically. Sorted by the number at the end of the file name, " +
             "so 00000 → 00013 comes out in frame order whatever the import order was.")]
    public Sprite[] slashFrames;

    [Header("Attack — blade slash: timing")]
    [Tooltip("ON (recommended): stretch the whole sequence across the attack window so " +
             "every frame is seen. OFF: play at Slash Fps and cut away when the frames " +
             "run out.")]
    public bool fitSlashToAttackWindow = true;

    [Tooltip("Fit To Attack Window only. Playback length as a multiple of the window the " +
             "weapon passes in — its attackCooldown, which is also how long the melee " +
             "hitbox stays live. 1 = the arc is on screen for exactly the swing.")]
    [Range(0.4f, 3f)] public float slashWindowScale = 1f;

    [Tooltip("Used when Fit To Attack Window is OFF. 14 frames at 40 fps = a 0.35s swing.")]
    [Min(1f)] public float slashFps = 40f;

    [Tooltip("Hold the arc back by this fraction of the playback. 0 (default) starts it " +
             "on the same frame the hitbox opens, which is what keeps it in sync.")]
    [Range(0f, 0.5f)] public float slashStartDelayFraction = 0f;

    [Tooltip("Fade the last N frames out to alpha 0 so the arc dissolves instead of " +
             "popping off. 0 = let the art end on its own.")]
    [Range(0, 8)] public int slashFadeOutFrames = 3;

    [Header("Attack — blade slash: placement (world units)")]
    [Tooltip("Size of the slash sprite in world units, independent of playerScale. The " +
             "BladeSlash art is 417x525px at 170 PPU, so 1 draws a 2.4 x 3.1 unit arc.")]
    [Range(0.05f, 3f)] public float slashScale = 0.45f;

    [Tooltip("How far in FRONT of the player the arc is centred. Mirrors with the facing.")]
    [Range(-1f, 3f)] public float slashOffsetForward = 0.35f;

    [Tooltip("Vertical offset. Positive lifts the arc toward the shoulder.")]
    [Range(-1.5f, 1.5f)] public float slashOffsetUp = 0f;

    [Tooltip("ON: the arc faces whichever way the player does, mirrored horizontally. " +
             "It never rotates with the cursor — aiming up or down keeps the stroke " +
             "exactly as it was drawn, so the art can't end up reading upside down.")]
    public bool mirrorWhenFacingLeft = true;

    [Tooltip("Mirror every other swing VERTICALLY, so a combo reads down / up / down " +
             "instead of the same stroke repeated.")]
    public bool alternateSwingDirection = false;

    [Tooltip("ON: the arc tracks the player if they keep moving mid-swing. OFF: it stays " +
             "where the swing started, which reads as a mark left in the air.")]
    public bool slashFollowsPlayer = true;

    [Tooltip("Multiplied into the sprite. Leave white to show the art as drawn.")]
    public Color slashTint = Color.white;

    [Tooltip("Sorting steps IN FRONT of the player.")]
    [Min(0)] public int slashSortingOffset = 1;

    [Tooltip("How many arcs may overlap. PlayerAttack buffers up to 2 attacks, so 3 " +
             "covers a fast combo without ever allocating mid-fight.")]
    [Range(1, 6)] public int maxConcurrentSlashes = 3;

    [Header("Attack — lean echoes (cape)")]
    [Tooltip("Low-alpha copies of the body frozen at the tilt they had a moment ago, so " +
             "the swing leaves a fan of cloth behind it.")]
    public bool enableEchoes = true;

    [Range(0, 6)] public int echoesPerSwing = 2;

    [Tooltip("Echo colour and starting opacity. Keep the alpha low — high values read as " +
             "a second character standing next to you.")]
    public Color echoTint = new Color(0.58f, 0.14f, 0.26f, 0.18f);

    [Tooltip("How far each echo sits behind the player, in world units.")]
    [Range(0f, 0.6f)] public float echoBackOffset = 0.1f;

    [Tooltip("Extra tilt on the echoes, past whatever the body had. Exaggerates the fan.")]
    [Range(0f, 30f)] public float echoTiltBoost = 7f;

    [Range(0.05f, 0.8f)] public float echoLifetime = 0.2f;

    [Tooltip("Sorting steps BEHIND the player.")]
    [Min(0)] public int echoSortingOffset = 1;

    [Header("Safety")]
    [Tooltip("Kept for prefab compatibility. The deformation now lives only on the '~Body' " +
             "visual proxy, so colliders, the rigidbody and child objects are never touched.")]
    public bool keepGameplayUnchanged = true;

    //  STATE

    private SpriteRenderer _sprite;     // the root renderer: gameplay scripts own it, we hide it
    private Transform _fxRoot;          // deformation-free container for all effects

    // Visual proxy. The ONLY transform this component deforms.
    private Transform _body;
    private SpriteRenderer _bodySr;
    private MaterialPropertyBlock _mpb;

    private float _phase;
    private float _swayPhase;
    private float _moveBlend;
    private bool _moving;
    private Vector2 _facing = Vector2.right;
    private float _facingSign = 1f;

    private bool _attacking;
    private float _attackTime;
    private float _attackDuration;
    private float _currentTilt;

    // Smoothing state. _facingBlend eases between -1 and +1 instead of snapping with
    // _facingSign; the carry fields hold the flourish's shape at the instant it was
    // retriggered, so the replacement swing adds to a decaying tail rather than to a
    // cliff; the two SmoothDamp pairs filter whatever comes out of all of it.
    private float _facingBlend = 1f;
    private float _attackEnvelope;
    private float _attackCarryCrouch;
    private float _attackCarryTilt;
    private float _attackCarryAge = float.MaxValue;
    private const float ATTACK_CARRY_TIME = 0.18f;
    private float _smoothScaleY = 1f;
    private float _smoothTilt;
    private float _scaleYVel;
    private float _tiltVel;

    // Blade slash — a small pool of sprite players, one per overlapping swing.
    private class SlashInstance
    {
        public GameObject go;
        public Transform t;
        public SpriteRenderer sr;
        public bool active;
        public float time;          // seconds since the swing started
        public float delay;         // seconds before frame 0 appears
        public float frameTime;     // seconds per frame
        public bool flipX;          // facing left
        public bool flipY;          // alternating stroke
        public Vector3 frozenBase;  // player position at the swing (slashFollowsPlayer off)
        public int shownFrame;
    }

    private readonly List<SlashInstance> _slashPool = new List<SlashInstance>();
    private Transform _slashRoot;
    private int _liveSlashes;
    private bool _flipNextSlash;

    private int SlashFrameCount => slashFrames == null ? 0 : slashFrames.Length;

    // Echoes
    private readonly List<Echo> _echoes = new List<Echo>();
    private int _echoesEmitted;

    private bool _suspended;

    private class Echo
    {
        public Transform t;
        public SpriteRenderer sr;
        public float age;
    }

    //  PUBLIC API (called by PlayerMovement)

    public void SetLocomotion(bool moving, Vector2 facing)
    {
        _moving = moving;
        SetFacing(facing);
    }

    // One place decides which way the body is facing. The threshold is hysteresis: near
    // vertical the horizontal component is noise, and re-deciding on every frame of it
    // used to flip the lean back and forth.
    private void SetFacing(Vector2 facing)
    {
        if (facing.sqrMagnitude <= 0.0001f) return;

        _facing = facing.normalized;
        if (Mathf.Abs(_facing.x) > facingFlipThreshold)
            _facingSign = _facing.x >= 0f ? 1f : -1f;
    }

    /// <summary>
    /// Body flourish for a swing. <paramref name="window"/> is how long the attack state
    /// is held. Fired by PlayerMovement on every attack input — including inputs a weapon
    /// then swallows on cooldown — so the blade arc is deliberately NOT started here.
    /// See <see cref="TriggerBladeSlash"/>.
    /// </summary>
    public void TriggerAttack(Vector2 direction, float window)
    {
        if (_suspended) return;
        if (!enableAttackFlourish && !enableSlash && !enableEchoes) return;

        // A swing that arrives on top of one barely started is the same click being
        // spammed through the weapon's cooldown. Let the current motion finish.
        if (_attacking && _attackDuration > 0.0001f
            && _attackTime < _attackDuration * attackRetriggerLockout)
        {
            SetFacing(direction);
            return;
        }

        SetFacing(direction);

        // Carry the shape the body is in right now so the new wind-up is added to a
        // decaying tail instead of replacing it in one frame.
        if (_attacking && _attackDuration > 0.0001f)
        {
            AttackShape(Mathf.Clamp01(_attackTime / _attackDuration),
                        out _attackCarryCrouch, out _attackCarryTilt);
            _attackCarryAge = 0f;
        }

        _attacking = true;
        _attackTime = 0f;
        _attackDuration = Mathf.Max(0.18f, window * flourishDurationScale);
        _echoesEmitted = 0;
    }

    /// <summary>
    /// Draw the blade arc. Called by Weapon.ExecuteWeaponAttack at the instant a melee
    /// swing actually happens, so a click eaten by the cooldown draws nothing.
    /// <paramref name="window"/> is the swing's live window (the weapon's attackCooldown).
    /// </summary>
    public void TriggerBladeSlash(float window)
    {
        if (_suspended || !enableSlash) return;
        SpawnSlash(window);
    }

    /// <summary>Overload for callers that know the facing better than the last
    /// SetLocomotion call did.</summary>
    public void TriggerBladeSlash(Vector2 direction, float window)
    {
        SetFacing(direction);
        TriggerBladeSlash(window);
    }

    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        if (suspended)
        {
            _attacking = false;
            ResetMotionState();
            HideSlash();
            RetireEchoes();
            ResetToBase();
        }
    }

    //  LIFECYCLE

    private void Awake()
    {
        _sprite = GetComponent<SpriteRenderer>();
        _mpb = new MaterialPropertyBlock();

        // The proxy sits exactly on the root's pivot, so squash and tilt pivot around the
        // same point they always did. It has no collider and no rigidbody, so moving,
        // scaling or rotating it can never feed back into physics.
        var bodyGo = new GameObject("~Body");
        bodyGo.layer = gameObject.layer;
        _body = bodyGo.transform;
        _body.SetParent(transform, false);
        _body.localPosition = Vector3.zero;
        _body.localRotation = Quaternion.identity;
        _body.localScale = Vector3.one;
        _bodySr = bodyGo.AddComponent<SpriteRenderer>();
        MirrorRenderer();

        _fxRoot = new GameObject("~PlayerFx").transform;
        _fxRoot.SetParent(transform, false);
        _fxRoot.localPosition = Vector3.zero;

        _slashRoot = new GameObject("~Slash").transform;
        _slashRoot.SetParent(_fxRoot, false);
        _slashRoot.localPosition = Vector3.zero;

        // A hole in the array (deleted frame, failed import) would throw on the frame it
        // lands on, minutes into a run. Strip holes once, here.
        slashFrames = CompactFrames(slashFrames);

#if UNITY_EDITOR
        // Entering Play with frames that were never serialised (fresh paste, reverted
        // prefab) would otherwise leave the swing with no arc at all.
        if (SlashFrameCount == 0 && autoLoadFramesInEditor) LoadSlashFramesFromFolder(false);
#else
        if (enableSlash && SlashFrameCount == 0)
            Debug.LogWarning("[PlayerProceduralAnimFx] Slash Frames is empty, so this build " +
                             "ships with no slash effect. In the editor, select Player.prefab, " +
                             "right-click the component \u25b8 Reload Slash Frames, and save.", this);
#endif

    }

    private void OnEnable()
    {
        // Hide the root sprite without touching its 'enabled' flag, which other scripts
        // (downed state, hit flashes, spawn blink) still read and write as before.
        if (_sprite != null) _sprite.forceRenderingOff = true;
        MirrorRenderer();
    }

    private void OnDisable()
    {
        ResetMotionState();
        HideSlash();
        RetireEchoes();
        ResetToBase();

        // Hand the drawing back to the real renderer.
        // (Renderer flag rather than SetActive: OnDisable also runs while the whole player
        // is being deactivated, and Unity refuses hierarchy changes during that.)
        if (_sprite != null) _sprite.forceRenderingOff = false;
        if (_bodySr != null) _bodySr.enabled = false;
    }

    private void OnDestroy()
    {
        if (_sprite != null) _sprite.forceRenderingOff = false;
        if (_body != null) Destroy(_body.gameObject);
    }

    // Copy everything visible from the root renderer onto the proxy. Runs every
    // LateUpdate at execution order 10000, i.e. after PlayerMovement has picked the frame,
    // the flipX and the Y-sort order for this frame, so the proxy is never a frame behind.
    private void MirrorRenderer()
    {
        if (_sprite == null || _bodySr == null) return;

        bool show = _sprite.enabled;
        if (_bodySr.enabled != show) _bodySr.enabled = show;
        if (!show) return;

        if (_bodySr.sprite != _sprite.sprite) _bodySr.sprite = _sprite.sprite;
        _bodySr.color = _sprite.color;
        _bodySr.flipX = _sprite.flipX;
        _bodySr.flipY = _sprite.flipY;
        if (_bodySr.sharedMaterial != _sprite.sharedMaterial)
            _bodySr.sharedMaterial = _sprite.sharedMaterial;
        _bodySr.sortingLayerID = _sprite.sortingLayerID;
        _bodySr.sortingOrder = _sprite.sortingOrder;
        _bodySr.maskInteraction = _sprite.maskInteraction;
        _bodySr.drawMode = _sprite.drawMode;
        if (_sprite.drawMode != SpriteDrawMode.Simple) _bodySr.size = _sprite.size;

        // Shader-driven hit flashes etc. set a property block on the root renderer.
        if (_sprite.HasPropertyBlock())
        {
            _sprite.GetPropertyBlock(_mpb);
            _bodySr.SetPropertyBlock(_mpb);
        }
        else if (_bodySr.HasPropertyBlock())
        {
            _bodySr.SetPropertyBlock(null);
        }
    }

    // Park the filters at rest. Leaving a velocity in them across a downed/revive or a
    // disable would spring the body on the frame it comes back.
    private void ResetMotionState()
    {
        _smoothScaleY = 1f;
        _smoothTilt = 0f;
        _scaleYVel = 0f;
        _tiltVel = 0f;
        _attackEnvelope = 0f;
        _attackCarryAge = float.MaxValue;
        _swayPhase = _phase;
        _facingBlend = _facingSign;
    }

    private void LateUpdate()
    {
        // Always mirror, even while suspended or paused: the downed/death frames and
        // tints still have to show on the proxy.
        MirrorRenderer();

        if (_suspended) return;

        // A zero-length frame — timeScale 0 on the pause menu — has nothing to advance,
        // and every filter below would be dividing by it.
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float target = _moving ? 1f : 0f;
        _moveBlend = Mathf.MoveTowards(_moveBlend, target, locomotionBlendSpeed * dt);

        float rate = Mathf.Lerp(idleBreathRate, walkStepRate, _moveBlend);
        _phase = Mathf.Repeat(_phase + rate * dt, 1f);

        // The lean runs on its own, slower clock. Bounce belongs to the FOOTFALL, sway
        // belongs to the STRIDE, and they are not the same event — driving both off one
        // phase is what made the body twitch once per foot instead of rocking once per
        // pair of steps.
        _swayPhase = Mathf.Repeat(_swayPhase + rate * swayRateMultiplier * dt, 1f);

        // Lean through a turn rather than mirroring in one frame.
        _facingBlend = Mathf.MoveTowards(_facingBlend, _facingSign, facingBlendSpeed * dt);
        _attackEnvelope = Mathf.MoveTowards(_attackEnvelope, _attacking ? 1f : 0f, 6f * dt);

        float scaleY = 1f;
        float tilt = 0f;

        if (enableBreathing)
        {
            float wave = Mathf.Sin(_phase * Mathf.PI * 2f);

            // Idle: slow sine. Walk: two dips per stride, on the footfalls.
            //
            // sin² rather than |sin|. They have the same peaks and the same zeros, but
            // |sin| has a CUSP at every footfall — an instant reversal of vertical
            // velocity twice a stride, which the eye reads as a snap rather than a step.
            float idle = wave * idleBreathAmount;
            float walk = walkBounceAmount * (0.5f - wave * wave);

            scaleY += Mathf.Lerp(idle, walk, _moveBlend);

            // Sway steps aside during a swing so the stride and the strike aren't
            // rotating the body against each other.
            float swayWave = Mathf.Sin(_swayPhase * Mathf.PI * 2f);
            float swayDamp = 1f - swayDampWhileAttacking * _attackEnvelope;
            tilt += swayWave * walkSwayDegrees * _moveBlend * _facingBlend * swayDamp;
        }

        if (_attacking)
        {
            _attackTime += dt;
            float t = Mathf.Clamp01(_attackTime / _attackDuration);

            AttackShape(t, out float crouch, out float attackTilt);
            if (enableAttackFlourish)
            {
                scaleY += crouch;
                tilt += attackTilt * _facingBlend;
            }

            if (enableEchoes) EmitScheduledEchoes(t, tilt);

            if (t >= 1f) _attacking = false;
        }

        // Decaying tail of a swing that was replaced mid-motion.
        if (_attackCarryAge < ATTACK_CARRY_TIME)
        {
            float k = 1f - _attackCarryAge / ATTACK_CARRY_TIME;
            k *= k;                       // ease out, so the tail leaves quietly
            if (enableAttackFlourish)
            {
                scaleY += _attackCarryCrouch * k;
                tilt += _attackCarryTilt * k * _facingBlend;
            }
            _attackCarryAge += dt;
        }

        // Everything above is a target, not a pose. Filtering it here means no single
        // contribution can step the body — a retrigger, a facing flip, walk starting or
        // stopping all arrive as a curve.
        if (motionSmoothing > 0.0001f)
        {
            _smoothScaleY = Damp(_smoothScaleY, scaleY, ref _scaleYVel, motionSmoothing, dt);
            _smoothTilt = Damp(_smoothTilt, tilt, ref _tiltVel, motionSmoothing, dt);
            scaleY = _smoothScaleY;
            tilt = _smoothTilt;
        }
        else
        {
            _smoothScaleY = scaleY;
            _smoothTilt = tilt;
            _scaleYVel = 0f;
            _tiltVel = 0f;
        }

        // Last line of defence: never let a NaN reach the proxy transform.
        if (float.IsNaN(scaleY) || float.IsInfinity(scaleY) ||
            float.IsNaN(tilt) || float.IsInfinity(tilt))
        {
            ResetMotionState();
            scaleY = 1f;
            tilt = 0f;
        }

        _currentTilt = tilt;

        // Volume preservation: what goes up vertically comes in horizontally. This is
        // what makes the breath read as a body rather than as a resizing image.
        float scaleX = 1f - (scaleY - 1f) * volumePreservation;

        Apply(new Vector3(scaleX, scaleY, 1f), tilt);

        UpdateSlash(dt);
        UpdateEchoes(dt);
    }

    //  ATTACK BODY CURVE
    //
    // Deliberately small. Three beats — dip, rise through the swing, settle — with no
    // vertical stretch at all, because a stretching torso on a static sprite is exactly
    // the "headbutt" look. The swing itself is the arc, not the body.
    private void AttackShape(float t, out float crouch, out float tilt)
    {
        float a = Mathf.Clamp01(anticipationFraction);
        float s = Mathf.Clamp(strikeFraction, 0.05f, 1f - a);

        if (t < a)
        {
            float k = EaseOutCubic(t / a);
            crouch = -anticipationCrouch * k;
            tilt = -tiltDegrees * 0.5f * k;
            return;
        }

        if (t < a + s)
        {
            float k = EaseOutCubic((t - a) / s);
            crouch = Mathf.Lerp(-anticipationCrouch, 0f, k);
            tilt = Mathf.Lerp(-tiltDegrees * 0.5f, tiltDegrees, k);
            return;
        }

        float r = (t - a - s) / Mathf.Max(0.0001f, 1f - a - s);
        float settle = SpringSettle(r);
        crouch = 0f;
        tilt = tiltDegrees * settle;
    }

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

    // 1 → 0 with two decaying wobbles.
    private static float SpringSettle(float t)
    {
        t = Mathf.Clamp01(t);
        return Mathf.Cos(t * Mathf.PI * 1.5f) * Mathf.Exp(-5.5f * t);
    }

    //  BLADE SLASH
    //
    // One pooled SpriteRenderer per live swing, parented under _slashRoot so it inherits
    // the player's position but none of the breathing squash or the attack tilt — an arc
    // that wobbled with the body would give the whole effect away.

    // Start one arc. Pooled because PlayerAttack buffers up to two attacks: a fast combo
    // gets overlapping arcs instead of one being cut short by the next.
    private void SpawnSlash(float window)
    {
        int frames = SlashFrameCount;
        if (frames == 0) return;

        var s = RentSlash();
        if (s == null) return;   // already at maxConcurrentSlashes — drop this swing's arc
                                 // rather than stealing one that is still on screen

        // The arc never rotates. A hand-drawn stroke carries its own light and weight,
        // and spinning it to follow the cursor turns an overhead swing into an upward one
        // and eventually into an upside-down sprite. Horizontal mirroring only, off
        // _facingSign — which holds its last horizontal value, so aiming straight up or
        // straight down keeps whichever side the player was last facing.
        bool flipX = mirrorWhenFacingLeft && _facingSign < 0f;

        bool flipY = false;
        if (alternateSwingDirection)
        {
            flipY = _flipNextSlash;
            _flipNextSlash = !_flipNextSlash;
        }

        float total = fitSlashToAttackWindow
            ? Mathf.Max(0.05f, window * slashWindowScale)
            : frames / Mathf.Max(1f, slashFps);

        s.active = true;
        s.time = 0f;
        s.delay = total * slashStartDelayFraction;
        s.frameTime = total / frames;
        s.flipX = flipX;
        s.flipY = flipY;
        s.frozenBase = transform.position;
        s.shownFrame = -1;
        s.go.SetActive(false);   // hidden until the start delay elapses

        _liveSlashes++;
    }

    private void UpdateSlash(float dt)
    {
        if (_liveSlashes == 0) return;

        int frames = SlashFrameCount;
        if (frames == 0) { HideSlash(); return; }

        // Sizes and offsets are authored in WORLD units; _slashRoot inherits the player's
        // base scale, so convert once here rather than making every number in the
        // inspector depend on playerScale.
        float w2l = WorldToLocal();
        int layer = _sprite != null ? _sprite.sortingLayerID : 0;
        int order = (_sprite != null ? _sprite.sortingOrder : 0) + slashSortingOffset;

        for (int i = 0; i < _slashPool.Count; i++)
        {
            var s = _slashPool[i];
            if (!s.active || s.go == null) continue;

            s.time += dt;
            float local = s.time - s.delay;
            if (local < 0f) continue;   // still on the wind-up

            float exact = local / Mathf.Max(0.0001f, s.frameTime);
            int frame = Mathf.FloorToInt(exact);

            if (frame >= frames)
            {
                s.active = false;
                s.shownFrame = -1;
                s.go.SetActive(false);
                _liveSlashes--;
                continue;
            }

            if (!s.go.activeSelf) s.go.SetActive(true);
            if (frame != s.shownFrame)
            {
                s.sr.sprite = slashFrames[frame];
                s.shownFrame = frame;
            }

            // Tail fade, counted from the END of the sequence so it follows whatever
            // frame count the folder actually has.
            var c = slashTint;
            if (slashFadeOutFrames > 0)
            {
                float firstFading = frames - slashFadeOutFrames;
                if (exact > firstFading)
                    c.a *= Mathf.Clamp01(1f - (exact - firstFading) / slashFadeOutFrames);
            }

            s.sr.color = c;
            s.sr.flipX = s.flipX;
            s.sr.flipY = s.flipY;
            s.sr.sortingLayerID = layer;
            s.sr.sortingOrder = order;

            // Axis-aligned by design — see SpawnSlash. The offset mirrors with the
            // facing so the arc stays in front of the player on either side.
            Vector3 off = new Vector3(slashOffsetForward * (s.flipX ? -1f : 1f),
                                      slashOffsetUp * (s.flipY ? -1f : 1f),
                                      0f);

            if (slashFollowsPlayer)
                s.t.localPosition = off * w2l;
            else
                s.t.position = new Vector3(s.frozenBase.x + off.x,
                                           s.frozenBase.y + off.y,
                                           s.frozenBase.z);

            s.t.localRotation = Quaternion.identity;
            s.t.localScale = new Vector3(slashScale * w2l, slashScale * w2l, 1f);
        }
    }

    private SlashInstance RentSlash()
    {
        for (int i = 0; i < _slashPool.Count; i++)
            if (!_slashPool[i].active) return _slashPool[i];

        if (_slashPool.Count >= Mathf.Max(1, maxConcurrentSlashes)) return null;

        var go = new GameObject("~BladeSlash");
        go.transform.SetParent(_slashRoot, false);

        // Default sprite material on purpose: the player's own material may carry a hit
        // flash or a tint that has nothing to do with the arc.
        var sr = go.AddComponent<SpriteRenderer>();
        go.SetActive(false);

        var s = new SlashInstance { go = go, t = go.transform, sr = sr, shownFrame = -1 };
        _slashPool.Add(s);
        return s;
    }

    private void HideSlash()
    {
        for (int i = 0; i < _slashPool.Count; i++)
        {
            var s = _slashPool[i];
            s.active = false;
            s.shownFrame = -1;
            if (s.go != null) s.go.SetActive(false);
        }
        _liveSlashes = 0;
    }

    private static Sprite[] CompactFrames(Sprite[] src)
    {
        if (src == null) return System.Array.Empty<Sprite>();

        int holes = 0;
        for (int i = 0; i < src.Length; i++) if (src[i] == null) holes++;
        if (holes == 0) return src;

        Debug.LogWarning($"[PlayerProceduralAnimFx] Slash Frames has {holes} empty slot(s); " +
                         "they were skipped. Right-click the component ▸ Reload Slash Frames.");

        var packed = new Sprite[src.Length - holes];
        int w = 0;
        for (int i = 0; i < src.Length; i++) if (src[i] != null) packed[w++] = src[i];
        return packed;
    }

    //  LEAN ECHOES

    private void EmitScheduledEchoes(float t, float currentTilt)
    {
        if (echoesPerSwing <= 0) return;

        float a = Mathf.Clamp01(anticipationFraction);
        float s = Mathf.Clamp(strikeFraction, 0.05f, 1f - a);
        if (t < a) return;

        float through = Mathf.Clamp01((t - a) / s);
        int want = Mathf.CeilToInt(through * echoesPerSwing);

        while (_echoesEmitted < want && _echoesEmitted < echoesPerSwing)
        {
            EmitEcho(currentTilt);
            _echoesEmitted++;
        }
    }

    private void EmitEcho(float tiltNow)
    {
        if (_sprite == null || _sprite.sprite == null) return;

        var e = RentEcho();
        if (e == null) return;

        float w2l = WorldToLocal();

        e.age = 0f;
        e.sr.sprite = _sprite.sprite;
        e.sr.flipX = _sprite.flipX;
        e.sr.sharedMaterial = _sprite.sharedMaterial;
        e.sr.sortingLayerID = _sprite.sortingLayerID;
        e.sr.color = echoTint;

        // Sit behind the body, frozen at the tilt it had this instant plus a push, so the
        // series of echoes fans out across the swing.
        e.t.gameObject.SetActive(true);
        e.t.localPosition = (Vector3)(-_facing * (echoBackOffset * w2l));
        e.t.localRotation = Quaternion.Euler(0f, 0f, -(tiltNow + echoTiltBoost * _facingBlend));
        e.t.localScale = Vector3.one;
    }

    private void UpdateEchoes(float dt)
    {
        if (_echoes.Count == 0) return;

        int order = (_sprite != null ? _sprite.sortingOrder : 0) - echoSortingOffset;

        for (int i = 0; i < _echoes.Count; i++)
        {
            var e = _echoes[i];
            if (e.t == null || !e.t.gameObject.activeSelf) continue;

            e.age += dt;
            float k = Mathf.Clamp01(e.age / Mathf.Max(0.01f, echoLifetime));

            if (k >= 1f) { e.t.gameObject.SetActive(false); continue; }

            float fade = (1f - k) * (1f - k);
            var c = echoTint;
            c.a = echoTint.a * fade;
            e.sr.color = c;
            e.sr.sortingOrder = order;
        }
    }

    private Echo RentEcho()
    {
        for (int i = 0; i < _echoes.Count; i++)
            if (_echoes[i].t != null && !_echoes[i].t.gameObject.activeSelf)
                return _echoes[i];

        if (_echoes.Count >= Mathf.Max(1, echoesPerSwing) * 2) return null;

        var go = new GameObject("~LeanEcho");
        go.transform.SetParent(_fxRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();

        var e = new Echo { t = go.transform, sr = sr };
        _echoes.Add(e);
        return e;
    }

    private void RetireEchoes()
    {
        for (int i = 0; i < _echoes.Count; i++)
            if (_echoes[i].t != null) _echoes[i].t.gameObject.SetActive(false);
    }

    // Effect sizes are authored in world units; _fxRoot inherits the player's scale, so
    // convert once here rather than making every number depend on playerScale. The root
    // is never deformed by this component any more, so its scale IS the rest scale.
    private float WorldToLocal() => 1f / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));

    //  APPLY
    //
    // Only the proxy moves. The root transform, its Rigidbody2D, its colliders and its
    // other children are never written, so physics interpolation stays intact and the
    // hitboxes / cursor are unaffected by construction — nothing to compensate.

    private void Apply(Vector3 fxScale, float tiltDeg)
    {
        if (_body == null) return;
        _body.localScale = fxScale;
        _body.localRotation = Quaternion.Euler(0f, 0f, -tiltDeg);
    }

    private void ResetToBase()
    {
        if (_body == null) return;
        _body.localScale = Vector3.one;
        _body.localRotation = Quaternion.identity;
    }

    private static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

    // Critically-damped spring, same curve as Mathf.SmoothDamp.
    //
    // Written out rather than called, because Mathf.SmoothDamp ends with an overshoot
    // correction that divides by deltaTime. On a frame where deltaTime is 0 — timeScale
    // 0, which is every frame of the pause menu — the output lands exactly on the target,
    // that branch fires, and it evaluates 0/0. The resulting NaN velocity survives into
    // the next real frame and from there into the transform, permanently. No clamp here,
    // so there is no division to get wrong; the caller guarantees dt > 0 anyway.
    private static float Damp(float current, float target, ref float vel, float smoothTime, float dt)
    {
        if (smoothTime <= 0.0001f || dt <= 0f)
        {
            vel = 0f;
            return target;
        }

        float omega = 2f / smoothTime;
        float x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (vel + omega * change) * dt;

        vel = (vel - omega * temp) * exp;
        return target + (change + temp) * exp;
    }

    //  EDITOR AUTO-LOAD
    //
    // Compiled out of builds. Reads the slash folder straight from the project and writes
    // the Sprite references into slashFrames, which the prefab then serialises — so the
    // build never touches AssetDatabase. Same approach as PlayerMovement's frame folders.

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!autoLoadFramesInEditor) return;
        if (Application.isPlaying) return;
        if (SlashFrameCount > 0) return;

        // AssetDatabase must not be touched from inside OnValidate (it runs during
        // deserialisation), so defer by a tick.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;   // component died during the recompile
            LoadSlashFramesFromFolder(true);
        };
    }

    // Existing prefabs keep whatever was serialised on them, so retuned DEFAULTS never
    // reach a component that is already in the scene. This writes the smooth-walk values
    // over them in one click, without the collateral damage of a full component Reset
    // (which would also clear Slash Frames).
    [ContextMenu("Apply Smooth Walk Preset")]
    private void ApplySmoothWalkPreset()
    {
        walkStepRate = 2.2f;
        walkBounceAmount = 0.015f;
        walkSwayDegrees = 1.2f;
        swayRateMultiplier = 0.5f;
        locomotionBlendSpeed = 7f;

        motionSmoothing = 0.07f;
        facingBlendSpeed = 6f;
        facingFlipThreshold = 0.25f;
        attackRetriggerLockout = 0.35f;
        swayDampWhileAttacking = 0.6f;

        tiltDegrees = 7f;
        echoesPerSwing = 2;
        echoTint = new Color(0.58f, 0.14f, 0.26f, 0.18f);

        UnityEditor.EditorUtility.SetDirty(this);
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);

        Debug.Log("[PlayerProceduralAnimFx] Smooth walk preset applied.", this);
    }

    [ContextMenu("Reload Slash Frames")]
    private void ReloadSlashFrames() => LoadSlashFramesFromFolder(true);

    private void LoadSlashFramesFromFolder(bool markDirty)
    {
        slashFrames = LoadSlashFolder(slashFolder);

        if (SlashFrameCount == 0)
            Debug.LogWarning($"[PlayerProceduralAnimFx] No slash frames found in '{slashFolder}'. " +
                             "Check the path (project-relative, e.g. Assets/Art/Player/BladeSlash) " +
                             "and that the PNGs import as Sprite (2D and UI).", this);
        else
            Debug.Log($"[PlayerProceduralAnimFx] Loaded {SlashFrameCount} slash frame(s).", this);

        if (!markDirty) return;

        UnityEditor.EditorUtility.SetDirty(this);
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
    }

    // Sorted by the number at the end of the file name. Sorting is the whole point:
    // Unity's asset search order is not frame order.
    private static Sprite[] LoadSlashFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return System.Array.Empty<Sprite>();

        folder = folder.Replace('\\', '/').TrimEnd('/');
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) return System.Array.Empty<Sprite>();

        var guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        var found = new List<(int number, string name, Sprite sprite)>(guids.Length);

        foreach (var guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);

            // LoadAllAssetsAtPath rather than LoadAssetAtPath<Sprite> so a frame left in
            // Multiple sprite mode still contributes its sub-sprites.
            foreach (var obj in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is Sprite sp)
                    found.Add((TrailingNumber(System.IO.Path.GetFileNameWithoutExtension(path)),
                               sp.name, sp));
        }

        found.Sort((a, b) =>
        {
            if (a.number != b.number) return a.number.CompareTo(b.number);
            return string.CompareOrdinal(a.name, b.name);
        });

        var result = new Sprite[found.Count];
        for (int i = 0; i < found.Count; i++) result[i] = found[i].sprite;
        return result;
    }

    private static int TrailingNumber(string name)
    {
        int end = name.Length;
        while (end > 0 && !char.IsDigit(name[end - 1])) end--;
        int start = end;
        while (start > 0 && char.IsDigit(name[start - 1])) start--;
        if (start == end) return int.MaxValue;
        return int.TryParse(name.Substring(start, end - start), out int v) ? v : int.MaxValue;
    }
#endif
}


