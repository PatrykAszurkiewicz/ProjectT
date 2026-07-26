using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// Per-player UPGRADE / DISASSEMBLE / CANCEL popup for a placed tower.
// Opened by PlayerTowerPlacer when this player presses the TOOL button (Right Mouse /
// Left Trigger) while aiming at a tower in placement mode. Buttons are built at runtime
// from the supplied HUD art (a panel + a button sprite, scaled to fit):
//   Upgrade tower (-upgradeCost):  +20% primary output and +20% health (Tower.ApplyUpgrade)
//   Disassemble   (+destroyRefund): removes the tower
//   Cancel:        close without doing anything
// Rendered on a Screen Space - Camera canvas bound to THIS player's camera, so in co-op
// split-screen it only shows on that player's half.
[DisallowMultipleComponent]
public class TowerActionMenu : MonoBehaviour
{
    const int BTN_UPGRADE = 0;
    const int BTN_DISASSEMBLE = 1;
    const int BTN_CANCEL = 2;
    const int ButtonCount = 3;

    [Header("Art (Resources paths, no extension)")]
    [Tooltip("Background panel sprite. Default points at the supplied HUD art.")]
    public string panelSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/MenuPanel 1";
    [Tooltip("Button sprite. Default points at the supplied HUD art.")]
    public string buttonSpritePath = "Sprites/HUD/PauseMenu/PauseMenuMiddlePanel/Button 1";

    [Header("Economy")]
    [Tooltip("Credits spent to upgrade (+20% primary output, +20% health).")]
    public int upgradeCost = 50;
    [Tooltip("Credits refunded when the tower is disassembled.")]
    public int destroyRefund = 50;

    [Header("Layout (1080p reference pixels)")]
    [Tooltip("Button width in reference px. The panel auto-sizes to wrap the buttons.")]
    public float buttonWidth = 400f;
    [Tooltip("Vertical gap between buttons (reference px).")]
    public float buttonGap = 20f;
    [Tooltip("Padding between the buttons and the panel edge (reference px).")]
    public float panelPaddingX = 58f;
    public float panelPaddingY = 52f;
    [Tooltip("Button label size (reference px). Auto-shrinks if a label would overflow.")]
    public float labelFontSize = 38f;

    // Selection is conveyed by BRIGHTNESS (which survives placement-mode desaturation),
    // not hue: the selected button is bright with a white halo + pop; others are dimmed.
    static readonly Color BtnSelected = Color.white;
    static readonly Color BtnIdle = new Color(0.42f, 0.42f, 0.42f, 1f);
    static readonly Color BtnDisabled = new Color(0.28f, 0.28f, 0.28f, 1f);
    static readonly Color HaloColor = new Color(1f, 1f, 1f, 0.95f); // white stays bright when desaturated
    const float SelectedPop = 1.07f;

    // Bindings (per player)
    private PlayerAim _aim;
    private InputAction _confirmBuild;   // Build (LMB / Right Trigger) — confirm
    private InputAction _cancelTool;     // Tool  (RMB / Left Trigger)  — opens AND cancels
    private PlayerRef _owner;
    private Camera _cam;
    private int _openedFrame = -1;       // ignore button input on the frame we opened
    private bool _navLatched;            // gamepad stick step debounce
    private float _navRepeatTimer;       // gamepad held-stick auto-repeat countdown

    // UI
    private Canvas _canvas;
    private CanvasScaler _scaler;
    private RectTransform _panelRT;
    private Image _panelImg;
    private RectTransform[] _btnRT = new RectTransform[ButtonCount];
    private Image[] _btnImg = new Image[ButtonCount];
    private RectTransform _selectorRT;   // bright halo behind the selected button
    private Image _selectorImg;
    private TextMeshProUGUI[] _btnText = new TextMeshProUGUI[ButtonCount];
    private Sprite _whiteSprite;
    private Sprite _haloSprite;

    private Tower _tower;
    private int _hover = 0;
    private bool _isOpen;
    private Coroutine _denyFlash;

    public bool IsOpen => _isOpen;

    public void Configure(PlayerAim aim, InputAction confirmBuild, InputAction cancelTool, PlayerRef owner)
    {
        _aim = aim;
        _confirmBuild = confirmBuild;
        _cancelTool = cancelTool;
        _owner = owner;
    }

    /// Show the menu for <paramref name="tower"/>, rendered on <paramref name="cam"/>.
    public void Open(Tower tower, Camera cam)
    {
        if (tower == null) return;
        _tower = tower;
        _cam = cam;

        EnsureUI();
        if (_canvas == null) return;

        // Bind to this player's viewport when we have a camera (split-screen safe);
        // otherwise fall back to a full-screen overlay so the popup still shows.
        if (_cam != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _cam;
            _canvas.planeDistance = 1f;
        }
        else
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        _hover = 0;
        _navLatched = false;
        _navRepeatTimer = 0f;
        _isOpen = true;
        _openedFrame = Time.frameCount;    // the press that opened us must not also act
        _canvas.gameObject.SetActive(true);

        RefreshLabels();
        ApplyHoverVisuals();
        StartCoroutine(PopIn());
    }

    public void Close()
    {
        _isOpen = false;
        _tower = null;
        if (_denyFlash != null) { StopCoroutine(_denyFlash); _denyFlash = null; }
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_isOpen) return;

        // Tower vanished (destroyed by enemies, or by the other co-op player) — bail.
        if (_tower == null || _tower.IsDestroyed()) { Close(); return; }

        // Selection.
        if (_aim != null && _aim.UsingGamepad)
        {
            // Use the RAW look input (returns to ~0 on release), not the sticky
            // normalized Direction — so small up/down flicks step immediately and
            // re-arm the moment the stick comes back to centre. Holding the stick
            // auto-repeats after a short delay.
            float y = _aim.LookInput.y;
            float mag = Mathf.Abs(y);
            const float engage = 0.30f;   // small push to step
            const float release = 0.12f;  // back below this to re-arm
            const float firstDelay = 0.30f, repeatDelay = 0.11f;

            if (mag < release)
            {
                _navLatched = false;
                _navRepeatTimer = 0f;
            }
            else if (mag > engage)
            {
                int dir = y > 0f ? -1 : 1;   // stick up → previous (Upgrade), down → next
                if (!_navLatched)
                {
                    SetHover(_hover + dir);
                    _navLatched = true;
                    _navRepeatTimer = firstDelay;
                }
                else
                {
                    _navRepeatTimer -= Time.unscaledDeltaTime;
                    if (_navRepeatTimer <= 0f) { SetHover(_hover + dir); _navRepeatTimer = repeatDelay; }
                }
            }
        }
        else
        {
            int nearest = NearestButtonToMouse();
            if (nearest >= 0) SetHover(nearest);
        }

        // Keep affordability/maxed state current while open (drops can change the wallet).
        ApplyHoverVisuals();

        // Don't act on the frame we opened (the tool press that spawned us).
        if (Time.frameCount == _openedFrame) return;

        // Tool button = safe CANCEL (never triggers an action by accident).
        if (_cancelTool != null && _cancelTool.WasPressedThisFrame()) { Close(); return; }

        // Build button = CONFIRM the highlighted button.
        if (_confirmBuild != null && _confirmBuild.WasPressedThisFrame())
            Activate(_hover);
    }

    private void SetHover(int idx)
    {
        idx = Mathf.Clamp(idx, 0, ButtonCount - 1);
        if (idx == _hover) return;
        _hover = idx;
        ApplyHoverVisuals();
    }

    // The button whose screen-space centre is nearest the cursor. Drives selection
    // from cursor MOVEMENT rather than requiring the cursor to be inside a button.
    private int NearestButtonToMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null) return -1;
        Vector2 sp = mouse.position.ReadValue();

        int best = -1;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < ButtonCount; i++)
        {
            if (_btnRT[i] == null) continue;
            Vector2 c = RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, _btnRT[i].position);
            float d = (c - sp).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }
        return best;
    }

    private bool UpgradeEnabled()
    {
        if (_tower == null || !_tower.CanStatUpgrade) return false;  // honours Can Upgrade + max level
        var em = EnergyManager.Instance;
        return em == null || em.CanPlayerAfford(upgradeCost);
    }

    private void ApplyHoverVisuals()
    {
        bool upOk = UpgradeEnabled();
        for (int i = 0; i < ButtonCount; i++)
        {
            if (_btnImg[i] == null) continue;
            bool disabled = (i == BTN_UPGRADE) && !upOk;
            bool selected = (i == _hover) && !disabled;

            _btnImg[i].color = disabled ? BtnDisabled : (selected ? BtnSelected : BtnIdle);
            if (_btnRT[i] != null)
                _btnRT[i].localScale = selected ? Vector3.one * SelectedPop : Vector3.one;
            if (_btnText[i] != null)
                _btnText[i].color = selected ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f);
        }

        // Bright white halo behind the selected (enabled) button — reads clearly even
        // through the greyscale placement-mode filter.
        if (_selectorRT != null && _selectorImg != null)
        {
            bool selDisabled = (_hover == BTN_UPGRADE) && !upOk;
            if (selDisabled || _btnRT[_hover] == null)
            {
                _selectorImg.enabled = false;
            }
            else
            {
                _selectorImg.enabled = true;
                _selectorRT.anchoredPosition = _btnRT[_hover].anchoredPosition;
                _selectorRT.sizeDelta = _btnRT[_hover].sizeDelta + new Vector2(26f, 26f);
            }
        }
    }

    private void RefreshLabels()
    {
        if (_btnText[BTN_UPGRADE] != null)
        {
            string sub = (_tower != null && !_tower.canUpgrade) ? "LOCKED"
                       : (_tower != null && _tower.IsAtMaxUpgrade) ? "MAX LEVEL"
                       : $"-{upgradeCost}";
            _btnText[BTN_UPGRADE].text = $"UPGRADE\n<size=65%>{sub}</size>";
        }
        if (_btnText[BTN_DISASSEMBLE] != null)
            _btnText[BTN_DISASSEMBLE].text = $"DISASSEMBLE\n<size=65%>+{destroyRefund}</size>";
        if (_btnText[BTN_CANCEL] != null)
            _btnText[BTN_CANCEL].text = "CANCEL";
    }

    private void Activate(int idx)
    {
        var em = EnergyManager.Instance;

        if (idx == BTN_UPGRADE)
        {
            if (_tower == null || !_tower.CanStatUpgrade) { Deny(BTN_UPGRADE); return; }
            if (em != null && !em.CanPlayerAfford(upgradeCost)) { Deny(BTN_UPGRADE); return; }

            if (em != null) em.TrySpendPlayerEnergy(upgradeCost);
            _tower.ApplyUpgrade();   // +20% output (live) and +20% health; persists via upgradeLevel
            PlayBuildSound(_tower.transform.position);
            Close();
        }
        else if (idx == BTN_DISASSEMBLE)
        {
            if (_tower == null) { Close(); return; }

            Vector3 at = _tower.transform.position;
            var slot = _tower.GetComponentInParent<TowerSlot>();

            // Fixed refund only — pass refundSellValue:false so the slot's own sell
            // refund doesn't stack on top of our advertised amount.
            if (em != null) em.GivePlayerEnergy(destroyRefund);
            if (slot != null) slot.RemoveTower(false);
            else Destroy(_tower.gameObject);

            PlayBuildSound(at);
            Close();
        }
        else // BTN_CANCEL
        {
            Close();
        }
    }

    private void Deny(int idx)
    {
        if (_denyFlash != null) StopCoroutine(_denyFlash);
        _denyFlash = StartCoroutine(DenyFlash(idx));
    }

    private IEnumerator DenyFlash(int idx)
    {
        if (_btnImg[idx] == null) yield break;
        Color baseCol = _btnImg[idx].color;
        for (int i = 0; i < 3; i++)
        {
            _btnImg[idx].color = new Color(1f, 0.3f, 0.3f, 1f);
            yield return new WaitForSecondsRealtime(0.08f);
            _btnImg[idx].color = baseCol;
            yield return new WaitForSecondsRealtime(0.08f);
        }
        _denyFlash = null;
        ApplyHoverVisuals();
    }

    private void PlayBuildSound(Vector3 at)
    {
        // Upgrade and disassemble share the unified tower-placement sound.
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.towerPlacement.IsNull)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerPlacement, at);
    }

    private IEnumerator PopIn()
    {
        if (_panelRT == null) yield break;
        float dur = 0.12f, t = 0f;
        Vector3 from = Vector3.one * 0.85f, to = Vector3.one;
        _panelRT.localScale = from;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = 1f - (1f - k) * (1f - k);
            _panelRT.localScale = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }
        _panelRT.localScale = to;
    }

    //  UI construction (runtime, no prefab) 

    private void EnsureUI()
    {
        if (_canvas != null) return;

        Sprite panelSprite = Resources.Load<Sprite>(panelSpritePath);
        Sprite buttonSprite = Resources.Load<Sprite>(buttonSpritePath);
        if (panelSprite == null)
            Debug.LogWarning($"[TowerActionMenu] Panel sprite not found at Resources/{panelSpritePath} " +
                             "— using a plain fallback. Ensure 'MenuPanel 1.PNG' is under a Resources/ " +
                             "folder and imported as Sprite (2D and UI).");
        if (buttonSprite == null)
            Debug.LogWarning($"[TowerActionMenu] Button sprite not found at Resources/{buttonSpritePath} " +
                             "— using a plain fallback. Ensure 'Button 1.PNG' is under a Resources/ folder " +
                             "and imported as Sprite (2D and UI).");

        // Canvas — screen space, bound to this player's camera (split-screen safe).
        var canvasGO = new GameObject($"TowerActionMenuCanvas_P{(_owner != null ? _owner.PlayerIndex : 0)}");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _cam;
        _canvas.planeDistance = 1f;
        _canvas.sortingOrder = 32750;

        _scaler = canvasGO.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920f, 1080f);
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 1f;   // match height → consistent size in split-screen
        canvasGO.AddComponent<GraphicRaycaster>();

        // Button size from the button art's aspect; panel auto-sizes to WRAP the stack.
        float btnAspect = SpriteAspect(buttonSprite, 2.52f);   // w/h fallback ≈ 648/257
        float btnW = buttonWidth;
        float btnH = btnW / Mathf.Max(0.01f, btnAspect);

        float stackH = ButtonCount * btnH + (ButtonCount - 1) * buttonGap;
        float panelW = btnW + panelPaddingX * 2f;
        float panelH = stackH + panelPaddingY * 2f;

        // Panel (centered) — stretched to the fitted size (NOT aspect-locked) so it
        // hugs the buttons.
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        _panelImg = panelGO.AddComponent<Image>();
        _panelImg.raycastTarget = false;
        _panelImg.preserveAspect = false;
        _panelImg.type = Image.Type.Simple;
        if (panelSprite != null) _panelImg.sprite = panelSprite;
        else { _panelImg.sprite = WhiteSprite(); _panelImg.color = new Color(0.10f, 0.10f, 0.12f, 0.97f); }
        _panelRT = _panelImg.rectTransform;
        _panelRT.anchorMin = _panelRT.anchorMax = _panelRT.pivot = new Vector2(0.5f, 0.5f);
        _panelRT.anchoredPosition = Vector2.zero;
        _panelRT.sizeDelta = new Vector2(panelW, panelH);

        // Bright selection halo (added BEFORE the buttons so it renders behind them).
        var selGO = new GameObject("Selector");
        selGO.transform.SetParent(_panelRT, false);
        _selectorImg = selGO.AddComponent<Image>();
        _selectorImg.raycastTarget = false;
        _selectorImg.sprite = HaloSprite();
        _selectorImg.type = Image.Type.Simple;
        _selectorImg.color = HaloColor;
        _selectorRT = _selectorImg.rectTransform;
        _selectorRT.anchorMin = _selectorRT.anchorMax = _selectorRT.pivot = new Vector2(0.5f, 0.5f);

        // Buttons, stacked top-to-bottom and centered in the panel.
        float topY = (stackH - btnH) * 0.5f;
        for (int i = 0; i < ButtonCount; i++)
        {
            string nm = i == BTN_UPGRADE ? "UpgradeButton" : (i == BTN_DISASSEMBLE ? "DisassembleButton" : "CancelButton");
            var bGO = new GameObject(nm);
            bGO.transform.SetParent(_panelRT, false);
            _btnImg[i] = bGO.AddComponent<Image>();
            _btnImg[i].preserveAspect = true;
            _btnImg[i].raycastTarget = true;
            if (buttonSprite != null) _btnImg[i].sprite = buttonSprite;
            else { _btnImg[i].sprite = WhiteSprite(); _btnImg[i].color = new Color(0.2f, 0.17f, 0.26f, 0.98f); }

            _btnRT[i] = _btnImg[i].rectTransform;
            _btnRT[i].anchorMin = _btnRT[i].anchorMax = _btnRT[i].pivot = new Vector2(0.5f, 0.5f);
            _btnRT[i].sizeDelta = new Vector2(btnW, btnH);
            _btnRT[i].anchoredPosition = new Vector2(0f, topY - i * (btnH + buttonGap));

            _btnText[i] = MakeText(_btnRT[i], "", labelFontSize, FontStyles.Bold,
                                   new Vector2(btnW * 0.94f, btnH * 0.92f), Vector2.zero);
        }

        canvasGO.SetActive(false);
    }

    private TextMeshProUGUI MakeText(RectTransform parent, string txt, float size,
                                     FontStyles style, Vector2 sizeDelta, Vector2 pos)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = txt;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        // Auto-size up to the requested size, shrinking only if a long label (e.g.
        // "DISASSEMBLE") would otherwise overflow — so it stays as large as it fits.
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = size * 0.5f;
        // Subtle dark outline so labels stay legible over the art.
        t.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        t.outlineWidth = 0.18f;

        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = pos;
        return t;
    }

    private static float SpriteAspect(Sprite s, float fallback)
    {
        if (s == null) return fallback;
        float h = s.rect.height;
        return h > 0.01f ? s.rect.width / h : fallback;
    }

    private Sprite WhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        var tex = new Texture2D(4, 4);
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), Vector2.one * 0.5f, 100f);
        return _whiteSprite;
    }

    // Soft-edged white rounded rectangle: opaque core fading to transparent at the
    // edges, so behind the (slightly smaller) button it reads as a glowing white rim.
    // Achromatic, so it stays bright under the placement-mode desaturation.
    private Sprite HaloSprite()
    {
        if (_haloSprite != null) return _haloSprite;

        int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        float feather = 16f;     // soft edge width (px)
        float radius = 22f;      // corner radius (px)
        Vector2 ext = new Vector2(size * 0.5f - 1f, size * 0.5f - 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(Mathf.Abs(x - size * 0.5f + 0.5f), Mathf.Abs(y - size * 0.5f + 0.5f));
                Vector2 q = p - (ext - new Vector2(radius, radius));
                float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                                + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float a = Mathf.Clamp01(1f - (outside + feather) / feather);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px); tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        _haloSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
        return _haloSprite;
    }

    void OnDisable() { _isOpen = false; }

    void OnDestroy()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
    }
}
