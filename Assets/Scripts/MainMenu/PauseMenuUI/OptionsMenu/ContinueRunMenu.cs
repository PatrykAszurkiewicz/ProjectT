using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

// CONTINUE-RUN LOBBY GATE 
// Reads the single resume save written by RunPersistence and shows how many
// players the saved run needs. "Continue Co-op Run" stays disabled until that
// many controller slots are seated; the player can always "Abandon Run & Start
// Solo" instead. Live-polls connected devices so the button enables the moment
// the missing controller is plugged in.
// Player-count handoff is decoupled from SessionConfig: when the player commits,
// this writes RunResumeIntent (in RunPersistence.cs). CoopManager reads it to
// seat the right number of players, and GameOrchestrator reads it on boot to
// decide resume-vs-fresh. So this works whether the menu sits in a dedicated
// lobby scene (set Gameplay Scene Name) or overlays the gameplay scene (leave it
// empty — it drives GameOrchestrator directly).
// Open from a Button's OnClick -> ContinueRunMenu.OpenMenu(), or via code:
//   ContinueRunMenu.Open();
public class ContinueRunMenu : MonoBehaviour
{
    [Header("Fonts (optional — matches OptionsMenu)")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private Font titleFontTtf;

    [Header("Where to go after a choice")]
    [Tooltip("Scene to load when the player continues or abandons. Leave EMPTY if " +
             "this menu already lives in the gameplay scene — it will then drive " +
             "GameOrchestrator directly instead of loading a scene.")]
    [SerializeField] private string gameplaySceneName = "";

    [Header("Controller gate")]
    [Tooltip("Count keyboard+mouse as ONE player slot (matches CoopManager's seating). " +
             "A co-op run started with KB+M as P1 then only needs the OTHER controller(s) " +
             "connected to continue.")]
    [SerializeField] private bool countKeyboardAsPlayer = true;

    private static ContinueRunMenu _instance;
    private GameObject _root;
    private TMP_FontAsset _font;

    private TextMeshProUGUI _summary, _status, _continueLabel;
    private Button _continueBtn;
    private Image _continueImg;

    private int _requiredPlayers = 1;
    private bool _hasSave;

    private bool _committing;               // blocks a second click before the reload lands
    private static bool _suppressNextOpen;  // static → survives the scene reload

    // Clear the suppress flag at session start so an editor with "no domain
    // reload" doesn't carry a stale value into the next Play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _suppressNextOpen = false;

    //  ENTRY POINTS 
    private void Awake() { if (_instance == null) _instance = this; }
    private void OnDestroy() { if (_instance == this) _instance = null; }

    public static void Open()
    {
        if (_instance == null) _instance = new GameObject("ContinueRunMenu").AddComponent<ContinueRunMenu>();
        _instance.OpenMenu();
    }

    /// <summary>Open the continue gate and load <paramref name="gameplayScene"/> on commit.
    /// Use when opening from a different scene (e.g. the main menu) so Continue loads the
    /// gameplay scene instead of reloading the menu.</summary>
    public static void Open(string gameplayScene)
    {
        if (_instance == null) _instance = new GameObject("ContinueRunMenu").AddComponent<ContinueRunMenu>();
        if (!string.IsNullOrEmpty(gameplayScene)) _instance.gameplaySceneName = gameplayScene;
        _instance.OpenMenu();
    }
    public static void Close() { if (_instance != null) _instance.CloseMenu(); }

    public void OpenMenu()
    {
        // After a commit, the scene reloads and this menu would otherwise reopen
        // on top of the freshly-started run (a full-screen dim canvas = "black
        // screen"). Swallow exactly that one auto-open.
        if (_suppressNextOpen) { _suppressNextOpen = false; CloseMenu(); return; }

        MenuTheme.EnsureEventSystem();
        ReadSave();
        if (_root == null) BuildUI();
        RefreshSummary();
        RefreshGate();
        _root.SetActive(true);
        Cursor.visible = true;
    }
    public void CloseMenu() { if (_root != null) _root.SetActive(false); }

    private void Update()
    {
        // Live gate: enable Continue the instant enough controllers are seated.
        if (_root != null && _root.activeSelf) RefreshGate();
    }

    //  SAVE / GATE STATE 
    private void ReadSave()
    {
        // Static, file-based: RunPersistence has no instance in the main-menu scene,
        // so we must read the save file directly or the gate always says "no save".
        _hasSave = RunPersistence.SaveExists;
        _requiredPlayers = Mathf.Max(1, RunPersistence.RequiredPlayersInSaveStatic());
    }

    // Player slots currently available, mirroring CoopManager's seating logic:
    // every connected gamepad is a slot, plus keyboard+mouse as one fallback slot.
    private int AvailableSlots()
    {
        int slots = Gamepad.all.Count;
        if (countKeyboardAsPlayer && Keyboard.current != null && Mouse.current != null) slots += 1;
        return slots;
    }

    private void RefreshGate()
    {
        if (_continueBtn == null) return;

        bool ok = _hasSave && AvailableSlots() >= _requiredPlayers;
        _continueBtn.interactable = ok;
        if (_continueImg != null)
            _continueImg.color = ok ? MenuTheme.BtnActive
                                    : new Color(0.18f, 0.20f, 0.26f, 1f); // greyed-out

        if (_continueLabel != null)
        {
            if (!_hasSave) _continueLabel.text = "No Saved Run";
            else if (ok) _continueLabel.text = _requiredPlayers > 1 ? "Continue Co-op Run" : "Continue Run";
            else _continueLabel.text = $"Waiting for controllers ({AvailableSlots()}/{_requiredPlayers})…";
            _continueLabel.color = ok ? Color.white : MenuTheme.ValueCol;
        }

        if (_status != null)
            _status.text = _hasSave
                ? $"Controllers connected: {AvailableSlots()} / {_requiredPlayers}"
                : "There is no run to continue.";
    }

    private void RefreshSummary()
    {
        if (_summary == null) return;
        if (!_hasSave) { _summary.text = "No saved run found."; return; }

        if (RunPersistence.TryReadSave(out var d) && d != null)
        {
            string who = _requiredPlayers > 1 ? $"{_requiredPlayers}-player co-op run" : "Solo run";
            _summary.text = $"{who}\nStage {d.stageIndex + 1}, Wave {d.waveIndex + 1}";
        }
        else _summary.text = "Saved run found.";
    }

    //  ACTIONS 
    private void OnContinue()
    {
        if (_committing || !_hasSave || AvailableSlots() < _requiredPlayers) return;
        _committing = true;
        RunResumeIntent.Set(resume: true, count: _requiredPlayers);
        GoToGameplay();
    }

    private void OnAbandonSolo()
    {
        if (_committing) return;
        _committing = true;
        // Drop the co-op save and start a fresh solo run. Static delete because there is
        // no RunPersistence instance in the menu scene.
        RunPersistence.DeleteSaveFile();
        RunResumeIntent.Set(resume: false, count: 1);
        GoToGameplay();
    }

    private void GoToGameplay()
    {
        CloseMenu();

        // The menu is typically opened from a paused state (Time.timeScale = 0), and
        // timeScale persists across a scene reload — a frozen new scene hangs the
        // run's intro on its first scaled WaitForSeconds (black screen). Reset it.
        Time.timeScale = 1f;

        string active = SceneManager.GetActiveScene().name;
        string scene = !string.IsNullOrEmpty(gameplaySceneName)
            ? gameplaySceneName
            : active; // menu is in the gameplay scene

        // Only arm the auto-open suppressor when we RELOAD THE SAME scene this menu
        // lives in (the in-gameplay overlay case), where this menu would otherwise
        // reopen over the freshly-started run. When loading a DIFFERENT scene (opened
        // from the main menu), this instance is destroyed by the scene change and can
        // never reopen — arming the static flag there only lingers and eats the NEXT
        // legitimate open, which is the "Continue needs two clicks" bug.
        _suppressNextOpen = (scene == active);

        // Reload a scene so CoopManager.Awake re-seats the right player count from
        // RunResumeIntent (a static — it survives the load). Driving the live
        // orchestrator in place can't change seating, which is what black-screens a
        // co-op→solo abandon.
        Debug.Log($"[ContinueRunMenu] Loading '{scene}' (resume={RunResumeIntent.Resume}, players={RunResumeIntent.PlayerCount}, suppressReopen={_suppressNextOpen}).");
        ScreenFade.LoadScene(scene);
    }

    //  UI CONSTRUCTION (mirrors OptionsMenu) 
    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);

        _root = new GameObject("ContinueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

        var title = MenuTheme.NewText("CONTINUE RUN", inner.transform, 50, TextAlignmentOptions.Center, _font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 58);

        AddDivider(inner.transform);

        _summary = MenuTheme.NewText("", inner.transform, 24, TextAlignmentOptions.Center, _font);
        _summary.color = MenuTheme.ValueCol;
        SetH(_summary, 80);

        _status = MenuTheme.NewText("", inner.transform, 22, TextAlignmentOptions.Center, _font);
        _status.color = MenuTheme.Magenta;
        SetH(_status, 32);

        // Continue (gated)
        _continueBtn = MenuTheme.NewButton("Continue", inner.transform, 24, _font);
        SetH(_continueBtn, 64);
        _continueImg = _continueBtn.targetGraphic as Image;
        _continueLabel = _continueBtn.GetComponentInChildren<TextMeshProUGUI>();
        _continueBtn.onClick.AddListener(OnContinue);

        // Abandon & solo (always available)
        var solo = MenuTheme.NewButton("Abandon Run & Start Solo", inner.transform, 22, _font);
        SetH(solo, 52);
        solo.onClick.AddListener(OnAbandonSolo);

        var spacer = MenuTheme.NewUI("Spacer", inner.transform);
        var sle = spacer.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 0f;

        var close = MenuTheme.NewButton("Back", inner.transform, 22, _font);
        SetH(close, 44);
        close.onClick.AddListener(CloseMenu);
    }

    private static void SetH(Component c, float h) => SetH(c.gameObject, h);
    private static void SetH(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;
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
}
