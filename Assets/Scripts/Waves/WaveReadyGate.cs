using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Runtime UI for the between-wave pacing modes (see WavePacingMode):
//   ReadyUp "READY" button bottom-right 
//   Countdown → a large ticking number in the upper-centre of the screen.
// Created at runtime by GameOrchestrator (AddComponent), like the other helpers.
// The overlay canvas is built lazily on first use, so the Immediate mode (and any
// run that never reaches a wait) pays for no UI.

public class WaveReadyGate : MonoBehaviour
{
    public static WaveReadyGate Instance { get; private set; }

    [Header("Font")]
    [Tooltip("Optional TMP font for the button + countdown (assign your menu font, e.g. " +
             "Cinzel-Black SDF). Falls back to TextMeshPro's default font asset.")]
    public TMP_FontAsset overrideFont;

    [Header("Ready button layout")]
    [Tooltip("Button size in reference (1920×1080) pixels.")]
    public Vector2 buttonSize = new Vector2(240f, 78f);
    [Tooltip("Distance from the bottom-right corner, in reference pixels.")]
    public Vector2 buttonMargin = new Vector2(28f, 28f);
    public float buttonFontSize = 30f;

    [Header("Countdown")]
    public float countdownFontSize = 127.5f;
    [Tooltip("Flat colour of the countdown number. IGNORED when Countdown Use Gradient is on.")]
    public Color countdownColor = new Color(1f, 0.86f, 0.55f, 1f);

    [Tooltip("Fade the number vertically between the two colours below instead of using the " +
             "flat Countdown Color. TextMeshPro does this per-vertex, so it costs nothing.")]
    public bool countdownUseGradient = true;

    [Tooltip("Top of the countdown number's vertical gradient — the lit edge.")]
    public Color countdownGradientTop = new Color(1f, 0.94f, 1f, 1f);

    [Tooltip("Bottom of the countdown number's vertical gradient — the colour it settles into.")]
    public Color countdownGradientBottom = new Color(0.72f, 0.42f, 0.98f, 1f);
    [Tooltip("Hard offset copy behind the number. OFF by default — it fights a light " +
             "gradient, which carries its own depth. Turn ON only if the number gets lost " +
             "against bright biomes.")]
    public bool countdownShowShadow = false;
    public Color countdownShadowColor = new Color(0.18f, 0.02f, 0.28f, 0.45f);
    [Tooltip("Caption above the countdown number. EMPTY (the default) shows just the number, " +
             "which is what the countdown was designed around — the number's position is fixed " +
             "by the group, so nothing shifts either way. Set it to e.g. \"NEXT WAVE\" to bring " +
             "the label back.")]
    public string countdownCaption = "";
    public Color captionColor = new Color(0.96f, 0.80f, 0.58f, 0.9f);
    [Tooltip("How far above screen-centre the countdown number sits (reference pixels). " +
             "Higher = further up. Note the number itself sits another 70px BELOW this, " +
             "from NumberPivot's offset, so the digit lands around (countdownYOffset - 70).")]
    public float countdownYOffset = 240f;

    [Header("Canvas")]
    [Tooltip("Sorting order. Sits just below the lore scroll (9998) / archive menu (9996) " +
             "so those in-game popups still cover it, but above gameplay HUD. This canvas " +
             "lives in the HUD sort band (9990s), which is ABOVE the menu band (~4800–5100) " +
             "and the pause menu below it — so it can't be layered behind the menus by sort " +
             "order alone. Instead the whole overlay is hidden while any menu is open " +
             "(see UIModalStack.IsOpen in Update), which keeps it behind the menus and in " +
             "front of the biome without depending on the pause menu's scene-authored order.")]
    public int sortingOrder = 9994;

    [Header("Co-op input")]
    [Tooltip("Gamepad button a pad player presses to ready up. Default North (Y/Triangle) " +
             "is rarely a core combat action; change it if it clashes with your bindings.")]
    public GamepadButton gamepadReadyButton = GamepadButton.North;

    private enum UiMode { ReadyUp, Countdown }

    // ── runtime state ──
    private bool _active;
    private bool _built;
    private UiMode _uiMode;
    private Func<bool> _aborted;
    private readonly System.Collections.Generic.HashSet<int> _ready = new System.Collections.Generic.HashSet<int>();

    // ── UI refs ──
    private GameObject _canvasGO;
    private GameObject _buttonGO;
    private Button _readyButton;
    private Image _readyButtonImage;
    private TextMeshProUGUI _readyLabel;
    private TextMeshProUGUI _coopCount;
    private GameObject _countdownGroup;
    private RectTransform _numberPivot;
    private TextMeshProUGUI _numberText;
    private TextMeshProUGUI _numberShadow;

    private TMP_FontAsset _font;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── public API (called by GameOrchestrator.WaitBeforeWave) ──

    // ReadyUp mode: hold until every player readies (or isAborted() fires).
    public IEnumerator WaitForAllReady(Func<bool> isAborted)
    {
        _aborted = isAborted;
        _ready.Clear();
        _active = true;
        EnterMode(UiMode.ReadyUp);
        RefreshReadyUI();
        while (_active)
            yield return null;
    }

    // Countdown mode: show a ticking number for `seconds`, then return. Uses scaled
    // time so pacing matches the original WaitForSeconds exactly.
    public IEnumerator WaitForCountdown(float seconds)
    {
        _aborted = null;
        _active = true;
        EnterMode(UiMode.Countdown);

        float remaining = Mathf.Max(0f, seconds);
        while (_active && remaining > 0f)
        {
            if (Aborted()) { Finish(); yield break; }
            UpdateCountdownVisual(remaining);
            remaining -= Time.deltaTime;
            yield return null;
        }
        Finish();
    }

    private void Update()
    {
        if (!_active) return;

        if (Aborted()) { Finish(); return; }

        // Step behind any open menu. This canvas sits in the HUD sort band (9990s),
        // above the menu band and the pause menu, so it would otherwise draw ON TOP of
        // the pause menu opened mid-countdown. Hiding it while a menu is on the modal
        // stack keeps it behind the menus and in front of the biome, regardless of the
        // pause menu's scene-authored sort order. Gameplay is frozen while a modal is
        // up (timeScale 0), so the countdown clock doesn't advance while hidden.
        bool menuOpen = MenuCovering();
        if (_canvasGO != null) _canvasGO.SetActive(!menuOpen);
        if (menuOpen) return;   // don't poll ready-ups while a menu has focus

        if (_uiMode == UiMode.ReadyUp)
        {
            PollGamepads();
            RefreshReadyUI();
            if (_ready.Count >= RequiredReadyCount())
                Finish();
        }
        // Countdown visuals are driven by the coroutine.
    }

    // True while any menu/modal (pause menu, options, rebind, …) is on the UIModalStack.
    // The pause menu registers as a modal, so this covers the reported case of opening
    // it during the between-wave countdown.
    private static bool MenuCovering() => UIModalStack.IsOpen;

    private bool Aborted()
    {
        // Leave the moment we're no longer in the between-wave window: an explicit
        // abort predicate, a missing orchestrator, or a state change (game over, a
        // rewind tearing the run down) all mean stop showing.
        var orch = GameOrchestrator.Instance;
        return (_aborted != null && _aborted.Invoke())
               || orch == null
               || orch.CurrentState != GameOrchestrator.RunState.WaveCountdown;
    }

    private void Finish()
    {
        _active = false;
        _aborted = null;
        if (_numberPivot != null) _numberPivot.localScale = Vector3.one;
        HideCanvas();
    }



    private int RequiredReadyCount() => Mathf.Max(1, PlayerRegistry.Count);

    private void ToggleReady(int playerIndex)
    {
        if (!_active) return;
        if (!_ready.Add(playerIndex)) _ready.Remove(playerIndex); // tap again to un-ready
    }

    private void PollGamepads()
    {
        var players = PlayerInput.all;
        if (players.Count > 0)
        {
            foreach (var pi in players)
                if (pi != null && GamepadConfirmPressed(pi))
                    ToggleReady(pi.playerIndex);
        }
        else
        {
            var gp = Gamepad.current;
            if (gp != null && gp[gamepadReadyButton].wasPressedThisFrame)
                ToggleReady(0);
        }
        // Mouse / keyboard players ready by clicking the button (handled via onClick).
    }

    private bool GamepadConfirmPressed(PlayerInput pi)
    {
        foreach (var d in pi.devices)
            if (d is Gamepad gp && gp[gamepadReadyButton].wasPressedThisFrame)
                return true;
        return false;
    }

    private int MouseOwnerIndex()
    {
        var mouse = Mouse.current;
        if (mouse != null)
            foreach (var pi in PlayerInput.all)
                foreach (var d in pi.devices)
                    if (d == mouse) return pi.playerIndex;
        return 0; // single player / unknown → P0
    }



    private void EnterMode(UiMode m)
    {
        EnsureBuilt();
        _uiMode = m;
        // Show now unless a menu is already up; Update keeps this in sync thereafter.
        if (_canvasGO != null) _canvasGO.SetActive(!MenuCovering());

        bool readyup = (m == UiMode.ReadyUp);
        if (_buttonGO != null) _buttonGO.SetActive(readyup);
        if (_coopCount != null) _coopCount.gameObject.SetActive(readyup && RequiredReadyCount() > 1);
        if (_countdownGroup != null) _countdownGroup.SetActive(!readyup);
    }

    private void RefreshReadyUI()
    {
        if (_coopCount == null) return;
        int req = RequiredReadyCount();
        bool coop = req > 1;
        _coopCount.gameObject.SetActive(coop);
        if (coop)
            _coopCount.text = $"{Mathf.Min(_ready.Count, req)} / {req} ready";
    }

    private void UpdateCountdownVisual(float remaining)
    {
        int shown = Mathf.Max(1, Mathf.CeilToInt(remaining));
        string s = shown.ToString();
        if (_numberText != null) _numberText.text = s;
        if (_numberShadow != null) _numberShadow.text = s;

        // Pop when a new number appears (pos≈1) then settle to 1 as the second elapses.
        if (_numberPivot != null)
        {
            float pos = Mathf.Clamp01(remaining - Mathf.Ceil(remaining) + 1f);
            float scale = 1f + 0.22f * pos;
            _numberPivot.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void HideCanvas() { if (_canvasGO != null) _canvasGO.SetActive(false); }



    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _font = overrideFont; // null → TMP default font asset

        _canvasGO = new GameObject("WaveReadyGateCanvas");
        _canvasGO.transform.SetParent(transform, false);
        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasGO.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        BuildReadyButton();
        BuildCountdown();

        _canvasGO.SetActive(false);
    }

    private void BuildReadyButton()
    {
        // Same themed pause-menu button the ARCHIVE button uses, with the charred
        // parchment art as a fallback when the themed sprite isn't in Resources.
        Sprite normal = LoadThemed("Button", new Vector4(70, 80, 70, 80));
        Sprite hi = LoadThemed("Button_1", new Vector4(80, 90, 80, 90));
        bool themed = normal != null;
        if (normal == null) normal = LorePaperArt.MakeButtonSprite(false);
        if (hi == null) hi = LorePaperArt.MakeButtonSprite(true);

        var img = NewImage("ReadyButton", _canvasGO.transform, normal, Color.white);
        img.type = Image.Type.Sliced;
        img.raycastTarget = true;
        _readyButtonImage = img;
        _buttonGO = img.gameObject;

        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-buttonMargin.x, buttonMargin.y);
        rt.sizeDelta = buttonSize;

        _readyButton = img.gameObject.AddComponent<Button>();
        _readyButton.targetGraphic = img;
        _readyButton.transition = Selectable.Transition.SpriteSwap;
        _readyButton.spriteState = new SpriteState
        {
            highlightedSprite = hi,
            pressedSprite = hi,
            selectedSprite = normal,
        };
        _readyButton.onClick.AddListener(() => ToggleReady(MouseOwnerIndex()));

        // Themed art reads best with light ink; parchment fallback reads best with dark ink.
        Color textColor = themed ? new Color(0.97f, 0.93f, 0.82f, 1f)
                                 : new Color(0.20f, 0.12f, 0.05f, 1f);
        _readyLabel = NewText("Label", rt, "READY", buttonFontSize, FontStyles.Bold,
                              TextAlignmentOptions.Center, textColor);
        StretchFull(_readyLabel.rectTransform);

        // Small "n / m ready" counter just above the button (co-op only).
        _coopCount = NewText("CoopCount", _canvasGO.transform, "", 24f, FontStyles.Bold,
                             TextAlignmentOptions.Right, new Color(0.97f, 0.93f, 0.82f, 0.92f));
        var cr = _coopCount.rectTransform;
        cr.anchorMin = cr.anchorMax = new Vector2(1f, 0f);
        cr.pivot = new Vector2(1f, 0f);
        cr.anchoredPosition = new Vector2(-buttonMargin.x - 6f, buttonMargin.y + buttonSize.y + 8f);
        cr.sizeDelta = new Vector2(360f, 40f);
        _coopCount.gameObject.SetActive(false);
    }

    private void BuildCountdown()
    {
        _countdownGroup = new GameObject("Countdown", typeof(RectTransform));
        _countdownGroup.transform.SetParent(_canvasGO.transform, false);
        var gr = (RectTransform)_countdownGroup.transform;
        gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.5f);
        gr.pivot = new Vector2(0.5f, 0.5f);
        gr.anchoredPosition = new Vector2(0f, countdownYOffset);
        gr.sizeDelta = new Vector2(620f, 360f);

        // Caption is opt-in. Skipped entirely when blank rather than created-and-hidden,
        // so the default costs no GameObject and no TMP submesh.
        if (!string.IsNullOrWhiteSpace(countdownCaption))
        {
            var caption = NewText("Caption", gr, countdownCaption, 34f, FontStyles.Bold,
                                  TextAlignmentOptions.Center, captionColor);
            caption.characterSpacing = 12f;
            var capRt = caption.rectTransform;
            capRt.anchorMin = new Vector2(0.5f, 1f); capRt.anchorMax = new Vector2(0.5f, 1f);
            capRt.pivot = new Vector2(0.5f, 1f);
            capRt.anchoredPosition = new Vector2(0f, 0f);
            capRt.sizeDelta = new Vector2(620f, 56f);
        }

        // Pivot we scale for the "pop", containing a shadow + the number.
        var pivotGO = new GameObject("NumberPivot", typeof(RectTransform));
        pivotGO.transform.SetParent(gr, false);
        _numberPivot = (RectTransform)pivotGO.transform;
        _numberPivot.anchorMin = _numberPivot.anchorMax = new Vector2(0.5f, 0.5f);
        _numberPivot.pivot = new Vector2(0.5f, 0.5f);
        _numberPivot.anchoredPosition = new Vector2(0f, -70f);
        _numberPivot.sizeDelta = new Vector2(400f, 240f);

        // Skipped entirely when off, rather than created transparent — one less TMP
        // submesh per frame. UpdateCountdownVisual already null-checks it.
        if (countdownShowShadow)
        {
            _numberShadow = NewText("Shadow", _numberPivot, "3", countdownFontSize, FontStyles.Bold,
                                    TextAlignmentOptions.Center, countdownShadowColor);
            var sr = _numberShadow.rectTransform;
            StretchFull(sr);
            sr.anchoredPosition = new Vector2(5f, -6f);
        }

        _numberText = NewText("Number", _numberPivot, "3", countdownFontSize, FontStyles.Bold,
                              TextAlignmentOptions.Center, countdownColor);
        StretchFull(_numberText.rectTransform);

        if (countdownUseGradient)
        {
            // TMP MULTIPLIES the vertex gradient by .color, so the face has to be white
            // or the gradient comes out tinted by countdownColor on top of itself.
            _numberText.color = Color.white;
            _numberText.enableVertexGradient = true;
            _numberText.colorGradient = new VertexGradient(
                countdownGradientTop, countdownGradientTop,
                countdownGradientBottom, countdownGradientBottom);
        }

        _countdownGroup.SetActive(false);
    }

    //  themed-sprite loading (mirrors LoreArchiveMenu.Themed/WithBorder) 
    private static Sprite LoadThemed(string resourceName, Vector4 border)
    {
        const string folder = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/";
        Sprite src = null;
        var all = Resources.LoadAll<Sprite>(folder + resourceName);
        if (all != null && all.Length > 0) src = all[0];
        if (src == null) src = Resources.Load<Sprite>(folder + resourceName);
        if (src == null) return null;
        try
        {
            float ppu = src.pixelsPerUnit > 0 ? src.pixelsPerUnit : 100f;
            return Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), ppu, 0,
                                 SpriteMeshType.FullRect, border);
        }
        catch { return src; }
    }

    //  UI helpers (mirror the lore menus) 
    private Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        return img;
    }

    private TextMeshProUGUI NewText(string name, Transform parent, string content, float size,
                                    FontStyles style, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) txt.font = _font; // else TMP default
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.alignment = align;
        txt.color = color;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.richText = false;
        txt.raycastTarget = false;
        return txt;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
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
}


