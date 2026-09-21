using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

//  INTRO TUTORIAL

public class IntroTutorial : MonoBehaviour
{
    //  TEMPORARY 
    // true  → opens on EVERY Play, and the "seen" flag is never written.
    // false → opens once per player, then remembers (the shipping behaviour).
    // (static readonly, not const: a const would make the "mark as seen" branch
    // compile-time unreachable and warn.)
    public static readonly bool AlwaysShow = false; //true;

    public const string SeenKey = "tutorial.intro.seen.v1";

    /// <summary>Arrow art, pointing RIGHT in the source image. Only ever rotated by
    /// multiples of 90°, so it never reads as a smeared diagonal.</summary>
    private const string ArrowResource = "Sprites/Tutorial/arrow";

    /// <summary>Row artwork lives here: Assets/Resources/Icons/Tutorial/*.png. Paths are
    /// relative to a Resources folder and carry no extension.</summary>
    private const string TutorialIcons = "Icons/Tutorial/";

    /// <summary>Small drawn mark beside a page title. Built from circles, rings, bars
    /// and triangles at runtime — no icon assets to import or keep in sync.</summary>
    public enum Emblem { None, Core, Tower, Tether, Blades, Spark, Pad, Screen }

    /// <summary>The annotated-screenshot pages. Each is a different set of labels over
    /// the same image.</summary>
    public enum CalloutSet
    {
        None,
        Hud,        // health, energy, weapon roll, tower slot, character, core
        Combat,     // tower, its range, the tether, an enemy
    }

    /// <summary>Small mark drawn at the head of a row. Same primitive kit as the page
    /// emblems, sized for a 34-unit box.</summary>
    public enum Glyph
    {
        None, Core, Wave, Energy, Decay, Move, Dash, Sword, Tool, Shield,
        Hammer, Cursor, Menu, Zone, Spark, Chest
    }

    /// <summary>One "key → meaning" line. The key is drawn as a badge so the controls
    /// and the concepts line up in a column instead of hiding inside a paragraph.</summary>
    [System.Serializable]
    public class Row
    {
        public string key;                      // "Space / Y", "Near", "Per run"…
        [TextArea(1, 4)] public string text;

        [Tooltip("Accent for this row's badge. Leave fully transparent to inherit the page accent.")]
        public Color keyColor = new Color(0f, 0f, 0f, 0f);

        public Glyph icon = Glyph.None;

        [Tooltip("Optional artwork for the row's mark, as a Resources path WITHOUT the " +
                 "extension — e.g. \"Icons/Tutorial/core\" for " +
                 "Assets/Resources/Icons/Tutorial/core.png. When it loads, it replaces the " +
                 "drawn glyph; when it doesn't, the glyph above is used instead, so a " +
                 "missing or renamed file can never leave a row with a blank gutter.")]
        public string iconSprite;
    }

    [System.Serializable]
    public class Page
    {
        public string title;
        [TextArea(2, 5)] public string intro;   // TMP rich text is allowed
        public List<Row> rows = new List<Row>();

        [Tooltip("Drives the title gradient, the divider and any row that doesn't set its own colour.")]
        public Color accent = new Color(0.85f, 0.45f, 1f, 1f);

        [Tooltip("Show the near / mid / far distance band under the rows.")]
        public bool tetherBands;

        [Tooltip("Show the drawn gamepad map instead of rows.")]
        public bool gamepad;

        [Tooltip("Show the run timeline strip (Resources/Sprites/HUD/stagespanel) with a legend.")]
        public bool runBar;

        public Emblem emblem = Emblem.None;

        [Tooltip("Which annotated screenshot this page shows, if any. On those pages the " +
                 "panel shrinks to a bottom bar and labelled arrows point into the image.")]
        public CalloutSet callouts = CalloutSet.None;
    }

    [Header("Pages (leave empty to use the built-in defaults)")]
    public List<Page> pages = new List<Page>();

    [Header("Fonts (optional — same slots as OptionsMenu)")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private Font titleFontTtf;

    [Tooltip("Font for the page titles and the callout headings — Cinzel-Tutorial. " +
             "Assign it here for certainty; otherwise it's looked up by name at runtime, " +
             "which only works if the asset is in a Resources folder or already loaded.")]
    [SerializeField] private TMP_FontAsset tutorialTitleFont;

    [Header("Behaviour")]
    [Tooltip("Extra realtime seconds to wait after the stage is up, before opening.")]
    public float openDelay = 0.05f;

    [Tooltip("Show a 'Full Guide' button that opens the existing TutorialScreen.")]
    public bool showFullGuideButton = true;

    [Tooltip("While the tutorial is open, any component on a player whose type NAME contains " +
             "one of these is disabled, then restored on close. This is what stops the " +
             "character aiming at the mouse behind the overlay.")]
    public string[] freezeComponents = { "Aim", "Cursor", "Crosshair", "Reticle", "Weapon" };

    [Tooltip("Same, but searched across the whole scene rather than just the players. " +
             "NightOverlay is here because its torch cone is redrawn every frame from " +
             "PlayerAim.All, which keeps swinging even with the aim components disabled.")]
    public string[] freezeSceneComponents = { "Cursor", "Crosshair", "Reticle", "NightOverlay" };

    [Tooltip("Screenshot the arrow page annotates, under Resources. Using a fixed image " +
             "instead of the live view means the page looks the same in single player and " +
             "co-op, on any map, at any camera position.")]
    public string screenshotResource = "Sprites/GamePrintScreen2";

    // palette — violet to match the arrow art, gold for control names
    private static readonly Color Violet = new Color(0.72f, 0.42f, 1f, 1f);
    private static readonly Color Gold = new Color(1f, 0.80f, 0.38f, 1f);
    private static readonly Color Ink = new Color(0.055f, 0.030f, 0.095f, 0.80f);
    private static readonly Color Paper = new Color(0.93f, 0.91f, 0.97f, 1f);
    private const string KeyHex = "#FFC44D";

    // Background for the full-size text pages. The slice itself comes from the sprite's
    // own Border when the importer sets one — these are only the fallback for a sprite
    // imported with no border at all. 200 clears the corner ornament, which runs ~154px
    // in from each edge, with room for the rail that curves out of it.
    private const string PanelSpriteResource =
        "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/FillAndFrameRotated";
    private const float PanelBorder = 200f;

    // Frame thickness — this is the knob for how big the corner ornaments are. It scales
    // the whole border evenly (corners and rails together), so it can't stretch anything.
    // 1 draws the border at its authored pixel size; 0.82 matches the proportion the art
    // was drawn at (1448×1046 art in an 1180×880 panel); 0.6 is the current, trimmer look.
    // Below ~0.45 the rails start to look like hairlines.
    private const float PanelFrameScale = 0.6f;

    private const float MenuPanelBorder = 180f;          // MenuTheme fallback panel
    private const float MenuPanelFrameScale = 100f / 180f;// matches its old 1.8 multiplier

    // Back / Full Guide / Next share one 648×257 image. The cracked ends run ~235px in
    // from each side (measured off the art), so those are the left/right slices; the
    // top/bottom slices take half the height each, which leaves a single row in the
    // middle to stretch vertically and keeps the ends from being squashed on a button
    // three times wider than it is tall.
    private const string ButtonSpriteResource =
        "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button1";
    private const float ButtonBorderX = 235f;
    private const float ButtonBorderY = 128f;

    // Draws the art's 257px height onto the 86-unit button row.
    private const float ButtonArtScale = 86f / 257f;

    // The close button's own art. Square-ish (250×243) against a square rect, so it is
    // drawn whole rather than sliced.
    private const string SquareButtonSpriteResource =
        "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button2Square";
    private const float LabelWidth = 368f;    // uniform, so the page reads as a grid

    // The row's mark gutter. Wide enough for a square icon, tall enough that the
    // portrait ones (the shard, the battery) still read at a glance; the artwork is
    // fitted inside it by its own aspect, never squashed to the box.
    //
    // IconArt and IconBoxW set the drawn size; IconBoxH is only what the gutter claims
    // from the layout, and it is deliberately left at 48 — it is the tallest child in a
    // row, so raising it would deepen every belt again. The art simply overhangs it into
    // the card's 14-unit padding, which nothing clips.
    private const float IconBoxW = 62f;
    private const float IconBoxH = 48f;
    private const float IconArt = 55f;        // longest the artwork may run vertically

    private const int RowPadY = 14;           // top/bottom inset of a row card; sets its depth

    // How far the run-timeline block may be shrunk to keep a page inside the frame.
    // Below this the strip's stop labels stop being legible, so an overflow past it is
    // left visible rather than hidden behind unreadable text.
    private const float RunBarMinScale = 0.62f;

    private GameObject _root;
    private RectTransform _panelRT, _calloutRT, _innerRT, _closeRT;
    private GameObject _frameBg, _barBg;
    private Image _dimImg;
    private TMP_FontAsset _font;
    private bool _isOpen;
    private int _page;

    private TextMeshProUGUI _title, _body;
    private GameObject _dotsRow;
    private RectTransform _rowsRT;
    private VerticalLayoutGroup _rowsLayout;

    // The row block used to sit directly under the title with ALL of the leftover
    // height dumped below it, which read as a cluster at the top of an empty panel.
    // Now the slack is split three ways: a margin above the block, a margin below it,
    // and — most of it — the gaps between the rows themselves.
    private LayoutElement _topSpacer, _bottomSpacer;
    private RectTransform _topSpacerRT, _bottomSpacerRT;

    private const float RowGapMin = 10f;    // tight baseline every page is measured from
    private const float RowGapMax = 56f;    // ceiling, so a 2-row page doesn't fly apart
    private const float RowGapShare = 0.72f;// share of the free height that goes between rows
    private GameObject _emblemRoot, _padRoot, _runBarRoot, _dotGap, _buttonRow;
    private RectTransform _shotRT, _demoSlotRT;
    private float _shotAspect = 16f / 9f;
    private GameObject _bandsRoot;
    private Image _dividerImg;
    private readonly List<RowUI> _rows = new List<RowUI>();

    // Resources.Load is not free and rows are rebuilt on every page turn, so each path
    // is looked up once. Misses are cached too — a typo shouldn't hit the disk forever.
    private readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

    private class RowUI
    {
        public GameObject Root;
        public GameObject Icon;
        public Image Badge, Accent;
        public TextMeshProUGUI Key, Text;
    }
    private Button _backBtn, _nextBtn;
    private TextMeshProUGUI _nextLabel;

    private readonly List<Callout> _callouts = new List<Callout>();
    private bool _labelsDirty = true;
    private CalloutSet _currentSet = CalloutSet.None;
    private readonly List<Behaviour> _pausedAim = new List<Behaviour>();
    private TMP_FontAsset _titleFont;
    private Sprite _head, _roundFill, _roundRing, _circleRing, _dot, _tri;

    //  entry points 

    /// <summary>Open it now, regardless of the seen flag (hook this to an Options button).</summary>
    public static void Show()
    {
        var inst = FindFirstObjectByType<IntroTutorial>(FindObjectsInactive.Include);
        if (inst == null) inst = new GameObject("IntroTutorial").AddComponent<IntroTutorial>();
        inst.Open();
    }

    /// <summary>Make it appear again on the next run ("Replay intro" in Options).</summary>
    public static void ResetSeen()
    {
        PlayerPrefs.DeleteKey(SeenKey);
        PlayerPrefs.Save();
    }

    public static bool Seen => PlayerPrefs.GetInt(SeenKey, 0) != 0;

    private static bool ShouldAutoOpen => AlwaysShow || !Seen;

    // AfterSceneLoad fires ONCE, for the scene the app boots into. Pressing Play on the
    // gameplay scene therefore worked, while booting from the menu did not: the check ran
    // in MenuScene, found no orchestrator, and never ran again. So this hook only
    // subscribes, and the real check runs on every scene load from here on.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;   // guard against double-subscribe
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();                                 // covers booting straight into gameplay
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryCreate();

    private static void TryCreate()
    {
        if (!ShouldAutoOpen) return;
        if (GameOrchestrator.Instance == null) return;   // not a gameplay scene

        var inst = FindFirstObjectByType<IntroTutorial>(FindObjectsInactive.Include);
        if (inst != null) { inst.gameObject.SetActive(true); return; }   // its own Start() opens it

        new GameObject("IntroTutorial").AddComponent<IntroTutorial>();
    }

    private void Start()
    {
        if (ShouldAutoOpen) StartCoroutine(OpenWhenReady());
    }

    private IEnumerator OpenWhenReady()
    {
        // The intro runs: black → build biome → "Stage 1: …" banner → fade in → state
        // moves to WaveCountdown (which is when the wave counter appears). Waiting for
        // the state change put us a beat late, so instead we wait for the cover to lift:
        // ScreenIsCovered goes false the moment the banner and fade are finished, which
        // is exactly the gap the tutorial should land in.
        float guard = 0f;
        bool sawStage = false;

        while (guard < 60f)
        {
            var orch = GameOrchestrator.Instance;
            if (orch == null) break;

            if (orch.CurrentState != GameOrchestrator.RunState.Idle) sawStage = true;
            if (sawStage && !orch.ScreenIsCovered) break;   // banner gone, arena visible

            guard += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, openDelay));
        while (UIModalStack.IsOpen) yield return null;   // don't land on top of a menu
        Open();
    }

    //  open / close 

    public void Open()
    {
        MenuTheme.EnsureEventSystem();
        if (pages == null || pages.Count == 0) pages = DefaultPages();
        if (_root == null) BuildUI();
        _currentSet = CalloutSet.None;   // RefreshPage builds whichever set a page asks for

        // Mark seen on OPEN, not on close: a crash mid-tutorial shouldn't make it
        // reappear every launch. Skipped while AlwaysShow is on, so turning that off
        // later still gives you a clean first-run.
        if (!AlwaysShow)
        {
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
        }

        _isOpen = true;
        _root.SetActive(true);      // must be active BEFORE laying out, or the rebuild no-ops

        _page = 0;
        RefreshPage();
        UIModalStack.Push(this, freeze: true);
        PauseAiming(true);

        if (EventSystem.current != null && _nextBtn != null)
            EventSystem.current.SetSelectedGameObject(_nextBtn.gameObject);   // gamepad focus
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        if (_root != null) _root.SetActive(false);
        PauseAiming(false);
        UIModalStack.Pop(this);
    }

    private void OnDisable()
    {
        if (_isOpen) { _isOpen = false; PauseAiming(false); UIModalStack.Pop(this); }
    }

    private void OnDestroy()
    {
        // The canvas is a scene root (not a child of this object, so an odd parent
        // scale can't reach it) — clean it up by hand.
        if (_root != null) Destroy(_root);
    }

    /// <summary>UIModalStack freezes the clock and suppresses attacks, but aiming is
    /// driven straight off the pointer in Update — several scripts read
    /// <c>Mouse.current.position</c> themselves rather than going through the frozen
    /// input actions — so the character kept swinging its weapon around while the
    /// tutorial was up. Disabling PlayerAim alone wasn't enough, because its Direction is
    /// computed on demand and its consumers keep asking.
    ///
    /// So: park every component on the players whose type name matches
    /// <see cref="freezeComponents"/>, and restore exactly the ones that were enabled.
    /// Name matching rather than hard types, because the aim/cursor scripts differ per
    /// project — widen or narrow the list in the Inspector if something is missed.</summary>
    private void PauseAiming(bool paused)
    {
        if (!paused)
        {
            foreach (var b in _pausedAim) if (b != null) b.enabled = true;
            _pausedAim.Clear();
            return;
        }

        _pausedAim.Clear();
        if (freezeComponents == null || freezeComponents.Length == 0) return;

        var roots = new List<GameObject>();
        foreach (var pr in Roster()) if (pr != null) roots.Add(pr.gameObject);
        if (roots.Count == 0)
        {
            var pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) roots.Add(pm.gameObject);
        }

        foreach (var root in roots)
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                FreezeIfMatch(mb);

        // Cursors, crosshairs and the night torch live outside the player hierarchy.
        if (freezeSceneComponents != null && freezeSceneComponents.Length > 0)
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && Matches(mb.GetType().Name, freezeSceneComponents))
                    FreezeIfMatch(mb, freezeSceneComponents);
    }

    private void FreezeIfMatch(MonoBehaviour mb, string[] needles = null)
    {
        if (mb == null || !mb.enabled || mb == this) return;
        if (!Matches(mb.GetType().Name, needles ?? freezeComponents)) return;

        mb.enabled = false;
        _pausedAim.Add(mb);
    }

    private static bool Matches(string typeName, string[] needles)
    {
        if (needles == null) return false;
        for (int i = 0; i < needles.Length; i++)
        {
            if (string.IsNullOrEmpty(needles[i])) continue;
            if (typeName.IndexOf(needles[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private void Update()
    {
        if (!_isOpen) return;
        if (MenuBackInput.ConsumeBack(this)) Close();   // Esc / B / Start
    }

    private void LateUpdate()
    {
        // Arrows track their targets every frame: the game is frozen, but the window
        // can be resized and the player object is only resolvable at runtime.
        if (_isOpen && _calloutRT != null && _calloutRT.gameObject.activeSelf) LayoutCallouts();
    }

    private void Next()
    {
        if (_page >= pages.Count - 1) { Close(); return; }
        _page++;
        RefreshPage();
    }

    private void Back()
    {
        if (_page <= 0) return;
        _page--;
        RefreshPage();
    }

    private void RefreshPage()
    {
        var p = pages[Mathf.Clamp(_page, 0, pages.Count - 1)];

        // Mode FIRST. It decides whether this page is the arrow overlay or a text page,
        // so it must not sit behind anything that can throw — a null further down used to
        // take the whole overlay with it.
        SetCalloutMode(p.callouts != CalloutSet.None);
        if (p.callouts != CalloutSet.None && p.callouts != _currentSet)
        {
            RebuildCallouts(p.callouts);
            _currentSet = p.callouts;
        }

        _title.text = p.title;
        _body.GetComponent<LayoutElement>().minHeight = 0f;   // re-measured in Relayout
        _body.text = p.intro;
        _body.gameObject.SetActive(!string.IsNullOrEmpty(p.intro));
        SyncRows(p);
        if (_bandsRoot != null) _bandsRoot.SetActive(p.tetherBands);
        if (_padRoot != null) _padRoot.SetActive(p.gamepad);
        if (_runBarRoot != null) _runBarRoot.SetActive(p.runBar);
        DrawEmblem(p.callouts != CalloutSet.None ? Emblem.None : p.emblem, p.accent);
        ApplyAccent(p.accent);
        SyncDots(_page, pages.Count);

        bool last = _page >= pages.Count - 1;
        _nextLabel.text = last ? "Let's Play" : "Next";
        _backBtn.interactable = _page > 0;
        Relayout();
        if (isActiveAndEnabled) StartCoroutine(RelayoutNextFrame());
    }

    /// <summary>TMP reports its wrapped height from its CURRENT width, and the layout
    /// group only sets that width during the same pass — so the first pass sizes every
    /// multi-line block as if it were one line, which is what made the intro text sit on
    /// top of the first row. Running the rebuild twice resolves width, then height.</summary>
    private void Relayout()
    {
        if (_innerRT == null || !_innerRT.gameObject.activeInHierarchy) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);

        // When a page wants more height than the panel has, the layout group shrinks
        // whatever has the lowest minimum — which was the intro line, so it collapsed to
        // nothing and its text spilled over the first row. Measure what the intro really
        // needs at its final width and pin that as its minimum.
        var le = _body.GetComponent<LayoutElement>();
        float width = _body.rectTransform.rect.width;
        if (_body.gameObject.activeSelf && width > 1f)
        {
            float need = _body.GetPreferredValues(_body.text, width, 0f).y + 4f;
            if (Mathf.Abs(le.minHeight - need) > 1f)
            {
                le.minHeight = need;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);
            }
        }

        TightenRows();
        FitRunBar();
        SpreadRows();
    }

    /// <summary>Row gaps back to the tight baseline, so every measurement starts from the
    /// same place rather than from whatever the previous page left behind.</summary>
    private void TightenRows()
    {
        if (_rowsLayout == null) return;
        if (Mathf.Abs(_rowsLayout.spacing - RowGapMin) > 0.01f)
        {
            _rowsLayout.spacing = RowGapMin;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);
        }
    }

    /// <summary>The run-timeline page carries more than the panel holds: rows, a caption,
    /// the strip with its labels, two rules and the shape line, and at full size the
    /// button row is pushed out through the bottom of the frame. A layout group won't
    /// shrink a child below its minimum, so the overflow has to be taken off something —
    /// and the strip is the one block that survives being smaller.
    ///
    /// So the block is measured at full size, and if the page is over, it is scaled down
    /// by exactly the overflow. The scale is uniform, so nothing in it distorts, and the
    /// inner column is set to account for child scale, so the layout sees the smaller
    /// size rather than leaving a hole where the full-height block used to be.</summary>
    private void FitRunBar()
    {
        if (_runBarRoot == null || _innerRT == null) return;

        var rt = _runBarRoot.transform as RectTransform;
        if (rt == null) return;

        if (!_runBarRoot.activeSelf)
        {
            rt.localScale = Vector3.one;
            return;
        }

        if (rt.localScale.y < 0.999f)
        {
            rt.localScale = Vector3.one;      // always measure at full size
            LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);
        }

        float need = ContentHeight(out float runBar);
        float avail = _innerRT.rect.height;
        if (runBar <= 1f || need <= avail + 0.5f) return;

        float k = Mathf.Clamp((avail - (need - runBar)) / runBar, RunBarMinScale, 1f);
        rt.localScale = new Vector3(k, k, 1f);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);
    }

    /// <summary>Height the inner column wants at full size, with the run bar's share of it
    /// reported separately.</summary>
    private float ContentHeight(out float runBarHeight)
    {
        runBarHeight = 0f;

        var v = _innerRT.GetComponent<VerticalLayoutGroup>();
        if (v == null) return 0f;

        float total = v.padding.top + v.padding.bottom;
        int counted = 0;

        for (int i = 0; i < _innerRT.childCount; i++)
        {
            var child = _innerRT.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeSelf) continue;

            float h = LayoutUtility.GetPreferredHeight(child);
            total += h;
            counted++;

            if (_runBarRoot != null && child.gameObject == _runBarRoot) runBarHeight = h;
        }

        if (counted > 1) total += v.spacing * (counted - 1);
        return total;
    }

    /// <summary>Opens the row gaps to suit the page. Pages carry two, three or five
    /// rows into a panel of one fixed height, so a single hard-coded spacing either
    /// bunches the short pages at the top or overflows the long one. This measures the
    /// height nothing else claimed and puts most of it between the rows, capped so a
    /// sparse page stays a list rather than becoming scattered; the two flexible
    /// spacers then split what's left above and below, which centres the block.</summary>
    private void SpreadRows()
    {
        if (_rowsLayout == null || _rowsRT == null) return;
        if (!_rowsRT.gameObject.activeSelf || !_rowsRT.gameObject.activeInHierarchy) return;

        // Measure from the tight baseline, never from whatever the previous page left
        // behind — otherwise the spacing ratchets up page by page.
        TightenRows();

        int visible = 0;
        for (int i = 0; i < _rowsRT.childCount; i++)
            if (_rowsRT.GetChild(i).gameObject.activeSelf) visible++;

        int gaps = visible - 1;
        if (gaps <= 0) return;

        float free = SpacerSlack();
        if (free <= 1f) return;

        float extra = Mathf.Min(RowGapMax - RowGapMin, free * RowGapShare / gaps);
        if (extra < 0.5f) return;

        _rowsLayout.spacing = RowGapMin + extra;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerRT);
    }

    /// <summary>Height the layout had nothing to do with: whatever the two spacers grew
    /// beyond their minimums after a rebuild.</summary>
    private float SpacerSlack()
    {
        float slack = 0f;
        if (_topSpacerRT != null && _topSpacerRT.gameObject.activeSelf && _topSpacer != null)
            slack += Mathf.Max(0f, _topSpacerRT.rect.height - _topSpacer.minHeight);
        if (_bottomSpacerRT != null && _bottomSpacer != null)
            slack += Mathf.Max(0f, _bottomSpacerRT.rect.height - _bottomSpacer.minHeight);
        return slack;
    }

    private IEnumerator RelayoutNextFrame()
    {
        yield return null;          // unscaled: coroutines still tick at timeScale 0
        Relayout();
    }

    //  DRAWING KIT — everything below is built from four primitives: a rounded bar,
    //  a filled circle, a ring and a triangle. No icon assets.

    /// <summary>Rounded bar (or square). Position and size are in the parent's local
    /// space, with the parent's centre as the origin.</summary>
    private RectTransform Bar(Transform parent, Vector2 pos, Vector2 size, Color col,
                              float rotation = 0f)
        => Piece(parent, _roundFill, pos, size, col, rotation);

    private RectTransform Disc(Transform parent, Vector2 pos, float diameter, Color col)
        => Piece(parent, _dot, pos, new Vector2(diameter, diameter), col);

    private RectTransform Ring(Transform parent, Vector2 pos, float diameter, Color col)
        => Piece(parent, _circleRing, pos, new Vector2(diameter, diameter), col);

    private RectTransform Wedge(Transform parent, Vector2 pos, Vector2 size, Color col,
                                float rotation = 0f)
        => Piece(parent, _tri, pos, size, col, rotation);

    private RectTransform Piece(Transform parent, Sprite sprite, Vector2 pos, Vector2 size,
                                Color col, float rotation = 0f)
    {
        var go = MenuTheme.NewUI("Piece", parent);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        if (sprite == _roundFill || sprite == _roundRing) img.type = Image.Type.Sliced;
        img.color = col;
        img.raycastTarget = false;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        if (rotation != 0f) rt.localEulerAngles = new Vector3(0f, 0f, rotation);
        return rt;
    }

    private TextMeshProUGUI Caption(Transform parent, string text, Vector2 pos, Vector2 size,
                                    int fontSize, TextAlignmentOptions align)
    {
        var t = MenuTheme.NewText(text, parent, fontSize, align, _font);
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var le = t.GetComponent<LayoutElement>();
        if (le != null) le.ignoreLayout = true;
        return t;
    }

    /// <summary>A row's mark: imported artwork when the row names one, the drawn glyph
    /// otherwise. Both go in the same gutter, so a page can mix the two.</summary>
    private void DrawRowIcon(GameObject host, Row r, Color accent)
    {
        if (host == null) return;

        var sprite = r != null ? LoadSprite(r.iconSprite) : null;
        if (sprite == null)
        {
            DrawGlyph(host, r != null ? r.icon : Glyph.None, accent);
            return;
        }

        DrawGlyph(host, Glyph.None, accent);   // clears the pooled row's previous mark

        var go = MenuTheme.NewUI("Art", host.transform);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        // Left as white: these are finished artwork with their own palette, and tinting
        // them to the page accent would mud the shading.

        // Fit by whichever edge runs out first, so the tall battery and the square
        // crossed blades end up at the same optical weight instead of one being
        // stretched to the box.
        Rect src = sprite.rect;
        float k = Mathf.Min(IconBoxW / src.width, IconArt / src.height);
        if (k <= 0f || float.IsNaN(k)) k = 1f;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(src.width * k, src.height * k);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>Resources path (no extension) → sprite, cached. Warns once per path when
    /// nothing is there; every caller has its own fallback.</summary>
    private Sprite LoadSprite(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_iconCache.TryGetValue(path, out var cached)) return cached;

        var sprite = Resources.Load<Sprite>(path);
        if (sprite == null)
            Debug.LogWarning($"[IntroTutorial] No sprite at Resources/{path} — " +
                             "check the file is under Assets/Resources and imported as " +
                             "Texture Type: Sprite (2D and UI).");
        _iconCache[path] = sprite;
        return sprite;
    }

    /// <summary>Draw a row's little mark. Cleared and rebuilt each time, because rows
    /// are pooled and reused from page to page.</summary>
    private void DrawGlyph(GameObject host, Glyph kind, Color col)
    {
        if (host == null) return;

        for (int i = host.transform.childCount - 1; i >= 0; i--)
        {
            var child = host.transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        if (kind == Glyph.None) return;

        var t = host.transform;
        Color dim = new Color(col.r, col.g, col.b, 0.45f);

        switch (kind)
        {
            case Glyph.Core:
                Ring(t, Vector2.zero, 28f, dim);
                Bar(t, Vector2.zero, new Vector2(10f, 10f), col, 45f);
                break;

            case Glyph.Wave:            // a rank of enemies coming in
                Wedge(t, new Vector2(-9f, 0f), new Vector2(9f, 16f), dim);
                Wedge(t, new Vector2(0f, 0f), new Vector2(9f, 16f), col);
                Wedge(t, new Vector2(9f, 0f), new Vector2(9f, 16f), dim);
                break;

            case Glyph.Energy:
                Bar(t, Vector2.zero, new Vector2(17f, 17f), col, 45f);
                Bar(t, Vector2.zero, new Vector2(7f, 7f), new Color(0.08f, 0.05f, 0.14f, 1f), 45f);
                break;

            case Glyph.Decay:           // a ring draining away
                Ring(t, new Vector2(0f, 3f), 22f, dim);
                Wedge(t, new Vector2(0f, -11f), new Vector2(12f, 12f), col, -90f);
                break;

            case Glyph.Move:
                Bar(t, Vector2.zero, new Vector2(24f, 5f), col);
                Bar(t, Vector2.zero, new Vector2(5f, 24f), col);
                break;

            case Glyph.Dash:
                Bar(t, new Vector2(0f, 7f), new Vector2(20f, 4f), dim, -18f);
                Bar(t, Vector2.zero, new Vector2(24f, 4f), col, -18f);
                Bar(t, new Vector2(0f, -7f), new Vector2(20f, 4f), dim, -18f);
                break;

            case Glyph.Sword:
                Bar(t, new Vector2(2f, 2f), new Vector2(26f, 5f), col, 45f);
                Bar(t, new Vector2(-6f, -6f), new Vector2(13f, 4f), dim, -45f);
                break;

            case Glyph.Tool:
                Bar(t, new Vector2(3f, 3f), new Vector2(20f, 6f), col, 45f);
                Bar(t, new Vector2(-8f, -8f), new Vector2(11f, 11f), dim);
                break;

            case Glyph.Shield:
                Bar(t, new Vector2(0f, 5f), new Vector2(22f, 16f), dim);
                Wedge(t, new Vector2(0f, -9f), new Vector2(22f, 14f), dim, -90f);
                Bar(t, new Vector2(0f, 2f), new Vector2(6f, 6f), col, 45f);
                break;

            case Glyph.Hammer:
                Bar(t, new Vector2(-3f, -3f), new Vector2(24f, 5f), dim, 45f);
                Bar(t, new Vector2(7f, 8f), new Vector2(16f, 10f), col, 45f);
                break;

            case Glyph.Cursor:
                Wedge(t, Vector2.zero, new Vector2(20f, 20f), col, 215f);
                break;

            case Glyph.Menu:
                Bar(t, new Vector2(0f, 8f), new Vector2(22f, 4f), col);
                Bar(t, Vector2.zero, new Vector2(22f, 4f), dim);
                Bar(t, new Vector2(0f, -8f), new Vector2(22f, 4f), dim);
                break;

            case Glyph.Zone:
                Ring(t, Vector2.zero, 28f, dim);
                Disc(t, Vector2.zero, 12f, col);
                break;

            case Glyph.Spark:
                Bar(t, Vector2.zero, new Vector2(11f, 11f), col, 45f);
                Bar(t, Vector2.zero, new Vector2(26f, 4f), dim);
                Bar(t, Vector2.zero, new Vector2(4f, 26f), dim);
                break;

            case Glyph.Chest:
                Bar(t, new Vector2(0f, -4f), new Vector2(24f, 14f), dim);
                Bar(t, new Vector2(0f, 6f), new Vector2(26f, 6f), col);
                Bar(t, new Vector2(0f, -4f), new Vector2(5f, 5f), col);
                break;
        }
    }

    /// <summary>Redraw the little mark beside the page title.</summary>
    private void DrawEmblem(Emblem kind, Color accent)
    {
        if (_emblemRoot == null) return;

        for (int i = _emblemRoot.transform.childCount - 1; i >= 0; i--)
        {
            var child = _emblemRoot.transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        if (kind == Emblem.None) return;

        var t = _emblemRoot.transform;
        Color dim = new Color(accent.r, accent.g, accent.b, 0.42f);

        switch (kind)
        {
            case Emblem.Core:                       // a guarded shard
                Ring(t, Vector2.zero, 48f, dim);
                Bar(t, Vector2.zero, new Vector2(17f, 17f), accent, 45f);
                break;

            case Emblem.Tower:                      // turret on a base
                Wedge(t, new Vector2(0f, 8f), new Vector2(30f, 26f), accent, 90f);
                Bar(t, new Vector2(0f, -14f), new Vector2(34f, 8f), dim);
                break;

            case Emblem.Tether:                     // three zones
                TetherColors(out Color n, out Color m, out Color f);
                Ring(t, Vector2.zero, 48f, new Color(f.r, f.g, f.b, 0.85f));
                Ring(t, Vector2.zero, 33f, new Color(m.r, m.g, m.b, 0.85f));
                Disc(t, Vector2.zero, 13f, n);
                break;

            case Emblem.Blades:                     // crossed blades
                Bar(t, Vector2.zero, new Vector2(46f, 7f), accent, 45f);
                Bar(t, Vector2.zero, new Vector2(46f, 7f), dim, -45f);
                break;

            case Emblem.Spark:                      // four-point star
                Bar(t, Vector2.zero, new Vector2(15f, 15f), accent, 45f);
                Bar(t, Vector2.zero, new Vector2(46f, 5f), dim);
                Bar(t, Vector2.zero, new Vector2(5f, 46f), dim);
                break;

            case Emblem.Pad:                        // controller silhouette
                Bar(t, Vector2.zero, new Vector2(46f, 26f), dim);
                Disc(t, new Vector2(-11f, 0f), 11f, accent);
                Disc(t, new Vector2(11f, 0f), 11f, accent);
                break;

            case Emblem.Screen:
                Ring(t, Vector2.zero, 46f, dim);
                Disc(t, Vector2.zero, 14f, accent);
                break;
        }
    }

    //  GAMEPAD  — a simple drawn pad, then a plain two-column legend under it.
    //  No leader lines: they crossed the body and each other and made a mess of it.

    private static readonly Color PadBody = new Color(0.19f, 0.16f, 0.27f, 1f);
    private static readonly Color PadEdge = new Color(0.45f, 0.34f, 0.64f, 0.85f);
    private static readonly Color PadKnob = new Color(0.32f, 0.26f, 0.45f, 1f);

    private void BuildGamepad(Transform parent)
    {
        _padRoot = MenuTheme.NewUI("Gamepad", parent);
        var v = _padRoot.AddComponent<VerticalLayoutGroup>();
        v.spacing = 22;
        v.padding = new RectOffset(0, 0, 4, 0);
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var stage = MenuTheme.NewUI("Pad", _padRoot.transform);
        SetH(stage, 270f);
        var t = stage.transform;

        // DRAW ORDER MATTERS: grips and shoulders go down first so the shell covers
        // their inner ends. Drawn on top they read as loose pills floating above the pad.
        Bar(t, new Vector2(-138f, -74f), new Vector2(104f, 158f), PadBody, 22f);
        Bar(t, new Vector2(138f, -74f), new Vector2(104f, 158f), PadBody, -22f);

        Shoulder(t, new Vector2(-112f, 96f), new Vector2(92f, 26f), "LT");   // trigger, further back
        Shoulder(t, new Vector2(112f, 96f), new Vector2(92f, 26f), "RT");
        Shoulder(t, new Vector2(-112f, 66f), new Vector2(104f, 26f), "LB");  // bumper, in front
        Shoulder(t, new Vector2(112f, 66f), new Vector2(104f, 26f), "RB");

        Bar(t, new Vector2(0f, -46f), new Vector2(250f, 110f), PadBody);     // belly
        Bar(t, new Vector2(0f, 8f), new Vector2(352f, 132f), PadBody);       // shell
        Outline(t, new Vector2(0f, 8f), new Vector2(352f, 132f), new Color(PadEdge.r, PadEdge.g, PadEdge.b, 0.5f));

        // controls: sticks high-left / low-right, d-pad low-left, buttons high-right
        Stick(t, new Vector2(-108f, 24f));
        Stick(t, new Vector2(46f, -52f));

        Vector2 dpad = new Vector2(-52f, -52f);
        Bar(t, dpad, new Vector2(22f, 62f), PadKnob);
        Bar(t, dpad, new Vector2(62f, 22f), PadKnob);
        Disc(t, dpad, 16f, PadBody);

        Vector2 face = new Vector2(108f, 24f);
        FaceButton(t, face + new Vector2(0f, 34f), "Y", new Color(1f, 0.79f, 0.25f));
        FaceButton(t, face + new Vector2(34f, 0f), "B", new Color(0.95f, 0.38f, 0.34f));
        FaceButton(t, face + new Vector2(0f, -34f), "A", new Color(0.48f, 0.86f, 0.48f));
        FaceButton(t, face + new Vector2(-34f, 0f), "X", new Color(0.44f, 0.67f, 1f));

        Bar(t, new Vector2(-26f, 26f), new Vector2(20f, 8f), PadKnob, 16f);
        Bar(t, new Vector2(26f, 26f), new Vector2(20f, 8f), PadKnob, -16f);
        Disc(t, new Vector2(0f, 54f), 26f, PadKnob);
        Disc(t, new Vector2(0f, 54f), 18f, PadBody);

        //  legend, two even columns
        var legend = MenuTheme.NewUI("Legend", _padRoot.transform);
        var lh = legend.AddComponent<HorizontalLayoutGroup>();
        lh.spacing = 40;
        lh.padding = new RectOffset(30, 30, 0, 0);
        lh.childControlWidth = true; lh.childControlHeight = true;
        lh.childForceExpandWidth = true; lh.childForceExpandHeight = false;

        var left = LegendColumn(legend.transform);
        LegendRow(left, "Left Stick", "Move");
        LegendRow(left, "D-Pad", "Swap weapon / tool");
        LegendRow(left, "LB", "Sprint");
        LegendRow(left, "LT", "Tool · Block · Tower menu");

        var right = LegendColumn(legend.transform);
        LegendRow(right, "Right Stick", "Aim");
        LegendRow(right, "RT", "Attack · Build");
        LegendRow(right, "Y", "Build mode");
        LegendRow(right, "B", "Dash");

        _padRoot.SetActive(false);
    }

    /// <summary>Rounded outline for a rectangle (the ring sprite, 9-sliced).</summary>
    private RectTransform Outline(Transform parent, Vector2 pos, Vector2 size, Color col)
        => Piece(parent, _roundRing, pos, size, col);


    private void Shoulder(Transform t, Vector2 pos, Vector2 size, string label)
    {
        Bar(t, pos, size, PadKnob);
        Caption(t, label, pos, size, 15, TextAlignmentOptions.Center)
            .color = new Color(0.86f, 0.82f, 0.95f, 1f);
    }

    private void Stick(Transform t, Vector2 pos)
    {
        Disc(t, pos, 56f, new Color(0.11f, 0.09f, 0.17f, 1f));
        Disc(t, pos, 42f, PadKnob);
        Ring(t, pos, 42f, PadEdge);
    }

    private void FaceButton(Transform t, Vector2 pos, string glyph, Color col)
    {
        Disc(t, pos, 28f, new Color(col.r * 0.32f, col.g * 0.32f, col.b * 0.32f, 1f));
        Ring(t, pos, 28f, col);
        Caption(t, glyph, pos, new Vector2(28f, 28f), 15, TextAlignmentOptions.Center).color = col;
    }

    private Transform LegendColumn(Transform parent)
    {
        var col = MenuTheme.NewUI("Column", parent);
        var v = col.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return col.transform;
    }

    private void LegendRow(Transform column, string key, string what)
    {
        var row = MenuTheme.NewUI("Entry", column);
        SetH(row, 38f);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        var badge = MenuTheme.NewUI("Key", row.transform);
        var img = badge.AddComponent<Image>();
        img.sprite = _roundFill; img.type = Image.Type.Sliced;
        img.color = new Color(0.24f, 0.20f, 0.36f, 0.9f);
        img.raycastTarget = false;
        var le = badge.AddComponent<LayoutElement>();
        le.minWidth = 148f; le.preferredWidth = 148f; le.flexibleWidth = 0f;

        var k = MenuTheme.NewText(key, badge.transform, 21, TextAlignmentOptions.Center, _font);
        k.fontStyle = FontStyles.Bold;
        k.color = Gold;
        k.raycastTarget = false;
        MenuTheme.Stretch(k.rectTransform);

        var d = MenuTheme.NewText(what, row.transform, 19, TextAlignmentOptions.Left, _font);
        d.color = Paper;
        d.raycastTarget = false;
        d.GetComponent<LayoutElement>().flexibleWidth = 1f;
    }

    //  RUN TIMELINE  — the HUD's own progress strip, reused here as an illustration of
    //  how a run is shaped.

    private const string StagesPanelResource = "Sprites/HUD/stagespanel";

    // Where each icon sits along the strip (fraction of its width) and what it is.
    // Read off the artwork left to right; if the art changes, this is the one place to
    // fix the labels.
    private static readonly (float at, string name)[] RunBarStops =
    {
        (0.089f, "Wave"),
        (0.248f, "Stage reward"),
        (0.410f, "Chest"),
        (0.581f, "Augment reward"),
        (0.744f, "Gremlin"),
        (0.898f, "Boss"),
    };

    private void BuildRunBar(Transform parent)
    {
        _runBarRoot = MenuTheme.NewUI("RunBar", parent);
        var v = _runBarRoot.AddComponent<VerticalLayoutGroup>();
        // Tight: every unit spent on the block's own chrome is a unit the strip doesn't
        // get, because FitRunBar scales the whole block down to a fixed height budget.
        v.spacing = 5;
        v.padding = new RectOffset(0, 0, 8, 0);
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        AddDivider(_runBarRoot.transform);

        var caption = MenuTheme.NewText("Run Progress", _runBarRoot.transform, 30,
                                        TextAlignmentOptions.Center, _font);
        caption.fontStyle = FontStyles.Bold;
        caption.characterSpacing = 4f;
        caption.color = new Color(0.80f, 0.72f, 0.92f, 1f);
        SetH(caption, 36f);

        // the strip itself, at the artwork's own 1230×211 ratio
        const float StripW = 990f, StripH = StripW * 211f / 1230f;

        // Room under the art for the stop names, and nothing more: the block used to
        // reserve 62 and leave half of it empty below the labels, which cost the strip
        // itself about 20 units of drawn size once the page was scaled to fit.
        const float LabelH = 36f, LabelGap = 8f;
        const float Reserve = LabelH + LabelGap;

        var stage = MenuTheme.NewUI("Strip", _runBarRoot.transform);
        SetH(stage, StripH + Reserve);

        var art = MenuTheme.NewUI("Art", stage.transform);
        var artRT = art.GetComponent<RectTransform>();
        artRT.anchorMin = artRT.anchorMax = new Vector2(0.5f, 0.5f);
        artRT.pivot = new Vector2(0.5f, 0.5f);
        artRT.sizeDelta = new Vector2(StripW, StripH);
        artRT.anchoredPosition = new Vector2(0f, Reserve * 0.5f);   // art up, labels under it

        var img = art.AddComponent<Image>();
        img.sprite = Resources.Load<Sprite>(StagesPanelResource);
        img.preserveAspect = true;
        img.raycastTarget = false;
        if (img.sprite == null) img.color = new Color(1f, 1f, 1f, 0f);   // missing art: draw nothing

        foreach (var stop in RunBarStops)
        {
            float x = (stop.at - 0.5f) * StripW;
            float y = Reserve * 0.5f - StripH * 0.5f - LabelGap - LabelH * 0.5f;
            var lbl = Caption(stage.transform, stop.name, new Vector2(x, y),
                              new Vector2(215f, LabelH), 28, TextAlignmentOptions.Center);
            lbl.color = new Color(0.86f, 0.82f, 0.94f, 1f);
        }

        AddDivider(_runBarRoot.transform);

        var ruleGap = MenuTheme.NewUI("RuleGap", _runBarRoot.transform);
        SetH(ruleGap, 4f);

        var shape = MenuTheme.NewText(RunShapeLine(), _runBarRoot.transform, 33,
                                      TextAlignmentOptions.Center, _font);
        shape.color = Paper;
        Wrap(shape, 0f);
        shape.GetComponent<LayoutElement>().preferredWidth = -1f;

        _runBarRoot.SetActive(false);
    }

    /// <summary>The run's shape, read off the live RunConfig where there is one so the
    /// tutorial can't drift from the actual setup.</summary>
    private static string RunShapeLine()
    {
        int stages = 8, waves = 8, perWave = 10;

        var cfg = GameOrchestrator.Instance != null ? GameOrchestrator.Instance.runConfig : null;
        if (cfg != null)
        {
            if (cfg.stageCount > 0) stages = cfg.stageCount;
            if (cfg.wavesPerStage > 0) waves = cfg.wavesPerStage;
            if (cfg.baseEnemiesPerWave > 0) perWave = cfg.baseEnemiesPerWave;
        }

        return $"<color={KeyHex}>{stages}</color> stages  ·  " +
               $"<color={KeyHex}>{waves}</color> waves each  ·  " +
               $"<color={KeyHex}>{perWave}</color> enemies per wave";
    }

    /// <summary>A near / mid / far distance strip in the live tether colours
    /// <summary>A near / mid / far distance strip in the live tether colours, so the
    /// three zone names on the tether page mean something before the player has ever
    /// seen a beam.</summary>
    private void BuildTetherBands(Transform parent)
    {
        TetherColors(out Color near, out Color mid, out Color far);

        _bandsRoot = MenuTheme.NewUI("TetherBands", parent);
        var v = _bandsRoot.AddComponent<VerticalLayoutGroup>();
        v.spacing = 6;
        v.padding = new RectOffset(0, 0, 12, 0);
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var strip = MenuTheme.NewUI("Strip", _bandsRoot.transform);
        SetH(strip, 54f);
        var h = strip.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 5;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;

        Band(strip.transform, "NEAR  0-30%", near, 30f);
        Band(strip.transform, "MID  30-80%", mid, 30f);
        Band(strip.transform, "FAR  80-100%", far, 40f);

        _bandsRoot.SetActive(false);
    }

    private void Band(Transform parent, string label, Color col, float weight)
    {
        var go = MenuTheme.NewUI("Band", parent);
        var img = go.AddComponent<Image>();
        img.sprite = _roundFill; img.type = Image.Type.Sliced;
        img.color = new Color(0.07f, 0.04f, 0.12f, 0.85f);
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = weight;

        var edge = MenuTheme.NewUI("Edge", go.transform);
        MenuTheme.Stretch(edge.GetComponent<RectTransform>());
        var edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = _roundRing; edgeImg.type = Image.Type.Sliced;
        edgeImg.color = new Color(col.r, col.g, col.b, 0.95f);
        edgeImg.raycastTarget = false;

        var txt = MenuTheme.NewText(label, go.transform, 20, TextAlignmentOptions.Center, _font);
        txt.fontStyle = FontStyles.Bold;
        txt.color = Color.Lerp(col, Color.white, 0.25f);
        txt.raycastTarget = false;
        MenuTheme.Stretch(txt.rectTransform);
    }

    /// <summary>The live zone colours off PlayerTowerTether, so the tutorial can never
    /// drift from what the beams actually look like. Falls back to that component's
    /// own defaults when no player is in the scene yet.</summary>
    private static void TetherColors(out Color near, out Color mid, out Color far)
    {
        near = new Color(0.30f, 0.90f, 1.00f, 1f);   // cyan       — defense
        mid = new Color(1.00f, 0.40f, 0.18f, 1f);    // red-orange — damage
        far = new Color(0.75f, 0.50f, 1.00f, 1f);    // violet     — range

        var t = FindFirstObjectByType<PlayerTowerTether>();
        if (t == null) return;
        near = Opaque(t.nearColor); mid = Opaque(t.midColor); far = Opaque(t.farColor);
    }

    private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

    /// <summary>Fill the row list for this page, reusing the row objects.</summary>
    private void SyncRows(Page p)
    {
        var rows = p.rows;
        int want = rows != null ? rows.Count : 0;

        while (_rows.Count < want) _rows.Add(NewRow());

        for (int i = 0; i < _rows.Count; i++)
        {
            var ui = _rows[i];
            bool used = i < want;
            ui.Root.SetActive(used);
            if (!used) continue;

            var r = rows[i];
            Color accent = r.keyColor.a > 0.01f ? r.keyColor : p.accent;

            ui.Key.text = r.key;
            ui.Key.color = accent;
            ui.Text.text = r.text;
            ui.Accent.color = accent;
            ui.Badge.color = new Color(accent.r, accent.g, accent.b, 0.13f);
            ui.Badge.gameObject.SetActive(!string.IsNullOrEmpty(r.key));
            DrawRowIcon(ui.Icon, r, accent);
        }
    }

    private RowUI NewRow()
    {
        var root = MenuTheme.NewUI("Row", _rowsRT);
        var cardImg = root.AddComponent<Image>();
        cardImg.sprite = _roundFill; cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(0.07f, 0.04f, 0.13f, 0.78f);   // contrast = legibility
        cardImg.raycastTarget = false;

        var hl = root.AddComponent<HorizontalLayoutGroup>();
        // The card's height comes from its tallest child (the 48-unit icon gutter) plus
        // this padding, so the belt is deepened by padding rather than by a hard height:
        // symmetric top and bottom keeps the badge and text optically centred, and a row
        // whose text wraps to two lines can still grow past it. 8 → 14 takes the card
        // from 64 to 76, ~20% taller.
        hl.padding = new RectOffset(13, 14, RowPadY, RowPadY);   // centres the glyph in its gutter
        hl.spacing = 16;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        hl.childAlignment = TextAnchor.UpperLeft;

        var iconBox = MenuTheme.NewUI("Icon", root.transform);
        var iconLE = iconBox.AddComponent<LayoutElement>();
        iconLE.minWidth = IconBoxW; iconLE.preferredWidth = IconBoxW; iconLE.flexibleWidth = 0f;
        iconLE.minHeight = IconBoxH; iconLE.preferredHeight = IconBoxH;

        var accent = MenuTheme.NewUI("Accent", root.transform);
        var accentImg = accent.AddComponent<Image>();
        accentImg.sprite = _roundFill; accentImg.type = Image.Type.Sliced;
        accentImg.raycastTarget = false;
        var accentLE = accent.AddComponent<LayoutElement>();
        accentLE.minWidth = 6f; accentLE.preferredWidth = 6f; accentLE.flexibleWidth = 0f;
        accentLE.minHeight = 46f; accentLE.flexibleHeight = 1f;

        var badge = MenuTheme.NewUI("Badge", root.transform);
        var badgeImg = badge.AddComponent<Image>();
        badgeImg.sprite = _roundFill; badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(0.20f, 0.10f, 0.31f, 0.95f);
        badgeImg.raycastTarget = false;
        var badgeLE = badge.AddComponent<LayoutElement>();
        badgeLE.minWidth = 250f; badgeLE.preferredWidth = 250f; badgeLE.flexibleWidth = 0f;
        badgeLE.minHeight = 44f; badgeLE.preferredHeight = 44f;

        var key = MenuTheme.NewText("", badge.transform, 24, TextAlignmentOptions.Center, _font);
        key.fontStyle = FontStyles.Bold;
        key.color = Gold;
        key.raycastTarget = false;
        MenuTheme.Stretch(key.rectTransform);

        var text = MenuTheme.NewText("", root.transform, 26, TextAlignmentOptions.TopLeft, _font);
        text.color = Paper;
        text.lineSpacing = 6f;
        text.margin = new Vector4(0f, 11f, 0f, 0f);   // first line level with the badge
        text.raycastTarget = false;
        Wrap(text, 0f);
        var textLE = text.GetComponent<LayoutElement>();
        textLE.preferredWidth = -1f; textLE.flexibleWidth = 1f; textLE.minHeight = 44f;

        return new RowUI
        {
            Root = root,
            Badge = badgeImg,
            Accent = accentImg,
            Key = key,
            Text = text,
            Icon = iconBox
        };
    }

    private void ApplyAccent(Color accent)
    {
        if (accent.a < 0.01f) accent = MenuTheme.Magenta;
        _title.color = Color.Lerp(accent, Color.white, 0.55f);   // light type on the plate
        if (_dividerImg != null)
            _dividerImg.color = new Color(accent.r, accent.g, accent.b, 0.6f);
    }

    /// <summary>Page dots, drawn as little circles — a text bullet would depend on the
    /// display font having the glyph.</summary>
    private void SyncDots(int page, int count)
    {
        if (_dotsRow == null) return;
        var row = _dotsRow.transform;
        while (row.childCount < count)
        {
            var dot = MenuTheme.NewUI("Dot", row);
            var img = dot.AddComponent<Image>();
            img.sprite = _dot;
            img.raycastTarget = false;
            ((RectTransform)dot.transform).sizeDelta = new Vector2(28f, 28f);
            var sh = dot.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(2.5f, -2.5f);
        }
        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i).gameObject;
            child.SetActive(i < count);
            var img = child.GetComponent<Image>();
            if (img != null)
                img.color = i == page ? new Color(0.98f, 0.84f, 1f, 1f)
                                      : new Color(0.74f, 0.64f, 0.88f, 0.95f);
        }
    }

    /// <summary>Callout page: lift the dim, swap the ornate frame for a slim bar,
    /// show the arrows.</summary>
    private void SetCalloutMode(bool on)
    {
        _dimImg.color = on ? new Color(1f, 1f, 1f, 0.40f) : new Color(1f, 1f, 1f, 0.92f);

        _frameBg.SetActive(!on);
        _barBg.SetActive(on);

        if (on)
        {
            _panelRT.anchorMin = _panelRT.anchorMax = new Vector2(0.5f, 0f);
            _panelRT.pivot = new Vector2(0.5f, 0f);
            _panelRT.anchoredPosition = new Vector2(0f, 30f);
            _panelRT.sizeDelta = new Vector2(1120, 272);
            _innerRT.offsetMin = new Vector2(44, 18);
            _innerRT.offsetMax = new Vector2(-44, -16);
            _closeRT.anchoredPosition = ClosePos(true);
        }
        else
        {
            _panelRT.anchorMin = _panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRT.pivot = new Vector2(0.5f, 0.5f);
            _panelRT.anchoredPosition = Vector2.zero;
            _panelRT.sizeDelta = new Vector2(1180, 880);
            _innerRT.offsetMin = new Vector2(90, 84);
            _innerRT.offsetMax = new Vector2(-90, -62);
            _closeRT.anchoredPosition = ClosePos(false);
        }

        _title.fontSize = on ? 30 : 42;
        SetH(_title.transform.parent.gameObject, on ? 38 : 58);
        _rowsRT.gameObject.SetActive(!on);

        // The bar has no slack to give away, so the margin above the rows would only
        // push the caption off the bottom edge.
        if (_topSpacer != null) _topSpacer.gameObject.SetActive(!on);

        // Everything below the text shrinks a little on the bar, so the buttons stay
        // inside it instead of hanging off the bottom edge.
        if (_dotsRow != null) SetH(_dotsRow, on ? 30f : 46f);
        if (_dotGap != null) SetH(_dotGap, on ? 4f : 16f);
        if (_buttonRow != null) SetH(_buttonRow, on ? 66f : 86f);
        _body.fontSize = on ? 22 : 28;
        _body.alignment = on ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
        _body.color = on ? Paper : new Color(0.92f, 0.89f, 0.98f, 1f);

        _calloutRT.gameObject.SetActive(on);
        if (on) LayoutCallouts();
    }

    //  UI 

    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);
        _titleFont = ResolveTutorialFont();
        _roundFill = RoundedSprite(false);
        _roundRing = RoundedSprite(true);
        _dot = DotSprite();
        _circleRing = CircleRingSprite();
        _tri = TriangleSprite();
        // The painted arrow, with the generated one as a fallback if it ever moves.
        _head = Resources.Load<Sprite>(ArrowResource) ?? ArrowSprite();

        _root = new GameObject("IntroTutorialCanvas",
                               typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4900;                       // under TutorialScreen (5000)
        canvas.pixelPerfect = true;                       // stops TMP going soft when the
                                                          // canvas is scaled off 1920×1080
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // full-screen dim (also blocks clicks into the game behind)
        var dim = MenuTheme.NewUI("Dim", _root.transform);
        MenuTheme.Stretch(dim.GetComponent<RectTransform>());
        _dimImg = dim.AddComponent<Image>();
        _dimImg.sprite = MenuTheme.VerticalGradient(MenuTheme.GradTop, MenuTheme.GradBottom);
        _dimImg.color = new Color(1f, 1f, 1f, 0.92f);

        // arrow layer, above the dim and below the panel
        var layer = MenuTheme.NewUI("Callouts", dim.transform);
        _calloutRT = layer.GetComponent<RectTransform>();
        MenuTheme.Stretch(_calloutRT);

        // the annotated screenshot, behind everything else on this page
        var shot = MenuTheme.NewUI("Screenshot", layer.transform);
        _shotRT = shot.GetComponent<RectTransform>();
        MenuTheme.Stretch(_shotRT);
        var shotImg = shot.AddComponent<Image>();
        shotImg.sprite = Resources.Load<Sprite>(screenshotResource);
        shotImg.preserveAspect = true;
        shotImg.raycastTarget = false;
        if (shotImg.sprite != null)
            _shotAspect = shotImg.sprite.rect.width / shotImg.sprite.rect.height;
        else
            shotImg.color = new Color(1f, 1f, 1f, 0f);

        // A drawn empty slot, mirroring the weapon wheel across the screen. The
        // screenshot's own free slots are scattered, so this one is placed rather than
        // found — it's what lets the two bottom labels be exact mirrors.
        var slot = MenuTheme.NewUI("DemoSlot", layer.transform);
        _demoSlotRT = slot.GetComponent<RectTransform>();
        _demoSlotRT.anchorMin = _demoSlotRT.anchorMax = new Vector2(0.5f, 0.5f);
        _demoSlotRT.pivot = new Vector2(0.5f, 0.5f);
        _demoSlotRT.sizeDelta = new Vector2(78f, 78f);
        Disc(slot.transform, Vector2.zero, 78f, new Color(0.90f, 0.90f, 0.92f, 0.85f));
        Ring(slot.transform, Vector2.zero, 78f, new Color(1f, 1f, 1f, 0.6f));
        var mark = new Color(0.16f, 0.10f, 0.25f, 0.95f);
        Bar(slot.transform, Vector2.zero, new Vector2(44f, 11f), mark);
        Bar(slot.transform, Vector2.zero, new Vector2(11f, 44f), mark);

        layer.SetActive(false);

        // panel shell — two interchangeable backgrounds live inside it
        var panel = MenuTheme.NewUI("Panel", dim.transform);
        _panelRT = panel.GetComponent<RectTransform>();
        _panelRT.anchorMin = _panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRT.pivot = new Vector2(0.5f, 0.5f);
        _panelRT.sizeDelta = new Vector2(1180, 880);

        // (a) ornate frame — used by the full-size text pages
        _frameBg = MenuTheme.NewUI("FrameBG", panel.transform);
        MenuTheme.Stretch(_frameBg.GetComponent<RectTransform>());
        var frameImg = _frameBg.AddComponent<Image>();

        var panelArt = LoadSprite(PanelSpriteResource);
        if (panelArt != null)
        {
            frameImg.sprite = panelArt;
            frameImg.color = Color.white;
            ApplyPanelFrame(frameImg, PanelBorder, PanelFrameScale);
        }
        else
        {
            // Nothing under Resources — fall back to the shared MenuTheme panel, so the
            // overlay never opens as a bare rectangle. Different image, so its own numbers.
            MenuTheme.ApplySprite(frameImg, MenuTheme.PanelSprite, MenuTheme.PanelSolid);
            ApplyPanelFrame(frameImg, MenuPanelBorder, MenuPanelFrameScale);
        }

        // (b) slim rounded bar — used by the callout page. The ornate frame's corner
        // ornaments look enormous at 250px tall, so the bar gets its own treatment.
        _barBg = MenuTheme.NewUI("BarBG", panel.transform);
        MenuTheme.Stretch(_barBg.GetComponent<RectTransform>());
        var barImg = _barBg.AddComponent<Image>();
        barImg.sprite = _roundFill; barImg.type = Image.Type.Sliced; barImg.color = Ink;
        var barRing = MenuTheme.NewUI("Edge", _barBg.transform);
        MenuTheme.Stretch(barRing.GetComponent<RectTransform>());
        var barRingImg = barRing.AddComponent<Image>();
        barRingImg.sprite = _roundRing; barRingImg.type = Image.Type.Sliced;
        barRingImg.color = new Color(Violet.r, Violet.g, Violet.b, 0.55f);
        barRingImg.raycastTarget = false;
        _barBg.SetActive(false);

        // inner column
        var inner = MenuTheme.NewUI("Inner", panel.transform);
        _innerRT = inner.GetComponent<RectTransform>();
        _innerRT.anchorMin = Vector2.zero; _innerRT.anchorMax = Vector2.one;
        _innerRT.offsetMin = new Vector2(90, 84); _innerRT.offsetMax = new Vector2(-90, -62);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;
        // Lets FitRunBar shrink a block by scale and have the column close up behind it
        // instead of reserving the full-size height and leaving a gap.
        v.childScaleHeight = true;

        var titleRow = MenuTheme.NewUI("TitleRow", inner.transform);
        SetH(titleRow, 62);
        var th = titleRow.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 18;
        th.childControlWidth = true; th.childControlHeight = true;
        th.childForceExpandWidth = false; th.childForceExpandHeight = false;
        th.childAlignment = TextAnchor.MiddleCenter;

        // The marble frame is a mottled mid-grey, so no single title colour reads well
        // against it — dark vanished, light vanished. Give the title its own dark plate
        // and put light type on it, same trick as the row cards.
        var titlePlate = MenuTheme.NewUI("TitlePlate", titleRow.transform);
        var tpRT = titlePlate.GetComponent<RectTransform>();
        tpRT.anchorMin = new Vector2(0f, 0f); tpRT.anchorMax = new Vector2(1f, 1f);
        tpRT.offsetMin = new Vector2(-26f, -6f); tpRT.offsetMax = new Vector2(26f, 6f);
        var tpImg = titlePlate.AddComponent<Image>();
        tpImg.sprite = _roundFill; tpImg.type = Image.Type.Sliced;
        tpImg.color = new Color(0.07f, 0.04f, 0.13f, 0.80f);
        tpImg.raycastTarget = false;
        titlePlate.AddComponent<LayoutElement>().ignoreLayout = true;

        _emblemRoot = MenuTheme.NewUI("Emblem", titleRow.transform);
        var eLE = _emblemRoot.AddComponent<LayoutElement>();
        eLE.minWidth = 54f; eLE.preferredWidth = 54f; eLE.flexibleWidth = 0f;
        eLE.minHeight = 54f; eLE.preferredHeight = 54f;

        _title = MenuTheme.NewText("", titleRow.transform, 42, TextAlignmentOptions.Center, _titleFont);
        _title.characterSpacing = 6f;   // letterspacing carries it; bold was unreadable
        _title.enableVertexGradient = false;
        NoShadow(_title);
        _dividerImg = AddDivider(inner.transform);

        _body = MenuTheme.NewText("", inner.transform, 28, TextAlignmentOptions.TopLeft, _font);
        _body.color = new Color(0.92f, 0.89f, 0.98f, 1f);
        _body.lineSpacing = 8f;
        Wrap(_body, 0f);
        var bodyLE = _body.GetComponent<LayoutElement>();
        bodyLE.preferredWidth = -1f;      // full panel width
        bodyLE.flexibleHeight = 0f; bodyLE.minHeight = 0f;

        // Free height ABOVE the rows. Paired with the spacer further down, it centres
        // the row block in the space between the divider and the dots instead of
        // letting it hug the title. Hidden on the callout pages, where the panel is a
        // short bar and there is no slack to hand out.
        var topSpacer = MenuTheme.NewUI("TopSpacer", inner.transform);
        _topSpacerRT = topSpacer.GetComponent<RectTransform>();
        _topSpacer = topSpacer.AddComponent<LayoutElement>();
        _topSpacer.minHeight = 0f; _topSpacer.flexibleHeight = 1f;

        // key → meaning rows
        var rows = MenuTheme.NewUI("Rows", inner.transform);
        _rowsRT = rows.GetComponent<RectTransform>();
        var rl = rows.AddComponent<VerticalLayoutGroup>();
        _rowsLayout = rl;
        rl.spacing = RowGapMin;   // re-measured per page in SpreadRows
        rl.padding = new RectOffset(0, 0, 10, 6);
        rl.childControlWidth = true; rl.childControlHeight = true;
        rl.childForceExpandWidth = true; rl.childForceExpandHeight = false;
        var rowsFit = rows.AddComponent<ContentSizeFitter>();
        rowsFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildTetherBands(inner.transform);
        // BuildGamepad(inner.transform);   // controller page — re-enable to bring it back
        BuildRunBar(inner.transform);

        // eats the leftover height so the dots and buttons sit at the bottom
        var spacer = MenuTheme.NewUI("Spacer", inner.transform);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.flexibleHeight = 1f; spacerLE.minHeight = 8f;
        _bottomSpacer = spacerLE;
        _bottomSpacerRT = spacer.GetComponent<RectTransform>();

        _dotsRow = MenuTheme.NewUI("Dots", inner.transform);
        SetH(_dotsRow, 46);
        var dh = _dotsRow.AddComponent<HorizontalLayoutGroup>();
        dh.spacing = 20;
        dh.childControlWidth = false; dh.childControlHeight = false;
        dh.childForceExpandWidth = false; dh.childForceExpandHeight = false;
        dh.childAlignment = TextAnchor.MiddleCenter;

        _dotGap = MenuTheme.NewUI("DotGap", inner.transform);   // lifts the dots off the buttons
        SetH(_dotGap, 16f);

        // button row — fixed-width, centred. Stretching three buttons across the whole
        // panel is what made the bar look like a toolbar.
        var row = MenuTheme.NewUI("Buttons", inner.transform);
        _buttonRow = row;
        SetH(row, 86);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 22;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        _backBtn = MenuTheme.NewButton("Back", row.transform, 30, _font);
        FixedWidth(_backBtn, 280f);
        SkinButton(_backBtn);
        Hoverable(_backBtn);
        _backBtn.onClick.AddListener(Back);

        if (showFullGuideButton)
        {
            var guide = MenuTheme.NewButton("Full Guide", row.transform, 30, _font);
            FixedWidth(guide, 300f);
            SkinButton(guide);
            Hoverable(guide);
            guide.onClick.AddListener(() =>
            {
                MenuBackInput.Consume();     // don't let this frame's press close the child
                TutorialScreen.ShowTutorial();
            });
        }

        _nextBtn = MenuTheme.NewButton("Next", row.transform, 30, _font);
        FixedWidth(_nextBtn, 300f);
        SkinButton(_nextBtn);
        Hoverable(_nextBtn);
        _nextBtn.onClick.AddListener(Next);
        _nextLabel = _nextBtn.GetComponentInChildren<TextMeshProUGUI>();

        // close button, top-right of the panel
        var close = MenuTheme.NewButton("X", panel.transform, 24, _font);
        _closeRT = (RectTransform)close.transform;
        _closeRT.anchorMin = _closeRT.anchorMax = new Vector2(1f, 1f);
        _closeRT.pivot = new Vector2(0.5f, 0.5f);       // scale about the centre, not a corner
        _closeRT.anchoredPosition = ClosePos(false);
        _closeRT.sizeDelta = new Vector2(52f, 52f);
        SkinSquareButton(close);
        close.gameObject.AddComponent<HoverScale>();
        close.onClick.AddListener(Close);

        _root.SetActive(false);
    }

    /// <summary>Grows a UI element while the pointer is over it. Uses unscaled time —
    /// the game is frozen while this screen is up.</summary>
    private class HoverScale : MonoBehaviour,
                               UnityEngine.EventSystems.IPointerEnterHandler,
                               UnityEngine.EventSystems.IPointerExitHandler
    {
        public float hovered = 1.18f;
        public float speed = 12f;

        private float _target = 1f;

        public void OnPointerEnter(PointerEventData _) => _target = hovered;
        public void OnPointerExit(PointerEventData _) => _target = 1f;
        private void OnDisable() { _target = 1f; transform.localScale = Vector3.one; }

        private void Update()
        {
            float t = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            float s = Mathf.Lerp(transform.localScale.x, _target, t);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Top-right corner offset for the close button, centre-pivoted.</summary>
    private static Vector2 ClosePos(bool bar) => bar ? new Vector2(-50f, -38f) : new Vector2(-112f, -90f);

    /// <summary>Find Cinzel-Tutorial. The asset lives outside Resources, so: the
    /// Inspector slot first, then a few Resources paths in case it gets moved there, then
    /// a scan of font assets already loaded in memory. Falls back to the menu font.</summary>
    private TMP_FontAsset ResolveTutorialFont()
    {
        if (tutorialTitleFont != null) return tutorialTitleFont;

        string[] paths =
        {
            "Fonts/Cinzel-Tutorial", "Cinzel-Tutorial",
            "Fonts/Cinzel/static/Cinzel-Tutorial", "Fonts/Cinzel/Cinzel-Tutorial",
        };
        foreach (var path in paths)
        {
            var f = Resources.Load<TMP_FontAsset>(path);
            if (f != null) return f;
        }

        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (f != null && f.name.IndexOf("Cinzel-Tutorial", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return f;

        return _font;
    }

    /// <summary>Turn off the font material's drop shadow (TMP calls it the underlay).
    /// Cinzel-Tutorial ships with one baked into its material; it muddies the title at
    /// this size. Wrapped, because a font without an underlay has no such property.</summary>
    private static void NoShadow(TMP_Text text)
    {
        try
        {
            var mat = text.fontMaterial;                 // instance, not the shared asset
            mat.DisableKeyword("UNDERLAY_ON");
            if (mat.HasProperty("_UnderlaySoftness")) mat.SetFloat("_UnderlaySoftness", 0f);
            if (mat.HasProperty("_UnderlayDilate")) mat.SetFloat("_UnderlayDilate", 0f);
            if (mat.HasProperty("_UnderlayOffsetX")) mat.SetFloat("_UnderlayOffsetX", 0f);
            if (mat.HasProperty("_UnderlayOffsetY")) mat.SetFloat("_UnderlayOffsetY", 0f);
        }
        catch { }
    }

    /// <summary>A gentle grow-on-hover. Subtler than the close button's, because these
    /// are wide and sit in a row — a big jump would shove against its neighbours.</summary>
    private static void Hoverable(Component c, float scale = 1.055f)
    {
        var h = c.gameObject.AddComponent<HoverScale>();
        h.hovered = scale;
    }

    private static void FixedWidth(Component c, float w)
    {
        var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.minWidth = w; le.flexibleWidth = 0f;
    }

    private Image AddDivider(Transform parent)
    {
        var go = MenuTheme.NewUI("Divider", parent);
        var img = go.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.55f);
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 3f; le.preferredHeight = 3f; le.flexibleHeight = 0f;
        return img;
    }

    private static void SetH(Component c, float h) => SetH(c.gameObject, h);

    private static void SetH(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;
    }

    /// <summary>Sets up a panel background as a proper 9-slice.
    ///
    /// The border the Sprite Editor carries wins — nudge the four green guides on the
    /// asset, where they can be seen against the art, and the frame follows. Only when
    /// the importer leaves the border at zero is a uniform one synthesised here, which
    /// is what produced the stretched corners: a square guess cuts through an ornament
    /// that is not square, and everything outside the cut lands in an edge slice, which
    /// 9-slicing stretches the length of the rail.
    ///
    /// The frame is then drawn at its authored pixel size — a 180px border renders 180
    /// units thick — rather than at whatever a hand-tuned multiplier happened to give.
    /// <paramref name="scale"/> thickens (>1) or thins (&lt;1) the whole frame evenly.</summary>
    /// <summary>Puts the shared button artwork on one of the nav buttons. The sprite's own
    /// Border wins if the importer sets one, same as the panel; otherwise the measured
    /// slice above is used. Left alone when the art is missing, so the buttons keep the
    /// MenuTheme look rather than turning into blank rectangles.</summary>
    /// <summary>The close button's artwork. Only the image changes: the 52×52 rect, the
    /// corner offset it is positioned by, the centre pivot it scales about and the hover
    /// growth are all left exactly as they were. Drawn whole rather than sliced — it is
    /// nearly square already, and slicing a rect this small would eat the corner detail.</summary>
    private void SkinSquareButton(Button btn)
    {
        if (btn == null) return;

        var sprite = LoadSprite(SquareButtonSpriteResource);
        if (sprite == null) return;

        var img = btn.targetGraphic as Image;
        if (img == null) img = btn.GetComponent<Image>();
        if (img == null) return;

        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.preserveAspect = true;   // 250×243 art, square rect — don't stretch it to fit
        img.color = Color.white;
    }

    private void SkinButton(Button btn)
    {
        if (btn == null) return;

        var sprite = LoadSprite(ButtonSpriteResource);
        if (sprite == null) return;

        var img = btn.targetGraphic as Image;
        if (img == null) img = btn.GetComponent<Image>();
        if (img == null) return;

        Vector4 border = sprite.border;
        if (border.sqrMagnitude < 1f)
            border = new Vector4(ButtonBorderX, ButtonBorderY, ButtonBorderX, ButtonBorderY);

        img.sprite = WithBorder(sprite, ClampBorder(border, sprite.rect));
        img.type = Image.Type.Sliced;
        img.fillCenter = true;
        img.color = Color.white;
        img.pixelsPerUnitMultiplier = FrameMultiplier(img, ButtonArtScale);
    }

    private void ApplyPanelFrame(Image img, float fallbackBorder, float scale)
    {
        var sprite = img != null ? img.sprite : null;
        if (sprite == null) return;

        Vector4 border = sprite.border;     // (left, bottom, right, top), in texture pixels
        if (border.sqrMagnitude < 1f)
        {
            border = new Vector4(fallbackBorder, fallbackBorder, fallbackBorder, fallbackBorder);
            Debug.Log($"[IntroTutorial] {sprite.name} has no Border set in its import " +
                      $"settings, so the panel falls back to a uniform {fallbackBorder}px " +
                      "slice. Set Border L/B/R/T in the Sprite Editor for a frame that " +
                      "matches the artwork exactly.");
        }

        img.sprite = WithBorder(sprite, ClampBorder(border, sprite.rect));
        img.type = Image.Type.Sliced;
        img.fillCenter = true;
        img.pixelsPerUnitMultiplier = FrameMultiplier(img, scale);
    }

    /// <summary>Keeps opposing borders from meeting: Unity would shrink them to fit
    /// anyway, but silently and in proportion, which reads as a frame that thins itself
    /// on small panels.</summary>
    private static Vector4 ClampBorder(Vector4 b, Rect rect)
    {
        float wide = b.x + b.z, tall = b.y + b.w;
        if (wide > rect.width * 0.98f && wide > 0f)
        {
            float k = rect.width * 0.98f / wide; b.x *= k; b.z *= k;
        }
        if (tall > rect.height * 0.98f && tall > 0f)
        {
            float k = rect.height * 0.98f / tall; b.y *= k; b.w *= k;
        }
        return b;
    }

    /// <summary>Multiplier that makes a sliced border draw one UI unit per texture pixel,
    /// whatever the canvas reference PPU is, times <paramref name="scale"/>.</summary>
    private static float FrameMultiplier(Image img, float scale)
    {
        float spritePPU = img.sprite != null && img.sprite.pixelsPerUnit > 0f
                        ? img.sprite.pixelsPerUnit : 100f;
        var canvas = img.canvas;
        float refPPU = canvas != null && canvas.referencePixelsPerUnit > 0f
                     ? canvas.referencePixelsPerUnit : 100f;
        return Mathf.Max(0.01f, refPPU / spritePPU / Mathf.Max(0.05f, scale));
    }

    private static Sprite WithBorder(Sprite src, float border)
        => WithBorder(src, new Vector4(border, border, border, border));

    private static Sprite WithBorder(Sprite src, Vector4 border)
    {
        if (src == null) return null;
        try
        {
            float ppu = src.pixelsPerUnit > 0 ? src.pixelsPerUnit : 100f;
            return Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), ppu, 0,
                                 SpriteMeshType.FullRect, border);
        }
        catch { return src; }
    }

    //  CALLOUTS (the arrow page) 

    private class Callout
    {
        public Func<Vector2?> Target;      // screen-space point, or null if unavailable
        public RectTransform Box, Head;

        // How much clear space the arrow leaves at the target end.
        public float Standoff;

        // Fixed viewport position for the label, or null to let PlaceLabels choose.
        public Vector2? Anchor;

        // Forces the arrow onto a given axis for a pinned label. Without it the axis is
        // inferred from the offset, which flips to vertical as soon as the target is
        // more below the label than beside it.
        public Vector2? ForceDir;

        // Extra distance to slide the arrow away from the label, along its own axis.
        public float ArrowNudge;

        // True: the arrow leaves the MIDDLE of the label's edge (two mirrored labels then
        // line their arrows up with each other). False: it leaves the edge level with its
        // own target, which is what keeps an arrow pointing exactly at the thing.
        public bool CenterAttach;

        // Resolved each frame by PlaceLabels.
        public Vector2 Pos;                // label centre
        public Vector2 Dir;                // box → target, always one of the four axes
        public bool Active;
    }

    private void RebuildCallouts(CalloutSet set)
    {
        foreach (var c in _callouts)
        {
            // Destroy is deferred to end of frame — hide first so the old set can't
            // flash on top of the new one.
            if (c.Box != null) { c.Box.gameObject.SetActive(false); Destroy(c.Box.gameObject); }
            if (c.Head != null) { c.Head.gameObject.SetActive(false); Destroy(c.Head.gameObject); }
        }
        _callouts.Clear();
        _labelsDirty = true;
        if (_demoSlotRT != null) _demoSlotRT.gameObject.SetActive(set == CalloutSet.Hud);

        if (set == CalloutSet.Combat) { BuildCombatCallouts(); return; }

        // Everything is positioned against the SCREENSHOT, not the live scene: fixed
        // coordinates inside the image (u from the left, v from the bottom), so the page
        // is identical in single player and co-op and can't be thrown off by where the
        // camera happens to be.
        //
        // The composition is built in mirrored pairs, and every arrow is centre-attached
        // so it leaves the middle of its label's edge — label, arrow and target end up on
        // one line by construction rather than by tuning.
        //
        //   health (0.375, 0.93) ←     →  (0.635, 0.93) energy   — level pair, top
        //   weapon (0.135, 0.395) ↓     ↓  (0.865, 0.395) slot    — mirrored pair, lower
        //   core   (0.682, 0.78) ↓      ↑  (0.50, 0.33) character
        Add("HEALTH & STAMINA",
            "Health, and stamina below.",
            () => ShotPoint(0.122f, 0.944f), 40f,
            new Vector2(0.375f, 0.93f), Vector2.left, true);

        Add("ENERGY",
            "Towers, upgrades, repairs.",
            () => ShotPoint(0.900f, 0.912f), 45f,
            new Vector2(0.635f, 0.93f), Vector2.right, true);

        Add("WEAPON ROLL",
            $"<color={KeyHex}>↓</color>  Scroll down — weapons\n" +
            $"<color={KeyHex}>↑</color>  Scroll up — tools",
            () => ShotPoint(WheelU, WheelV), 80f,
            new Vector2(0.135f, 0.395f), Vector2.down);

        Add("THE CORE",
            "Zero energy ends the run.",
            () => ShotPoint(0.682f, 0.500f), 85f,
            new Vector2(0.682f, 0.78f), Vector2.down, true);

        Add("TOWER SLOT",
            $"Press <color={KeyHex}>Space</color> to build tower\n" +
            $"Select tower from the <color={KeyHex}>Wheel</color>",
            () => DemoSlotPoint(), 70f,
            new Vector2(0.865f, 0.395f), Vector2.down);

        Add("YOUR CHARACTER",
            $"<color={KeyHex}>WASD</color> move, <color={KeyHex}>Shift</color> dash",
            () => ShotPoint(0.503f, 0.495f), 70f,
            new Vector2(0.500f, 0.33f), Vector2.up, true);
    }

    /// <summary>Second annotated page: what's actually happening on the field.</summary>
    private void BuildCombatCallouts()
    {
        //   range  (0.33, 0.855) →        ↓ (0.455, 0.635) tether
        //   tower  (0.165, 0.455) →        ↓ (0.845, 0.72) enemy
        Add("TOWER",
            "Shoots on its own. Upgrade it to hit harder.",
            () => ShotPoint(0.328f, 0.486f), 70f,
            new Vector2(0.165f, 0.455f), Vector2.right, true, 18f);

        // The OUTER ring is the attack range: measured off the screenshot, it's centred
        // on the tower at (0.331, 0.468) with a radius of 555px — the small dashed ring
        // at 180px is a different circle. At this label's height the outer ring crosses
        // x = 0.521, which is where the arrow is aimed; move the label vertically and
        // that number has to be recomputed from the circle.
        Add("ATTACK RANGE",
            "The tower only hits inside this ring.",
            () => ShotPoint(0.521f, 0.855f), 25f,
            new Vector2(0.330f, 0.855f), Vector2.right, true);

        Add("TETHER",
            "Stand near a tower and this beam buffs it.",
            () => ShotPoint(0.455f, 0.495f), 34f,
            new Vector2(0.455f, 0.635f), Vector2.down, true, 16f);

        Add("ENEMY",
            "Kill it before it reaches your Core.",
            () => ShotPoint(0.784f, 0.519f), 60f,
            new Vector2(0.845f, 0.72f), Vector2.down);
    }

    // The weapon wheel's spot in the screenshot. The drawn slot marker is its mirror
    // image, so the two bottom labels and their arrows line up whatever happens here.
    private const float WheelU = 0.062f, WheelV = 0.218f;

    private Vector2 DemoSlotPoint() => ShotPoint(1f - WheelU, WheelV);

    /// <summary>A point inside the screenshot, in screen pixels. (u, v) are fractions of
    /// the image from its bottom-left. The image is letterboxed to fit, so this repeats
    /// the same fit maths Image.preserveAspect uses.</summary>
    private Vector2 ShotPoint(float u, float v)
    {
        float sw = Screen.width, sh = Screen.height;

        float w = sw, h = sw / _shotAspect;
        if (h > sh) { h = sh; w = sh * _shotAspect; }

        return new Vector2((sw - w) * 0.5f + u * w, (sh - h) * 0.5f + v * h);
    }


    private void Add(string title, string desc, Func<Vector2?> target, float standoff = 60f,
                     Vector2? anchor = null, Vector2? forceDir = null, bool centerAttach = false,
                     float arrowNudge = 0f)
    {
        var c = new Callout
        {
            Target = target,
            Standoff = standoff,
            Anchor = anchor,
            ForceDir = forceDir,
            CenterAttach = centerAttach,
            ArrowNudge = arrowNudge
        };

        // Just the arrow, sitting next to its target and pointing at it. Added before
        // the label so the label always draws on top.
        c.Head = NewGraphic("Arrow", _head, Color.white);

        var box = MenuTheme.NewUI("Label", _calloutRT);
        c.Box = box.GetComponent<RectTransform>();
        c.Box.anchorMin = c.Box.anchorMax = new Vector2(0.5f, 0.5f);
        c.Box.pivot = new Vector2(0.5f, 0.5f);

        var bg = box.AddComponent<Image>();
        bg.sprite = _roundFill; bg.type = Image.Type.Sliced; bg.color = Ink;
        bg.raycastTarget = false;

        // The layout group lives on the BOX (not a child), so the ContentSizeFitter
        // has something to measure and the height follows the text.
        var vl = box.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(18, 18, 12, 14);
        vl.spacing = 1;
        vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

        var fit = box.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;   // width stays fixed
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        c.Box.sizeDelta = new Vector2(LabelWidth, 84f);

        var edge = MenuTheme.NewUI("Edge", box.transform);
        MenuTheme.Stretch(edge.GetComponent<RectTransform>());
        var edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = _roundRing; edgeImg.type = Image.Type.Sliced;
        edgeImg.color = new Color(Violet.r, Violet.g, Violet.b, 0.5f);
        edgeImg.raycastTarget = false;
        edge.AddComponent<LayoutElement>().ignoreLayout = true;   // border, not a row

        var t = MenuTheme.NewText(title, box.transform, 25, TextAlignmentOptions.TopLeft, _titleFont);
        t.characterSpacing = 2f;
        t.color = Gold;
        t.raycastTarget = false;
        Wrap(t, LabelWidth - 38f);

        var d = MenuTheme.NewText(desc, box.transform, 21, TextAlignmentOptions.TopLeft, _font);
        d.color = Paper;
        d.lineSpacing = 6f;
        d.raycastTarget = false;
        Wrap(d, LabelWidth - 38f);

        _callouts.Add(c);
    }

    /// <summary>Turn word wrapping ON and pin the wrap width. This project's TMP default
    /// has wrapping off, which is why label text ran past the box edge. The property was
    /// renamed in newer TMP (enableWordWrapping → textWrappingMode), so it's set through
    /// whichever one this version actually has — no version-dependent compile error, no
    /// obsolete-API warning.</summary>
    private static void Wrap(TMP_Text tmp, float width)
    {
        var prop = typeof(TMP_Text).GetProperty("textWrappingMode");
        if (prop != null) prop.SetValue(tmp, System.Enum.ToObject(prop.PropertyType, 1)); // Normal
        else
        {
            prop = typeof(TMP_Text).GetProperty("enableWordWrapping");
            if (prop != null) prop.SetValue(tmp, true);
        }

        var le = tmp.GetComponent<LayoutElement>() ?? tmp.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.flexibleWidth = 0f;
    }

    private RectTransform NewGraphic(string name, Sprite sprite, Color col)
    {
        var go = MenuTheme.NewUI(name, _calloutRT);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = col;
        img.raycastTarget = false;
        img.preserveAspect = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    /// <summary>Resolve every target, place each label squarely above / below / left of /
    /// right of it, then draw the connecting arrow.</summary>
    private void LayoutCallouts()
    {
        if (_demoSlotRT != null && ToLocal(DemoSlotPoint(), out Vector2 slotAt))
            _demoSlotRT.anchoredPosition = slotAt;

        var tips = new Vector2[_callouts.Count];

        for (int i = 0; i < _callouts.Count; i++)
        {
            var c = _callouts[i];
            Vector2? target = null;
            try { target = c.Target(); } catch { }

            c.Active = target.HasValue && ToLocal(target.Value, out tips[i]);
            c.Box.gameObject.SetActive(c.Active);
            c.Head.gameObject.SetActive(c.Active);
        }

        if (_labelsDirty)
        {
            // Placement works off box sizes, so the size fitters have to have run first.
            foreach (var c in _callouts)
                if (c.Box != null) LayoutRebuilder.ForceRebuildLayoutImmediate(c.Box);
            _labelsDirty = false;
        }

        PlaceLabels(tips);

        for (int i = 0; i < _callouts.Count; i++)
        {
            var c = _callouts[i];
            if (!c.Active) continue;

            c.Box.anchoredPosition = c.Pos;
            DrawArrow(c, tips[i]);
        }
    }

    private const float ArrowLen = 54f;    // arrow.png is 230x177
    private const float ArrowGap = 10f;

    // box → target. Only these four, so the arrow art is never rotated to a diagonal.
    private static readonly Vector2[] Axes =
        { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

    // Extra distance a label may be pushed away from its target, on top of the minimum.
    private static readonly float[] Pushes = { 0f, 90f, 200f, 340f, 520f };

    // Where along its edge the arrow leaves the box, as a fraction of the usable half.
    private static readonly float[] Slides = { 0f, 0.55f, -0.55f, 1f, -1f };

    /// <summary>Place every label on one of the four sides of its target with nothing
    /// overlapping and nothing off-screen. Candidates are 4 sides × 5 distances × 5 slide
    /// positions; anything that runs off-screen, covers the bar, overlaps a placed label
    /// or sits on another callout's target is rejected, and the closest survivor wins.</summary>
    private void PlaceLabels(Vector2[] tips)
    {
        var canvasRT = (RectTransform)_calloutRT.parent;
        float w = canvasRT.rect.width, h = canvasRT.rect.height;

        Rect screen = new Rect(-w * 0.5f + 22f, -h * 0.5f + 22f, w - 44f, h - 44f);
        Rect bar = LocalRectOf(_panelRT);
        bar.xMin -= 20f; bar.xMax += 20f; bar.yMin -= 20f; bar.yMax += 20f;

        // most constrained first: whoever is nearest an edge has the fewest options
        var order = new List<int>();
        for (int i = 0; i < _callouts.Count; i++) if (_callouts[i].Active) order.Add(i);
        order.Sort((x, y) => EdgeDistance(tips[x], screen).CompareTo(EdgeDistance(tips[y], screen)));

        var placed = new List<Rect>();

        // pinned first — they define the composition, the rest fit around them
        foreach (int i in order)
        {
            var c = _callouts[i];
            if (!c.Anchor.HasValue) continue;

            Vector2 sz = c.Box.rect.size;
            Vector2 at = new Vector2((c.Anchor.Value.x - 0.5f) * w, (c.Anchor.Value.y - 0.5f) * h);
            at.x = Mathf.Clamp(at.x, screen.xMin + sz.x * 0.5f, screen.xMax - sz.x * 0.5f);
            at.y = Mathf.Clamp(at.y, screen.yMin + sz.y * 0.5f, screen.yMax - sz.y * 0.5f);

            Vector2 d = tips[i] - at;
            c.Pos = at;
            c.Dir = c.ForceDir ?? (Mathf.Abs(d.x) >= Mathf.Abs(d.y)
                  ? (d.x >= 0f ? Vector2.right : Vector2.left)
                  : (d.y >= 0f ? Vector2.up : Vector2.down));
            placed.Add(new Rect(at.x - sz.x * 0.5f, at.y - sz.y * 0.5f, sz.x, sz.y));
        }

        foreach (int i in order)
        {
            var c = _callouts[i];
            if (c.Anchor.HasValue) continue;
            Vector2 size = c.Box.rect.size;

            float best = float.MaxValue;
            Vector2 bestPos = tips[i];
            Vector2 bestDir = Vector2.left;

            foreach (var dir in Axes)
            {
                bool horizontal = Mathf.Abs(dir.x) > 0.5f;
                Vector2 perp = horizontal ? Vector2.up : Vector2.right;

                float half = horizontal ? size.x * 0.5f : size.y * 0.5f;
                float halfPerp = (horizontal ? size.y : size.x) * 0.5f;
                float slideRoom = Mathf.Max(0f, halfPerp - 34f);
                float minReach = c.Standoff + ArrowLen + ArrowGap * 2f + half;

                foreach (float push in Pushes)
                    foreach (float slide in Slides)
                    {
                        Vector2 pos = tips[i] - dir * (minReach + push) + perp * (slide * slideRoom);
                        Rect box = new Rect(pos.x - size.x * 0.5f, pos.y - size.y * 0.5f, size.x, size.y);

                        float bad = OutsideArea(box, screen) + OverlapArea(box, bar);
                        foreach (var q in placed) bad += OverlapArea(box, q);
                        for (int k = 0; k < _callouts.Count; k++)
                            if (k != i && _callouts[k].Active && Grow(box, 26f).Contains(tips[k]))
                                bad += 40000f;

                        float score = (bad > 0.5f ? 1e7f + bad : 0f)
                                    + push * 60f
                                    + Mathf.Abs(slide) * slideRoom * 25f
                                    + (horizontal ? 0f : 9000f);

                        if (score < best) { best = score; bestPos = pos; bestDir = dir; }
                    }
            }

            c.Pos = bestPos;
            c.Dir = bestDir;
            placed.Add(new Rect(bestPos.x - size.x * 0.5f, bestPos.y - size.y * 0.5f, size.x, size.y));
        }
    }

    private static Rect Grow(Rect r, float by)
        => new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

    private static float EdgeDistance(Vector2 p, Rect bounds)
        => Mathf.Min(Mathf.Min(p.x - bounds.xMin, bounds.xMax - p.x),
                     Mathf.Min(p.y - bounds.yMin, bounds.yMax - p.y));

    private static float OverlapArea(Rect a, Rect b)
    {
        float x = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float y = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return (x > 0f && y > 0f) ? x * y : 0f;
    }

    /// <summary>How much of `box` sticks out of `bounds`.</summary>
    private static float OutsideArea(Rect box, Rect bounds)
        => box.width * box.height - OverlapArea(box, bounds);

    /// <summary>The arrow runs from the label's edge to the target, always along one of
    /// the four axes. It leaves the edge at the point that lines up with the target, so
    /// the label doesn't have to be centred on it.</summary>
    private void DrawArrow(Callout c, Vector2 tip)
    {
        Vector2 size = c.Box.rect.size;
        Vector2 dir = c.Dir;
        bool horizontal = Mathf.Abs(dir.x) > 0.5f;

        float half = horizontal ? size.x * 0.5f : size.y * 0.5f;
        Vector2 edge = c.Pos + dir * half;
        // A label with a forced axis is part of a deliberate composition, so its arrow
        // leaves the MIDDLE of that edge — two mirrored labels then have their arrows on
        // exactly the same line, instead of each drifting to its own target's height.
        float across = (horizontal ? size.y : size.x) * 0.5f - 22f;   // stay off the corners
        Vector2 attach;
        if (c.CenterAttach)
            attach = horizontal ? new Vector2(edge.x, c.Pos.y) : new Vector2(c.Pos.x, edge.y);
        else
            attach = horizontal
                ? new Vector2(edge.x, Mathf.Clamp(tip.y, c.Pos.y - across, c.Pos.y + across))
                : new Vector2(Mathf.Clamp(tip.x, c.Pos.x - across, c.Pos.x + across), edge.y);

        Vector2 start = attach + dir * ArrowGap;
        Vector2 end = tip - dir * c.Standoff;
        float run = Vector2.Dot(end - start, dir);

        // Sit against the label, not against the target: two mirrored labels then put
        // their arrows on exactly the same line, which is what reads as symmetry.
        float along = run > ArrowLen ? ArrowLen * 0.5f : Mathf.Max(run * 0.5f, 14f);
        along = Mathf.Min(along + c.ArrowNudge, Mathf.Max(run - ArrowLen * 0.5f, along));

        c.Head.sizeDelta = new Vector2(ArrowLen, ArrowLen * 0.77f);
        c.Head.anchoredPosition = start + dir * along;
        c.Head.localEulerAngles = new Vector3(0f, 0f, AngleOf(dir));
    }

    /// <summary>The art points +X
    /// <summary>The art points +X
    /// <summary>The art points +X, so: right 0, up 90, left 180, down 270.</summary>
    private static float AngleOf(Vector2 dir)
    {
        if (dir.x > 0.5f) return 0f;
        if (dir.x < -0.5f) return 180f;
        return dir.y > 0f ? 90f : 270f;
    }

    private bool ToLocal(Vector2 screen, out Vector2 local)
        => RectTransformUtility.ScreenPointToLocalPointInRectangle(_calloutRT, screen, null, out local);

    private Rect LocalRectOf(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = _calloutRT.InverseTransformPoint(corners[0]);
        Vector2 max = _calloutRT.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    //  target resolution 

    private static List<PlayerRef> Roster()
    {
        var list = new List<PlayerRef>();
        var reg = PlayerRegistry.Instance;
        if (reg != null)
            foreach (var pr in reg.All)
                if (pr != null) list.Add(pr);
        return list;
    }

    //  generated sprites (only the arrow head comes from Resources) 

    /// <summary>A 9-sliced rounded rectangle — filled, or just its 3px edge.</summary>
    private static Sprite RoundedSprite(bool ringOnly)
    {
        const int S = 48, R = 18;
        const float Ring = 3f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float half = S * 0.5f, straight = half - R;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - straight, 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - straight, 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);        // distance past the corner arc
                float a = ringOnly
                    ? Mathf.Clamp01(R - d) * Mathf.Clamp01(d - (R - Ring) + 1f)
                    : Mathf.Clamp01(R - d + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
    }

    /// <summary>Filled triangle pointing +X, used by the emblems.</summary>
    private static Sprite TriangleSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float t = x / (float)(S - 1);
                float halfH = (1f - t) * (S * 0.5f);
                float d = halfH - Mathf.Abs(y - (S - 1) * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(d)));
            }
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>A clean tapered arrow pointing +X, drawn at 4× supersampling so the
    /// edges are smooth, with its own dark outline baked in so it stays readable over
    /// both the snow and the dark UI. Two-tone, so it's tinted white, not violet.</summary>
    private static Sprite ArrowSprite()
    {
        const int W = 192, H = 96, SS = 4;      // 2:1, four samples per axis
        var fill = new Color(0.84f, 0.62f, 1.00f);
        var edge = new Color(0.14f, 0.05f, 0.24f);

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float inner = 0f, outer = 0f;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float u = (x + (sx + 0.5f) / SS) / W;             // 0..1 along the arrow
                        float v = (y + (sy + 0.5f) / SS) / H - 0.5f;      // -0.5..0.5 across it
                        if (InArrow(u, v, 0f)) inner += 1f;
                        if (InArrow(u, v, 0.055f)) outer += 1f;           // same shape, grown
                    }

                float ia = inner / (SS * SS), oa = outer / (SS * SS);
                var c = Color.Lerp(edge, fill, ia);
                c.a = oa;
                px[y * W + x] = c;
            }

        tex.SetPixels(px);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Arrow silhouette: a tapered shaft running into a triangular head,
    /// optionally dilated by `grow` to produce the outline pass.</summary>
    private static bool InArrow(float u, float v, float grow)
    {
        v = Mathf.Abs(v);
        float tip = 1f - 0.02f + grow;
        if (u > tip) return false;

        const float Neck = 0.56f;
        if (u >= Neck - grow)
        {
            float t = Mathf.InverseLerp(Neck - grow, tip, u);      // 0 at the neck, 1 at the point
            return v <= Mathf.Lerp(0.40f + grow, 0f, t);
        }

        if (u < 0.04f - grow) return false;                        // flat tail
        float shaft = Mathf.Lerp(0.07f, 0.13f, u / Neck) + grow;   // slight taper
        return v <= shaft;
    }

    /// <summary>Thin circular annulus.</summary>
    private static Sprite CircleRingSprite()
    {
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float c = (S - 1) * 0.5f, outer = c, inner = c - 4.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite DotSprite()
    {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(c - 0.5f - d)));
            }
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    //  default content 

    private static Row R(string key, string text, Glyph icon = Glyph.None)
        => new Row { key = key, text = text, icon = icon };

    private static Row R(string key, string text, Color accent, Glyph icon = Glyph.None)
        => new Row { key = key, text = text, keyColor = accent, icon = icon };

    /// <summary>Row with imported artwork. The glyph stays on as the fallback, so the
    /// page still reads if the sprite is ever moved out of Resources.</summary>
    private static Row Art(string key, string text, string sprite, Glyph fallback = Glyph.None)
        => new Row { key = key, text = text, iconSprite = sprite, icon = fallback };

    /// <summary>As above, for rows that also carry their own badge accent — the tether
    /// bands, where near / mid / far each have a colour of their own.</summary>
    private static Row Art(string key, string text, Color accent, string sprite, Glyph fallback = Glyph.None)
        => new Row { key = key, text = text, keyColor = accent, iconSprite = sprite, icon = fallback };

    private static List<Page> DefaultPages()
    {
        TetherColors(out Color near, out Color mid, out Color far);

        // Everything sits in the game's own range: arcane purples through to the
        // tether's cyan. No yellows, no reds, no greens.
        var magenta = new Color(0.85f, 0.45f, 1.00f, 1f);   // core / UI purple
        var periwinkle = magenta;   // building
        var orchid = magenta;       // combat
        var arcane = magenta;       // augments

        return new List<Page>
        {
            new Page {
                title = "DEFEND THE CORE",
                accent = magenta, emblem = Emblem.Core,
                rows = new List<Row> {
                    Art("Core",   "Its energy is your life - protect it",
                        TutorialIcons + "core", Glyph.Core),
                    Art("Waves",  "Clear enemy waves to reach the next stage",
                        TutorialIcons + "waves", Glyph.Wave),
                    Art("Energy", "Spend it on towers, upgrades and repairs",
                        TutorialIcons + "energy", Glyph.Energy),
                    Art("Decay",  "Towers and Core lose energy without Generators",
                        TutorialIcons + "decay", Glyph.Decay),
                },
            },
            new Page {
                title = "FIGHT AND SURVIVE",
                accent = orchid, emblem = Emblem.Blades,
                rows = new List<Row> {
                    Art("WASD  /  Stick",     "Move",
                        TutorialIcons + "wsad", Glyph.Move),
                    Art("Left Shift  /  B",   "Dash. Consumes Stamina",
                        TutorialIcons + "dashsprint", Glyph.Dash),
                    Art("Left Click  /  RT",  "Attack",
                        TutorialIcons + "attack", Glyph.Sword),
                    Art("Right Click  /  LT", "Tool: shield, mines, traps, turrets, abilities",
                        TutorialIcons + "tool", Glyph.Tool),
                    Art("Parry",              "Shield up the instant a <b>\"!\"</b> shows",
                        TutorialIcons + "parry", Glyph.Shield),
                },
            },
            new Page {
                title = "BUILD YOUR DEFENCE",
                accent = periwinkle, emblem = Emblem.Tower,
                rows = new List<Row> {
                    Art("Space  /  Y",        "Building mode",
                        TutorialIcons + "build", Glyph.Hammer),
                    Art("Left Click  /  RT",  "Build on the slot you aim at",
                        TutorialIcons + "buildTarget", Glyph.Cursor),
                    Art("Right Click  /  LT", "Upgrade or sell the Tower",
                        TutorialIcons + "upgrade", Glyph.Menu),
                },
            },
            new Page {
                title = "THE TETHER CONNECTION",
                accent = Color.Lerp(near, far, 0.5f), emblem = Emblem.Tether,
                tetherBands = true,
                rows = new List<Row> {
                    Art("Near", "Slows energy decay for connected tower", near,
                        TutorialIcons + "tether1", Glyph.Zone),
                    Art("Mid",  "+10% damage increase per connected tower", mid,
                        TutorialIcons + "tether2", Glyph.Zone),
                    Art("Far",  "+5% range increase per connected tower", far,
                        TutorialIcons + "tether3", Glyph.Zone),
                },
            },
            new Page {
                title = "AUGMENTS & BLUEPRINTS",
                accent = arcane, emblem = Emblem.Spark,
                rows = new List<Row> {
                    Art("Augments",    "Ephemeral rewards for beating waves",
                        TutorialIcons + "Augments", Glyph.Spark),
                    Art("Blueprints",  "Permanent weapon augment unlocks",
                        TutorialIcons + "Unlock", Glyph.Chest),
                },
                runBar = true,
            },
            new Page {
                title = "GAME SCREEN",
                accent = magenta,
                callouts = CalloutSet.Hud,
                intro = "       Full guide: <b>Options → Tutorial</b>.",
            },
            new Page {
                title = "ON THE FIELD",
                accent = magenta,
                callouts = CalloutSet.Combat,
                intro = "       Towers fight for you. Keep their energy up.",
            },
        };
    }
}


