using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Programmatic Options menu
//   button that opens the Keyboard/Gamepad rebinding screen,
//   audio faders (Master / Music / Ambience / SFX) + a Music on/off toggle,
//   wave start selector (Countdown / Immediate / Ready) bound to RunConfig,
//   difficulty selector (Normal / Nightmare) for the next run.
// Settings persist to PlayerPrefs. Audio is also re-applied at game start.
// Open  from a Button's On Click() -> OptionsMenu.OpenMenu(), or from code via
// OptionsMenu.Open().
public class OptionsMenu : MonoBehaviour
{
    [Tooltip("Best quality: a TMP Font Asset built from Cinzel-ExtraBold.ttf.")]
    [SerializeField] private TMP_FontAsset titleFont;
    [Tooltip("Convenience: drag the raw Cinzel-ExtraBold.ttf here (converted at runtime).")]
    [SerializeField] private Font titleFontTtf;

    [Tooltip("RunConfig to edit for the Wave Start setting. Leave null to use " +
             "GameOrchestrator.Instance.runConfig at runtime.")]
    [SerializeField] private RunConfig runConfig;

    // PlayerPrefs keys
    private const string KMaster = "opt.vol.master";
    private const string KMusic = "opt.vol.music";
    private const string KAmb = "opt.vol.ambience";
    private const string KSfx = "opt.vol.sfx";
    private const string KMusOn = "opt.music.enabled";
    private const string KWave = "opt.wavePacing";
    // NOTE: difficulty persistence lives in EnemyStatModifierManager (its own PlayerPrefs
    // key), so this menu just reads/writes through SelectDifficulty / SelectedMode.

    private static OptionsMenu _instance;
    private GameObject _root;
    private TMP_FontAsset _font;

    private TextMeshProUGUI _musicBtnLabel;
    private readonly List<Button> _waveButtons = new List<Button>();
    private readonly WavePacingMode[] _waveModes = { WavePacingMode.Countdown, WavePacingMode.Immediate, WavePacingMode.ReadyUp };
    private readonly string[] _waveLabels = { "Countdown", "Immediate", "Ready" };

    private readonly List<Button> _difficultyButtons = new List<Button>();
    private readonly EnemyStatModifierManager.DifficultyMode[] _difficultyModes =
        { EnemyStatModifierManager.DifficultyMode.Normal, EnemyStatModifierManager.DifficultyMode.Nightmare };
    private readonly string[] _difficultyLabels = { "Normal", "Nightmare" };

    //  ENTRY POINTS
    private void Awake() { if (_instance == null) _instance = this; }
    private void OnDestroy() { if (_instance == this) _instance = null; }

    public static void Open()
    {
        if (_instance == null) _instance = new GameObject("OptionsMenu").AddComponent<OptionsMenu>();
        _instance.OpenMenu();
    }
    public static void Close() { if (_instance != null) _instance.CloseMenu(); }

    public void OpenMenu()
    {
        MenuTheme.EnsureEventSystem();
        if (_root == null) BuildUI();
        SyncFromState();
        _root.SetActive(true);
        Cursor.visible = true;
    }
    public void CloseMenu() { if (_root != null) _root.SetActive(false); }
    public void ToggleMenu()
    {
        if (_root != null && _root.activeSelf) CloseMenu(); else OpenMenu();
    }
    public void OpenRebinding() => ControlRebindScreen.Open();

    //  STATE BINDING
    private RunConfig ResolveConfig()
    {
        if (runConfig != null) return runConfig;
        return GameOrchestrator.Instance != null ? GameOrchestrator.Instance.runConfig : null;
    }

    // Re-read current values into the widgets each time the menu opens.
    private void SyncFromState()
    {
        var rc = ResolveConfig();
        if (rc != null)
        {
            // Saved preference wins, and is applied back onto the live config.
            var saved = (WavePacingMode)PlayerPrefs.GetInt(KWave, (int)rc.wavePacingMode);
            rc.wavePacingMode = saved;
            HighlightWave(saved);
        }
        // Reflect the persisted difficulty selection (its own PlayerPrefs key).
        HighlightDifficulty(EnemyStatModifierManager.SelectedMode);
        RefreshMusicLabel();
    }

    //  UI CONSTRUCTION
    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);

        _root = new GameObject("OptionsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4900; // below the rebind screen (5000)
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
        // Grown 860 → 980 (DIFFICULTY) → 1075 (CAMERA shake slider). There is no scroll
        // view, so the panel height must cover the fixed row heights below plus a little
        // slack for the flexible spacer that pins Close to the bottom.
        pr.sizeDelta = new Vector2(900, 1075);
        MenuTheme.ApplySprite(panel.AddComponent<Image>(), MenuTheme.PanelSprite, MenuTheme.PanelSolid);

        var inner = MenuTheme.NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(64, 58); irt.offsetMax = new Vector2(-64, -58);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;

        // Title (sits high now that the top inset is smaller)
        var title = MenuTheme.NewText("OPTIONS", inner.transform, 48, TextAlignmentOptions.Center, _font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 50);

        AddDivider(inner.transform);

        // Everything lives directly in the panel now — no scroll, so nothing is
        // clipped and the heights below decide the exact fit.
        BuildBody(inner.transform);

        // Flexible spacer absorbs any slack and pins Close to the bottom.
        var spacer = MenuTheme.NewUI("Spacer", inner.transform);
        var sle = spacer.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 0f;

        var close = MenuTheme.NewButton("Close", inner.transform, 24, _font);
        SetH(close, 60);
        close.onClick.AddListener(CloseMenu);
    }

    // Force an exact element height (min == preferred, no flexing) so the column
    // lays out predictably without a scroll view.
    private static void SetH(Component c, float h) => SetH(c.gameObject, h);
    private static void SetH(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;
    }

    private void BuildBody(Transform body)
    {
        //  CONTROLS 
        AddHeader(body, "CONTROLS");
        var rebind = MenuTheme.NewButton("Keyboard / Gamepad Rebinding", body, 24, _font);
        SetH(rebind, 62);
        rebind.onClick.AddListener(OpenRebinding);

        //  AUDIO 
        AddHeader(body, "AUDIO");
        var am = AudioManager.instance;
        AddSlider(body, "Master", am != null ? am.masterVolume : PlayerPrefs.GetFloat(KMaster, 1f), KMaster, v => { if (am) am.masterVolume = v; });
        AddSlider(body, "Music", am != null ? am.musicVolume : PlayerPrefs.GetFloat(KMusic, 1f), KMusic, v => { if (am) am.musicVolume = v; });
        AddSlider(body, "Ambience", am != null ? am.ambienceVolume : PlayerPrefs.GetFloat(KAmb, 1f), KAmb, v => { if (am) am.ambienceVolume = v; });
        AddSlider(body, "SFX", am != null ? am.SFXVolume : PlayerPrefs.GetFloat(KSfx, 1f), KSfx, v => { if (am) am.SFXVolume = v; });

        var musicBtn = MenuTheme.NewButton("Music: On", body, 22, _font);
        SetH(musicBtn, 54);
        _musicBtnLabel = musicBtn.GetComponentInChildren<TextMeshProUGUI>();
        musicBtn.onClick.AddListener(ToggleMusic);

        //  CAMERA 
        AddHeader(body, "CAMERA");
        AddCameraShakeSlider(body);

        //  GAMEPLAY 
        AddHeader(body, "GAMEPLAY");
        BuildWaveSelector(body);

        //  DIFFICULTY 
        AddHeader(body, "DIFFICULTY");
        BuildDifficultySelector(body);
    }

    private void AddHeader(Transform parent, string text)
    {
        var t = MenuTheme.NewText(text, parent, 20, TextAlignmentOptions.Left, _font);
        t.color = MenuTheme.Magenta; t.fontStyle = FontStyles.Bold; t.characterSpacing = 4f;
        SetH(t, 22);
    }

    private void AddDivider(Transform parent)
    {
        var holder = MenuTheme.NewUI("RuleHolder", parent);
        SetH(holder, 10);
        var rule = MenuTheme.NewUI("Rule", holder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.20f, 0.5f); rr.anchorMax = new Vector2(0.80f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.8f);
    }

    // label | slider | value%
    private void AddSlider(Transform parent, string label, float value, string prefKey, System.Action<float> apply)
    {
        var row = MenuTheme.NewUI("Row_" + label, parent);
        var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 48; rle.preferredHeight = 48; rle.flexibleHeight = 0;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14; h.padding = new RectOffset(6, 6, 2, 2);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;

        var name = MenuTheme.NewText(label, row.transform, 22, TextAlignmentOptions.MidlineLeft, _font);
        var nle = name.GetComponent<LayoutElement>(); nle.preferredWidth = 140; nle.flexibleWidth = 0;

        // apply saved value if present
        float v0 = PlayerPrefs.HasKey(prefKey) ? PlayerPrefs.GetFloat(prefKey) : value;
        var pct = MenuTheme.NewText(Pct(v0), row.transform, 22, TextAlignmentOptions.Right, _font);

        var slider = MenuTheme.NewSlider(row.transform, v0, v =>
        {
            apply?.Invoke(v);
            PlayerPrefs.SetFloat(prefKey, v);
            PlayerPrefs.Save();
            if (pct != null) pct.text = Pct(v);
        });
        var sle = slider.GetComponent<LayoutElement>();
        sle.flexibleWidth = 1; sle.minWidth = 240; sle.minHeight = 22;

        // value label sits last visually; reorder so it's on the right
        pct.transform.SetAsLastSibling();
        var ple = pct.GetComponent<LayoutElement>(); ple.preferredWidth = 70; ple.flexibleWidth = 0;

        apply?.Invoke(v0); // make sure live state matches the shown value
    }

    // Camera-shake intensity. Unlike the audio faders this is NOT a 0..100% value:
    // the slider's 0..1 travel maps to a 0..MaxIntensityScale (2x) MULTIPLIER on the
    // authored shake — left = no shake, centre = normal (1x/100%), right = 2x/200%.
    // The multiplier is owned + persisted by CameraShake, so there's no separate
    // PlayerPrefs key to manage here.
    private float _lastShakePreview = -10f;
    private void AddCameraShakeSlider(Transform parent)
    {
        var row = MenuTheme.NewUI("Row_CameraShake", parent);
        var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 48; rle.preferredHeight = 48; rle.flexibleHeight = 0;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14; h.padding = new RectOffset(6, 6, 2, 2);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;

        var name = MenuTheme.NewText("Shake", row.transform, 22, TextAlignmentOptions.MidlineLeft, _font);
        var nle = name.GetComponent<LayoutElement>(); nle.preferredWidth = 140; nle.flexibleWidth = 0;

        float mult0 = CameraShake.IntensityScale;                         // 0..2, default 1
        float s0 = Mathf.Clamp01(mult0 / CameraShake.MaxIntensityScale);  // slider travel 0..1
        var pct = MenuTheme.NewText(ShakePct(mult0), row.transform, 22, TextAlignmentOptions.Right, _font);

        var slider = MenuTheme.NewSlider(row.transform, s0, s =>
        {
            float mult = s * CameraShake.MaxIntensityScale;   // 0..2
            CameraShake.SetIntensityScale(mult);              // persists + applies globally
            if (pct != null) pct.text = ShakePct(mult);

            // Light, throttled preview so the setting is felt while dragging. Scaled
            // internally by the value we just set (so 0% produces nothing).
            if (Time.unscaledTime - _lastShakePreview > 0.1f)
            {
                _lastShakePreview = Time.unscaledTime;
                CameraShake.ShakeAll(0.12f, 0.18f);
            }
        });
        var sle = slider.GetComponent<LayoutElement>();
        sle.flexibleWidth = 1; sle.minWidth = 240; sle.minHeight = 22;

        pct.transform.SetAsLastSibling();
        var ple = pct.GetComponent<LayoutElement>(); ple.preferredWidth = 70; ple.flexibleWidth = 0;
    }

    // Shown as a percentage of normal: 0% (off) … 100% (normal) … 200% (2x).
    private static string ShakePct(float multiplier) => Mathf.RoundToInt(multiplier * 100f) + "%";

    private void BuildWaveSelector(Transform parent)
    {
        // "Wave Start" on its own full-width line so it can never collapse to a
        // sliver and stack vertically.
        var caption = MenuTheme.NewText("Wave Start", parent, 22, TextAlignmentOptions.Left, _font);
        caption.color = MenuTheme.ValueCol;
        SetH(caption, 22);

        // Three mode buttons sharing the full width.
        var row = MenuTheme.NewUI("Row_Wave", parent);
        var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 56; rle.preferredHeight = 56; rle.flexibleHeight = 0;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10; h.padding = new RectOffset(6, 6, 2, 2);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        _waveButtons.Clear();
        for (int i = 0; i < _waveModes.Length; i++)
        {
            var mode = _waveModes[i];
            var b = MenuTheme.NewButton(_waveLabels[i], row.transform, 20, _font);
            var le = b.GetComponent<LayoutElement>(); le.flexibleWidth = 1; le.minWidth = 100;
            b.onClick.AddListener(() => SetWaveMode(mode));
            _waveButtons.Add(b);
        }
        HighlightWave(ResolveConfig() != null ? ResolveConfig().wavePacingMode : WavePacingMode.Countdown);
    }

    private void BuildDifficultySelector(Transform parent)
    {
        // Caption makes clear this is a per-run choice, not a live toggle: it takes
        // effect on the NEXT run you start; a run in progress keeps its own difficulty.
        // Nightmare = +30% enemy & boss HP and damage (stacked on the per-stage scaling).
        var caption = MenuTheme.NewText("Applies to your next run", parent, 22, TextAlignmentOptions.Left, _font);
        caption.color = MenuTheme.ValueCol;
        SetH(caption, 22);

        // Two mode buttons sharing the full width — same pattern as the wave selector.
        var row = MenuTheme.NewUI("Row_Difficulty", parent);
        var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 56; rle.preferredHeight = 56; rle.flexibleHeight = 0;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10; h.padding = new RectOffset(6, 6, 2, 2);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        _difficultyButtons.Clear();
        for (int i = 0; i < _difficultyModes.Length; i++)
        {
            var mode = _difficultyModes[i];
            var b = MenuTheme.NewButton(_difficultyLabels[i], row.transform, 20, _font);
            var le = b.GetComponent<LayoutElement>(); le.flexibleWidth = 1; le.minWidth = 100;
            b.onClick.AddListener(() => SetDifficulty(mode));
            _difficultyButtons.Add(b);
        }
        HighlightDifficulty(EnemyStatModifierManager.SelectedMode);
    }

    //  ACTIONS
    private void ToggleMusic()
    {
        var am = AudioManager.instance;
        if (am != null) am.ToggleMusic();
        bool on = am != null ? am.musicEnabled : (PlayerPrefs.GetInt(KMusOn, 1) == 1);
        PlayerPrefs.SetInt(KMusOn, on ? 1 : 0); PlayerPrefs.Save();
        RefreshMusicLabel();
    }

    private void RefreshMusicLabel()
    {
        if (_musicBtnLabel == null) return;
        var am = AudioManager.instance;
        bool on = am != null ? am.musicEnabled : (PlayerPrefs.GetInt(KMusOn, 1) == 1);
        _musicBtnLabel.text = on ? "Music: On" : "Music: Off";
    }

    private void SetWaveMode(WavePacingMode mode)
    {
        var rc = ResolveConfig();
        if (rc != null) rc.wavePacingMode = mode;       // applied on the next wave gap
        PlayerPrefs.SetInt(KWave, (int)mode); PlayerPrefs.Save();
        HighlightWave(mode);
    }

    private void HighlightWave(WavePacingMode mode)
    {
        for (int i = 0; i < _waveButtons.Count; i++)
        {
            bool active = _waveModes[i] == mode;
            if (_waveButtons[i].targetGraphic is Image img)
                img.color = active ? MenuTheme.BtnActive : (MenuTheme.ButtonSprite != null ? Color.white : MenuTheme.BtnSolid);
            var lbl = _waveButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = active ? Color.white : MenuTheme.ValueCol;
        }
    }

    private void SetDifficulty(EnemyStatModifierManager.DifficultyMode mode)
    {
        // Persists to PlayerPrefs inside SelectDifficulty; only the NEXT run reads it,
        // so changing it here never alters a run already in progress.
        EnemyStatModifierManager.SelectDifficulty(mode);
        HighlightDifficulty(mode);
    }

    private void HighlightDifficulty(EnemyStatModifierManager.DifficultyMode mode)
    {
        for (int i = 0; i < _difficultyButtons.Count; i++)
        {
            bool active = _difficultyModes[i] == mode;
            if (_difficultyButtons[i].targetGraphic is Image img)
                img.color = active ? MenuTheme.BtnActive : (MenuTheme.ButtonSprite != null ? Color.white : MenuTheme.BtnSolid);
            var lbl = _difficultyButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = active ? Color.white : MenuTheme.ValueCol;
        }
    }

    private static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";

    //  BOOT: re-apply saved settings at launch AND on every scene load, so audio
    //  and wave pacing are restored even if the player never opens Options.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        ApplySavedSettings();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        => ApplySavedSettings();

    private static void ApplySavedSettings()
    {
        var am = AudioManager.instance;
        if (am != null)
        {
            if (PlayerPrefs.HasKey(KMaster)) am.masterVolume = PlayerPrefs.GetFloat(KMaster);
            if (PlayerPrefs.HasKey(KMusic)) am.musicVolume = PlayerPrefs.GetFloat(KMusic);
            if (PlayerPrefs.HasKey(KAmb)) am.ambienceVolume = PlayerPrefs.GetFloat(KAmb);
            if (PlayerPrefs.HasKey(KSfx)) am.SFXVolume = PlayerPrefs.GetFloat(KSfx);
            if (PlayerPrefs.HasKey(KMusOn)) am.musicEnabled = PlayerPrefs.GetInt(KMusOn) == 1;
        }

        // Wave pacing → the live RunConfig (the orchestrator reads it fresh each
        // between-wave gap, so this takes effect on the next wave).
        if (PlayerPrefs.HasKey(KWave) && GameOrchestrator.Instance != null && GameOrchestrator.Instance.runConfig != null)
            GameOrchestrator.Instance.runConfig.wavePacingMode = (WavePacingMode)PlayerPrefs.GetInt(KWave);

        // Difficulty is loaded by EnemyStatModifierManager itself (static field
        // initialised from its own PlayerPrefs key), so nothing to re-apply here.
    }
}
