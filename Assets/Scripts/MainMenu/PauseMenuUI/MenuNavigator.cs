using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// GAMEPAD MENU NAVIGATION
//   RIGHT STICK  → free cursor (GamepadMenuCursor), RIGHT TRIGGER clicks.
//   LEFT STICK / DPAD → step between buttons, sliders and toggles.
//   A (South) / Enter  → activate the focused control.
//   B (East) / Esc     → back (MenuBackInput closes the frontmost modal).
//   Sliders: while focused, left/right adjusts the value instead of moving focus.

[DisallowMultipleComponent]
public class MenuNavigator : MonoBehaviour
{
    [Header("Feel")]
    [Tooltip("Left-stick deflection that counts as a directional step.")]
    [Range(0.1f, 0.9f)] public float navDeadzone = 0.5f;

    [Tooltip("Right-stick deflection that hands control back to the free cursor.")]
    [Range(0.1f, 0.9f)] public float cursorDeadzone = 0.3f;

    [Tooltip("Seconds a direction must be held before it starts repeating.")]
    public float repeatDelay = 0.40f;

    [Tooltip("Seconds between repeats once repeating.")]
    public float repeatRate = 0.12f;

    [Tooltip("Extra margin (px) kept around the focused element when auto-scrolling.")]
    public float scrollPadding = 24f;

    [Tooltip("Fraction of a slider's range moved per left/right press. Hold to repeat.")]
    [Range(0.01f, 0.5f)] public float sliderStep = 0.05f;

    [Tooltip("Content pixels per second when the left stick scrolls a read-only panel " +
             "(e.g. the Tutorial). See the note on ReadOnlyScrollRect().")]
    public float readingScrollSpeed = 1400f;

    [Header("Behaviour")]
    [Tooltip("Focus the first control whenever a menu opens.")]
    public bool selectOnOpen = true;

    [Tooltip("Right trigger also activates the focused control, matching its role as the " +
             "'click' button in cursor mode.")]
    public bool submitWithRightTrigger = true;

    [Tooltip("Upgrade Navigation=None controls to Automatic at runtime so focus can reach " +
             "hand-authored menus built mouse-only. Does not modify assets.")]
    public bool forceAutomaticNavigation = true;

    [Tooltip("Take Move/Submit/Cancel away from InputSystemUIInputModule so they can't " +
             "double-fire alongside this script. Leave ON.")]
    public bool claimModuleActions = true;

    [Tooltip("Draw an outline around the focused control. Hand-authored Buttons usually " +
             "leave Selected Color at near-white, which is indistinguishable from Normal — " +
             "so navigation looks broken even when it's working.")]
    public bool showFocusRing = true;

    public Color focusRingColor = new Color(0.95f, 0.45f, 1f, 1f);
    public float focusRingPadding = 6f;
    public float focusRingThickness = 3f;

    [Tooltip("Log focus changes to the Console.")]
    public bool debugLog = false;

    /// <summary>True while the player is driving menus by focus rather than by cursor.
    /// GamepadScrollRouter reads this so the left stick isn't doing two jobs.</summary>
    public static bool NavigationActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => NavigationActive = false;

    private bool _selectPending;
    private Canvas _topCanvas;
    private bool _topDirty = true;
    private float _nextTopScan;

    private Vector2 _lastNav;      // direction we last acted on
    private float _repeatAt;       // unscaled time of the next repeat
    private bool _moduleClaimed;

    // Cursor intent has to be MOTION, not deflection. A worn right stick that rests at,
    // say, 0.35 magnitude reads as "the player wants the cursor" on every single frame —
    // so focus was acquired and instantly cleared, forever, and the pad behaved purely as
    // a mouse. Same for the mouse: `delta` is noisy, so compare actual positions.
    private Vector2 _rightPrev;
    private Vector2 _mousePrev;
    private bool _havePrev;
    private bool _loggedOnce;

    // A trigger already held when navigation engages (the player was firing to clear the
    // wave, or just clicked a button in cursor mode) must not instantly submit. Latch it
    // out until released — same hazard GamepadMenuCursor guards against on menu open.
    private bool _swallowTrigger;

    private static readonly List<Selectable> _scratch = new List<Selectable>();

    private void OnEnable()
    {
        UIModalStack.OnChanged += OnStackChanged;
        _topDirty = true;
        _selectPending = selectOnOpen && UIModalStack.IsOpen;
    }

    private void OnDisable()
    {
        UIModalStack.OnChanged -= OnStackChanged;
        ReleaseModuleActions();
        NavigationActive = false;
        if (_ring != null) _ring.gameObject.SetActive(false);
    }

    private void OnStackChanged()
    {
        _topDirty = true;
        if (selectOnOpen && UIModalStack.IsOpen) _selectPending = true;
    }

    //  The one and only source of Move / Submit 
    // If the EventSystem's input module ALSO dispatches Move/Submit, every press fires
    // twice — once from the module, once from us — and focus jumps two controls.
    // `sendNavigationEvents = false` is the version-proof off switch: every input module
    // gates SendMoveEventToSelectedObject / SendSubmitEventToSelectedObject on it. Poking
    // InputSystemUIInputModule's action properties directly would tie this file to one
    // Input System package version (the property names have changed across releases) —
    // and would silently do nothing if the module type were ever swapped.
    //
    // Our own ExecuteEvents.Execute calls are direct dispatch and are unaffected by it.
    // Pointer events (hover, click) are also unaffected, so the mouse still works.
    private void ClaimModuleActions()
    {
        if (_moduleClaimed || !claimModuleActions) return;
        var es = EventSystem.current;
        if (es == null) return;

        es.sendNavigationEvents = false;
        _moduleClaimed = true;
    }

    // Hand navigation back if this component is torn down, so a scene without a
    // MenuNavigator isn't left with navigation events permanently disabled.
    private void ReleaseModuleActions()
    {
        if (!_moduleClaimed) return;
        var es = EventSystem.current;
        if (es != null) es.sendNavigationEvents = true;
        _moduleClaimed = false;
    }

    private void Update()
    {
        var es = EventSystem.current;
        if (es == null) return;
        if (es.sendNavigationEvents && claimModuleActions) { _moduleClaimed = false; }  // new EventSystem
        ClaimModuleActions();

        // Outside menus nothing should be focused.
        if (!UIModalStack.MenuInputActive)
        {
            if (es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
            NavigationActive = false;
            _selectPending = false;
            _lastNav = Vector2.zero;
            UpdateFocusRing(null);
            return;
        }

        var pad = Gamepad.current;

        //  Cursor vs focus 
        bool cursorIntent = CursorIntent(pad);

        if (cursorIntent && NavigationActive)
        {
            NavigationActive = false;
            es.SetSelectedGameObject(null);
            UpdateFocusRing(null);
        }

        //  Reading panels 
        // The Tutorial's scroll area contains no Selectables at all — just text — so
        // there is nothing for focus to land on inside it, and both sticks correctly
        // reported "only buttons to move between". You could not scroll it by any means.
        //
        // When the frontmost canvas holds a scrollable panel with NOTHING focusable in it,
        // the left stick scrolls that panel and the DPAD keeps moving focus (tabs, Back).
        // A panel that DOES contain controls (the rebind list) behaves as before: the
        // stick moves focus and ScrollIntoView follows it. So this never steals the stick
        // from a menu that needs it.
        var reader = ReadOnlyScrollRect();
        bool stickScrolls = reader != null;

        if (stickScrolls && NavigationActive)
        {
            float sy = ReadStick(pad).y;
            if (Mathf.Abs(sy) > 0.01f) ScrollBy(reader, sy * readingScrollSpeed * Time.unscaledDeltaTime);
        }

        Vector2 nav = ReadNav(pad, includeStick: !stickScrolls);
        bool submit = SubmitPressed(pad);

        // Engaging focus mode still counts a stick push, even where the stick scrolls.
        if (!NavigationActive && (nav != Vector2.zero || submit || ReadStick(pad) != Vector2.zero))
        {
            NavigationActive = true;
            _selectPending = true;   // the first press FOCUSES; it must not also step
            _lastNav = nav;
            _repeatAt = Time.unscaledTime + repeatDelay;
            _swallowTrigger = pad != null && pad.rightTrigger.isPressed;
            nav = Vector2.zero;
            submit = false;
        }

        if (!NavigationActive) return;

        if (debugLog && !_loggedOnce)
        {
            _loggedOnce = true;
            var top = TopCanvas();
            int count = 0;
            if (top != null)
            {
                top.GetComponentsInChildren(false, _scratch);
                for (int i = 0; i < _scratch.Count; i++) if (Usable(_scratch[i])) count++;
            }
            Debug.Log($"[MenuNavigator] Navigation engaged. pad={(pad != null ? pad.displayName : "NONE")}, " +
                      $"eventSystem='{es.name}', topCanvas='{(top != null ? top.name : "NONE")}' " +
                      $"(sortingOrder={(top != null ? top.sortingOrder : 0)}), usableSelectables={count}");
        }

        //  Keep the selection alive and in front 
        var current = es.currentSelectedGameObject;
        if (_selectPending || current == null || !current.activeInHierarchy || !IsInTopCanvas(current))
        {
            _selectPending = false;
            var first = FirstSelectable();
            if (first != null)
            {
                es.SetSelectedGameObject(first.gameObject);
                if (debugLog) Debug.Log($"[MenuNavigator] Focus → {first.name}");
            }
            else if (debugLog)
            {
                Debug.LogWarning("[MenuNavigator] No selectable control found in the frontmost canvas.");
            }
            ScrollIntoView(es.currentSelectedGameObject);
            UpdateFocusRing(es.currentSelectedGameObject);
            return;   // don't step on the frame we acquired focus
        }

        //  Directional step, with hold-to-repeat 
        if (nav == Vector2.zero)
        {
            _lastNav = Vector2.zero;
        }
        else
        {
            bool fresh = nav != _lastNav;
            if (fresh || Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + (fresh ? repeatDelay : repeatRate);
                _lastNav = nav;

                // Sliders first. Dispatching a horizontal move at a Slider does work —
                // Slider.OnMove nudges the value — but the selection then stays put, and
                // the geometric fallback in Move() read "selection unchanged" as "nowhere
                // to go" and jumped focus to the control on the left instead. So the
                // fader never moved and focus wandered. Handle it explicitly, which also
                // lets us pick the step size instead of Unity's fixed 10%.
                if (!TryAdjustSlider(current, nav))
                {
                    Move(es, current, nav);
                    current = es.currentSelectedGameObject;
                }
            }
        }

        if (submit && current != null)
        {
            // ISubmitHandler is what Button listens to; it runs the same onClick path.
            ExecuteEvents.Execute(current, new BaseEventData(es), ExecuteEvents.submitHandler);
            if (debugLog) Debug.Log($"[MenuNavigator] Submit → {current.name}");
        }

        ScrollIntoView(es.currentSelectedGameObject);
        UpdateFocusRing(es.currentSelectedGameObject);
    }

    // Selectable.OnMove finds the neighbour and selects it; Slider.OnMove adjusts its
    // value on a horizontal move. Sending the event gets both behaviours for free.
    // Unity's Selectable.OnMove is used ONLY where a designer asked for it (Navigation =
    // Explicit). Everywhere else we do the search ourselves, because Automatic navigation
    // has two properties this UI can't live with:
    //   It searches Selectable.allSelectablesArray — EVERY selectable in the scene, on
    //     every canvas, with no idea an overlay is covering them. From the Music button in
    //     Options, "down" lands on a MenuScene button BEHIND the panel; we then reject it
    //     and re-focus the first control, so the ring appears to fall off the bottom of
    //     the list and restart at the top.
    //   It scores by alignment/distance with no notion of rows, so from "Immediate",
    //     "right" prefers "Nightmare" on the next row over "Ready" in the same one — which
    //     is why Ready was unreachable.
    // NearestInDirection is canvas-scoped and lane-aware, so it has neither problem.
    private void Move(EventSystem es, GameObject current, Vector2 dir)
    {
        var sel = current.GetComponent<Selectable>();
        if (sel != null && sel.navigation.mode == Navigation.Mode.Explicit)
        {
            var data = new AxisEventData(es) { moveVector = dir, moveDir = ToMoveDir(dir) };
            ExecuteEvents.Execute(current, data, ExecuteEvents.moveHandler);

            var landed = es.currentSelectedGameObject;
            if (landed != null && landed != current && IsInTopCanvas(landed))
            {
                if (debugLog) Debug.Log($"[MenuNavigator] {current.name} → {landed.name} (explicit)");
                return;
            }
            if (landed != current) es.SetSelectedGameObject(current);   // reject / restore
        }

        var next = NearestInDirection(current.transform as RectTransform, dir);
        if (next != null)
        {
            es.SetSelectedGameObject(next.gameObject);
            if (debugLog) Debug.Log($"[MenuNavigator] {current.name} → {next.name}");
        }
        else if (debugLog)
        {
            Debug.Log($"[MenuNavigator] {current.name}: nothing to move to ({ToMoveDir(dir)}).");
        }
    }

    /// <summary>A directional press on a focused Slider or Scrollbar changes its value,
    /// if the press is along that control's own axis. Returns true if the input was
    /// consumed (so focus must not move). Vertical scrollbars are included, so a focused
    /// scrollbar scrolls with up/down rather than throwing focus to a neighbour.</summary>
    private bool TryAdjustSlider(GameObject current, Vector2 nav)
    {
        if (current == null || nav == Vector2.zero) return false;

        var slider = current.GetComponent<Slider>();
        if (slider != null && slider.IsInteractable())
        {
            bool horizontal = slider.direction == Slider.Direction.LeftToRight ||
                              slider.direction == Slider.Direction.RightToLeft;
            if (horizontal != (nav.x != 0f)) return false;   // pressed across its axis

            float along = horizontal ? nav.x : nav.y;
            bool reversed = slider.direction == Slider.Direction.RightToLeft ||
                            slider.direction == Slider.Direction.TopToBottom;

            float range = slider.maxValue - slider.minValue;
            float step = slider.wholeNumbers ? 1f : Mathf.Max(range * sliderStep, 0.0001f);
            float sign = reversed ? -along : along;

            slider.value = Mathf.Clamp(slider.value + sign * step, slider.minValue, slider.maxValue);
            if (debugLog) Debug.Log($"[MenuNavigator] {slider.name} = {slider.value:0.###}");
            return true;
        }

        var bar = current.GetComponent<Scrollbar>();
        if (bar != null && bar.IsInteractable())
        {
            bool horizontal = bar.direction == Scrollbar.Direction.LeftToRight ||
                              bar.direction == Scrollbar.Direction.RightToLeft;
            if (horizontal != (nav.x != 0f)) return false;

            float along = horizontal ? nav.x : nav.y;
            bool reversed = bar.direction == Scrollbar.Direction.RightToLeft ||
                            bar.direction == Scrollbar.Direction.TopToBottom;

            bar.value = Mathf.Clamp01(bar.value + (reversed ? -along : along) * sliderStep);
            return true;
        }

        return false;
    }

    // Nearest usable control along `dir`, within the frontmost canvas.
    //
    // A plain weighted sum of (distance along) and (drift across) cannot express what a
    // player expects, and both weightings I tried failed in opposite ways:
    //
    //   along + across*2   → pressing DOWN from a full-width button skipped past the
    //                        Countdown|Immediate|Ready row to a distant but perfectly
    //                        centred button further down. Across was punished too little.
    //   along*4 + across   → pressing RIGHT from Immediate preferred "Nightmare" on the
    //                        NEXT ROW (nearer horizontally) over "Ready" in the same row.
    //                        Along dominated, so leaving the row was cheap. This is why
    //                        Ready was unreachable.
    //
    // The thing that actually matters is whether the candidate SHARES A LANE with the
    // source — do their rects overlap on the axis perpendicular to travel? Same row when
    // moving sideways; same column when moving up/down. In-lane candidates always beat
    // out-of-lane ones.
    //
    // Among in-lane candidates, the NEAREST ROW must win outright, and only then should
    // sideways distance choose the column. Blending the two (`along + across*0.5`) let a
    // perfectly-centred control one row further away beat an offset control in the very
    // next row:
    //
    //   Normal|Nightmare + DOWN → skipped "Display Unlocked Weapons" (across 195) and
    //                             landed on "Tutorial" a row below it (across 0).
    //   Close + UP             → skipped the Tutorial|Lore row entirely, straight to
    //                             "Display Unlocked Weapons".
    //   Display + DOWN         → skipped Tutorial|Lore, straight to "Close".
    //
    // So `along` is bucketed (8 px, to absorb layout jitter) and dominates absolutely;
    // `across` only separates controls that are genuinely in the same row.
    private Selectable NearestInDirection(RectTransform from, Vector2 dir)
    {
        if (from == null) return null;
        var top = TopCanvas();
        if (top == null) return null;

        top.GetComponentsInChildren(false, _scratch);

        bool horizontal = Mathf.Abs(dir.x) > 0.5f;
        Vector2 origin = from.position;
        Vector2 fromExt = HalfExtents(from);

        Selectable best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < _scratch.Count; i++)
        {
            var s = _scratch[i];
            if (!Usable(s) || s.transform == from) continue;

            var rt = s.transform as RectTransform;
            if (rt == null) continue;

            Vector2 delta = (Vector2)rt.position - origin;
            float along = Vector2.Dot(delta, dir);
            if (along <= 1f) continue;                       // not in that direction

            Vector2 toExt = HalfExtents(rt);
            float across = horizontal ? Mathf.Abs(delta.y) : Mathf.Abs(delta.x);
            float lane = horizontal ? (fromExt.y + toExt.y) : (fromExt.x + toExt.x);

            // 0.9 so controls that merely graze each other don't count as the same lane.
            bool inLane = across < lane * 0.9f;

            // Nearest row first (bucketed), then nearest column within that row.
            float rowBucket = Mathf.Round(along / RowTolerance);
            float score = (inLane ? 0f : 1e9f) + rowBucket * 10000f + across;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // Controls whose centres are within this many screen pixels along the travel axis
    // count as the same row/column. Absorbs layout rounding so a row of buttons that
    // differ by a pixel isn't split into two "rows".
    private const float RowTolerance = 8f;

    private static Vector2 HalfExtents(RectTransform rt)
    {
        Vector2 size = Vector2.Scale(rt.rect.size, rt.lossyScale);
        return new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f;
    }

    private static MoveDirection ToMoveDir(Vector2 v)
    {
        if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) return v.x > 0 ? MoveDirection.Right : MoveDirection.Left;
        return v.y > 0 ? MoveDirection.Up : MoveDirection.Down;
    }

    // Raw left-stick vector, before it is snapped to a cardinal direction. Needed so a
    // reading panel can be scrolled smoothly and proportionally.
    private Vector2 ReadStick(Gamepad pad)
    {
        if (pad == null) return Vector2.zero;
        Vector2 v = pad.leftStick.ReadValue();
        return v.magnitude > navDeadzone ? v : Vector2.zero;
    }

    // One cardinal direction, never diagonal — these menus are lists.
    private Vector2 ReadNav(Gamepad pad, bool includeStick)
    {
        Vector2 v = Vector2.zero;

        if (pad != null)
        {
            if (includeStick) v = ReadStick(pad);

            if (pad.dpad.up.isPressed) v = Vector2.up;
            else if (pad.dpad.down.isPressed) v = Vector2.down;
            else if (pad.dpad.left.isPressed) v = Vector2.left;
            else if (pad.dpad.right.isPressed) v = Vector2.right;
        }

        var kb = Keyboard.current;
        if (v == Vector2.zero && kb != null)
        {
            if (kb.upArrowKey.isPressed) v = Vector2.up;
            else if (kb.downArrowKey.isPressed) v = Vector2.down;
            else if (kb.leftArrowKey.isPressed) v = Vector2.left;
            else if (kb.rightArrowKey.isPressed) v = Vector2.right;
        }

        if (v == Vector2.zero) return Vector2.zero;
        return Mathf.Abs(v.x) > Mathf.Abs(v.y)
            ? new Vector2(Mathf.Sign(v.x), 0f)
            : new Vector2(0f, Mathf.Sign(v.y));
    }

    private bool SubmitPressed(Gamepad pad)
    {
        if (pad != null)
        {
            if (pad.buttonSouth.wasPressedThisFrame) return true;

            if (submitWithRightTrigger)
            {
                // rightTrigger is a ButtonControl, so it has a proper press edge at its
                // default press point — no manual thresholding needed.
                if (!pad.rightTrigger.isPressed) _swallowTrigger = false;
                else if (!_swallowTrigger && pad.rightTrigger.wasPressedThisFrame) return true;
            }
        }
        var kb = Keyboard.current;
        return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
    }

    // "The player wants the cursor" means the right stick or the mouse MOVED — not that
    // they're sitting somewhere off centre. Stick drift and idle mouse jitter otherwise
    // cancel focus on every frame, which is exactly what made navigation look dead.
    private bool CursorIntent(Gamepad pad)
    {
        Vector2 right = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;
        var mouse = Mouse.current;
        Vector2 mousePos = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

        if (!_havePrev)
        {
            _rightPrev = right; _mousePrev = mousePos; _havePrev = true;
            return false;
        }

        bool stickMoved = right.magnitude > cursorDeadzone && (right - _rightPrev).magnitude > 0.02f;

        // The cursor's warp echo arrives as a synthetic mouse-move one frame after the
        // warp; ignore the whole window rather than trying to subtract it out.
        bool mouseMoved = !GamepadMenuCursor.WarpedRecently &&
                          (mousePos - _mousePrev).magnitude > 2f;

        _rightPrev = right;
        _mousePrev = mousePos;
        return stickMoved || mouseMoved;
    }

    // ── Frontmost canvas ────────────────────────────────────────────────────
    private Canvas TopCanvas()
    {
        if (!_topDirty && _topCanvas != null && _topCanvas.isActiveAndEnabled)
            return _topCanvas;

        // Throttle: without this, a frame where no canvas qualifies would run a full
        // FindObjectsByType<Canvas> every frame.
        if (Time.unscaledTime < _nextTopScan && !_topDirty) return _topCanvas;
        _nextTopScan = Time.unscaledTime + 0.2f;
        _topDirty = false;

        _topCanvas = null;
        int best = int.MinValue;

        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isActiveAndEnabled || !c.isRootCanvas) continue;
            if (c.sortingOrder < best) continue;
            if (!HasSelectable(c)) continue;
            best = c.sortingOrder;
            _topCanvas = c;
        }
        return _topCanvas;
    }

    private bool HasSelectable(Canvas c)
    {
        c.GetComponentsInChildren(false, _scratch);
        for (int i = 0; i < _scratch.Count; i++)
            if (Usable(_scratch[i])) return true;
        return false;
    }

    private bool IsInTopCanvas(GameObject go)
    {
        var top = TopCanvas();
        if (top == null) return true;
        var c = go.GetComponentInParent<Canvas>();
        return c != null && c.rootCanvas == top;
    }

    private bool Usable(Selectable s)
    {
        if (s == null || !s.IsActive() || !s.IsInteractable()) return false;
        if (s.navigation.mode == Navigation.Mode.None)
        {
            // A mouse-only menu would otherwise be unreachable by the pad.
            if (!forceAutomaticNavigation) return false;
            var n = s.navigation;
            n.mode = Navigation.Mode.Automatic;
            s.navigation = n;
        }
        return true;
    }

    // Reading order: topmost, then leftmost.
    private Selectable FirstSelectable()
    {
        var top = TopCanvas();
        if (top == null) return null;

        top.GetComponentsInChildren(false, _scratch);

        Selectable best = null;
        Vector3 bestPos = Vector3.zero;
        for (int i = 0; i < _scratch.Count; i++)
        {
            var s = _scratch[i];
            if (!Usable(s)) continue;

            Vector3 p = s.transform.position;
            if (best == null || p.y > bestPos.y + 0.5f ||
                (Mathf.Abs(p.y - bestPos.y) <= 0.5f && p.x < bestPos.x))
            {
                best = s;
                bestPos = p;
            }
        }
        return best;
    }

    //  Focus ring 
    // Unity shows focus through Selectable.colors.selectedColor. Hand-authored buttons
    // leave that at ~white, which reads as "not highlighted", so a perfectly working
    // navigation system looks like it does nothing. Draw our own outline instead of
    // relying on whatever the artist left in the inspector.
    private RectTransform _ring;
    private static Sprite _ringSprite;

    private void UpdateFocusRing(GameObject selected)
    {
        if (!showFocusRing) { if (_ring != null) _ring.gameObject.SetActive(false); return; }

        if (selected == null || !NavigationActive)
        {
            if (_ring != null) _ring.gameObject.SetActive(false);
            return;
        }

        var target = selected.transform as RectTransform;
        var canvas = selected.GetComponentInParent<Canvas>();
        if (target == null || canvas == null)
        {
            if (_ring != null) _ring.gameObject.SetActive(false);
            return;
        }

        if (_ring == null) BuildRing();

        // Re-parent to whichever canvas owns the focused control, and draw on top of it.
        var root = canvas.rootCanvas.transform;
        if (_ring.parent != root) _ring.SetParent(root, false);
        _ring.SetAsLastSibling();

        _ring.gameObject.SetActive(true);

        // Match the target's screen rect. Anchors are centred so a plain position +
        // sizeDelta copy is enough for both overlay and camera canvases.
        _ring.position = target.position;
        Vector2 size = Vector2.Scale(target.rect.size, target.lossyScale);
        Vector2 ringScale = _ring.lossyScale;
        if (ringScale.x != 0f && ringScale.y != 0f) size = new Vector2(size.x / ringScale.x, size.y / ringScale.y);
        _ring.sizeDelta = size + Vector2.one * (focusRingPadding * 2f);
    }

    private void BuildRing()
    {
        var go = new GameObject("MenuFocusRing", typeof(RectTransform));
        _ring = (RectTransform)go.transform;
        _ring.anchorMin = _ring.anchorMax = new Vector2(0.5f, 0.5f);
        _ring.pivot = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.sprite = RingSprite();
        img.type = Image.Type.Sliced;
        img.color = focusRingColor;
        img.raycastTarget = false;   // must never eat a click
    }

    // A 9-sliced hollow square: opaque border, transparent middle.
    private Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;

        const int n = 16;
        int t = Mathf.Max(1, Mathf.RoundToInt(focusRingThickness));
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool border = x < t || y < t || x >= n - t || y >= n - t;
                tex.SetPixel(x, y, border ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        tex.Apply();

        float b = t + 1;
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f,
                                    0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        return _ringSprite;
    }

    // ── Reading panels ──────────────────────────────────────────────────────
    /// <summary>
    /// A scrollable ScrollRect in the frontmost canvas whose content holds no focusable
    /// control — a pure reading area. Returns null when the panel has controls inside it
    /// (then focus navigation plus ScrollIntoView already handles scrolling), or when
    /// nothing needs scrolling.
    /// </summary>
    private ScrollRect _reader;
    private Canvas _readerCanvas;
    private float _nextReaderScan;

    private ScrollRect ReadOnlyScrollRect()
    {
        var top = TopCanvas();
        if (top == null) return null;

        // Cache: this runs every frame, and GetComponentsInChildren allocates.
        if (top == _readerCanvas && Time.unscaledTime < _nextReaderScan &&
            (_reader == null || _reader.isActiveAndEnabled))
            return _reader;

        _readerCanvas = top;
        _nextReaderScan = Time.unscaledTime + 0.25f;
        _reader = null;

        var rects = top.GetComponentsInChildren<ScrollRect>(false);
        for (int i = 0; i < rects.Length; i++)
        {
            var sr = rects[i];
            if (sr == null || !sr.isActiveAndEnabled || sr.content == null || sr.viewport == null) continue;
            if (sr.content.rect.height - sr.viewport.rect.height <= 1f) continue;   // fits

            sr.content.GetComponentsInChildren(false, _scratch);
            bool hasControl = false;
            for (int j = 0; j < _scratch.Count; j++)
                if (Usable(_scratch[j])) { hasControl = true; break; }

            if (!hasControl) { _reader = sr; return _reader; }
        }
        return null;
    }

    // deltaPixels > 0 scrolls toward the top of the content (stick up reveals earlier text).
    private static void ScrollBy(ScrollRect sr, float deltaPixels)
    {
        float hidden = sr.content.rect.height - sr.viewport.rect.height;
        if (hidden <= 1f) return;
        sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + deltaPixels / hidden);
    }

    // ── Auto-scroll ─────────────────────────────────────────────────────────
    private void ScrollIntoView(GameObject selected)
    {
        if (selected == null) return;

        var scroll = selected.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;

        var target = selected.transform as RectTransform;
        if (target == null) return;

        float hidden = scroll.content.rect.height - scroll.viewport.rect.height;
        if (hidden <= 1f) return;   // content fits

        Vector3 contentLocal = scroll.content.InverseTransformPoint(target.position);
        float elementTop = -contentLocal.y + target.rect.height * 0.5f + scrollPadding;
        float elementBottom = -contentLocal.y - target.rect.height * 0.5f - scrollPadding;

        float viewTop = (1f - scroll.verticalNormalizedPosition) * hidden;
        float viewBottom = viewTop + scroll.viewport.rect.height;

        float scrolled = viewTop;
        if (elementTop < viewTop) scrolled = elementTop;
        else if (elementBottom > viewBottom) scrolled = elementBottom - scroll.viewport.rect.height;
        else return;   // already visible

        scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - scrolled / hidden);
    }
}


