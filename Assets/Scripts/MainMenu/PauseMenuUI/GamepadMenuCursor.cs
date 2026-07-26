using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Pad-driven mouse cursor for menus. RIGHT STICK moves, RIGHT TRIGGER clicks.
// (For focus-based navigation — left stick / dpad stepping between buttons and
// sliders — see MenuNavigator, which auto-installs alongside this.)

public class GamepadMenuCursor : MonoBehaviour
{
    [Tooltip("Cursor speed in screen pixels per second at full stick deflection.")]
    public float speed = 1600f;

    [Tooltip("Right-stick magnitude before the cursor starts moving.")]
    public float stickDeadzone = 0.2f;

    [Tooltip("Right-trigger pull required to count as a click.")]
    public float clickThreshold = 0.5f;

    [Tooltip("Move the real OS pointer to match. Turn off if warping stutters on your platform; " +
             "the click still lands, you just won't see the hardware cursor follow.")]
    public bool warpRealCursor = true;

    [Tooltip("How far (screen px) the mouse must report from our warp target before we accept it " +
             "as real player movement and hand control back to the mouse.")]
    public float realMouseTolerance = 12f;

    [Tooltip("Keep the system cursor visible and unlocked in scenes that contain no " +
             "GameOrchestrator (main menu, lobbies).")]
    public bool showCursorInMenuScenes = true;

    [Tooltip("Hide the system cursor during gameplay while no menu is open. Turn off if a " +
             "gameplay mode of yours needs the OS pointer. Set ForceCursorVisible from code " +
             "for a temporary override.")]
    public bool hideCursorInGameplay = true;

    private Vector2 pos;
    private bool active;
    private bool wasClickDown;

    private bool wasMenuOpenLast;
    private bool swallowTrigger;   // ignore a trigger already held when the menu opened

    /// <summary>
    /// True if this component warped the OS pointer on this frame OR the previous one.
    /// The warp's echo — a synthetic mouse-move event — arrives on the frame AFTER the
    /// warp, so a strict "this frame" test never catches it. (The earlier version was
    /// always false: it compared _warpFrame to Time.frameCount at the TOP of Update,
    /// before the warp that would have set it, and by the next frame the counter had
    /// already moved on. MenuNavigator therefore saw the echo as real mouse movement
    /// and dropped focus every single frame — which is why navigation looked dead.)
    /// </summary>
    public static bool WarpedRecently => Time.frameCount - _warpFrame <= 1;
    private static int _warpFrame = -1;

    // Set by a menu that wants the RIGHT TRIGGER for itself (e.g. AugmentsMenu in
    // DirectionalSwitch mode). The owning menu clears it on close.
    public static bool ClicksSuppressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ClicksSuppressed = false;
        _warpFrame = -1;
        _menuScene = false;
        ForceCursorVisible = false;
    }

    //  Self-install 
    // MenuScene has no GamepadMenuCursor object, so the pad could never move the
    // cursor there. Ensure exactly one of each menu-input helper exists in whatever
    // scene just loaded. A scene-placed instance always wins, so a tuned gameplay
    // object is untouched.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureInScene();
        RefreshSceneKind();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        EnsureInScene();
        RefreshSceneKind();
    }

    //  Cursor visibility: ONE owner, both directions 
    // Cursor.visible is GLOBAL and survives LoadScene, so it needs an owner that asserts
    // it every frame rather than a pile of screens each writing it once.
    //
    // What used to hide it in gameplay was PauseMenuController doing a bare
    // `Cursor.visible = false` on resume. Replacing that with "restore the captured
    // baseline" (so a menu opened over another menu couldn't clobber it) removed the only
    // thing that ever hid the pointer — and forcing it visible in menu scenes then
    // guaranteed the captured baseline was `true` on the way into a run. Result: a cursor
    // parked on the gameplay screen until some menu happened to open.
    //
    // Rule, asserted in LateUpdate (after UIModalStack has applied its own state):
    //   • scene with no GameOrchestrator  → nothing but menus: visible + unlocked.
    //   • gameplay scene, no modal open   → hidden.
    //   • gameplay scene, modal open      → UIModalStack owns it (it shows the cursor).
    //
    // Tested against the SCENE, not GameOrchestrator.Instance: Instance is null for the
    // first frames of a gameplay scene, which would flash the cursor on every load.
    private static bool _menuScene;

    /// <summary>Set from gameplay code that temporarily needs the OS pointer (a build
    /// mode, a world-space picker). Overrides <see cref="hideCursorInGameplay"/>.</summary>
    public static bool ForceCursorVisible;

    private static void RefreshSceneKind()
    {
        _menuScene = FindFirstObjectByType<GameOrchestrator>(FindObjectsInactive.Include) == null;
    }

    private void LateUpdate()
    {
        if (_menuScene)
        {
            if (!showCursorInMenuScenes) return;
            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            return;
        }

        if (!hideCursorInGameplay || ForceCursorVisible) return;

        // A modal is up: UIModalStack already made the cursor visible. Don't fight it.
        if (UIModalStack.IsOpen) return;

        if (Cursor.visible) Cursor.visible = false;
    }

    private static void EnsureInScene()
    {
        bool hasCursor = FindFirstObjectByType<GamepadMenuCursor>(FindObjectsInactive.Include) != null;
        bool hasScroll = FindFirstObjectByType<GamepadScrollRouter>(FindObjectsInactive.Include) != null;
        bool hasNav = FindFirstObjectByType<MenuNavigator>(FindObjectsInactive.Include) != null;
        if (hasCursor && hasScroll && hasNav) return;

        var go = new GameObject("MenuInput (auto)");
        if (!hasCursor) go.AddComponent<GamepadMenuCursor>();
        if (!hasScroll) go.AddComponent<GamepadScrollRouter>();
        if (!hasNav) go.AddComponent<MenuNavigator>();
    }

    // A menu is up if the modal stack says so, the clock is frozen, or we're in a
    // scene with no run (the main menu is entirely a menu).
    private static bool MenuOpen => UIModalStack.MenuInputActive;

    void Update()
    {
        bool menuOpen = MenuOpen;
        var pad = Gamepad.current;
        var mouse = Mouse.current;

        if (!menuOpen || pad == null || mouse == null)
        {
            if (wasMenuOpenLast) ReleaseMouseButton(mouse);
            active = false;
            wasClickDown = false;
            wasMenuOpenLast = false;
            return;
        }

        Vector2 stick = pad.rightStick.ReadValue();
        bool clickDown = pad.rightTrigger.ReadValue() > clickThreshold;

        if (!wasMenuOpenLast)
        {
            swallowTrigger = clickDown;   // trigger held over from clearing the wave
            wasClickDown = false;
            active = false;
        }
        wasMenuOpenLast = true;

        if (swallowTrigger && !clickDown) swallowTrigger = false;

        bool effectiveClick = clickDown && !swallowTrigger;
        if (ClicksSuppressed) effectiveClick = false;

        // While the player is stepping through controls with the left stick, the trigger
        // means "activate the FOCUSED control" (MenuNavigator handles it). If we also
        // fired a mouse click, one pull would both submit the focused button AND click
        // wherever the stale cursor happened to be parked. It also must not drag the
        // cursor back into existence and steal focus.
        if (MenuNavigator.NavigationActive) effectiveClick = false;

        if (stick.magnitude > stickDeadzone || effectiveClick)
        {
            if (!active)
            {
                active = true;
                pos = mouse.position.ReadValue();
            }
        }
        else if (RealMouseMoved(mouse))
        {
            if (active && wasClickDown) ReleaseMouseButton(mouse);
            active = false;
        }

        if (!active)
        {
            wasClickDown = false;
            return;
        }

        // Move (unscaled — timeScale is 0 while paused).
        if (stick.magnitude > stickDeadzone)
            pos += stick * speed * Time.unscaledDeltaTime;
        pos.x = Mathf.Clamp(pos.x, 0f, Screen.width);
        pos.y = Mathf.Clamp(pos.y, 0f, Screen.height);

        // Assert position AND buttons every frame. A warp event that arrives out of
        // order can now only cost one frame instead of eating the whole click.
        var st = new MouseState { position = pos };
        st = st.WithButton(MouseButton.Left, effectiveClick);
        InputSystem.QueueStateEvent(mouse, st);

        if (warpRealCursor)
        {
            mouse.WarpCursorPosition(pos);
            _warpFrame = Time.frameCount;
        }

        wasClickDown = effectiveClick;
    }

    // Real movement lands far from where we last warped the pointer; the warp's own
    // echo lands right on top of it. Reading `mouse.delta` alone cannot tell them
    // apart, which is what made the pad cursor flicker on and off every frame.
    private bool RealMouseMoved(Mouse mouse)
    {
        if (mouse.delta.ReadValue().magnitude <= 0.5f) return false;
        if (!active || !warpRealCursor) return true;
        return (mouse.position.ReadValue() - pos).magnitude > realMouseTolerance;
    }

    private void OnDisable() => ReleaseMouseButton(Mouse.current);

    private void ReleaseMouseButton(Mouse mouse)
    {
        if (mouse == null || !wasClickDown) { wasClickDown = false; return; }
        var st = new MouseState { position = mouse.position.ReadValue() };
        st = st.WithButton(MouseButton.Left, false);
        InputSystem.QueueStateEvent(mouse, st);
        wasClickDown = false;
    }
}

