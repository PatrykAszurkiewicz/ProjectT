using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;


// Lets a controller operate the (mouse-driven) menus while the game is paused.
// The RIGHT TRIGGER is the menu "click". That is also the in-game AttackWeapon
// control, which creates two carry-over hazards handled here and in MenuInputGuard:
//  1) MENU ENTRY (this script): if the player clears the last enemy by HOLDING the
//     fire trigger, the trigger is already held the instant the augment/reward menu
//     opens. A click is a press EDGE (not-pressed -> pressed), so a carried-over
//     hold must NOT count as a click — otherwise it queues a stray mouse-button-down
//     at the wrong spot and then a held "drag" that never selects anything. We latch
//     such a carried-over hold out until it is released; the next fresh pull clicks.
//  2) MENU EXIT (MenuInputGuard): gameplay attacks are not resumed until the trigger
//     is released, so a still-held trigger can't leave the weapon unable to re-fire.

public class GamepadMenuCursor : MonoBehaviour
{
    [Tooltip("Cursor speed in screen pixels per second at full stick deflection.")]
    public float speed = 1600f;

    [Tooltip("Right-stick magnitude before the cursor starts moving.")]
    public float stickDeadzone = 0.2f;

    [Tooltip("Right-trigger pull required to count as a click.")]
    public float clickThreshold = 0.5f;

    private Vector2 pos;
    private bool active;
    private bool wasClickDown;

    // Menu-open edge detection + carry-over-trigger handling.
    private bool wasPausedLast;
    private bool swallowTrigger;   // ignore a trigger already held when the menu opened

    // Set true by a menu that wants the RIGHT TRIGGER for itself (e.g. AugmentsMenu in
    // DirectionalSwitch mode, where the trigger confirms the highlighted panel). While
    // true, the trigger neither activates the cursor nor queues a mouse click. Stick
    // movement still works. The owning menu clears it on close.
    public static bool ClicksSuppressed;

    void Update()
    {
        bool paused = Time.timeScale == 0f;
        var pad = Gamepad.current;
        var mouse = Mouse.current;

        if (!paused || pad == null || mouse == null)
        {
            active = false;
            wasPausedLast = false; // the next paused frame is a fresh menu-open
            return;
        }

        Vector2 stick = pad.rightStick.ReadValue();
        bool clickDown = pad.rightTrigger.ReadValue() > clickThreshold;

        // Menu just opened this frame. If the trigger is already held (the player was
        // firing to clear the wave), latch it out so the carried-over hold can't be
        // read as a click. It clears the moment the trigger is released below.
        if (!wasPausedLast)
        {
            swallowTrigger = clickDown;
            wasClickDown = false;
            active = false;
        }
        wasPausedLast = true;

        if (swallowTrigger && !clickDown)
            swallowTrigger = false; // released — fresh pulls now click normally

        bool effectiveClick = clickDown && !swallowTrigger;
        if (ClicksSuppressed) effectiveClick = false; // another menu owns the trigger

        // Hand control to the pad when the stick/trigger is used; hand it back to
        // the real mouse the moment the mouse is moved — so they never fight.
        if (stick.magnitude > stickDeadzone || effectiveClick)
        {
            if (!active)
            {
                active = true;
                pos = mouse.position.ReadValue(); // start from where the cursor already is
            }
        }
        else if (mouse.delta.ReadValue().magnitude > 0.5f)
        {
            active = false;
        }

        if (!active)
        {
            wasClickDown = effectiveClick;
            return;
        }

        // Drive the real cursor with the stick (unscaled time — timeScale is 0 paused).
        if (stick.magnitude > stickDeadzone)
            pos += stick * speed * Time.unscaledDeltaTime;
        pos.x = Mathf.Clamp(pos.x, 0f, Screen.width);
        pos.y = Mathf.Clamp(pos.y, 0f, Screen.height);
        mouse.WarpCursorPosition(pos);

        // Trigger -> real left mouse button. The button is a packed bit, so a float
        // delta event is rejected; a full MouseState event sets it correctly.
        // Queue only on change for a clean press -> release -> click.
        if (effectiveClick != wasClickDown)
        {
            var st = new MouseState { position = pos };
            st = st.WithButton(MouseButton.Left, effectiveClick);
            InputSystem.QueueStateEvent(mouse, st);
        }
        wasClickDown = effectiveClick;
    }
}
