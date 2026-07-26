using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

// Key/button reassignment screen.
// Open  with ControlRebindScreen.Open() (e.g. from a pause-menu button, or via
// the RebindScreenOpener / PauseControlsButton helpers).
public class ControlRebindScreen : MonoBehaviour
{
    [Tooltip("Optional. The project InputActionAsset to edit. Leave null to clone " +
             "the asset from the first live player at runtime (recommended for the " +
             "gameplay scene). Assign explicitly for a menu scene with no players.")]
    [SerializeField] private InputActionAsset sourceAsset;

    [Tooltip("Best quality: a TMP Font Asset (Window > TextMeshPro > Font Asset Creator, " +
             "source = Cinzel-ExtraBold.ttf). Leave empty to use the raw .ttf slot below.")]
    [SerializeField] private TMP_FontAsset titleFont;

    [Tooltip("Convenience: drag the RAW Cinzel-ExtraBold.ttf here and it is converted to a " +
             "TMP font asset at runtime. Used only when Title Font (above) is empty.")]
    [SerializeField] private Font titleFontTtf;

    // Resources-relative (no extension). Spaces are fine.
    private const string PanelSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/MenuPanel 1";
    private const string ButtonSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button 1";

    private const string MapName = "Player";
    private const string KbmGroup = "Keyboard&Mouse";
    private const string PadGroup = "Gamepad";

    private static ControlRebindScreen _instance;

    private InputActionAsset _workingCopy;
    private string _scheme = KbmGroup;
    private InputActionRebindingExtensions.RebindingOperation _rebindOp;
    private InputAction _pendingAction;      // what StartRebind is currently listening for
    private int _pendingIndex = -1;

    private GameObject _root;
    private RectTransform _listContent;
    private GameObject _listenOverlay;
    private TextMeshProUGUI _listenLabel;
    private TextMeshProUGUI _cancelHint;
    private Button _kbmTab, _padTab;

    private Sprite _panelSprite, _buttonSprite;
    private TMP_FontAsset _headerFont;

    // ----- THEME --------------------------------------------------------------
    private static readonly Color Magenta = new Color(0.78f, 0.30f, 0.92f, 1f); // #C84CEB
    private static readonly Color Violet = new Color(0.49f, 0.18f, 0.78f, 1f); // #7D2EC7
    private static readonly Color GradTop = new Color(0.11f, 0.05f, 0.18f, 0.98f);
    private static readonly Color GradBottom = new Color(0.03f, 0.01f, 0.05f, 0.98f);
    private static readonly Color PanelSolid = new Color(0.10f, 0.07f, 0.15f, 0.98f);
    private static readonly Color BtnSolid = new Color(0.17f, 0.12f, 0.24f, 1f);
    private static readonly Color BtnActive = new Color(0.55f, 0.24f, 0.78f, 1f);
    private static readonly Color TextCol = new Color(0.95f, 0.92f, 0.99f, 1f);
    private static readonly Color ValueCol = new Color(1.00f, 0.86f, 1.00f, 1f);

    //  PUBLIC ENTRY POINTS

    // A scene-placed ControlRebindScreen registers itself here, so the instance
    // YOU configured in the Inspector (font slots etc.) is the one that runs —
    // rather than a fresh, unconfigured one created on demand.
    private void Awake()
    {
        if (_instance == null) _instance = this;
    }

    //  static API (call from anywhere in code) 
    public static void Open()
    {
        if (_instance == null)
            _instance = new GameObject("ControlRebindScreen").AddComponent<ControlRebindScreen>();
        _instance.OpenInternal();
    }

    public static void Close()
    {
        if (_instance != null) _instance.CloseInternal();
    }

    // instance API 
    // These are what RebindScreenOpener used to provide — folded in so it's one
    // script. Wire a Button's On Click() to ControlRebindScreen -> OpenScreen().
    public void OpenScreen() => OpenInternal();
    public void CloseScreen() => CloseInternal();
    public void ToggleScreen()
    {
        if (_root != null && _root.activeSelf) CloseInternal();
        else OpenInternal();
    }

    //  LIFECYCLE

    private void OpenInternal()
    {
        EnsureEventSystem();

        if (_workingCopy == null && !BuildWorkingCopy())
        {
            Debug.LogWarning("[ControlRebindScreen] No source asset and no live PlayerInput to clone from — cannot open.");
            return;
        }

        if (_root == null) BuildUI();
        _root.SetActive(true);

        // Freeze only if we're over a running game. On the main menu there is
        // nothing to freeze, but we still want to own the Esc key while open.
        UIModalStack.Push(this, freeze: UIModalStack.GameplayActive);

        ControlRebindService.ApplyTo(_workingCopy);
        SelectScheme(_scheme);
    }

    private void CloseInternal()
    {
        CancelActiveRebind();
        if (_root != null) _root.SetActive(false);
        UIModalStack.Pop(this);
    }

    private void Update()
    {
        if (_root == null || !_root.activeSelf) return;

        // While an interactive rebind is listening, Escape is that operation's
        // cancel control (WithCancelingThrough). Swallow the press here so it can't
        // ALSO reach PauseMenuController and toggle pause behind this screen.
        if (_rebindOp != null)
        {
            if (MenuBackInput.PressedThisFrame) MenuBackInput.Consume();
            return;
        }

        if (MenuBackInput.ConsumeBack(this)) CloseInternal();
    }

    private void OnDisable()
    {
        CancelActiveRebind();
        if (UIModalStack.Contains(this)) UIModalStack.Pop(this);
    }

    private void OnDestroy()
    {
        CancelActiveRebind();
        if (_instance == this) _instance = null;
    }

    private bool BuildWorkingCopy()
    {
        InputActionAsset src = sourceAsset;
        if (src == null && PlayerInput.all.Count > 0 && PlayerInput.all[0] != null)
            src = PlayerInput.all[0].actions;
        if (src == null) return false;

        _workingCopy = Instantiate(src);
        ControlRebindService.ApplyTo(_workingCopy);
        return true;
    }

    //  SCHEME + LIST

    private void SelectScheme(string group)
    {
        _scheme = group;
        SkinTab(_kbmTab, group == KbmGroup);
        SkinTab(_padTab, group == PadGroup);
        RebuildList();
    }

    private void RebuildList()
    {
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        var map = _workingCopy.FindActionMap(MapName, false);
        if (map == null) return;

        // Composite parts (WASD) are collapsed to their primary so the list stays
        // tidy. Simple button actions show EVERY binding in this scheme — primary
        // AND alternates — so mouse buttons that live in an alternate slot (e.g.
        // AttackTool's right-mouse) are editable instead of hidden.
        var seenParts = new HashSet<string>();
        var simpleCount = new Dictionary<string, int>();

        foreach (var action in map.actions)
        {
            // `Build` mirrors `AttackWeapon` (ControlRebindService.MirrorAliases keeps
            // them identical). Showing it invited the player to set the same control
            // twice and to desync the pair by only doing it once.
            if (ControlRebindService.IsAlias(action.name)) continue;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                var b = action.bindings[i];
                if (b.isComposite) continue;
                if (!GroupMatches(b.groups, _scheme)) continue;

                if (b.isPartOfComposite)
                {
                    string key = action.name + "/" + b.name;
                    if (!seenParts.Add(key)) continue;        // primary part only
                    AddRow(action, i, $"{Nice(action.name)} {Nice(b.name)}");
                }
                else
                {
                    if (action.type != InputActionType.Button) continue; // skip sticks/delta/pointer

                    int n = simpleCount.TryGetValue(action.name, out var c) ? c : 0;
                    simpleCount[action.name] = n + 1;

                    string label = Nice(action.name);
                    if (n == 1) label += "  (alt)";
                    else if (n >= 2) label += $"  (alt {n})";
                    AddRow(action, i, label);
                }
            }
        }

        if (_listContent.childCount == 0)
            AddInfoRow(_scheme == PadGroup
                ? "No rebindable gamepad buttons in this map."
                : "No rebindable controls in this map.");
    }

    // Each row uses ANCHORED children (no horizontal layout group), so the label
    // is pinned to the left of the row and can never be clipped.
    private void AddRow(InputAction action, int bindingIndex, string label)
    {
        var row = NewUI("Row", _listContent);
        row.AddComponent<LayoutElement>().minHeight = 58;

        // Faint row backing for readability over the panel art.
        var backing = row.AddComponent<Image>();
        backing.color = new Color(0f, 0f, 0f, 0.22f);

        // Label — left 60% of the row, left-aligned, generous inset.
        var labelTmp = NewText(label, row.transform, 27, TextAlignmentOptions.MidlineLeft);
        var lrt = labelTmp.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(0.60f, 1f);
        lrt.offsetMin = new Vector2(22f, 0f);
        lrt.offsetMax = new Vector2(-8f, 0f);
        labelTmp.overflowMode = TextOverflowModes.Ellipsis;

        // Value button — right side of the row.
        var btn = NewButton(CurrentDisplay(action, bindingIndex), row.transform, 24);
        var brt = ((RectTransform)btn.transform);
        brt.anchorMin = new Vector2(0.62f, 0.12f);
        brt.anchorMax = new Vector2(0.985f, 0.88f);
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        InputAction a = action; int idx = bindingIndex; string lbl = label;
        btn.onClick.AddListener(() => StartRebind(a, idx, btn, lbl));
    }

    private void AddInfoRow(string text)
    {
        var row = NewUI("Info", _listContent);
        row.AddComponent<LayoutElement>().minHeight = 58;
        var t = NewText(text, row.transform, 22, TextAlignmentOptions.Center);
        Stretch(t.rectTransform);
    }

    private string CurrentDisplay(InputAction action, int bindingIndex)
    {
        string s = action.GetBindingDisplayString(bindingIndex,
            InputBinding.DisplayStringOptions.DontUseShortDisplayNames);
        return string.IsNullOrWhiteSpace(s) ? "—" : s;
    }

    //  INTERACTIVE REBIND

    private void StartRebind(InputAction action, int bindingIndex, Button btn, string displayName)
    {
        CancelActiveRebind();
        GamepadMenuCursor.ClicksSuppressed = true;
        _pendingAction = action;
        _pendingIndex = bindingIndex;

        var btnLabel = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnLabel != null) btnLabel.text = "...";
        ShowListening($"Press a {(_scheme == PadGroup ? "gamepad button" : "key or mouse button")} for\n<b>{displayName}</b>");
        SetCancelHint(_scheme == PadGroup ? "Cancel (Back / Select)" : "Cancel (Esc)");

        var op = action.PerformInteractiveRebinding(bindingIndex)
            .OnMatchWaitForAnother(0.05f)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Gamepad>/leftStick")
            .WithControlsExcluding("<Gamepad>/rightStick");

        if (_scheme == KbmGroup)
        {
            op.WithControlsExcluding("<Gamepad>");
            op.WithCancelingThrough("<Keyboard>/escape");
        }
        else
        {
            op.WithControlsExcluding("<Keyboard>");
            op.WithControlsExcluding("<Mouse>");
            // NOT <Gamepad>/start: that is the Pause binding, so cancelling through it
            // made Start the one gamepad control that could never be assigned to
            // anything. Select/Back is bound to nothing in the Player map.
            op.WithCancelingThrough("<Gamepad>/select");
        }

        op.OnComplete(_ => FinishRebind(true)).OnCancel(_ => FinishRebind(false));
        _rebindOp = op;
        op.Start();
    }

    private void FinishRebind(bool commit)
    {
        DisposeOp();
        GamepadMenuCursor.ClicksSuppressed = false;
        HideListening();
        if (commit)
        {
            if (_pendingAction != null) ClearConflicts(_pendingAction, _pendingIndex);
            ControlRebindService.CaptureFrom(_workingCopy);
        }
        _pendingAction = null;
        RebuildList();
    }

    // Unbind anything else in this scheme that the new control was already doing.
    //
    // Without this the screen happily let a player build the same clash the asset used
    // to ship with (Sprint and Dash both on LeftShift), where one press fires both
    // actions and neither is obviously wrong. The freed row shows "—" and can be
    // reassigned; "Reset to defaults" brings everything back.
    private void ClearConflicts(InputAction changed, int changedIndex)
    {
        var map = _workingCopy.FindActionMap(MapName, false);
        if (map == null) return;

        string path = changed.bindings[changedIndex].effectivePath;
        if (string.IsNullOrEmpty(path)) return;

        foreach (var action in map.actions)
        {
            if (ControlRebindService.IsAlias(action.name)) continue;   // mirrored, not a real clash

            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action == changed && i == changedIndex) continue;

                var b = action.bindings[i];
                if (b.isComposite) continue;
                if (!GroupMatches(b.groups, _scheme)) continue;
                if (b.effectivePath != path) continue;

                // A composite's OWN parts may legitimately share nothing, but two parts
                // of the same composite (Move up/down) clashing is still a clash.
                action.ApplyBindingOverride(i, new InputBinding { overridePath = string.Empty });
                Debug.Log($"[ControlRebindScreen] {path} was also bound to " +
                          $"{action.name}; unbound it to keep {changed.name} unambiguous.");
            }
        }
    }

    private void CancelActiveRebind()
    {
        if (_rebindOp != null)
        {
            try { _rebindOp.Cancel(); } catch { /* already done */ }
        }
        DisposeOp();
        GamepadMenuCursor.ClicksSuppressed = false;
        HideListening();
        _pendingAction = null;
    }

    private void DisposeOp()
    {
        if (_rebindOp != null) { _rebindOp.Dispose(); _rebindOp = null; }
    }

    private void DoReset()
    {
        CancelActiveRebind();
        ControlRebindService.ResetAll();
        ControlRebindService.ApplyTo(_workingCopy);
        RebuildList();
    }

    //  UI CONSTRUCTION (procedural)

    private void BuildUI()
    {
        _panelSprite = Resources.Load<Sprite>(PanelSpritePath);
        _buttonSprite = Resources.Load<Sprite>(ButtonSpritePath);
        _headerFont = ResolveTitleFont();

        // Canvas
        _root = new GameObject("RebindCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5100;   // above TutorialScreen (5000)
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Full-screen dimmer with a vertical purple gradient.
        var dim = NewUI("Dim", _root.transform);
        Stretch(dim.GetComponent<RectTransform>());
        var dimImg = dim.AddComponent<Image>();
        dimImg.sprite = MakeVerticalGradient(GradTop, GradBottom);
        dimImg.type = Image.Type.Simple;

        // Centered panel (portrait-ish to suit the ornate frame sprite).
        var panel = NewUI("Panel", dim.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(900, 1000);
        var panelImg = panel.AddComponent<Image>();
        ApplySprite(panelImg, _panelSprite, PanelSolid);

        // Inner content column, inset so it clears the frame border + corner art.
        var inner = NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(64, 72);   // left, bottom
        irt.offsetMax = new Vector2(-64, -78);  // right, top
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(0, 0, 0, 0); v.spacing = 16;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;

        // Title with a magenta→violet vertex gradient.
        var title = NewText("CONTROLS", inner.transform, 52, TextAlignmentOptions.Center);
        if (_headerFont != null) title.font = _headerFont;
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var titleTop = new Color(0.97f, 0.88f, 1f, 1f);   // bright lavender top
        title.colorGradient = new VertexGradient(titleTop, titleTop, Magenta, Magenta);
        title.GetComponent<LayoutElement>().minHeight = 66;

        // Centred, soft-edged divider that fades out at both ends — tied to the
        // title rather than running edge to edge.
        var ruleHolder = NewUI("RuleHolder", inner.transform);
        ruleHolder.AddComponent<LayoutElement>().minHeight = 18;
        var rule = NewUI("Rule", ruleHolder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.20f, 0.5f);
        rr.anchorMax = new Vector2(0.80f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f);
        rr.sizeDelta = new Vector2(0f, 3f);
        var ruleImg = rule.AddComponent<Image>();
        ruleImg.sprite = MakeHorizontalFade();
        ruleImg.color = new Color(Magenta.r, Magenta.g, Magenta.b, 0.8f);

        // Scheme tabs
        var tabs = NewUI("Tabs", inner.transform);
        var th = tabs.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 14; th.childForceExpandWidth = true; th.childControlWidth = true;
        th.childControlHeight = true; th.childAlignment = TextAnchor.MiddleCenter;
        tabs.AddComponent<LayoutElement>().minHeight = 56;
        _kbmTab = NewButton("Keyboard / Mouse", tabs.transform, 26, _headerFont);
        _kbmTab.onClick.AddListener(() => SelectScheme(KbmGroup));
        _padTab = NewButton("Gamepad", tabs.transform, 26, _headerFont);
        _padTab.onClick.AddListener(() => SelectScheme(PadGroup));

        // Scroll list
        var scrollGO = NewUI("Scroll", inner.transform);
        var scrollLE = scrollGO.AddComponent<LayoutElement>();
        scrollLE.flexibleHeight = 1; scrollLE.minHeight = 480;
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.scrollSensitivity = 24f;
        scrollGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.30f);
        scrollGO.AddComponent<RectMask2D>();

        var viewport = NewUI("Viewport", scrollGO.transform);
        var vpRT = viewport.GetComponent<RectTransform>(); Stretch(vpRT);
        scroll.viewport = vpRT;

        var content = NewUI("Content", viewport.transform);
        _listContent = content.GetComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1);
        _listContent.pivot = new Vector2(0.5f, 1f);
        // Span the viewport width EXACTLY. Without this the leftover sizeDelta makes
        // the content wider than the viewport and off-centre, clipping the first
        // character of every row label.
        _listContent.sizeDelta = new Vector2(0f, _listContent.sizeDelta.y);
        var cv = content.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 6; cv.padding = new RectOffset(10, 10, 10, 10);
        cv.childForceExpandWidth = true; cv.childControlWidth = true;
        cv.childControlHeight = true; cv.childForceExpandHeight = false;
        var fit = content.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _listContent;

        // Footer
        var footer = NewUI("Footer", inner.transform);
        var fh = footer.AddComponent<HorizontalLayoutGroup>();
        fh.spacing = 14; fh.childForceExpandWidth = true; fh.childControlWidth = true;
        fh.childControlHeight = true; fh.childAlignment = TextAnchor.MiddleCenter;
        footer.AddComponent<LayoutElement>().minHeight = 60;
        NewButton("Reset to defaults", footer.transform, 24, _headerFont).onClick.AddListener(DoReset);
        NewButton("Close", footer.transform, 24, _headerFont).onClick.AddListener(CloseInternal);

        BuildListenOverlay();
    }

    private void BuildListenOverlay()
    {
        _listenOverlay = NewUI("Listening", _root.transform);
        Stretch(_listenOverlay.GetComponent<RectTransform>());
        _listenOverlay.AddComponent<Image>().color = new Color(0.04f, 0.01f, 0.07f, 0.86f);

        var box = NewUI("Box", _listenOverlay.transform);
        var br = box.GetComponent<RectTransform>();
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
        br.pivot = new Vector2(0.5f, 0.5f); br.sizeDelta = new Vector2(720, 260);
        var boxImg = box.AddComponent<Image>();
        ApplySprite(boxImg, _panelSprite, PanelSolid);

        var bv = box.AddComponent<VerticalLayoutGroup>();
        bv.padding = new RectOffset(50, 50, 44, 44); bv.spacing = 18;
        bv.childAlignment = TextAnchor.MiddleCenter; bv.childForceExpandWidth = true;
        bv.childControlWidth = true; bv.childControlHeight = true;

        _listenLabel = NewText("Press a key…", box.transform, 30, TextAlignmentOptions.Center);
        if (_headerFont != null) _listenLabel.font = _headerFont;
        _listenLabel.GetComponent<LayoutElement>().flexibleHeight = 1;

        var cancelBtn = NewButton("Cancel (Esc)", box.transform, 24, _headerFont);
        cancelBtn.onClick.AddListener(CancelActiveRebind);
        _cancelHint = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
        _listenOverlay.SetActive(false);
    }

    private void ShowListening(string msg)
    {
        if (_listenOverlay == null) return;
        if (_listenLabel != null) _listenLabel.text = msg;
        _listenOverlay.transform.SetAsLastSibling();
        _listenOverlay.SetActive(true);
    }

    private void SetCancelHint(string text)
    {
        if (_cancelHint != null) _cancelHint.text = text;
    }

    private void HideListening()
    {
        if (_listenOverlay != null) _listenOverlay.SetActive(false);
    }

    //  tiny UI factory 

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI NewText(string text, Transform parent, float size, TextAlignmentOptions align)
    {
        var go = NewUI("Text", parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = TextCol;
        tmp.alignment = align; tmp.richText = true;
        go.AddComponent<LayoutElement>();
        return tmp;
    }

    private Button NewButton(string text, Transform parent, float fontSize, TMP_FontAsset font = null)
    {
        var go = NewUI("Button", parent);
        var img = go.AddComponent<Image>();
        ApplySprite(img, _buttonSprite, BtnSolid);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.05f, 1.2f, 1f);
        colors.pressedColor = new Color(0.8f, 0.6f, 0.95f, 1f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        go.AddComponent<LayoutElement>();

        var label = NewText(text, go.transform, fontSize, TextAlignmentOptions.Center);
        if (font != null) label.font = font;
        label.color = ValueCol;
        label.fontStyle = FontStyles.Bold;
        Stretch(label.rectTransform);
        return btn;
    }

    // Tint the active scheme tab magenta; idle tabs use the normal skin.
    private void SkinTab(Button b, bool active)
    {
        if (b == null || !(b.targetGraphic is Image img)) return;
        img.color = active ? BtnActive : (_buttonSprite != null ? Color.white : BtnSolid);
        var label = b.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = active ? Color.white : ValueCol;
    }

    private static void ApplySprite(Image img, Sprite sprite, Color fallback)
    {
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = Color.white;
        }
        else
        {
            img.color = fallback;
        }
    }

    private TMP_FontAsset ResolveTitleFont() => MenuTheme.ResolveFont(titleFont, titleFontTtf);

    private static Sprite MakeHorizontalFade()
    {
        const int w = 128;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            float a = 1f - Mathf.Abs(t - 0.5f) * 2f;   // 0 at the ends, 1 in the middle
            a = Mathf.SmoothStep(0f, 1f, a);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite MakeVerticalGradient(Color top, Color bottom)
    {
        const int h = 128;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(h - 1)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private static bool GroupMatches(string groups, string scheme)
        => !string.IsNullOrEmpty(groups) && groups.Contains(scheme);

    // "NextWeapon" -> "Next Weapon", "Hotbar10" -> "Hotbar 10", "up" -> "Up".
    // The list now contains multi-word actions (PreviousWeapon, HotbarModifier) and ten
    // numbered slots, which ran together unreadably under the old capitalize-only rule.
    private static string Nice(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool boundary = i > 0 &&
                ((char.IsUpper(c) && !char.IsUpper(s[i - 1])) ||
                 (char.IsDigit(c) && !char.IsDigit(s[i - 1])));
            if (boundary) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpper(c) : c);
        }
        return sb.ToString();
    }
}
