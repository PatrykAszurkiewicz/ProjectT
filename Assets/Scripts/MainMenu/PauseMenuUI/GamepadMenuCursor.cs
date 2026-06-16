using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;


// Lets a controller operate the (mouse-driven) menus while the game is paused

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

    void Update()
    {
        bool paused = Time.timeScale == 0f;
        var pad = Gamepad.current;
        var mouse = Mouse.current;

        if (!paused || pad == null || mouse == null)
        {
            active = false;
            return;
        }

        Vector2 stick = pad.rightStick.ReadValue();
        bool clickDown = pad.rightTrigger.ReadValue() > clickThreshold;

        // Hand control to the pad when the stick/trigger is used; hand it back to
        // the real mouse the moment the mouse is moved — so they never fight.
        if (stick.magnitude > stickDeadzone || clickDown)
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

        if (!active) return;

        // Drive the real cursor with the stick (unscaled time — timeScale is 0 paused).
        if (stick.magnitude > stickDeadzone)
            pos += stick * speed * Time.unscaledDeltaTime;
        pos.x = Mathf.Clamp(pos.x, 0f, Screen.width);
        pos.y = Mathf.Clamp(pos.y, 0f, Screen.height);
        mouse.WarpCursorPosition(pos);

        // Right trigger -> real left mouse button. The button is a packed bit, so a
        // float delta event is rejected; a full MouseState event sets it correctly.
        // Queue only on change for a clean press -> release -> click.
        if (clickDown != wasClickDown)
        {
            var st = new MouseState { position = pos };
            st = st.WithButton(MouseButton.Left, clickDown);
            InputSystem.QueueStateEvent(mouse, st);
        }
        wasClickDown = clickDown;
    }
}


