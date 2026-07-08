using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Single source of truth for "where is the player aiming". Mouse and gamepad both feed into this via the PlayerInput component.
// Two outputs, used by different weapon types:
//   Direction  normalized absolute aim direction (right stick / mouse vector).
//                  Used by directional weapons (melee, ranged, flamethrower, shield)
//                  and the on-screen directional cursor. Snappy.
//   WorldPoint a world position. For the mouse this is the cursor's world
//                  position. For the gamepad it's a free-steered "virtual cursor"
//                  reticle: the right stick MOVES it (like a trackball), it holds
//                  position when the stick is released, and it's clamped to
//                  reticleMaxRange of the player. Used by ground-target weapons (mortar / smoke).
[RequireComponent(typeof(PlayerInput))]
public class PlayerAim : MonoBehaviour
{
    public static PlayerAim Instance { get; private set; }

    // Co-op: every live PlayerAim registers itself here so per-player consumers
    // (e.g. the NightOverlay torch cones) can iterate all players instead of
    // reading the single global Instance, which only ever reflects one player.
    // In single player this list has exactly one entry.
    public static readonly List<PlayerAim> All = new List<PlayerAim>();

    [Header("Gamepad")]
    [Tooltip("Right-stick magnitude required before it counts as aiming.")]
    public float stickDeadzone = 0.25f;

    [Header("Gamepad reticle (mortar / smoke ground-aim)")]
    [Tooltip("How fast the right stick slides the ground reticle, in world units " +
             "per second at full stick deflection.")]
    public float reticleMoveSpeed = 12f;

    [Tooltip("Furthest the reticle can sit from the player (world units).")]
    public float reticleMaxRange = 12f;

    [Tooltip("Where the reticle first appears (distance in front of the player) " +
             "when you pick up the gamepad.")]
    public float reticleSeedDistance = 5f;

    /// <summary>True while the Gamepad control scheme is active.</summary>
    public bool UsingGamepad { get; private set; }

    /// <summary>Normalized world-space aim direction. Never zero.</summary>
    public Vector2 Direction { get; private set; } = Vector2.right;

    /// <summary>World-space point being aimed at (mouse cursor, or steered gamepad reticle).</summary>
    public Vector3 WorldPoint { get; private set; }

    /// <summary>Raw Look input this frame (right stick / mouse delta), BEFORE the
    /// "sticky" normalization that <see cref="Direction"/> applies. Unlike Direction,
    /// this returns to ~zero when the stick is released, which makes it suitable for
    /// menu navigation (flick-to-step that re-arms on release).</summary>
    public Vector2 LookInput => lookInputVector;

    private Camera cam;
    private PlayerInput playerInput;
    private PlayerRef playerRef;
    private Vector2 lookInputVector = Vector2.zero;

    // Steered gamepad reticle state.
    private Vector3 reticleWorld;
    private bool reticleSeeded;

    void Awake()
    {
        // NOTE: Instance is kept as a single-player fallback through Phase 2.
        // It is removed in Phase 3 once every consumer reads its own sibling
        // PlayerAim. In co-op the last-spawned player wins Instance, which is
        // fine because the only Phase-2 consumer (the shared cursor) is still
        // single until Phase 3.
        Instance = this;
        playerRef = GetComponent<PlayerRef>();
        cam = ResolveCamera();
        playerInput = GetComponent<PlayerInput>();
    }

    // Per-player camera: prefer this player's assigned camera (set by
    // PlayerCameraController in co-op), fall back to Camera.main in single player.
    private Camera ResolveCamera()
    {
        if (playerRef != null && playerRef.Camera != null) return playerRef.Camera;
        return Camera.main;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (Instance == this) Instance = null;
    }

    /// Hook this up to your "Look" action in the PlayerInput Events section,
    /// exactly like OnAttackWeapon / OnAttackTool in PlayerAttack.cs.
    public void OnLook(InputAction.CallbackContext context)
    {
        lookInputVector = context.ReadValue<Vector2>();
    }

    void Update()
    {
        // Re-bind to this player's camera once PlayerCameraController assigns it
        // (it may be null on the spawn frame). Single player keeps Camera.main.
        if (playerRef != null && playerRef.Camera != null) cam = playerRef.Camera;
        else if (cam == null) cam = ResolveCamera();

        // Resolve THIS player's aim devices. Co-op: each player has its own paired
        // pad / mouse. Single player now binds ONE PlayerInput to keyboard+mouse AND a
        // pad at once, so both show up here.
        Gamepad pad = null;
        Mouse mouse = null;
        if (playerInput != null)
        {
            var devs = playerInput.devices;
            for (int i = 0; i < devs.Count; i++)
            {
                if (pad == null && devs[i] is Gamepad g) pad = g;
                if (mouse == null && devs[i] is Mouse m) mouse = m;
            }
        }
        if (mouse == null && playerInput == null) mouse = Mouse.current; // legacy object w/o PlayerInput

        Vector2 stick = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;

        // Pick the aim source by the device actually used, NOT by currentControlScheme.
        // The single-player setup keeps ONE PlayerInput on the Keyboard&Mouse scheme even
        // while a pad is paired (so both work), so the old scheme check left the right
        // stick dead. A player pinned to the Gamepad scheme (co-op pad player) is always
        // gamepad; otherwise the stick (past its deadzone) selects gamepad, mouse motion
        // selects mouse, and an idle frame keeps whatever was used last so aim never
        // snaps on its own.
        bool schemeGamepad = playerInput != null && playerInput.currentControlScheme == "Gamepad";
        if (schemeGamepad || stick.magnitude > stickDeadzone)
            UsingGamepad = true;
        else if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.25f)
            UsingGamepad = false;
        // else: neither active this frame — keep the previous UsingGamepad.

        if (UsingGamepad)
        {
            UpdateGamepadAim(stick);
        }
        else
        {
            UpdateMouseAim(mouse);
            // Re-seed the reticle in front of the player next time we pick up the pad.
            reticleSeeded = false;
        }
    }

    private void UpdateGamepadAim(Vector2 stick)
    {
        float mag = stick.magnitude;

        // Absolute aim direction — drives directional weapons + the on-screen cursor.
        if (mag > stickDeadzone)
            Direction = stick.normalized;

        // Steered ground reticle — drives mortar / smoke.
        if (!reticleSeeded)
        {
            reticleWorld = transform.position + (Vector3)(Direction * reticleSeedDistance);
            reticleSeeded = true;
        }

        // Move the reticle proportionally to stick push (small push = slow, full = fast).
        if (mag > stickDeadzone)
            reticleWorld += (Vector3)(stick * reticleMoveSpeed * Time.deltaTime);

        // Clamp within reach of the player and flatten to the play plane.
        Vector3 off = reticleWorld - transform.position;
        if (off.sqrMagnitude > reticleMaxRange * reticleMaxRange)
            reticleWorld = transform.position + off.normalized * reticleMaxRange;
        reticleWorld.z = 0f;

        WorldPoint = reticleWorld;
    }

    private void UpdateMouseAim(Mouse mouse)
    {
        if (mouse == null || cam == null) return;

        Vector3 mw = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        mw.z = 0f;
        WorldPoint = mw;

        Vector2 d = ((Vector2)mw - (Vector2)transform.position);
        if (d.sqrMagnitude > 0.0001f)
            Direction = d.normalized;
    }
}
