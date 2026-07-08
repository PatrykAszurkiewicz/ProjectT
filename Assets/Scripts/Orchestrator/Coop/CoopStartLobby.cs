using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

// START-OF-RUN LOBBY GATE
// Sibling of ContinueRunMenu, same MenuTheme look and the same "live-poll controllers,
// gate the start button until enough are seated" pattern — but this one starts a NEW
// run rather than resuming a saved one.
// Co-op run must KNOW it's 2 players at the moment it begins (difficulty scaling,
// seating, split-screen). Starting co-op with only one controller present produced a
// half-seated run that then surprise-split when the 2nd pad was switched on mid-fight.
// This gate refuses to start co-op until both controller slots are seated.

public class CoopStartLobby : MonoBehaviour
{
    [Header("Fonts (optional — matches OptionsMenu)")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private Font titleFontTtf;

    [Header("Where to go after a choice")]
    [Tooltip("Scene to load to start the run. Leave EMPTY if this menu already lives in " +
             "the gameplay scene — it will reload the current scene (matches ContinueRunMenu).")]
    [SerializeField] private string gameplaySceneName = "";

    [Header("Controller gate")]
    [Tooltip("Count keyboard+mouse as ONE player slot (matches CoopManager's seating).")]
    [SerializeField] private bool countKeyboardAsPlayer = true;

    [Tooltip("Minimum number of CONNECTED GAMEPADS for co-op. With keyboard counted as a " +
             "slot, 1 means co-op can start on 1 pad + keyboard OR 2 pads (player's choice). " +
             "Set to 2 to force both players onto physical gamepads.")]
    [SerializeField] private int minGamepads = 1;

    private static CoopStartLobby _instance;
    private GameObject _root;
    private TMP_FontAsset _font;

    private TextMeshProUGUI _summary, _status, _coopLabel;
    private Button _coopBtn;
    private Image _coopImg;

    private bool _committing;

    //  ENTRY POINTS 
    private void Awake() { if (_instance == null) _instance = this; }
    private void OnDestroy() { if (_instance == this) _instance = null; }

    // Open the co-op wait screen that loads gameplayScene once enough controllers are connected. Called from the "Start Co-op" button
    public static void OpenCoop(string gameplayScene)
    {
        if (_instance == null) _instance = new GameObject("CoopStartLobby").AddComponent<CoopStartLobby>();
        _instance.gameplaySceneName = gameplayScene;
        _instance.OpenMenu();
    }
    public static void Close() { if (_instance != null) _instance.CloseMenu(); }

    public void OpenMenu()
    {
        MenuTheme.EnsureEventSystem();
        if (_root == null) BuildUI();
        RefreshGate();
        _root.SetActive(true);
        Cursor.visible = true;
    }
    public void CloseMenu() { if (_root != null) _root.SetActive(false); }

    private void Update()
    {
        if (_root != null && _root.activeSelf) RefreshGate();
    }

    //  GATE 
    private int ConnectedGamepads() => Gamepad.all.Count;

    private int CoopAvailableSlots()
    {
        int slots = Gamepad.all.Count;
        if (countKeyboardAsPlayer && Keyboard.current != null && Mouse.current != null) slots += 1;
        return slots;
    }

    private bool CoopReady()
    {
        // A slot is a connected gamepad OR keyboard+mouse (one KB&M slot), matching
        // CoopManager's seating. So co-op is ready with 2 pads OR 1 pad + keyboard —
        // whatever the players prefer. minGamepads (default 1) guards against starting
        // with zero real controllers (KB&M alone is only one slot anyway).
        return CoopAvailableSlots() >= 2 && ConnectedGamepads() >= Mathf.Clamp(minGamepads, 0, 2);
    }

    private void RefreshGate()
    {
        if (_coopBtn == null) return;

        bool ready = CoopReady();
        _coopBtn.interactable = ready;
        if (_coopImg != null)
            _coopImg.color = ready ? MenuTheme.BtnActive : new Color(0.18f, 0.20f, 0.26f, 1f);

        if (_coopLabel != null)
        {
            _coopLabel.text = ready
                ? "Start Co-op (2 Players)"
                : $"Waiting for players ({CoopAvailableSlots()}/2)…";
            _coopLabel.color = ready ? Color.white : MenuTheme.ValueCol;
        }

        if (_status != null)
        {
            string kb = (countKeyboardAsPlayer && Keyboard.current != null && Mouse.current != null)
                ? " + keyboard" : "";
            _status.text = $"Players ready: {CoopAvailableSlots()} / 2   (gamepads: {ConnectedGamepads()}{kb})";
        }
    }

    //  ACTIONS 
    private void OnStartCoop()
    {
        if (_committing || !CoopReady()) return;
        _committing = true;

        // Fresh 2-player run. Tell CoopManager (seating) and GameOrchestrator
        // (resume-vs-fresh) this is a FRESH co-op run. resume:false → StartRun → BeginRun
        // deletes the stale save, so a leftover co-op save can't hijack/stall this launch.
        if (SessionConfig.Instance != null) SessionConfig.Instance.SetPlayerCount(2);
        RunPersistence.DeleteSaveFile();
        RunResumeIntent.Set(resume: false, count: 2);

        CloseMenu();
        Time.timeScale = 1f; // persists across loads; a frozen new scene hangs on its intro.

        string scene = !string.IsNullOrEmpty(gameplaySceneName)
            ? gameplaySceneName
            : SceneManager.GetActiveScene().name;

        //Debug.Log($"[CoopStartLobby] Starting FRESH co-op run → loading '{scene}'.");
        ScreenFade.LoadScene(scene);
    }

    //  UI (mirrors ContinueRunMenu) 
    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);

        _root = new GameObject("CoopStartCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4800;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var dim = MenuTheme.NewUI("Dim", _root.transform);
        MenuTheme.Stretch(dim.GetComponent<RectTransform>());
        dim.AddComponent<Image>().sprite = MenuTheme.VerticalGradient(MenuTheme.GradTop, MenuTheme.GradBottom);

        var panel = MenuTheme.NewUI("Panel", dim.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(900, 620);
        MenuTheme.ApplySprite(panel.AddComponent<Image>(), MenuTheme.PanelSprite, MenuTheme.PanelSolid);

        var inner = MenuTheme.NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(64, 72); irt.offsetMax = new Vector2(-64, -78);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 14; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperCenter;

        var title = MenuTheme.NewText("CO-OP", inner.transform, 50, TextAlignmentOptions.Center, _font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 58);

        AddDivider(inner.transform);

        _summary = MenuTheme.NewText("Connect controllers to begin a co-op run.\n(1 pad + keyboard, or 2 pads.)",
                inner.transform, 24, TextAlignmentOptions.Center, _font);
        _summary.color = MenuTheme.ValueCol;
        SetH(_summary, 80);

        _status = MenuTheme.NewText("", inner.transform, 22, TextAlignmentOptions.Center, _font);
        _status.color = MenuTheme.Magenta;
        SetH(_status, 32);

        // Co-op (gated)
        _coopBtn = MenuTheme.NewButton("Start Co-op (2 Players)", inner.transform, 24, _font);
        SetH(_coopBtn, 64);
        _coopImg = _coopBtn.targetGraphic as Image;
        _coopLabel = _coopBtn.GetComponentInChildren<TextMeshProUGUI>();
        _coopBtn.onClick.AddListener(OnStartCoop);

        var spacer = MenuTheme.NewUI("Spacer", inner.transform);
        var sle = spacer.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 0f;

        var back = MenuTheme.NewButton("Back", inner.transform, 22, _font);
        SetH(back, 44);
        back.onClick.AddListener(CloseMenu);
    }

    private void AddDivider(Transform parent)
    {
        var holder = MenuTheme.NewUI("RuleHolder", parent);
        SetH(holder, 14);
        var rule = MenuTheme.NewUI("Rule", holder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.20f, 0.5f); rr.anchorMax = new Vector2(0.80f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.8f);
    }

    private static void SetH(Component c, float h) => SetH(c.gameObject, h);
    private static void SetH(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;
    }
}
