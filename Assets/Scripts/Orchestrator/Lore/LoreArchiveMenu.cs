using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// LORE ARCHIVE MENU (TextMeshPro) 
// Recovered logs browser styled to match the pause menu: the ornate MenuPanel_1
// box + magenta-flame frame as backdrop, the flame Button/Button_1 sprites for the
// controls, burned-paper sheets layered ON TOP as the reading surface. Text uses
// TextMeshPro so you can assign your menu font (e.g. Cinzel-Black SDF) directly.

public class LoreArchiveMenu : MonoBehaviour
{
    private static LoreArchiveMenu _instance;
    public static LoreArchiveMenu Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<LoreArchiveMenu>();
                if (_instance == null)
                    _instance = new GameObject("LoreArchiveMenu").AddComponent<LoreArchiveMenu>();
            }
            return _instance;
        }
    }

    public static LoreArchiveMenu Ensure(bool showButton, KeyCode hotkey, bool debugReset = false)
    {
        var inst = Instance;
        inst.hotkey = hotkey;
        inst.debugResetButton = debugReset;
        inst.SetButtonVisible(showButton);
        return inst;
    }

    /// Assign the pause-menu sprites + TMP font directly (preferred over Resources).
    /// Call BEFORE the panel is first opened (e.g. from LoreChestSpawner.Start).
    public void ApplyTheme(Sprite panel, Sprite listPanel, Sprite button, Sprite buttonHighlight, TMP_FontAsset uiFont)
    {
        assignedPanel = panel;
        assignedListPanel = listPanel;
        assignedButton = button;
        assignedButtonHi = buttonHighlight;
        assignedFont = uiFont;
    }

    private const string ThemeFolder = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/";

    // Palette (text + accents; the panel/button look comes from the themed sprites).
    private readonly Color backdropColor = new Color(0.02f, 0.02f, 0.04f, 0.88f);
    private readonly Color titleColor = new Color(0.97f, 0.93f, 0.82f, 1f);
    private readonly Color listBgColor = new Color(0f, 0f, 0f, 0.45f);
    private readonly Color itemText = new Color(0.96f, 0.93f, 0.86f, 1f);
    private readonly Color inkTitle = new Color(0.22f, 0.13f, 0.06f, 1f);
    private readonly Color inkBody = new Color(0.17f, 0.11f, 0.06f, 1f);
    private readonly Color magentaText = new Color(0.95f, 0.55f, 0.95f, 1f);

    public KeyCode hotkey = KeyCode.J;
    public bool debugResetButton = false;

    // runtime — button
    private GameObject buttonCanvas, buttonGO;
    private bool builtButton;

    // runtime — panel
    private Canvas canvas;
    private GameObject root;
    private RectTransform listContent;
    private TextMeshProUGUI headerLabel, readingTitle, readingBody;
    private bool builtPanel, isOpen;
    private float prevTimeScale;
    private bool prevInputSuppressed;
    private bool prevCursorVisible;

    private TMP_FontAsset font;                 // resolved font (assigned, else TMP default)
    private Sprite solid, paper;                // generated fallbacks / paper
    private Sprite itemNormal, itemSel;         // generated list-item buttons (gradient + charred edge)
    private Sprite themePanel, themeBtn, themeBtnHi, themeListPanel; // resolved theme sprites
    private Sprite assignedPanel, assignedListPanel, assignedButton, assignedButtonHi; // from Inspector
    private TMP_FontAsset assignedFont;
    private static bool _warnedLoad;

    private class Item { public int id; public Image bg; }
    private readonly List<Item> items = new List<Item>();
    private int selectedId = int.MinValue;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy() { if (_instance == this) _instance = null; }

    void Update()
    {
        if (HotkeyDown(hotkey)) Toggle();
        if (isOpen && EscDown()) Close();
    }

    //  control 
    public bool IsOpen => isOpen;
    public void Toggle() { if (isOpen) Close(); else Open(); }

    public void Open()
    {
        if (builtPanel && (root == null || canvas == null)) builtPanel = false;
        EnsurePanel();
        Populate();
        root.SetActive(true);
        isOpen = true;

        CombatJuice.StopAllShake();
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        prevCursorVisible = Cursor.visible;
        Cursor.visible = true;
        prevInputSuppressed = PlayerAttack.InputSuppressed;
        PlayerAttack.InputSuppressed = true;
    }

    public void Close()
    {
        if (!isOpen) return;
        Time.timeScale = prevTimeScale;
        PlayerAttack.InputSuppressed = prevInputSuppressed;
        // Restore whatever the cursor was before (e.g. the pause menu
        // underneath had it visible) instead of forcing it off.
        Cursor.visible = prevCursorVisible;
        if (root != null) root.SetActive(false);
        isOpen = false;
    }

    public void SetButtonVisible(bool visible)
    {
        if (visible) EnsureButton();
        if (buttonGO != null) buttonGO.SetActive(visible);
    }

    //  themed-art loading 
    private void CacheArt()
    {
        if (assignedFont != null) font = assignedFont;   // your menu font (TMP). null → TMP default.
        if (solid == null) solid = LorePaperArt.MakeSolidSprite();
        if (paper == null) paper = LorePaperArt.MakePaperSprite();
        if (itemNormal == null) itemNormal = LorePaperArt.MakeButtonSprite(false);
        if (itemSel == null) itemSel = LorePaperArt.MakeButtonSprite(true);

        if (themePanel == null)
        {
            themePanel = Themed(assignedPanel, "MenuPanel_1", new Vector4(140, 140, 140, 140));
            if (themePanel == null) { themePanel = LorePaperArt.MakePanelSprite(); WarnLoadFailed(); }
        }
        if (themeListPanel == null && assignedListPanel != null)
            themeListPanel = WithBorder(assignedListPanel, new Vector4(140, 140, 140, 140));
        if (themeBtn == null) themeBtn = Themed(assignedButton, "Button", new Vector4(70, 80, 70, 80));
        if (themeBtnHi == null) themeBtnHi = Themed(assignedButtonHi, "Button_1", new Vector4(80, 90, 80, 90));
    }

    private static void WarnLoadFailed()
    {
        if (_warnedLoad) return;
        _warnedLoad = true;
        Debug.LogWarning("[LoreArchive] Themed panel not found — using fallback art. " +
            "Either assign the sprites on the LoreChestSpawner (Archive Theming), or make sure the PNGs " +
            "live under 'Assets/Resources/" + ThemeFolder + "' imported as Sprites (path: '" + ThemeFolder + "MenuPanel_1').");
    }

    private Sprite Themed(Sprite assigned, string resourceName, Vector4 border)
    {
        Sprite src = assigned;
        if (src == null && !string.IsNullOrEmpty(resourceName))
        {
            string path = ThemeFolder + resourceName;
            var all = Resources.LoadAll<Sprite>(path);
            if (all != null && all.Length > 0) src = all[0];
            if (src == null) src = Resources.Load<Sprite>(path);
        }
        return src != null ? WithBorder(src, border) : null;
    }

    private static Sprite WithBorder(Sprite src, Vector4 border)
    {
        try
        {
            float ppu = src.pixelsPerUnit > 0 ? src.pixelsPerUnit : 100f;
            return Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), ppu, 0,
                                 SpriteMeshType.FullRect, border);
        }
        catch { return src; }
    }

    //  on-screen "ARCHIVE" button 
    private void EnsureButton()
    {
        if (builtButton) return;
        builtButton = true;
        CacheArt();

        buttonCanvas = new GameObject("LoreArchiveButtonCanvas");
        var c = buttonCanvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 9990;
        var sc = buttonCanvas.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        buttonCanvas.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var btn = ThemedButton("ArchiveButton", buttonCanvas.transform, "ARCHIVE", 28, out RectTransform rt);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(26, 26);
        rt.sizeDelta = new Vector2(220, 74);
        btn.onClick.AddListener(Open);
        buttonGO = btn.gameObject;
    }

    //  archive panel 
    private void EnsurePanel()
    {
        if (builtPanel) return;
        builtPanel = true;
        CacheArt();

        var go = new GameObject("LoreArchiveCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9996;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        root = go;

        // Backdrop (click closes).
        var backdrop = NewImage("Backdrop", root.transform, solid, backdropColor);
        StretchFull(backdrop.rectTransform);
        var bb = backdrop.gameObject.AddComponent<Button>();
        bb.transition = Selectable.Transition.None;
        bb.onClick.AddListener(Close);

        // Themed panel (bigger now), 9-sliced.
        var panelImg = NewImage("Panel", root.transform, themePanel, Color.white);
        panelImg.type = Image.Type.Sliced;
        var pr = panelImg.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(1600, 900);

        // Header title (inset from sides so corner flames don't obscure it).
        headerLabel = NewText("Header", pr, "Recovered Logs", 40, FontStyles.Bold, TextAlignmentOptions.Top, titleColor);
        var hr = headerLabel.rectTransform;
        hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f); hr.pivot = new Vector2(0.5f, 1f);
        hr.offsetMin = new Vector2(260, -126); hr.offsetMax = new Vector2(-260, -54);

        // Close button (top-right, inset past corner ornament).
        var closeBtn = ThemedButton("Close", pr, "X", 28, out RectTransform cr);
        cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(1f, 1f);
        cr.anchoredPosition = new Vector2(-60, -54);
        cr.sizeDelta = new Vector2(70, 64);
        closeBtn.onClick.AddListener(Close);

        // Optional debug reset (top-left, inset).
        if (debugResetButton)
        {
            var resetBtn = ThemedButton("Reset", pr, "Reset Lore", 22, out RectTransform rr);
            rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f); rr.pivot = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(60, -54);
            rr.sizeDelta = new Vector2(210, 64);
            resetBtn.onClick.AddListener(() =>
            {
                LoreCodex.Instance.ClearAll();
                selectedId = int.MinValue;
                Populate();
            });
        }

        //  LEFT: scrollable list 
        Image listBg;
        float vpInset;
        if (themeListPanel != null)
        {
            listBg = NewImage("ListBg", pr, themeListPanel, Color.white);
            listBg.type = Image.Type.Sliced;
            listBg.pixelsPerUnitMultiplier = 2.4f;  // shrink the box's ornate frame so items don't run into it
            vpInset = 30f;
        }
        else
        {
            listBg = NewImage("ListBg", pr, solid, listBgColor);
            vpInset = 10f;
        }
        var lr = listBg.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0.33f, 1f);
        lr.offsetMin = new Vector2(82, 86); lr.offsetMax = new Vector2(-6, -142);

        var viewport = NewImage("Viewport", lr, solid, new Color(0, 0, 0, 0.001f));
        var vr = viewport.rectTransform;
        vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
        vr.offsetMin = new Vector2(vpInset, vpInset); vr.offsetMax = new Vector2(-vpInset, -vpInset);
        viewport.gameObject.AddComponent<RectMask2D>();

        var contentGO = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(viewport.transform, false);
        listContent = contentGO.GetComponent<RectTransform>();
        listContent.anchorMin = new Vector2(0f, 1f); listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0f, 1f);
        listContent.sizeDelta = Vector2.zero;
        listContent.anchoredPosition = Vector2.zero;
        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(8, 8, 16, 18);     // top/bottom breathing room so the list never ends flush
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        var fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = listBg.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = vr; scroll.content = listContent;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34f;

        //  RIGHT: burned-paper reading pane (on top of the themed panel) 
        var pane = NewImage("ReadingPaper", pr, paper, Color.white);
        pane.type = Image.Type.Simple;
        var pnr = pane.rectTransform;
        pnr.anchorMin = new Vector2(0.33f, 0f); pnr.anchorMax = new Vector2(1f, 1f);
        pnr.offsetMin = new Vector2(16, 70); pnr.offsetMax = new Vector2(-72, -138);

        readingTitle = NewText("RTitle", pnr, "", 32, FontStyles.Bold, TextAlignmentOptions.Center, inkTitle);
        var rtt = readingTitle.rectTransform;
        rtt.anchorMin = new Vector2(0f, 1f); rtt.anchorMax = new Vector2(1f, 1f); rtt.pivot = new Vector2(0.5f, 1f);
        rtt.offsetMin = new Vector2(90, -188); rtt.offsetMax = new Vector2(-90, -98);

        readingBody = NewText("RBody", pnr, "", 25, FontStyles.Italic, TextAlignmentOptions.TopLeft, inkBody);
        readingBody.enableAutoSizing = true;    // long fragments shrink to fit the paper
        readingBody.fontSizeMin = 15;
        readingBody.fontSizeMax = 25;
        readingBody.lineSpacing = 8f;
        var rbb = readingBody.rectTransform;
        rbb.anchorMin = Vector2.zero; rbb.anchorMax = Vector2.one;
        rbb.offsetMin = new Vector2(98, 80); rbb.offsetMax = new Vector2(-98, -202);

        root.SetActive(false);
    }

    private void Populate()
    {
        items.Clear();
        if (listContent != null)
            for (int i = listContent.childCount - 1; i >= 0; i--)
                Destroy(listContent.GetChild(i).gameObject);

        var codex = LoreCodex.Instance;
        int total = LoreContent.TotalCount;
        int found = codex != null ? codex.DiscoveredCount : 0;
        if (headerLabel != null) headerLabel.text = $"Recovered Logs    {found} / {total}";

        var discovered = new List<LoreFragment>();
        if (codex != null)
            foreach (int id in codex.DiscoveredIds)
            {
                var f = LoreContent.Get(id);
                if (f != null) discovered.Add(f);
            }
        discovered.Sort((a, b) => a.id.CompareTo(b.id));

        if (discovered.Count == 0)
        {
            readingTitle.text = "No Logs Yet";
            readingBody.text = "You haven't recovered any logs. Open chests out in the wasteland — " +
                               "follow the pale footprints — and the pages you find will be collected here.";
            return;
        }

        foreach (var frag in discovered) AddListItem(frag);

        int toShow = discovered.Exists(f => f.id == selectedId) ? selectedId : discovered[0].id;
        ShowFragment(toShow);
    }

    private void AddListItem(LoreFragment frag)
    {
        var itemImg = NewImage($"Item_{frag.id}", listContent, itemNormal, Color.white);
        itemImg.type = Image.Type.Sliced;
        var le = itemImg.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 92; le.preferredHeight = 92;

        int id = frag.id;
        itemImg.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowFragment(id));

        // Centered, wrapping, auto-shrinking text so long titles stay inside the button.
        var label = NewText("Title", itemImg.rectTransform, frag.title, 22, FontStyles.Normal, TextAlignmentOptions.Center, itemText);
        label.enableAutoSizing = true;
        label.fontSizeMin = 15;
        label.fontSizeMax = 22;
        var lr = label.rectTransform;
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
        lr.offsetMin = new Vector2(26, 16); lr.offsetMax = new Vector2(-26, -16);   // padding inside the button

        items.Add(new Item { id = frag.id, bg = itemImg });
    }

    private void ShowFragment(int id)
    {
        selectedId = id;
        var frag = LoreContent.Get(id);
        if (frag != null) { readingTitle.text = frag.title; readingBody.text = frag.body; }
        foreach (var it in items)
            if (it.bg != null) it.bg.sprite = (it.id == id) ? itemSel : itemNormal;
    }

    //  UI helpers 
    private Button ThemedButton(string name, Transform parent, string label, float fontSize, out RectTransform rt)
    {
        Image img;
        if (themeBtn != null)
        {
            img = NewImage(name, parent, themeBtn, Color.white);
            img.type = Image.Type.Sliced;
        }
        else
        {
            img = NewImage(name, parent, solid, new Color(0.16f, 0.10f, 0.16f, 0.95f));
        }
        rt = img.rectTransform;

        var btn = img.gameObject.AddComponent<Button>();
        if (themeBtn != null && themeBtnHi != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState { highlightedSprite = themeBtnHi, pressedSprite = themeBtnHi, selectedSprite = themeBtn };
        }
        else
        {
            var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.7f, 1f, 1f); btn.colors = cb;
        }

        var txt = NewText(name + "Label", rt, label, fontSize, FontStyles.Bold, TextAlignmentOptions.Center,
                          themeBtn != null ? Color.white : magentaText);
        StretchFull(txt.rectTransform);
        return btn;
    }

    private void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
        esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
    }

    private Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite; img.color = color;
        return img;
    }

    private TextMeshProUGUI NewText(string name, Transform parent, string content, float size,
                                    FontStyles style, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (font != null) txt.font = font;        // else TMP uses its default font asset
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.alignment = align;
        txt.color = color;
        txt.textWrappingMode = TextWrappingModes.Normal;
        txt.richText = false;                      // titles/bodies contain literal '<' rarely, keep plain
        txt.raycastTarget = false;
        return txt;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    //  input (backend-agnostic) 
    private bool HotkeyDown(KeyCode k)
    {
        if (k == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
        Key key = ToKey(k);
        return key != Key.None && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(k);
#else
        return false;
#endif
    }

    private bool EscDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static Key ToKey(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.J: return Key.J;
            case KeyCode.L: return Key.L;
            case KeyCode.P: return Key.P;
            case KeyCode.B: return Key.B;
            case KeyCode.M: return Key.M;
            case KeyCode.I: return Key.I;
            case KeyCode.K: return Key.K;
            case KeyCode.O: return Key.O;
            case KeyCode.Tab: return Key.Tab;
            default: return Key.None;
        }
    }
#endif
}
