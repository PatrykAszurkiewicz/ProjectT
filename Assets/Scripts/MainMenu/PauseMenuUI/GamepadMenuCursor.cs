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

    [Tooltip("TEMP: log why the cursor is visible during gameplay, so we can find " +
             "what keeps showing it. Rate-limited. Turn off once diagnosed.")]
    public bool logCursorState = true;

    // Diagnostic state (see LateUpdate).
    private static bool _dbgWroteHiddenLastFrame;
    private static float _dbgNextLog;

    // Menu-side diagnostics (see LateUpdate / EndOfFrameAssert).
    private static int _dbgStackChangedFrame = -1;   // frame UIModalStack last changed
    private static int _dbgMenuAssertFrame = -1;     // frame we last asserted a MENU state
    private static bool _dbgMenuAssertVisible;
    private static int _dbgLoggedPadCount = -1;
    private static float _dbgFrozenSince = -1f;

    private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
    private Coroutine _endOfFrameRoutine;

    /// <summary>Set by a mouse-only overlay (the IMGUI DebugMenu panel) that needs the OS
    /// pointer while it is open even if a gamepad is connected. Only consulted while a
    /// menu is open; unlike <see cref="ForceCursorVisible"/> it never affects gameplay.</summary>
    public static bool MenuPointerRequired;

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
        MenuPointerRequired = false;
        _dbgStackChangedFrame = -1;
        _dbgMenuAssertFrame = -1;
        _dbgMenuAssertVisible = false;
        _dbgLoggedPadCount = -1;
        _dbgFrozenSince = -1f;
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
        // Self-heal the scene kind: if the orchestrator wasn't in the scene at load
        // time (spawned a frame or two later by a loader) _menuScene would be stuck
        // TRUE and we'd treat live gameplay as a menu — cursor parked on screen.
        // Flipping only menu->gameplay here can't cause the first-frame flash the
        // load-time snapshot was written to avoid (that needs gameplay->menu).
        if (_menuScene && GameOrchestrator.Instance != null) _menuScene = false;

        // ---- DIAGNOSTIC ------------------------------------------------------
        // If we drove the cursor HIDDEN last frame but it is visible again now, and
        // we're in plain gameplay, then some OTHER script re-showed it after our
        // LateUpdate. That is the only way this branch can be reached with a true
        // cursor. Names the culprit category so we can stop guessing.
        if (logCursorState && !_menuScene && !UIModalStack.IsOpen && !ForceCursorVisible
            && hideCursorInGameplay && _dbgWroteHiddenLastFrame && Cursor.visible)
        {
            DbgLog("Cursor is visible in gameplay even though THIS component hid it last " +
                   "frame → an EXTERNAL script sets Cursor.visible=true during gameplay " +
                   "(search your project for 'Cursor.visible = true'), OR a second " +
                   "GamepadMenuCursor with different settings runs after this one.");
        }
        // ----------------------------------------------------------------------

        // A connected pad drives menus by focus (MenuNavigator) and the pad-cursor,
        // so a visible OS pointer just clutters the screen. The cursor is therefore
        // shown only for mouse-and-keyboard players.
        bool padConnected = Gamepad.all.Count > 0;

        if (logCursorState) DiagnoseMenuCursorAtLateUpdate(padConnected);

        // Escape hatch: gameplay code that needs the OS pointer wins outright.
        if (ForceCursorVisible)
        {
            if (logCursorState && !_menuScene && !UIModalStack.IsOpen)
                DbgLog("Cursor visible because ForceCursorVisible == true (some gameplay " +
                       "mode set it and may not have cleared it).");
            SetCursor(true);
            if (_menuScene || UIModalStack.IsOpen) NoteMenuAssert(true);
            return;
        }

        if (_menuScene)
        {
            if (!showCursorInMenuScenes) { _dbgWroteHiddenLastFrame = false; return; }
            bool vis = MenuCursorVisible(padConnected);
            SetCursor(vis);
            NoteMenuAssert(vis);
            _dbgWroteHiddenLastFrame = false;   // menu state, not the gameplay hide
            return;
        }

        // ---- gameplay scene ----

        // A menu / reward screen is up. Pause, options, augment, the debug panel AND
        // the post-stage reward screen all register with UIModalStack now, so this
        // one test covers every case. Same rule: visible for mouse, hidden for pad.
        if (UIModalStack.IsOpen)
        {
            bool vis = MenuCursorVisible(padConnected);
            SetCursor(vis);
            NoteMenuAssert(vis);
            _dbgWroteHiddenLastFrame = false;
            _dbgFrozenSince = -1f;
            return;
        }

        if (logCursorState) DiagnoseFrozenWithoutModal();

        // Live gameplay, nothing open -> the cursor is never wanted.
        if (!hideCursorInGameplay)
        {
            if (logCursorState && Cursor.visible)
                DbgLog($"Cursor visible because hideCursorInGameplay == false on '{name}'. " +
                       $"Tick it ON in the Inspector (there are " +
                       $"{FindObjectsByType<GamepadMenuCursor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length} " +
                       $"GamepadMenuCursor instance(s) in the scene).");
            _dbgWroteHiddenLastFrame = false;   // opted out; we did NOT hide it
            return;
        }

        SetCursor(false);
        _dbgWroteHiddenLastFrame = true;        // we drove it hidden this frame
    }

    private void DbgLog(string msg)
    {
        if (Time.unscaledTime < _dbgNextLog) return;
        _dbgNextLog = Time.unscaledTime + 1f;   // at most once/sec
        Debug.LogWarning("[CursorDiag] " + msg);
    }

    private static void SetCursor(bool visible)
    {
        if (Cursor.visible != visible) Cursor.visible = visible;
        if (visible && Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;
    }

    //  Menu cursor: enforced LAST in the frame 
    // While a menu was open this component asserted "visible for mouse players" only in
    // LateUpdate, with no execution order. Cursor.visible is last-writer-wins, so any
    // script whose LateUpdate ran after it — or that hides the pointer from OnGUI or an
    // end-of-frame coroutine — got the final word, and the menu opened with no pointer.
    // The project does have per-frame cursor hiders outside this file: IntroTutorial has
    // to disable "Cursor" / "Crosshair" components while it is up, and DebugMenu's
    // LateUpdate exists specifically to beat "a per-frame cursor-hider elsewhere".
    //
    // Fix: while a menu owns the cursor, re-assert the same decision at END OF FRAME,
    // after every Update, LateUpdate, coroutine and OnGUI. Scope is deliberately limited
    // to menus; plain gameplay keeps exactly the old LateUpdate-only behaviour.

    private static bool MenuCursorVisible(bool padConnected)
        => !padConnected || MenuPointerRequired;

    /// <summary>True if a menu owns the cursor right now; <paramref name="visible"/> is the
    /// state it should have. Mirrors the menu branches of LateUpdate exactly.</summary>
    private bool TryGetMenuCursorState(out bool visible)
    {
        visible = false;
        if (_menuScene)
        {
            if (!showCursorInMenuScenes && !ForceCursorVisible) return false;
        }
        else if (!UIModalStack.IsOpen)
        {
            return false;
        }

        visible = ForceCursorVisible || MenuCursorVisible(Gamepad.all.Count > 0);
        return true;
    }

    private static void NoteMenuAssert(bool visible)
    {
        _dbgMenuAssertFrame = Time.frameCount;
        _dbgMenuAssertVisible = visible;
    }

    private void OnEnable()
    {
        UIModalStack.OnChanged -= OnModalStackChanged;
        UIModalStack.OnChanged += OnModalStackChanged;
        _endOfFrameRoutine = StartCoroutine(EndOfFrameAssert());
    }

    private static void OnModalStackChanged() => _dbgStackChangedFrame = Time.frameCount;

    private System.Collections.IEnumerator EndOfFrameAssert()
    {
        while (true)
        {
            yield return EndOfFrame;

            // Disabling a component does not stop its coroutines; honour it anyway.
            if (!isActiveAndEnabled) continue;
            if (!TryGetMenuCursorState(out bool visible)) continue;

            if (logCursorState && visible && !Cursor.visible
                && _dbgMenuAssertFrame == Time.frameCount && _dbgMenuAssertVisible
                && _dbgStackChangedFrame != Time.frameCount)
            {
                DbgLog("A menu is open and the cursor should be visible, but a script HID it " +
                       "after GamepadMenuCursor.LateUpdate this frame (a later LateUpdate or " +
                       "OnGUI — typically a crosshair / custom-cursor script). Re-asserted at " +
                       "end of frame. Search the project for 'Cursor.visible = false'.");
            }

            SetCursor(visible);
            NoteMenuAssert(visible);
        }
    }

    // Menu open but the cursor ends up hidden: say WHY, once, in plain words.
    private void DiagnoseMenuCursorAtLateUpdate(bool padConnected)
    {
        if (!TryGetMenuCursorState(out bool wantVisible)) { _dbgLoggedPadCount = -1; return; }

        // (a) Hidden on purpose because a gamepad is connected. If the player has no
        //     controller plugged in, the device list names the phantom (Steam Input,
        //     DS4Windows / ViGEm, Parsec, a gaming mouse's HID interface, …).
        if (!wantVisible && padConnected)
        {
            int n = Gamepad.all.Count;
            if (n != _dbgLoggedPadCount || _dbgStackChangedFrame == Time.frameCount)
            {
                _dbgLoggedPadCount = n;
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    var p = Gamepad.all[i];
                    if (i > 0) names.Append(", ");
                    names.Append(p != null ? $"'{p.displayName}' ({p.layout})" : "null");
                }
                Debug.Log($"[CursorDiag] Menu open → cursor hidden because {n} gamepad(s) are " +
                          $"connected: {names}. If you have no controller plugged in, that " +
                          "device is virtual — close the software creating it.");
            }
            return;
        }
        _dbgLoggedPadCount = -1;

        // (b) Should be visible, we left it visible at the end of last frame, and it is
        //     hidden now without the modal stack changing → an Update / coroutine hid it.
        if (wantVisible && !Cursor.visible
            && _dbgMenuAssertFrame == Time.frameCount - 1 && _dbgMenuAssertVisible
            && _dbgStackChangedFrame != Time.frameCount)
        {
            DbgLog("A menu is open and the cursor should be visible, but a script hid it " +
                   "during Update this frame. GamepadMenuCursor restores it; search the " +
                   "project for 'Cursor.visible = false' to remove the fight.");
        }
    }

    // Gameplay scene, clock frozen for a while, nothing on UIModalStack: some screen is
    // pausing the game the legacy way (Time.timeScale = 0) without registering, so the
    // gameplay rule hides the pointer on top of it.
    private void DiagnoseFrozenWithoutModal()
    {
        if (Time.timeScale != 0f) { _dbgFrozenSince = -1f; return; }
        if (_dbgFrozenSince < 0f) { _dbgFrozenSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _dbgFrozenSince < 0.5f) return;   // ignore hit-stops

        DbgLog("Time.timeScale has been 0 for a while but NO screen is registered with " +
               "UIModalStack, so this counts as gameplay and the cursor is hidden. The " +
               "screen that is open should call UIModalStack.Push(this) / Pop(this).");
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

    private void OnDisable()
    {
        UIModalStack.OnChanged -= OnModalStackChanged;
        if (_endOfFrameRoutine != null) { StopCoroutine(_endOfFrameRoutine); _endOfFrameRoutine = null; }
        ReleaseMouseButton(Mouse.current);
    }

    private void ReleaseMouseButton(Mouse mouse)
    {
        if (mouse == null || !wasClickDown) { wasClickDown = false; return; }
        var st = new MouseState { position = mouse.position.ReadValue() };
        st = st.WithButton(MouseButton.Left, false);
        InputSystem.QueueStateEvent(mouse, st);
        wasClickDown = false;
    }
}



