using UnityEngine;
using UnityEngine.InputSystem;

/// Single source of truth for "where is the player aiming". Mouse and gamepad both feed into this via the PlayerInput component.
/// Two outputs, used by different weapon types:
///   Direction  normalized absolute aim direction (right stick / mouse vector).
///                  Used by directional weapons (melee, ranged, flamethrower, shield)
///                  and the on-screen directional cursor. Snappy.
///   WorldPoint a world position. For the mouse this is the cursor's world
///                  position. For the gamepad it's a free-steered "virtual cursor"
///                  reticle: the right stick MOVES it (like a trackball), it holds
///                  position when the stick is released, and it's clamped to
///                  reticleMaxRange of the player. Used by ground-target weapons
///                  (mortar / smoke).
[RequireComponent(typeof(PlayerInput))]
public class PlayerAim : MonoBehaviour
{
    public static PlayerAim Instance { get; private set; }

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

    private Camera cam;
    private PlayerInput playerInput;
    private Vector2 lookInputVector = Vector2.zero;

    // Steered gamepad reticle state.
    private Vector3 reticleWorld;
    private bool reticleSeeded;

    void Awake()
    {
        Instance = this;
        cam = Camera.main;
        playerInput = GetComponent<PlayerInput>();
    }

    void OnDestroy()
    {
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
        if (cam == null) cam = Camera.main;

        if (playerInput != null)
            UsingGamepad = (playerInput.currentControlScheme == "Gamepad");

        if (UsingGamepad)
        {
            UpdateGamepadAim();
        }
        else
        {
            UpdateMouseAim();
            // Re-seed the reticle in front of the player next time we pick up the pad.
            reticleSeeded = false;
        }
    }

    private void UpdateGamepadAim()
    {
        // Absolute aim direction — drives directional weapons + the on-screen cursor.
        if (lookInputVector.magnitude > stickDeadzone)
            Direction = lookInputVector.normalized;

        // Steered ground reticle — drives mortar / smoke.
        if (!reticleSeeded)
        {
            reticleWorld = transform.position + (Vector3)(Direction * reticleSeedDistance);
            reticleSeeded = true;
        }

        // Move the reticle proportionally to stick push (small push = slow, full = fast).
        if (lookInputVector.magnitude > stickDeadzone)
            reticleWorld += (Vector3)(lookInputVector * reticleMoveSpeed * Time.deltaTime);

        // Clamp within reach of the player and flatten to the play plane.
        Vector3 off = reticleWorld - transform.position;
        if (off.sqrMagnitude > reticleMaxRange * reticleMaxRange)
            reticleWorld = transform.position + off.normalized * reticleMaxRange;
        reticleWorld.z = 0f;

        WorldPoint = reticleWorld;
    }

    private void UpdateMouseAim()
    {
        var mouse = Mouse.current;
        if (mouse == null || cam == null) return;

        Vector3 mw = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        mw.z = 0f;
        WorldPoint = mw;

        Vector2 d = ((Vector2)mw - (Vector2)transform.position);
        if (d.sqrMagnitude > 0.0001f)
            Direction = d.normalized;
    }
}

