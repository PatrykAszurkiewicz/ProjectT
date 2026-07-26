using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

// Shared look-and-feel + procedural UI factory for the in-game menus
// (ControlRebindScreen, OptionsMenu). Keeps the panel/button sprites, purple
// theme, fonts and widget construction in ONE place so the menus match.
public static class MenuTheme
{
    public const string PanelSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/MenuPanel 1";
    public const string ButtonSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button 1";

    public static readonly Color Magenta = new Color(0.78f, 0.30f, 0.92f, 1f);
    public static readonly Color Violet = new Color(0.49f, 0.18f, 0.78f, 1f);
    public static readonly Color GradTop = new Color(0.11f, 0.05f, 0.18f, 0.98f);
    public static readonly Color GradBottom = new Color(0.03f, 0.01f, 0.05f, 0.98f);
    public static readonly Color PanelSolid = new Color(0.10f, 0.07f, 0.15f, 0.98f);
    public static readonly Color BtnSolid = new Color(0.17f, 0.12f, 0.24f, 1f);
    public static readonly Color BtnActive = new Color(0.55f, 0.24f, 0.78f, 1f);
    public static readonly Color TextCol = new Color(0.95f, 0.92f, 0.99f, 1f);
    public static readonly Color ValueCol = new Color(1.00f, 0.86f, 1.00f, 1f);

    private static bool _spritesLoaded;
    private static Sprite _panel, _button;
    public static Sprite PanelSprite { get { EnsureSprites(); return _panel; } }
    public static Sprite ButtonSprite { get { EnsureSprites(); return _button; } }

    private static void EnsureSprites()
    {
        if (_spritesLoaded) return;
        _panel = Resources.Load<Sprite>(PanelSpritePath);
        _button = Resources.Load<Sprite>(ButtonSpritePath);
        _spritesLoaded = true;
    }

    private static bool _fontResolved;
    private static TMP_FontAsset _cachedFont;

    // Priority: explicit TMP asset → explicit .ttf (converted) → Resources TMP →
    // Resources .ttf (converted) → default. Cached so it resolves once.
    public static TMP_FontAsset ResolveFont(TMP_FontAsset tmpSlot, Font ttfSlot)
    {
        // Reuse a previously resolved font (so assigning it on either menu skins both).
        if (_fontResolved && _cachedFont != null) return _cachedFont;

        if (tmpSlot != null) { _cachedFont = tmpSlot; _fontResolved = true; return _cachedFont; }

        if (ttfSlot != null) _cachedFont = TMP_FontAsset.CreateFontAsset(ttfSlot);

        if (_cachedFont == null)
        {
            string[] tmpPaths = { "Fonts/Cinzel-ExtraBold SDF", "Fonts/Cinzel/Cinzel-ExtraBold SDF", "Cinzel-ExtraBold SDF" };
            foreach (var p in tmpPaths) { var f = Resources.Load<TMP_FontAsset>(p); if (f != null) { _cachedFont = f; break; } }
        }
        if (_cachedFont == null)
        {
            string[] ttfPaths = { "Fonts/Cinzel-ExtraBold", "Fonts/Cinzel/static/Cinzel-ExtraBold", "Cinzel-ExtraBold" };
            foreach (var p in ttfPaths) { var f = Resources.Load<Font>(p); if (f != null) { _cachedFont = TMP_FontAsset.CreateFontAsset(f); break; } }
        }

        // Mark resolved only once we actually have a font, so a menu opened later
        // with the font assigned can still populate the cache.
        if (_cachedFont != null) _fontResolved = true;
        return _cachedFont;
    }

    //  generated gradient sprites 
    public static Sprite VerticalGradient(Color top, Color bottom)
    {
        const int h = 128;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(h - 1)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
    }

    public static Sprite HorizontalFade()
    {
        const int w = 128;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            float a = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(t - 0.5f) * 2f);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f), 100f);
    }

    //  widget factory 
    public static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    public static TextMeshProUGUI NewText(string text, Transform parent, float size,
                                          TextAlignmentOptions align, TMP_FontAsset font = null)
    {
        var go = NewUI("Text", parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = TextCol;
        tmp.alignment = align; tmp.richText = true;
        if (font != null) tmp.font = font;
        go.AddComponent<LayoutElement>();
        return tmp;
    }

    public static Button NewButton(string text, Transform parent, float fontSize, TMP_FontAsset font = null)
    {
        var go = NewUI("Button", parent);
        var img = go.AddComponent<Image>();
        ApplySprite(img, ButtonSprite, BtnSolid);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var c = btn.colors;
        c.highlightedColor = new Color(1.15f, 1.05f, 1.2f, 1f);
        c.pressedColor = new Color(0.8f, 0.6f, 0.95f, 1f);
        // Gamepad focus (MenuNavigator) shows through selectedColor. The default is
        // barely distinguishable from normal, so the focused button looked unfocused.
        c.selectedColor = new Color(1.30f, 1.05f, 1.45f, 1f);
        c.fadeDuration = 0.08f;
        btn.colors = c;
        go.AddComponent<LayoutElement>();

        var label = NewText(text, go.transform, fontSize, TextAlignmentOptions.Center, font);
        label.color = ValueCol;
        label.fontStyle = FontStyles.Bold;
        Stretch(label.rectTransform);
        return btn;
    }

    // A horizontal value slider (0..1) built via Unity's DefaultControls, then
    // recoloured to the purple theme.
    public static Slider NewSlider(Transform parent, float value, UnityAction<float> onChanged)
    {
        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.transform.SetParent(parent, false);
        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f; slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);

        var bg = go.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = new Color(0f, 0f, 0f, 0.5f);
            // Thicker track (default is the middle 50% of the slider height).
            var brt = bg.rectTransform;
            brt.anchorMin = new Vector2(0f, 0.18f); brt.anchorMax = new Vector2(1f, 0.82f);
            brt.offsetMin = new Vector2(brt.offsetMin.x, 0f); brt.offsetMax = new Vector2(brt.offsetMax.x, 0f);
        }
        var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
        if (fill != null) fill.color = Magenta;
        // Same focus tint as the buttons, so a focused slider is obvious.
        var sc = slider.colors;
        sc.selectedColor = new Color(1.30f, 1.05f, 1.45f, 1f);
        sc.highlightedColor = new Color(1.15f, 1.05f, 1.2f, 1f);
        sc.fadeDuration = 0.08f;
        slider.colors = sc;

        var handle = go.transform.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
        if (handle != null)
        {
            handle.color = new Color(0.96f, 0.90f, 1f, 1f);
            // Wider handle = bigger grab target.
            handle.rectTransform.sizeDelta = new Vector2(34f, handle.rectTransform.sizeDelta.y);
        }

        go.AddComponent<LayoutElement>();
        return slider;
    }

    public static void ApplySprite(Image img, Sprite sprite, Color fallback)
    {
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = Color.white;
        }
        else img.color = fallback;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        var module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        // Without an actions asset the module has no Move / Submit / Cancel, so gamepad
        // navigation (MenuNavigator relies on the module to dispatch them) would do
        // nothing on any runtime-created EventSystem.
        if (module.actionsAsset == null) module.AssignDefaultActions();
    }
}


// ═════════════════════════════════════════════════════════════════════════════
//  UIModalStack — the SINGLE owner of "the game is paused by a menu".
//
//  Lives in MenuTheme.cs on purpose: this is the shared menu module, every menu
//  already references it, and neither type is a MonoBehaviour so Unity does not
//  require its own file. No new scripts to lose track of.
//
//  WHY IT EXISTS
//  Every screen used to do this on open:
//      _prev = Time.timeScale; Time.timeScale = 0f;
//      Cursor.visible = true;  PlayerAttack.SetAllSuppressed(true);
//  …and write _prev back on close. Correct only while exactly ONE screen is open.
//  With two, the inner screen's *restore* overwrites the outer screen's state
//  with a value snapshotted at the wrong moment:
//
//    • Pause menu up + wave ends → the orchestrator captures prevTimeScale = 0.
//      You un-pause (1). You pick an augment → it restores 0.
//      FROZEN GAME, NO MENU, NO INPUT.
//    • Pause → Options → Tutorial → Esc. TutorialScreen.Close() and the Pause
//      action both fire on that one press, in undefined order. Either the
//      tutorial restores its captured 0 after pause set 1 (frozen), or pause
//      resumes the game with the full-screen OptionsCanvas still active,
//      invisibly eating every click.
//
//  Screens now Push(this) / Pop(this). This class recomputes the global state
//  FROM THE STACK on every change — it never restores a caller's snapshot — so
//  ordering and interleaving stop mattering.
// ═════════════════════════════════════════════════════════════════════════════
public static class UIModalStack
{
    private class Entry
    {
        public object Owner;
        public bool Freeze;
        public int Frame;      // Time.frameCount when pushed
    }

    private static readonly List<Entry> _stack = new List<Entry>();

    // Captured only on the 0 → 1 transition and restored when the stack drains.
    // Never on a nested push — that is exactly how a nested screen used to record
    // "paused" as the resting state.
    private static float _baseTimeScale = 1f;
    private static bool _baseCursorVisible;
    private static bool _hasBaseState;
    private static bool _suppressed;

    public static event System.Action OnChanged;

    public static int Depth => _stack.Count;
    public static bool IsOpen => _stack.Count > 0;

    /// <summary>True while at least one open modal wants gameplay frozen.</summary>
    public static bool IsFrozen
    {
        get
        {
            for (int i = 0; i < _stack.Count; i++)
                if (_stack[i].Freeze) return true;
            return false;
        }
    }

    /// <summary>
    /// Is there a run to freeze? Menus in the main-menu scene pass this as their
    /// `freeze` argument: freezing there would stall the menu's own animations and
    /// unscaled-time work for no benefit. In-run they freeze as expected.
    /// </summary>
    public static bool GameplayActive => GameOrchestrator.Instance != null;

    /// <summary>
    /// Should the gamepad drive the on-screen menu cursor right now? True whenever a
    /// modal is open, the clock is frozen, OR we're in a scene with no run at all —
    /// the whole main menu is a menu, even though nothing is "open" and timeScale is 1.
    /// That last clause is why the pad couldn't move the cursor in MenuScene.
    /// </summary>
    public static bool MenuInputActive => IsOpen || !GameplayActive || Time.timeScale == 0f;

    public static object Top => _stack.Count > 0 ? _stack[_stack.Count - 1].Owner : null;

    /// <summary>Is <paramref name="owner"/> the frontmost modal?</summary>
    public static bool IsTop(object owner) => owner != null && ReferenceEquals(Top, owner);

    /// <summary>Frontmost, or nothing is open at all. The test a pause menu wants.</summary>
    public static bool IsTopOrEmpty(object owner) => _stack.Count == 0 || IsTop(owner);

    /// <summary>Frame on which <paramref name="owner"/> was pushed, or -1. Lets a
    /// screen ignore the very key press that opened it.</summary>
    public static int PushedFrame(object owner)
    {
        for (int i = 0; i < _stack.Count; i++)
            if (ReferenceEquals(_stack[i].Owner, owner)) return _stack[i].Frame;
        return -1;
    }

    public static bool Contains(object owner)
    {
        for (int i = 0; i < _stack.Count; i++)
            if (ReferenceEquals(_stack[i].Owner, owner)) return true;
        return false;
    }

    // ── lifetime ────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _stack.Clear();
        _hasBaseState = false;
        _baseTimeScale = 1f;
        _baseCursorVisible = false;
        _suppressed = false;          // MUST reset: with "no domain reload" a stale
                                      // `true` makes the cache skip the un-suppress
                                      // call and attacks stay dead for the session.
        OnChanged = null;

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // A scene load destroys every menu on the stack, but Time.timeScale and the
    // attack-suppression flag survive it. Left alone, a menu-driven scene change
    // (Quit to Main Menu, Continue Run, Start Co-op) drops you into a scene frozen
    // at 0 with attacks off — the classic "black screen / dead controls after
    // loading". Drain here so no caller has to remember `Time.timeScale = 1f`.
    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                                      UnityEngine.SceneManagement.LoadSceneMode m) => ForceClear();

    // ── push / pop ──────────────────────────────────────────────────────────

    public static void Push(object owner, bool freeze = true)
    {
        if (owner == null) return;
        Prune();

        // Re-opening an already-open screen must not stack twice — two pushes would
        // need two pops and would strand the freeze on.
        if (Contains(owner)) { Bump(owner, freeze); Apply(); return; }

        if (_stack.Count == 0) CaptureBaseState();
        _stack.Add(new Entry { Owner = owner, Freeze = freeze, Frame = Time.frameCount });
        Apply();
    }

    public static void Pop(object owner)
    {
        if (owner == null) return;
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].Owner, owner)) { _stack.RemoveAt(i); break; }
        }
        Prune();
        Apply();
    }

    /// <summary>
    /// Re-assert the global state from the stack. Insurance for any legacy screen
    /// that still writes Time.timeScale itself; changes nothing about the stack.
    /// </summary>
    public static void Reassert() => Apply();

    /// <summary>
    /// Close everything and unfreeze. Used when LEAVING for another scene.
    ///
    /// Deliberately does NOT restore Cursor.visible. We're mid-transition from an
    /// open menu (cursor shown) to a scene that has not run yet; writing back the
    /// gameplay baseline of `false` would land you in the main menu with no mouse
    /// pointer — a hard lockout for mouse players. The destination scene owns the
    /// cursor from here.
    /// </summary>
    public static void ForceClear()
    {
        _stack.Clear();
        _hasBaseState = false;
        _baseTimeScale = 1f;
        Time.timeScale = 1f;
        SetAttacksSuppressed(false);
        OnChanged?.Invoke();
    }

    private static void Bump(object owner, bool freeze)
    {
        for (int i = 0; i < _stack.Count; i++)
        {
            if (ReferenceEquals(_stack[i].Owner, owner))
            {
                var e = _stack[i];
                e.Freeze = freeze;
                e.Frame = Time.frameCount;
                _stack.RemoveAt(i);
                _stack.Add(e);
                return;
            }
        }
    }

    // A destroyed MonoBehaviour compares == null against UnityEngine.Object but
    // never against a plain object reference, so it would pin the stack — and
    // therefore timeScale at 0 — forever. Sweep on every mutation.
    private static void Prune()
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            var o = _stack[i].Owner;
            if (o == null || (o is UnityEngine.Object uo && uo == null))
                _stack.RemoveAt(i);
        }
    }

    private static void CaptureBaseState()
    {
        // Never record a frozen clock as the resting state.
        _baseTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
        _baseCursorVisible = Cursor.visible;
        _hasBaseState = true;
    }

    private static void Apply()
    {
        bool open = _stack.Count > 0;
        bool freeze = IsFrozen;

        if (open)
        {
            if (!_hasBaseState) CaptureBaseState();
            Time.timeScale = freeze ? 0f : _baseTimeScale;
            Cursor.visible = true;
        }
        else
        {
            Time.timeScale = _hasBaseState && _baseTimeScale > 0.0001f ? _baseTimeScale : 1f;
            if (_hasBaseState) Cursor.visible = _baseCursorVisible;
            _hasBaseState = false;
        }

        SetAttacksSuppressed(freeze);
        OnChanged?.Invoke();
    }

    // Attacks are suppressed exactly while a freezing modal is up. Cached so the
    // per-frame Reassert() doesn't hammer every PlayerAttack in the scene.
    private static void SetAttacksSuppressed(bool value)
    {
        if (_suppressed == value) return;
        _suppressed = value;
        PlayerAttack.SetAllSuppressed(value);
    }
}


// ═════════════════════════════════════════════════════════════════════════════
//  MenuBackInput — ONE "back" press = ONE reaction.
//
//  Esc / Start is read in several places (the Pause action, TutorialScreen,
//  OptionsMenu, ControlRebindScreen, the pad-poll fallback). Unity delivers the
//  same press to all of them in the same frame and Update order between them is
//  undefined. This makes the press a CONSUMABLE resource: the frontmost modal
//  takes it, everyone else sees false. Resets automatically each frame.
// ═════════════════════════════════════════════════════════════════════════════
public static class MenuBackInput
{
    private static int _readFrame = -1;
    private static bool _pressed;
    private static bool _pausePressed;
    private static int _consumedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _readFrame = -1; _consumedFrame = -1; _pressed = false; _pausePressed = false;
    }

    /// <summary>Any back/cancel control this frame: Esc, gamepad Start, or gamepad B.</summary>
    public static bool PressedThisFrame { get { Poll(); return _pressed; } }

    /// <summary>Esc or gamepad Start only — never B. The pause menu's fallback poll uses
    /// this, because B is a gameplay control (dodge/cancel) and must not open pause.</summary>
    public static bool PausePressedThisFrame { get { Poll(); return _pausePressed; } }

    public static bool ConsumedThisFrame => _consumedFrame == Time.frameCount;

    /// <summary>
    /// Claim this frame's back press for <paramref name="owner"/>. Succeeds only if a
    /// press happened, nobody claimed it yet, and <paramref name="owner"/> is the
    /// frontmost modal (or, with requireTop:false, at least isn't buried under one).
    /// A press on the same frame the owner opened is ignored — that press opened it.
    /// </summary>
    public static bool ConsumeBack(object owner, bool requireTop = true)
    {
        if (ConsumedThisFrame) return false;
        if (!PressedThisFrame) return false;

        bool eligible = requireTop ? UIModalStack.IsTop(owner) : UIModalStack.IsTopOrEmpty(owner);
        if (!eligible) return false;

        if (UIModalStack.PushedFrame(owner) == Time.frameCount) return false;

        _consumedFrame = Time.frameCount;
        return true;
    }

    /// <summary>Burn this frame's press without acting on it — used by a screen that
    /// opens a child on the same press, so the child can't close on its first frame.</summary>
    public static void Consume()
    {
        if (PressedThisFrame) _consumedFrame = Time.frameCount;
    }

    private static void Poll()
    {
        if (_readFrame == Time.frameCount) return;   // Update order must not matter
        _readFrame = Time.frameCount;
        _pressed = false; _pausePressed = false;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { _pressed = true; _pausePressed = true; }

        // Every pad, not just `current`: in co-op either player's Start must work.
        var pads = UnityEngine.InputSystem.Gamepad.all;
        for (int i = 0; i < pads.Count; i++)
        {
            var p = pads[i];
            if (p == null) continue;
            if (p.startButton.wasPressedThisFrame) { _pressed = true; _pausePressed = true; }
            if (p.bButton.wasPressedThisFrame) _pressed = true;
        }
    }
}

