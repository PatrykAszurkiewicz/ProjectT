using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using FMOD.Studio;

// PLAYER ANIMATION — image sequences from Assets/Art/Player/Walk + /Attack
//
// Paste this over your existing PlayerMovement.cs. Nothing else to install: the
// frame folders are read straight out of the project in the editor and the
// resulting Sprite references are serialised onto the prefab, so builds work too
// (no Resources folder, no Animator controller, no .anim assets).
//
// TWO BACKENDS, chosen once in Start() and never mixed:
//
//   FRAME-CLIP MODE — walkFrames / attackFrames are populated. Real Walk state,
//                     one-shot Attack, per-frame facing.
//   LEGACY MODE     — they are empty. Every animation call goes through the
//                     ORIGINAL Utilities.AnimateSprite coroutines against the
//                     12-frame Resources spritesheet, same start frames, same
//                     looping, same animationSpeed. Identical to before.
//
// Every public member that existed before still exists with the same signature.
// The Start*Attack methods gained an optional duration overload, so PlayerAttack.cs
// does NOT have to be modified.
public class PlayerMovement : MonoBehaviour
{
    PlayerStats pstats;
    Vector2 move;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;

    bool isSprinting = false;

    // While true, PlayerMovement yields control of the Rigidbody2D to whoever
    // set this flag (e.g. the grappling hook). FixedUpdate will not call
    // MovePosition, so external systems can move the player freely.
    public bool IsBeingGrappled { get; set; } = false;

    private bool isDashing = false;
    private Vector2 dashDirection;
    private float dashTimer;
    private float lastDashTime = -Mathf.Infinity;
    private float dashRegenTimer = 0f;
    public float dashStaminaCost = 1f;

    private EventInstance footsteps;

    // Sprite Animation Variables
    [Header("Sprite Animation (legacy spritesheet fallback)")]
    [SerializeField] private Sprite[] playerSprites; // Fallback Sprite
    public float animationSpeed = 0.1f;
    [SerializeField] private float playerScale = 0.5f; // Configurable scale

    //  FRAME SEQUENCES

    [Header("Frame Folders (auto-loaded in the editor)")]
    [Tooltip("Project-relative folder of the walk frames. Loaded automatically in the " +
             "editor; the resulting sprites are saved onto the prefab so builds work.")]
    public string walkFolder = "Assets/Art/Player/Walk";

    [Tooltip("Project-relative folder of the attack frames.")]
    public string attackFolder = "Assets/Art/Player/Attack";

    [Tooltip("OPTIONAL separate ranged-attack folder. Empty → the attack folder is used for both.")]
    public string rangedAttackFolder = "";

    [Tooltip("OPTIONAL idle folder. Empty → the player holds Idle Hold Frame of the walk cycle.")]
    public string idleFolder = "";

    [Tooltip("OPTIONAL death/downed folder. Empty → the player holds Downed Pose Frame of the walk cycle.")]
    public string deathFolder = "";

    [Tooltip("Re-scan the folders when this component loads and the frame lists are empty. " +
             "Turn OFF once you're happy, or if you want to hand-pick frames.")]
    public bool autoLoadFramesInEditor = true;

    [Header("Frames (filled in automatically — no need to touch)")]
    public Sprite[] walkFrames;
    public Sprite[] attackFrames;
    public Sprite[] rangedAttackFrames;
    public Sprite[] idleFrames;
    public Sprite[] deathFrames;

    [Header("Frame Playback")]
    [Tooltip("Walk cycle rate. 48 frames at 24 fps = a 2s stride; raise this if the walk drags.")]
    [Min(1f)] public float walkFps = 24f;

    [Tooltip("Idle rate (only used when an idle folder is set).")]
    [Min(1f)] public float idleFps = 12f;

    [Tooltip("Attack rate, used when 'Fit Attack To Attack Window' is OFF.")]
    [Min(1f)] public float attackFps = 24f;

    [Tooltip("Death / downed rate.")]
    [Min(1f)] public float deathFps = 18f;

    [Tooltip("ON (recommended): squeeze the whole attack sequence into the window PlayerAttack " +
             "holds the attack state for, so all of the swing is visible instead of its first " +
             "few frames. OFF: play at Attack Fps and cut away when the window ends.")]
    public bool fitAttackToAttackWindow = true;

    [Tooltip("How long the attack state lasts, when the caller doesn't say. Match PlayerAttack's " +
             "'Attack Animation Duration' (0.3 by default).")]
    [Min(0.05f)] public float attackWindowFallback = 0.3f;

    [Tooltip("Which walk frame to stand on while idle (when there are no idle frames).")]
    [Min(0)] public int idleHoldFrame = 0;

    [Tooltip("Which walk frame to hold while downed/dead (when there are no death frames). " +
             "-1 = the last walk frame.")]
    public int downedPoseFrame = -1;

    [Tooltip("Movement input magnitude above which the walk cycle plays.")]
    [Range(0.01f, 0.9f)] public float moveThreshold = 0.05f;

    [Header("Facing")]
    [Tooltip("None — never touch facing.\n" +
             "Mirror Horizontally — flipX toward the facing direction. Right for a single cycle.\n" +
             "Directional Frames — the folder is a DIRECTIONAL sheet: set Frames Per Direction " +
             "and the facing angle picks which block of frames plays.")]
    public FacingMode facingMode = FacingMode.MirrorHorizontally;

    [Tooltip("Aim — face where this player is aiming. Movement — face the direction of travel.")]
    public FacingSource facingSource = FacingSource.Aim;

    [Tooltip("ON if the source art faces RIGHT (or straight at the camera).")]
    public bool artFacesRight = true;

    [Tooltip("Directional Frames only. Frames per direction in the walk sequence. " +
             "48 frames with 6 here = 8 directions. 0 = one single cycle.")]
    [Min(0)] public int walkFramesPerDirection = 0;

    [Tooltip("Directional Frames only. Frames per direction in the attack sequence.")]
    [Min(0)] public int attackFramesPerDirection = 0;

    [Tooltip("Directional Frames only. World angle of the FIRST direction block. " +
             "270 = down, 0 = right, 90 = up, 180 = left.")]
    public float firstDirectionAngle = 270f;

    [Tooltip("Directional Frames only. ON if later direction blocks rotate clockwise.")]
    public bool directionsClockwise = true;

    [Header("Procedural FX")]
    [Tooltip("Breathing, walk bounce, attack squash/stretch and the cape swoosh. " +
             "Adds PlayerProceduralAnimFx at runtime if it isn't already on the prefab; " +
             "add that component by hand if you want to tune it in the inspector.")]
    public bool enableProceduralFx = true;

    [Header("Y-Sort")]
    [Tooltip("Must match GrassCartoonOverlay.sortPrecision")]
    public float sortPrecision = 10f;

    [Tooltip("Must match GrassCartoonOverlay.sortOrderBase")]
    public int sortOrderBase = 1000;

    [Tooltip("Y offset for the sort point relative to transform center. " +
             "Negative = sort from lower on the sprite (feet). " +
             "Adjust so the front/behind transition happens at the player's feet.")]
    public float sortYOffset = -0.3f;

    public enum FacingMode { None, MirrorHorizontally, DirectionalFrames }
    public enum FacingSource { Aim, Movement }

    // Animation state tracking
    private enum AnimationState
    {
        Idle,
        MeleeAttack,
        RangedAttack,
        Dying,
        Walk        // appended: the existing values keep their meaning
    }

    //private AnimationState currentAnimationState = AnimationState.Idle;
    private AnimationState currentAnimationState = (AnimationState)(-1); // force first animation to play

    private Coroutine currentAnimationCoroutine;
    private bool isMeleeAttacking = false;
    private bool isRangedAttacking = false;
    private bool isDying = false;

    // Animation frame indices (3 frames per each state) — legacy spritesheet only
    private const int IDLE_START_FRAME = 0;
    private const int MELEE_ATTACK_START_FRAME = 3;
    private const int DYING_START_FRAME = 6;
    private const int RANGED_ATTACK_START_FRAME = 9;
    private const int FRAMES_PER_ANIMATION = 3;

    // Frame-clip state. All per-instance: two co-op players animate independently
    // while sharing the same Sprite assets.
    private bool _useFrameClips;
    private Sprite[] _clip;
    private int _clipOffset;             // first frame of the active direction block
    private int _clipLength;             // frames in the active block
    private int _clipCursor;             // 0 .. _clipLength-1
    private float _clipFrameTime;        // seconds per frame
    private float _clipTimer;
    private bool _clipLoop;
    private bool _clipFinished;
    private int _clipFramesPerDirection; // 0 = not directional
    private PlayerAim _aim;
    private Vector2 _lastFacing = Vector2.right;
    private float _attackWindow;
    private PlayerProceduralAnimFx _fx;

    void Start()
    {
        footsteps = AudioManager.instance.CreateInstance(FMODEvents.instance.footstepsSound);

        pstats = GetComponent<PlayerStats>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        _aim = GetComponent<PlayerAim>();

        pstats.dashesLeft = pstats.maxDashes;
        dashStaminaCost = pstats.maxStamina / pstats.maxDashes;

        // Decide the backend BEFORE the legacy loader runs: in frame-clip mode the old
        // Resources spritesheet is never loaded, so its "expected at least 12 sprites"
        // LogError can't fire for a project that no longer ships it.
        _useFrameClips = ResolveFrameClips();

        if (!_useFrameClips)
        {
            LoadPlayerSprites();

            if (playerSprites != null && playerSprites.Length > 0)
            {
                spriteRenderer.sprite = playerSprites[0];
            }
        }

#if !UNITY_EDITOR
        // In a build there is no AssetDatabase to fall back on: if the arrays weren't
        // serialised onto the prefab the player silently ships with the old dummy sheet.
        // Say so loudly in the player log rather than leaving it to be noticed on stream.
        if (!_useFrameClips)
            Debug.LogWarning("[PlayerMovement] No animation frames were serialised onto the prefab, " +
                             "so this build is running the legacy spritesheet. In the editor, select " +
                             "Player.prefab, check Walk/Attack Frames are populated, and save.");
#endif

        // Scale the player
        transform.localScale = new Vector3(playerScale, playerScale, 1f);

        // Procedural FX. Created after the scale is set so it captures the right rest
        // pose. Safe to add at runtime: everything it does is undone for gameplay.
        if (enableProceduralFx)
        {
            _fx = GetComponent<PlayerProceduralAnimFx>();
            if (_fx == null) _fx = gameObject.AddComponent<PlayerProceduralAnimFx>();
        }

        // Start the Idle animation
        //StartCoroutine(DelayedStartAnimation());
        PlayIdleAnimation();
    }

    private IEnumerator DelayedStartAnimation()
    {
        yield return null; // Wait one frame
        PlayIdleAnimation();
    }

    private void LoadPlayerSprites()
    {
        if (playerSprites != null && playerSprites.Length >= 12)
        {
            Debug.Log($"Using Inspector-assigned sprites: {playerSprites.Length} sprites");
            return;
        }

        //TODO move the sprite paths to the Battle Orchestrator when ready
        Sprite[] loadedSprites = Resources.LoadAll<Sprite>("Sprites/player/player_character_spritesheet4");

        if (loadedSprites != null && loadedSprites.Length >= 12)
        {
            playerSprites = loadedSprites;

            for (int i = 0; i < Mathf.Min(3, playerSprites.Length); i++)
            {
            }
        }
        else
        {
            Debug.LogError($"Failed to load player sprites. Found {(loadedSprites?.Length ?? 0)} sprites, expected at least 12.");
            Texture2D spritesheet = Resources.Load<Texture2D>("Sprites/player/player_character_spritesheet4");
            if (spritesheet != null)
            {
                Debug.LogWarning("Found texture but not sprites. Make sure the texture is set to 'Sprite (2D and UI)' and sliced properly.");
            }
        }
    }

    // True when there is enough new art to run the frame-clip backend.
    private bool ResolveFrameClips()
    {
        // A hole in an array (deleted frame, failed import) would throw on the frame it
        // lands on, minutes into a run. Strip holes once, here, loudly.
        walkFrames = Compact(walkFrames, "Walk Frames");
        attackFrames = Compact(attackFrames, "Attack Frames");
        rangedAttackFrames = Compact(rangedAttackFrames, "Ranged Attack Frames");
        idleFrames = Compact(idleFrames, "Idle Frames");
        deathFrames = Compact(deathFrames, "Death Frames");

#if UNITY_EDITOR
        // Editor safety net: if the arrays somehow never got serialised (fresh paste,
        // reverted prefab), read the folders now so Play still shows the new art.
        if (walkFrames.Length == 0 && attackFrames.Length == 0 && autoLoadFramesInEditor)
            LoadFramesFromFolders(false);
#endif

        bool any = walkFrames.Length > 0 || attackFrames.Length > 0;

        if (any && walkFrames.Length == 0)
            Debug.LogWarning("[PlayerMovement] Attack frames are assigned but walk frames are not — " +
                             "idle/walk will fall back to the first attack frame.");

        return any;
    }

    private static Sprite[] Compact(Sprite[] src, string label)
    {
        if (src == null) return Array.Empty<Sprite>();

        int holes = 0;
        for (int i = 0; i < src.Length; i++) if (src[i] == null) holes++;
        if (holes == 0) return src;

        Debug.LogWarning($"[PlayerMovement] {label} has {holes} empty slot(s); they were skipped. " +
                         "Right-click the component ▸ Reload Animation Frames to rebuild the list.");

        var packed = new Sprite[src.Length - holes];
        int w = 0;
        for (int i = 0; i < src.Length; i++) if (src[i] != null) packed[w++] = src[i];
        return packed;
    }

    void Update()
    {
        RegenerateDash();
        UpdateAnimationState();

        // Facing is tracked in BOTH modes: the FX component needs a direction even when
        // the legacy spritesheet is driving the frames. The flipX write inside is still
        // gated on frame-clip mode, so legacy play looks exactly as it always did.
        UpdateFacing();

        if (_useFrameClips)
            AdvanceClip();

        if (_fx != null)
            _fx.SetLocomotion(IsMovingForAnimation(), _lastFacing);
    }


    // Update the player's sortingOrder based on Y position every frame.
    // Same formula as GrassCartoonOverlay baked values:
    //   sortingOrder = sortOrderBase + round(-(y + sortYOffset) * sortPrecision)
    // This is 1 integer assignment per frame on 1 object — zero performance cost.
    void LateUpdate()
    {
        if (spriteRenderer != null)
        {
            float sortY = transform.position.y + sortYOffset;
            spriteRenderer.sortingOrder = sortOrderBase + Mathf.RoundToInt(-sortY * sortPrecision);
        }
    }

    private void UpdateAnimationState()
    {
        if (isDying)
            return;

        // Priority: Ranged > Melee > (Walk) > Idle. Walk only exists in frame-clip
        // mode, so in legacy mode this reduces to the original three-branch chain.
        if (isRangedAttacking && currentAnimationState != AnimationState.RangedAttack)
        {
            PlayRangedAttackAnimation();
        }
        else if (isMeleeAttacking && currentAnimationState != AnimationState.MeleeAttack)
        {
            PlayMeleeAttackAnimation();
        }
        else if (!isMeleeAttacking && !isRangedAttacking)
        {
            if (_useFrameClips && IsMovingForAnimation())
            {
                if (currentAnimationState != AnimationState.Walk) PlayWalkAnimation();
            }
            else if (currentAnimationState != AnimationState.Idle)
            {
                PlayIdleAnimation();
            }
        }
    }

    private bool IsMovingForAnimation()
    {
        if (DownedFreeze) return false;
        if (isDashing) return true;
        return move.sqrMagnitude > moveThreshold * moveThreshold;
    }

    private void PlayIdleAnimation()
    {
        if (currentAnimationState == AnimationState.Idle)
            return;

        if (_useFrameClips)
        {
            currentAnimationState = AnimationState.Idle;

            if (idleFrames.Length > 0)
                PlayClip(idleFrames, idleFps, true, 0);
            else
                HoldFrame(walkFrames.Length > 0 ? walkFrames : attackFrames, idleHoldFrame);
            return;
        }

        if (playerSprites == null)
            return;

        currentAnimationState = AnimationState.Idle;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            IDLE_START_FRAME,
            animationSpeed
        ));
    }

    // Frame-clip mode only: there is no walk block in the legacy sheet, so legacy
    // play never reaches this and keeps idling exactly as it did.
    private void PlayWalkAnimation()
    {
        if (!_useFrameClips || currentAnimationState == AnimationState.Walk)
            return;

        currentAnimationState = AnimationState.Walk;

        if (walkFrames.Length > 0)
            PlayClip(walkFrames, walkFps, true, walkFramesPerDirection);
        else
            HoldFrame(attackFrames, 0);
    }

    private void PlayMeleeAttackAnimation()
    {
        if (currentAnimationState == AnimationState.MeleeAttack)
            return;

        if (_useFrameClips)
        {
            currentAnimationState = AnimationState.MeleeAttack;
            PlayAttackClip(attackFrames);
            return;
        }

        if (playerSprites == null)
            return;

        currentAnimationState = AnimationState.MeleeAttack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            MELEE_ATTACK_START_FRAME,
            animationSpeed
        ));
    }

    private void PlayRangedAttackAnimation()
    {
        if (currentAnimationState == AnimationState.RangedAttack)
            return;

        if (_useFrameClips)
        {
            currentAnimationState = AnimationState.RangedAttack;
            PlayAttackClip(rangedAttackFrames.Length > 0 ? rangedAttackFrames : attackFrames);
            return;
        }

        if (playerSprites == null)
            return;

        currentAnimationState = AnimationState.RangedAttack;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            RANGED_ATTACK_START_FRAME,
            animationSpeed
        ));
    }

    // One-shot attack: plays through once, holds the last frame if the attack window
    // outlasts it. The rate is derived from how long the attack state will be held, so
    // a 48-frame swing fits inside the 0.3s window instead of showing its first 7 frames.
    private void PlayAttackClip(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
        {
            HoldFrame(walkFrames, idleHoldFrame);
            return;
        }

        int perDirection = attackFramesPerDirection;
        int blockLength = (facingMode == FacingMode.DirectionalFrames && perDirection > 0)
            ? perDirection
            : frames.Length;

        float window = _attackWindow > 0.01f ? _attackWindow : attackWindowFallback;

        float fps = attackFps;
        if (fitAttackToAttackWindow && window > 0.01f)
            fps = Mathf.Clamp(blockLength / window, 1f, 240f);

        PlayClip(frames, fps, false, perDirection);
    }

    public void PlayDeathAnimation()
    {
        if (currentAnimationState == AnimationState.Dying)
            return;

        if (_useFrameClips)
        {
            isDying = true;
            currentAnimationState = AnimationState.Dying;

            if (deathFrames.Length > 0)
                PlayClip(deathFrames, deathFps, false, 0);
            else
                HoldFrame(walkFrames.Length > 0 ? walkFrames : attackFrames,
                          downedPoseFrame < 0 ? int.MaxValue : downedPoseFrame);
            return;
        }

        if (playerSprites == null)
            return;

        isDying = true;
        currentAnimationState = AnimationState.Dying;

        if (currentAnimationCoroutine != null)
            StopCoroutine(currentAnimationCoroutine);

        currentAnimationCoroutine = StartCoroutine(Utilities.AnimateSprite(
            spriteRenderer,
            playerSprites,
            true,
            FRAMES_PER_ANIMATION,
            DYING_START_FRAME,
            animationSpeed
        ));
    }

    //  FRAME-CLIP PLAYER

    private void PlayClip(Sprite[] frames, float fps, bool loop, int framesPerDirection)
    {
        if (frames == null || frames.Length == 0) return;

        // Stop any legacy coroutine so the two backends can never fight over
        // spriteRenderer.sprite.
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }

        _clip = frames;
        _clipFramesPerDirection = (facingMode == FacingMode.DirectionalFrames && framesPerDirection > 0)
            ? Mathf.Min(framesPerDirection, frames.Length)
            : 0;

        _clipLength = _clipFramesPerDirection > 0 ? _clipFramesPerDirection : frames.Length;
        _clipOffset = _clipFramesPerDirection > 0 ? DirectionBlockOffset() : 0;
        _clipCursor = 0;
        _clipTimer = 0f;
        _clipLoop = loop;
        _clipFinished = false;
        _clipFrameTime = 1f / Mathf.Max(1f, fps);

        ApplyCurrentFrame();
    }

    // Stop animating and sit on one frame. int.MaxValue means "the last frame".
    private void HoldFrame(Sprite[] frames, int index)
    {
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }

        _clip = null;
        if (frames == null || frames.Length == 0 || spriteRenderer == null) return;

        spriteRenderer.sprite = frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }

    private void AdvanceClip()
    {
        if (_clip == null || _clipLength <= 0) return;

        // Directional clips re-pick their block every frame but KEEP the cursor, so
        // turning on the spot swaps the angle without restarting the stride.
        if (_clipFramesPerDirection > 0)
        {
            int offset = DirectionBlockOffset();
            if (offset != _clipOffset)
            {
                _clipOffset = offset;
                ApplyCurrentFrame();
            }
        }

        if (_clipFinished) return;

        // Scaled time on purpose: timeScale = 0 freezes the animation, exactly like
        // the WaitForSeconds in the legacy coroutine did.
        _clipTimer += Time.deltaTime;
        if (_clipTimer < _clipFrameTime) return;

        // Loop rather than if(): a long frame (spike, stage load) catches up instead of
        // playing the rest of the clip in slow motion.
        while (_clipTimer >= _clipFrameTime)
        {
            _clipTimer -= _clipFrameTime;
            _clipCursor++;

            if (_clipCursor >= _clipLength)
            {
                if (_clipLoop)
                {
                    _clipCursor = 0;
                }
                else
                {
                    _clipCursor = _clipLength - 1;
                    _clipFinished = true;
                    break;
                }
            }
        }

        ApplyCurrentFrame();
    }

    private void ApplyCurrentFrame()
    {
        if (spriteRenderer == null || _clip == null) return;

        int i = _clipOffset + _clipCursor;
        if (i < 0 || i >= _clip.Length) return;

        var s = _clip[i];
        if (s != null) spriteRenderer.sprite = s;
    }

    //  FACING

    private void UpdateFacing()
    {
        // Downed/dead: freeze facing so the body doesn't keep pivoting to a stale aim
        // vector while it's meant to be lying there.
        if (isDying || DownedFreeze) return;

        Vector2 f = ReadFacing();
        if (f.sqrMagnitude > 0.0001f) _lastFacing = f.normalized;

        if (!_useFrameClips) return;   // never re-flip the legacy dummy sheet
        if (facingMode != FacingMode.MirrorHorizontally || spriteRenderer == null) return;

        // Deadzone: near-vertical facing must not flap flipX every frame.
        if (Mathf.Abs(_lastFacing.x) < 0.05f) return;

        bool facingLeft = _lastFacing.x < 0f;
        spriteRenderer.flipX = artFacesRight ? facingLeft : !facingLeft;
    }

    private Vector2 ReadFacing()
    {
        if (facingSource == FacingSource.Aim && _aim != null)
            return _aim.Direction;

        return move.sqrMagnitude > 0.0001f ? move : _lastFacing;
    }

    private int DirectionBlockOffset()
    {
        if (_clip == null || _clipFramesPerDirection <= 0) return 0;

        int directions = Mathf.Max(1, _clip.Length / _clipFramesPerDirection);
        float angle = Mathf.Atan2(_lastFacing.y, _lastFacing.x) * Mathf.Rad2Deg;

        float relative = directionsClockwise
            ? firstDirectionAngle - angle
            : angle - firstDirectionAngle;

        relative = Mathf.Repeat(relative, 360f);

        int index = Mathf.RoundToInt(relative / (360f / directions)) % directions;
        return index * _clipFramesPerDirection;
    }

    // Co-op: Dwned freeze + visual
    // Additive and gated: DownedFreeze defaults false and is only ever set by the
    // co-op PlayerDownedState component (which only acts when Count > 1), so the
    // single-player movement path is byte-identical. The component is kept ENABLED
    // while downed (so the prone-animation coroutine keeps running); FixedUpdate
    // just bails on DownedFreeze, exactly like the grapple case above.
    public bool DownedFreeze { get; private set; }

    /// <summary>Enter the downed look: freeze movement and play the prone/dying frames.</summary>
    public void EnterDownedVisual()
    {
        DownedFreeze = true;
        // A corpse shouldn't breathe. Suspending also parks the transform back at its
        // exact rest scale/rotation, so nothing is left deformed while downed.
        if (_fx != null) _fx.SetSuspended(true);
        PlayDeathAnimation();
    }

    /// <summary>Leave the downed look: unfreeze and return to the idle animation.</summary>
    public void ExitDownedVisual()
    {
        DownedFreeze = false;
        isDying = false;                                // clear the death lock
        currentAnimationState = (AnimationState)(-1);   // force the next state to re-play
        if (_fx != null) _fx.SetSuspended(false);
        PlayIdleAnimation();
    }

    /// <summary>
    /// Begin the melee attack pose. <paramref name="windowSeconds"/> is how long the
    /// caller will hold this state (PlayerAttack's attackAnimationDuration) so the
    /// sequence can be fitted to it. 0 / no argument = use attackWindowFallback.
    /// </summary>
    public void StartMeleeAttack(float windowSeconds)
    {
        _attackWindow = Mathf.Max(0f, windowSeconds);
        isMeleeAttacking = true;

        // Fired here rather than from the clip code so the flourish also plays in legacy
        // spritesheet mode, and so it lands on the frame the swing actually starts.
        if (_fx != null)
            _fx.TriggerAttack(_lastFacing, _attackWindow > 0.01f ? _attackWindow : attackWindowFallback);
    }

    public void StartMeleeAttack()
    {
        StartMeleeAttack(0f);
    }

    public void EndMeleeAttack()
    {
        isMeleeAttacking = false;
    }

    /// <summary>Ranged equivalent of <see cref="StartMeleeAttack(float)"/>.</summary>
    public void StartRangedAttack(float windowSeconds)
    {
        _attackWindow = Mathf.Max(0f, windowSeconds);
        isRangedAttacking = true;

        if (_fx != null)
            _fx.TriggerAttack(_lastFacing, _attackWindow > 0.01f ? _attackWindow : attackWindowFallback);
    }

    public void StartRangedAttack()
    {
        StartRangedAttack(0f);
    }

    public void EndRangedAttack()
    {
        isRangedAttacking = false;
    }

    // TODO Review and remove legacy methods for backward compatibility
    public void StartAttack()
    {
        // Handled in PlayerAttack script
    }

    public void EndAttack()
    {
        // Handled PlayerAttack script
    }

    private void FixedUpdate()
    {
        // Grappling hook owns position while active — bail out completely so
        // we don't fight it with MovePosition or input-based movement.
        // Phase 7 (co-op): DownedFreeze bails the same way while this player is
        // downed. Default false → single player is unaffected.
        if (IsBeingGrappled || DownedFreeze)
            return;

        if (isDashing)
        {
            rb.MovePosition(rb.position + dashDirection * pstats.dashSpeed * Time.fixedDeltaTime);
            dashTimer -= Time.fixedDeltaTime;
            if (dashTimer <= 0f)
            {
                isDashing = false;
            }
            return;
        }

        float currentSpeed = pstats.moveSpeed;

        bool isTryingToSprint = isSprinting && move.magnitude > 0.01f && pstats.currentStamina > 0;

        if (isTryingToSprint && pstats.currentStamina > 0)
        {
            currentSpeed *= pstats.sprintMultiplier;
            pstats.currentStamina -= pstats.staminaDrainRate * Time.fixedDeltaTime;
            pstats.currentStamina = Mathf.Max(pstats.currentStamina, 0f);
        }
        else
        {
            pstats.currentStamina += pstats.staminaRegenRate * Time.fixedDeltaTime;
            pstats.currentStamina = Mathf.Min(pstats.currentStamina, pstats.maxStamina);
        }

        Vector2 movement = move.normalized * currentSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);

        UpdateSound();
    }

    public void Move(InputAction.CallbackContext context)
    {
        move = context.ReadValue<Vector2>();
    }

    // Sprint = HOLD to run. Bound to leftShift / leftShoulder / leftStickPress (L3).
    // Dashing is now a SEPARATE action (see Dash below) so the left-stick press can
    // run WITHOUT also dashing.
    public void Sprint(InputAction.CallbackContext context)
    {
        isSprinting = context.ReadValueAsButton();
    }

    // Dash = PRESS to dash. Bound to leftShift (keyboard) + buttonEast 'B' (gamepad).
    // Wire this to the "Dash" action in the PlayerInput Events list (it is already
    // wired in Player.prefab). ButtonControl covers both keys and pad buttons.
    public void Dash(InputAction.CallbackContext context)
    {
        if (context.started && context.control is ButtonControl btn && btn.wasPressedThisFrame)
            TryDash();
    }

    private void TryDash()
    {
        // Guard against calls before everything is initialized
        if (rb == null || pstats == null)
            return;

        if (!isDashing && pstats.dashesLeft > 0 && Time.time - lastDashTime >= pstats.dashCooldown)
        {
            if (AudioManager.instance != null && FMODEvents.instance != null)
                AudioManager.instance.PlayOneShot(FMODEvents.instance.dashSound, rb.position);

            dashDirection = move.normalized;
            if (dashDirection == Vector2.zero)
                dashDirection = Vector2.up;

            isDashing = true;
            dashTimer = pstats.dashTime;
            lastDashTime = Time.time;
            pstats.dashesLeft--;

            pstats.currentStamina -= dashStaminaCost;
            pstats.currentStamina = Mathf.Max(pstats.currentStamina, 0f);
        }
    }

    private void RegenerateDash()
    {
        if (pstats.dashesLeft >= pstats.maxDashes)
            return;

        dashRegenTimer += Time.deltaTime;

        if (dashRegenTimer >= pstats.dashRegenRate && pstats.currentStamina > dashStaminaCost)
        {
            pstats.dashesLeft++;
            dashRegenTimer = 0f;
        }
    }

    private void UpdateSound()
    {
        if (move.magnitude > 0.01f)
        {
            // Keep the (now 3D) footsteps event positioned on this player, or FMOD mutes it.
            footsteps.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(transform));

            PLAYBACK_STATE playbackState;
            footsteps.getPlaybackState(out playbackState);
            if (playbackState.Equals(PLAYBACK_STATE.STOPPED))
            {
                footsteps.start();
            }
        }
        else
        {
            footsteps.stop(STOP_MODE.ALLOWFADEOUT);
        }
    }

    //  EDITOR AUTO-LOAD
    //
    // Everything below is compiled out of builds. It reads the frame folders straight
    // from the project and writes the Sprite references into the arrays above, which
    // are then serialised onto the prefab — so the build never touches AssetDatabase
    // and never needs a Resources folder.

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!autoLoadFramesInEditor) return;
        if (Application.isPlaying) return;

        bool haveFrames = (walkFrames != null && walkFrames.Length > 0)
                       || (attackFrames != null && attackFrames.Length > 0);
        if (haveFrames) return;

        // AssetDatabase must not be touched from inside OnValidate (it runs during
        // deserialisation), so defer by a tick.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;          // component died during the recompile
            LoadFramesFromFolders(true);
        };
    }

    [ContextMenu("Reload Animation Frames")]
    private void ReloadAnimationFrames() => LoadFramesFromFolders(true);

    // Loads each folder, sorted by the number at the end of the file name. Sorting is
    // the whole point: Unity's asset search order is not frame order.
    private void LoadFramesFromFolders(bool markDirty)
    {
        walkFrames = LoadFolder(walkFolder);
        attackFrames = LoadFolder(attackFolder);
        rangedAttackFrames = LoadFolder(rangedAttackFolder);
        idleFrames = LoadFolder(idleFolder);
        deathFrames = LoadFolder(deathFolder);

        if (walkFrames.Length == 0 && attackFrames.Length == 0)
        {
            Debug.LogWarning($"[PlayerMovement] No frames found in '{walkFolder}' or '{attackFolder}'. " +
                             "Check the paths (project-relative, e.g. Assets/Art/Player/Walk) and that " +
                             "the PNGs import as Sprite (2D and UI).", this);
        }
        else
        {
            Debug.Log($"[PlayerMovement] Loaded {walkFrames.Length} walk + {attackFrames.Length} attack frame(s).", this);
        }

        if (markDirty)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
    }

    private static Sprite[] LoadFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return Array.Empty<Sprite>();

        folder = folder.Replace('\\', '/').TrimEnd('/');
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) return Array.Empty<Sprite>();

        var guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        var found = new List<(int number, string name, Sprite sprite)>(guids.Length);

        foreach (var guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);

            // LoadAllAssetsAtPath rather than LoadAssetAtPath<Sprite> so a frame left in
            // Multiple sprite mode still contributes its sub-sprites.
            foreach (var obj in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s)
                    found.Add((TrailingNumber(System.IO.Path.GetFileNameWithoutExtension(path)), s.name, s));
            }
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


