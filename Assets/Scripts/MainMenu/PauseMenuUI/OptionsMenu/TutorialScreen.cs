using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

//  TUTORIAL SCREEN 
//  A self-contained "How to Play" overlay, built programmatically and skinned
//  with your shared MenuTheme (same MenuPanel 1 frame, Button 1 sprites, purple
//  gradient backdrop and Cinzel font used by OptionsMenu / ContinueRunMenu).
//  CONTROLS-AWARE - resolved at runtime from your real
//  PlayerInputActions asset, so it shows whatever each action is actually bound
//  to (and follows live rebinds), falling back to clean defaults if the asset
//  can't be found (e.g. on a bare main-menu scene).

public class TutorialScreen : MonoBehaviour
{
    [Header("Fonts (optional - matches OptionsMenu)")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private Font titleFontTtf;

    [Header("Input (optional - auto-resolved if left empty)")]
    [Tooltip("Drag your PlayerInputActions asset here for exact, rebind-aware " +
             "control hints. If empty, the screen tries a PlayerInput in the " +
             "scene, then Resources/PlayerInputActions, then built-in defaults.")]
    public InputActionAsset inputActions;

    [Header("Economy labels (auto-read from EnergyManager if present)")]
    public int towerBuildCost = 100;
    public int towerUpgradeCost = 50;
    public int towerDisassembleRefund = 50;

    [Header("Behaviour")]
    [Tooltip("Freeze the game (Time.timeScale = 0) while open. Harmless on the " +
             "main menu; useful if you open it mid-run.")]
    public bool pauseGameWhileOpen = true;

    [Tooltip("Which control scheme to show first. Auto = most recently used device.")]
    public DefaultScheme defaultScheme = DefaultScheme.Auto;
    public enum DefaultScheme { Auto, KeyboardMouse, Gamepad }

    // ---- runtime state ---------------------------------------------------
    private GameObject _root;
    private TMP_FontAsset _font;
    private bool _isOpen;
    private bool _showGamepad;
    private float _prevTimeScale = 1f;

    private readonly List<ChipBinding> _chips = new List<ChipBinding>();
    private Button _kbTab, _padTab;

    private const string GROUP_KBM = "Keyboard&Mouse";
    private const string GROUP_PAD = "Gamepad";

    // theme-derived colours
    private static readonly Color RowA = new Color(0.78f, 0.30f, 0.92f, 0.07f); // faint magenta stripe
    private static readonly Color RowB = new Color(1f, 1f, 1f, 0.025f);
    private static readonly Color DescCol = new Color(0.82f, 0.78f, 0.90f, 1f);
    private static readonly Color TipCol = new Color(0.80f, 0.62f, 0.94f, 0.95f);


    public static void ShowTutorial()
    {
        var inst = FindFirstObjectByType<TutorialScreen>();
        if (inst == null) inst = new GameObject("TutorialScreen").AddComponent<TutorialScreen>();
        inst.Open();
    }

    public void Open()
    {
        MenuTheme.EnsureEventSystem();
        if (_root == null) BuildUI();

        RefreshEconomyFromGame();
        ApplyRebindOverrides(ResolveAsset());   // reflect any committed rebinds
        _showGamepad = ResolveInitialScheme();
        RefreshAllChips();
        UpdateSchemeTabs();

        _isOpen = true;
        _root.SetActive(true);
        Cursor.visible = true;

        if (pauseGameWhileOpen) { _prevTimeScale = Time.timeScale; Time.timeScale = 0f; }
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        if (_root != null) _root.SetActive(false);
        if (pauseGameWhileOpen) Time.timeScale = _prevTimeScale;
    }

    public void Toggle() { if (_isOpen) Close(); else Open(); }

    private void Update()
    {
        if (!_isOpen) return;
        bool close = false;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) close = true;
        var pad = Gamepad.current;
        if (pad != null && (pad.bButton.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) close = true;
        if (close) Close();
    }


    private List<Section> BuildContent()
    {
        string buildCost = towerBuildCost.ToString();
        string upCost = towerUpgradeCost.ToString();
        string refund = towerDisassembleRefund.ToString();

        var s = new List<Section>();

        s.Add(new Section("YOUR MISSION", new List<Entry>
        {
            Info("Defend the Central Core",
                 "Waves of enemies push toward your Central Core. If it falls, the run ends. " +
                 "Build and upgrade towers, fight on the front line, and keep the Core alive.",
                 "GOAL")
        }));

        s.Add(new Section("MOVEMENT", new List<Entry>
        {
            Fallback("Move", "WASD", "Left Stick", "Walk around the battlefield."),
            Bind("Sprint", "Sprint", "leftShift", "leftShoulder", "Left Shift", "LB",
                 "Hold to move faster.", "Shares its input with Dodge - hold to sprint, tap to dodge."),
            Bind("Dodge / Dash", "Dash", "leftShift", "buttonEast", "Left Shift", "B",
                 "A quick burst of movement to escape attacks.", "Tap it - don't hold."),
            Fallback("Aim", "Mouse", "Right Stick",
                 "Aim your weapon and the build reticle. Mouse points directly; the right " +
                 "stick steers a reticle for ground-targeted tools.")
        }));

        s.Add(new Section("COMBAT", new List<Entry>
        {
            Bind("Attack", "AttackWeapon", "leftButton", "rightTrigger", "Left Click", "RT",
                 "Attack with your equipped weapon. Hold for charge / continuous weapons (hammer, flamethrower)."),
            Bind("Use Tool", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 "Use your equipped tool: shield, bombs, traps, turrets and more. Some are held, some single-use."),
            new Entry {
                title = "Switch Weapon", desc = "Cycle to your next equipped weapon.",
                fallbackOnly = true, kbFallback = "Scroll Down", gpFallback = "D-Pad",
                tip = "Mouse wheel: scroll DOWN = weapons, scroll UP = tools. The 1 / 2 keys also cycle."
            },
            new Entry {
                title = "Switch Tool", desc = "Cycle to your next equipped tool.",
                fallbackOnly = true, kbFallback = "Scroll Up", gpFallback = "D-Pad"
            },
            Bind("Parry", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 "With a shield equipped, raise it the instant an enemy flashes a \"!\" above its head to " +
                 "parry - this stuns the attacker and negates the hit.",
                 "Block early to be safe; parry on the \"!\" for the reward.")
        }));

        s.Add(new Section("BUILDING & TOWERS", new List<Entry>
        {
            Bind("Enter Build Mode", "Placement", "space", "buttonNorth", "Space", "Y",
                 "Toggle build mode. The screen desaturates while you're building.",
                 "Press again to exit, or to back out of an open menu / wheel."),
            Bind("Build Tower", "Build", "leftButton", "rightTrigger", "Left Click", "RT",
                 $"In build mode, aim at an empty slot and build a tower (costs {buildCost} energy). " +
                 "With several tower types, a selection wheel opens - aim to pick, confirm with the same button."),
            Bind("Upgrade Tower", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 $"In build mode, aim at one of your towers to open its menu, then choose UPGRADE " +
                 $"(-{upCost} energy) for +20% output and +20% health."),
            Bind("Disassemble Tower", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 $"Same tower menu -> DISASSEMBLE removes the tower and refunds +{refund} energy."),
            Bind("Supply / Repair", "Build", "leftButton", "rightTrigger", "Hold L-Click", "Hold RT",
                 "In build mode, hold on a damaged tower or the Central Core to pour your energy into it and repair it.")
        }));

        s.Add(new Section("TETHER BUFFS", new List<Entry>
        {
            Info("Stand Near Your Towers",
                 "Get close and energy tethers form automatically - no button needed. The zone you're in " +
                 "sets the buff: NEAR = slower decay (defense), MID = +damage, FAR = +range. " +
                 "More tethered towers = stronger buffs.",
                 "Proximity")
        }));

        s.Add(new Section("ENERGY & AUGMENTS", new List<Entry>
        {
            Info("Collect Energy",
                 "Defeated enemies drop energy. Walk over the drops to collect it. Energy is your currency " +
                 "for building, upgrading and supplying towers.",
                 "Walk over"),
            Info("Choose Augments",
                 "Between challenges you'll pick Augments - run-long upgrades for your towers, weapons and " +
                 "abilities. Pick what suits your strategy.",
                 "Pick one")
        }));

        s.Add(new Section("SYSTEM", new List<Entry>
        {
            Bind("Pause", "Pause", "escape", "start", "Esc", "Start", "Pause the game and open the menu.")
        }));

        return s;
    }

    // entry factories 
    private Entry Info(string title, string desc, string chip)
        => new Entry { title = title, desc = desc, infoChip = chip };

    private Entry Fallback(string title, string kb, string gp, string desc)
        => new Entry { title = title, desc = desc, fallbackOnly = true, kbFallback = kb, gpFallback = gp };

    private Entry Bind(string title, string action, string kbPrefer, string gpPrefer,
                       string kbFallback, string gpFallback, string desc, string tip = null)
        => new Entry
        {
            title = title,
            desc = desc,
            tip = tip,
            action = action,
            kbPrefer = kbPrefer,
            gpPrefer = gpPrefer,
            kbFallback = kbFallback,
            gpFallback = gpFallback
        };

    private Entry Combo(string title, string actionA, string actionB,
                        string kbA, string kbB, string gp, string desc, string tip = null)
        => new Entry
        {
            title = title,
            desc = desc,
            tip = tip,
            action = actionA,
            comboAction = actionB,
            kbFallback = kbA + " / " + kbB,
            comboKbFallback = kbB,
            gpFallback = gp
        };

    //  UI CONSTRUCTION
    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);

        _root = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // dim backdrop (theme gradient)
        var dim = MenuTheme.NewUI("Dim", _root.transform);
        MenuTheme.Stretch(dim.GetComponent<RectTransform>());
        dim.AddComponent<Image>().sprite = MenuTheme.VerticalGradient(MenuTheme.GradTop, MenuTheme.GradBottom);

        // ornate panel (MenuPanel 1)
        var panel = MenuTheme.NewUI("Panel", dim.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(1240, 1000);
        MenuTheme.ApplySprite(panel.AddComponent<Image>(), MenuTheme.PanelSprite, MenuTheme.PanelSolid);

        // inner column, inset well clear of the decorative frame. Smaller TOP inset
        // pulls the title up and hands the freed space to the scroll body below.
        var inner = MenuTheme.NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(82, 86); irt.offsetMax = new Vector2(-82, -58);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperCenter;

        // title (compact so it doesn't steal scroll space)
        var title = MenuTheme.NewText("HOW TO PLAY", inner.transform, 52, TextAlignmentOptions.Center, _font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 50);

        var sub = MenuTheme.NewText("Defend the Central Core", inner.transform, 25, TextAlignmentOptions.Center, _font);
        sub.color = MenuTheme.ValueCol;
        SetH(sub, 28);

        // device toggle
        var tabs = MenuTheme.NewUI("Tabs", inner.transform);
        SetH(tabs, 62);
        var th = tabs.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 16; th.childControlWidth = true; th.childControlHeight = true;
        th.childForceExpandWidth = true; th.childForceExpandHeight = true;
        _kbTab = MenuTheme.NewButton("Keyboard & Mouse", tabs.transform, 24, _font);
        _padTab = MenuTheme.NewButton("Gamepad", tabs.transform, 24, _font);
        _kbTab.onClick.AddListener(() => { _showGamepad = false; RefreshAllChips(); UpdateSchemeTabs(); });
        _padTab.onClick.AddListener(() => { _showGamepad = true; RefreshAllChips(); UpdateSchemeTabs(); });

        AddDivider(inner.transform);

        // scrolling body absorbs the remaining height
        var scrollHolder = MenuTheme.NewUI("Scroll", inner.transform);
        var she = scrollHolder.AddComponent<LayoutElement>(); she.flexibleHeight = 1f; she.minHeight = 300f;
        BuildScroll((RectTransform)scrollHolder.transform);

        // footer
        var back = MenuTheme.NewButton("Back", inner.transform, 24, _font);
        SetH(back, 56);
        back.onClick.AddListener(Close);

        _root.SetActive(false);
    }

    private void BuildScroll(RectTransform holder)
    {
        var sr = holder.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;

        // viewport (clipping). Small left gutter + right gap for the scrollbar so
        // text never kisses the mask edge.
        var viewport = MenuTheme.NewUI("Viewport", holder);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(6, 0); vrt.offsetMax = new Vector2(-18, 0);
        viewport.AddComponent<RectMask2D>();
        sr.viewport = vrt;

        // zero sizeDelta + anchoredPosition. A RectTransform made
        // via `new GameObject` can carry a non-zero default sizeDelta; with stretch
        // anchors that makes the content WIDER than the viewport and centred, which
        // clips both sides (this was the "MOVEMENT -> VEMENT" bug).
        var content = MenuTheme.NewUI("Content", viewport.transform);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = Vector2.zero;
        crt.anchoredPosition = Vector2.zero;
        var cv = content.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 10; cv.padding = new RectOffset(16, 16, 4, 10);
        cv.childControlWidth = true; cv.childControlHeight = true;
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = crt;

        // thin themed scrollbar
        var sbGO = MenuTheme.NewUI("Scrollbar", holder);
        var sbrt = sbGO.GetComponent<RectTransform>();
        sbrt.anchorMin = new Vector2(1f, 0f); sbrt.anchorMax = new Vector2(1f, 1f);
        sbrt.pivot = new Vector2(1f, 0.5f); sbrt.sizeDelta = new Vector2(8f, 0f);
        sbrt.anchoredPosition = Vector2.zero;
        var sbImg = sbGO.AddComponent<Image>(); sbImg.color = new Color(0f, 0f, 0f, 0.35f);
        var scrollbar = sbGO.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var handle = MenuTheme.NewUI("Handle", sbGO.transform);
        MenuTheme.Stretch(handle.GetComponent<RectTransform>());
        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.55f);
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = hImg;
        sr.verticalScrollbar = scrollbar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        // populate
        foreach (var section in BuildContent())
        {
            var head = MenuTheme.NewText(section.title, content.transform, 24,
                                         TextAlignmentOptions.Left, _font);
            head.color = MenuTheme.Magenta; head.fontStyle = FontStyles.Bold; head.characterSpacing = 5f;
            head.margin = new Vector4(2, 10, 0, 2);
            SetH(head, 40);

            for (int i = 0; i < section.entries.Count; i++)
                BuildRow((RectTransform)content.transform, section.entries[i], i % 2 == 1);
        }
    }

    private void BuildRow(RectTransform parent, Entry e, bool striped)
    {
        var row = MenuTheme.NewUI("Row", parent);
        var rImg = row.AddComponent<Image>();
        rImg.color = striped ? RowA : RowB;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 20; h.padding = new RectOffset(16, 16, 14, 14);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.UpperLeft;
        var rcsf = row.AddComponent<ContentSizeFitter>();
        rcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // chip (Button 1 sprite)
        bool isInfo = !string.IsNullOrEmpty(e.infoChip);
        var chip = MenuTheme.NewUI("Chip", row.transform);
        var chipImg = chip.AddComponent<Image>();
        MenuTheme.ApplySprite(chipImg, MenuTheme.ButtonSprite, MenuTheme.BtnSolid);
        if (isInfo) chipImg.color = MenuTheme.ButtonSprite != null
            ? new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 1f)
            : MenuTheme.Violet;
        var cle = chip.GetComponent<LayoutElement>() ?? chip.AddComponent<LayoutElement>();
        cle.minWidth = 232; cle.preferredWidth = 232; cle.flexibleWidth = 0;
        cle.minHeight = 66; cle.preferredHeight = 66; cle.flexibleHeight = 0;

        var chipText = MenuTheme.NewText(isInfo ? e.infoChip : "", chip.transform, 25,
                                         TextAlignmentOptions.Center, _font);
        chipText.fontStyle = FontStyles.Bold;
        chipText.color = isInfo ? Color.white : MenuTheme.ValueCol;
        chipText.enableAutoSizing = true; chipText.fontSizeMin = 15; chipText.fontSizeMax = 25;
        chipText.margin = new Vector4(10, 4, 10, 4);
        chipText.textWrappingMode = TextWrappingModes.NoWrap;
        MenuTheme.Stretch(chipText.rectTransform);
        if (!isInfo) _chips.Add(new ChipBinding { entry = e, label = chipText });

        // text block
        var block = MenuTheme.NewUI("Text", row.transform);
        var bv = block.AddComponent<VerticalLayoutGroup>();
        bv.spacing = 3; bv.childControlWidth = true; bv.childControlHeight = true;
        bv.childForceExpandWidth = true; bv.childForceExpandHeight = false;
        var bcsf = block.AddComponent<ContentSizeFitter>();
        bcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var ble = block.GetComponent<LayoutElement>() ?? block.AddComponent<LayoutElement>();
        ble.flexibleWidth = 1; ble.minWidth = 540;

        var title = MenuTheme.NewText(e.title, block.transform, 28, TextAlignmentOptions.TopLeft, _font);
        title.fontStyle = FontStyles.Bold; title.color = Color.white;
        title.textWrappingMode = TextWrappingModes.Normal;

        var desc = MenuTheme.NewText(e.desc, block.transform, 23, TextAlignmentOptions.TopLeft, _font);
        desc.color = DescCol; desc.textWrappingMode = TextWrappingModes.Normal;

        if (!string.IsNullOrEmpty(e.tip))
        {
            var tip = MenuTheme.NewText("TIP   " + e.tip, block.transform, 21, TextAlignmentOptions.TopLeft, _font);
            tip.color = TipCol; tip.fontStyle = FontStyles.Italic;
            tip.textWrappingMode = TextWrappingModes.Normal;
            tip.margin = new Vector4(0, 4, 0, 0);
        }
    }

    private void AddDivider(Transform parent)
    {
        var holder = MenuTheme.NewUI("RuleHolder", parent);
        SetH(holder, 12);
        var rule = MenuTheme.NewUI("Rule", holder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.12f, 0.5f); rr.anchorMax = new Vector2(0.88f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.8f);
    }

    private static void SetH(Component c, float hgt) => SetH(c.gameObject, hgt);
    private static void SetH(GameObject go, float hgt)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = hgt; le.preferredHeight = hgt; le.flexibleHeight = 0f;
    }

    //  DEVICE TABS + CHIP REFRESH
    private void UpdateSchemeTabs()
    {
        StyleTab(_kbTab, !_showGamepad);
        StyleTab(_padTab, _showGamepad);
    }

    private void StyleTab(Button btn, bool active)
    {
        if (btn == null) return;
        if (btn.targetGraphic is Image img)
            img.color = active ? MenuTheme.BtnActive
                               : (MenuTheme.ButtonSprite != null ? Color.white : MenuTheme.BtnSolid);
        var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.color = active ? Color.white : MenuTheme.ValueCol;
    }

    private void RefreshAllChips()
    {
        foreach (var c in _chips)
            if (c.label != null) c.label.text = ChipTextFor(c.entry, _showGamepad);
    }

    private string ChipTextFor(Entry e, bool gamepad)
    {
        if (e.fallbackOnly) return gamepad ? e.gpFallback : e.kbFallback;

        if (e.comboAction != null)
        {
            if (gamepad) return e.gpFallback;
            string a = ResolveBinding(e.action, false, null, null, FirstToken(e.kbFallback));
            string b = ResolveBinding(e.comboAction, false, null, null, e.comboKbFallback);
            return a + " / " + b;
        }

        string fallback = gamepad ? e.gpFallback : e.kbFallback;
        return ResolveBinding(e.action, gamepad, e.kbPrefer, e.gpPrefer, fallback);
    }

    private static string FirstToken(string s) =>
        string.IsNullOrEmpty(s) ? s : s.Split('/')[0].Trim();

    //  BINDING RESOLUTION 
    private InputActionAsset _resolvedAsset;
    private bool _assetResolved;

    private InputActionAsset ResolveAsset()
    {
        if (_assetResolved) return _resolvedAsset;
        _assetResolved = true;
        if (inputActions != null) { _resolvedAsset = inputActions; return _resolvedAsset; }
        var pi = FindFirstObjectByType<PlayerInput>();
        if (pi != null && pi.actions != null) { _resolvedAsset = pi.actions; return _resolvedAsset; }
        _resolvedAsset = Resources.Load<InputActionAsset>("PlayerInputActions");
        return _resolvedAsset;
    }

    // Push any committed rebinds onto our asset so the chips match what the player
    // sees in-game. Called via reflection so this file still compiles if dropped
    // into a project without ControlRebindService. Equivalent to:
    //   ControlRebindService.ApplyTo(asset);
    private static System.Reflection.MethodInfo _applyTo;
    private static bool _applyToResolved;
    private static void ApplyRebindOverrides(InputActionAsset asset)
    {
        if (asset == null) return;
        try
        {
            if (!_applyToResolved)
            {
                _applyToResolved = true;
                Type svc = Type.GetType("ControlRebindService");
                if (svc == null)
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    { svc = asm.GetType("ControlRebindService"); if (svc != null) break; }
                _applyTo = svc?.GetMethod("ApplyTo",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(InputActionAsset) }, null);
            }
            _applyTo?.Invoke(null, new object[] { asset });
        }
        catch { /* no rebind service present - chips fall back to defaults */ }
    }

    private string ResolveBinding(string actionName, bool gamepad,
                                  string kbPrefer, string gpPrefer, string fallback)
    {
        var asset = ResolveAsset();
        if (asset == null || string.IsNullOrEmpty(actionName)) return fallback;
        var action = asset.FindAction(actionName, false);
        if (action == null) return fallback;

        string group = gamepad ? GROUP_PAD : GROUP_KBM;
        string prefer = gamepad ? gpPrefer : kbPrefer;

        string firstMatch = null;
        try
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                if (!(b.groups ?? string.Empty).Contains(group)) continue;
                string path = b.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;
                if (firstMatch == null) firstMatch = path;
                if (!string.IsNullOrEmpty(prefer) &&
                    path.IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0)
                    return Friendly(path, gamepad);
            }
        }
        catch { return fallback; }

        return firstMatch != null ? Friendly(firstMatch, gamepad) : fallback;
    }

    private static string Friendly(string path, bool gamepad)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string p = path.ToLowerInvariant();

        if (gamepad)
        {
            if (p.Contains("buttonnorth")) return "Y";
            if (p.Contains("buttonsouth")) return "A";
            if (p.Contains("buttoneast")) return "B";
            if (p.Contains("buttonwest")) return "X";
            if (p.Contains("righttrigger")) return "RT";
            if (p.Contains("lefttrigger")) return "LT";
            if (p.Contains("rightshoulder")) return "RB";
            if (p.Contains("leftshoulder")) return "LB";
            if (p.Contains("dpad")) return "D-Pad";
            if (p.Contains("leftstickpress")) return "L3";
            if (p.Contains("rightstickpress")) return "R3";
            if (p.Contains("leftstick")) return "Left Stick";
            if (p.Contains("rightstick")) return "Right Stick";
            if (p.Contains("start")) return "Start";
            if (p.Contains("select")) return "Back";
        }
        else
        {
            if (p.Contains("leftbutton")) return "Left Click";
            if (p.Contains("rightbutton")) return "Right Click";
            if (p.Contains("middlebutton")) return "Middle Click";
        }

        try
        {
            string s = InputControlPath.ToHumanReadableString(
                path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(s) ? path : s;
        }
        catch { return path; }
    }

    //  SCHEME / ECONOMY
    private bool ResolveInitialScheme()
    {
        if (defaultScheme == DefaultScheme.Gamepad) return true;
        if (defaultScheme == DefaultScheme.KeyboardMouse) return false;
        var pad = Gamepad.current;
        if (pad == null) return false;
        double padT = pad.lastUpdateTime;
        double kbT = Keyboard.current != null ? Keyboard.current.lastUpdateTime : 0;
        double mT = Mouse.current != null ? Mouse.current.lastUpdateTime : 0;
        return padT >= Math.Max(kbT, mT);
    }

    private void RefreshEconomyFromGame()
    {
        var em = EnergyManager.Instance;
        if (em == null) return;
        try { int build = em.GetTowerBuildCost(); if (build > 0) towerBuildCost = build; }
        catch { /* keep inspector defaults */ }
    }

    //  DATA TYPES
    private class Section
    {
        public string title; public List<Entry> entries;
        public Section(string t, List<Entry> e) { title = t; entries = e; }
    }

    private class Entry
    {
        public string title, desc, tip;
        public string action, comboAction;
        public string kbPrefer, gpPrefer;
        public string kbFallback, gpFallback, comboKbFallback;
        public string infoChip;
        public bool fallbackOnly;
    }

    private class ChipBinding { public Entry entry; public TextMeshProUGUI label; }
}


