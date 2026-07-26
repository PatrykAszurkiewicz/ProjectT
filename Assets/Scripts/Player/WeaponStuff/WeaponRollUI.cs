using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WeaponRollUI : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 bottomLeftPadding = new Vector2(70f, 150f);
    public float circleSize = 153f;
    public float gapBetweenRolls = 16f;
    public float rollStep = 85f;
    public float shrinkPerStep = 0.80f;

    [Header("Ring Frame Sprites")]
    [Tooltip("Resources path to the ACTIVE (equipped) ring frame sprite.")]
    public string activeRingPath = "Sprites/HUD/WeaponActive/Weapon active";
    [Tooltip("Resources path to the INACTIVE (not-equipped) ring frame sprite.")]
    public string inactiveRingPath = "Sprites/HUD/WeaponActive/WeaponNotActive";
    [Tooltip("If a ring sprite fails to load, fall back to the old procedural " +
             "tinted circle so the hotbar still renders.")]
    public bool ringFallbackToCircle = true;

    [Header("Colours")]
    // Ring frames now carry the active/inactive look themselves, so the BG is
    // no longer colour-tinted — these two are kept only for the procedural
    // fallback when a ring sprite is missing.
    public Color weaponActiveBg = new Color(0.18f, 0.54f, 0.44f, 1f);
    public Color toolActiveBg = new Color(0.44f, 0.28f, 0.58f, 1f);
    public Color inactiveBg = new Color(0.10f, 0.10f, 0.10f, 0.88f);
    public Color activeIcon = Color.white;
    [Tooltip("Tint for icons in NON-equipped slots. A mid-grey reads as " +
             "'greyed out' — the weapon art is dimmed and desaturated, not " +
             "just faded.")]
    public Color inactiveIcon = new Color(0.42f, 0.42f, 0.46f, 0.85f);
    public Color emptyToolBg = new Color(0.15f, 0.15f, 0.15f, 0.55f);

    [Header("Animation")]
    public float scrollSpeed = 0.16f;
    public float popSpeed = 0.30f;

    [Header("Gauge Overlay (cooldown / fuel)")]
    // Near-opaque LIGHT grey
    static readonly Color gaugeDimTint = new Color(0.66f, 0.67f, 0.72f, 0.6f);
    // Cool blue-grey for the ACTIVE-EFFECT clock — semi-transparent so the
    // running effect's icon shows through while the wedge tracks time left.
    static readonly Color gaugeActiveTint = new Color(0.42f, 0.54f, 0.80f, 0.62f);
    // Soft glow pulsed over the icon when a cooldown completes / fuel refills.
    static readonly Color gaugeReadyFlash = new Color(0.70f, 0.95f, 1f, 0.85f);
    // Warning tint for very low flamethrower fuel (mid red, stays visible).
    static readonly Color gaugeLowFuelColor = new Color(0.86f, 0.34f, 0.26f, 0.96f);

    [Tooltip("How long the ready flash lasts, in seconds.")]
    public float gaugeReadyFlashDuration = 0.4f;
    [Tooltip("Below this fuel fraction the flamethrower gauge tints red as a " +
             "low-fuel warning.")]
    [Range(0f, 1f)] public float gaugeLowFuelWarn = 0.22f;
    [Tooltip("Caps how far the low-fuel tint blends toward the warning colour. " +
             "1 = full red at empty; ~0.6 keeps it a muted reddish grey.")]
    [Range(0f, 1f)] public float gaugeLowFuelMaxBlend = 0.6f;

    WeaponRollController _ctrl;
    PlayerRef _playerRef;
    Canvas _canvas;
    Weapon _weapon; // resolved lazily; used for live gauge state

    RectTransform _weaponRoot;
    readonly List<RectTransform> _weaponRects = new List<RectTransform>();
    readonly List<Image> _weaponBgs = new List<Image>();
    readonly List<Image> _weaponIcons = new List<Image>();
    // Per-slot gauge: a grey "uncharged" mask (a vertical-filled copy of the icon)
    readonly List<Image> _weaponGaugeMasks = new List<Image>();

    RectTransform _toolRoot;
    readonly List<RectTransform> _toolRects = new List<RectTransform>();
    readonly List<Image> _toolBgs = new List<Image>();
    readonly List<Image> _toolIcons = new List<Image>();
    readonly List<Image> _toolGaugeMasks = new List<Image>();

    RectTransform _emptyToolRect;
    Image _emptyToolBg;

    Coroutine _weaponScrollCo;
    Coroutine _toolScrollCo;

    const int VisibleRadius = 2;

    //  Gauge state 
    // Tracks the last-seen readiness per equipped slot so we can detect the
    // 0.99→1.0 transition and flash the icon. Keyed by "W"/"T" + a bool isn't
    // enough — we track one value per roll since only one item per roll is
    // equipped at a time.
    float _lastWeaponGaugeValue = 1f;
    float _lastToolGaugeValue = 1f;
    float _weaponReadyFlashTimer = 0f;
    float _toolReadyFlashTimer = 0f;
    // Smoothed display values — the raw cooldown/fuel can tick unevenly frame
    // to frame; we ease the rendered progress toward it so the fill-line
    // glides instead of stepping.
    float _weaponGaugeDisplay = 1f;
    float _toolGaugeDisplay = 1f;
    // Equipped indices last seen — when these change the gauge belongs to a
    // different item, so the smoothed display snaps instead of animating.
    int _lastWeaponSel = -1;
    int _lastToolSel = -1;
    // Last-seen tool gauge phase — a phase change (clock↔fill) also snaps the
    // smoothed display, since the two styles don't share a continuous value.
    Weapon.ToolGaugePhase _lastToolPhase = Weapon.ToolGaugePhase.Ready;
    [Tooltip("How fast the gauge eases toward its target (higher = snappier).")]
    public float gaugeSmoothing = 12f;

    void Start() => StartCoroutine(Init());

    IEnumerator Init()
    {
        yield return null;
        _playerRef = GetComponentInParent<PlayerRef>();
        // Bind to THIS player's controller (sibling). Fallback to scene search
        // for the single-player / legacy layout.
        _ctrl = GetComponent<WeaponRollController>();
        if (_ctrl == null) _ctrl = FindFirstObjectByType<WeaponRollController>();
        if (_ctrl == null) yield break;
        BuildCanvas();
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged += OnUnlocks;
        RebuildAll();
        SnapAll();
    }

    void OnDestroy()
    {
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged -= OnUnlocks;
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    /// <summary>
    /// Show/hide this player's weapon-roll hotbar. Toggles the whole roll canvas so
    /// nothing under it renders. Used to hide the hotbar on the Win/Lose screen.
    /// </summary>
    public void SetHudVisible(bool visible)
    {
        if (_canvas != null) _canvas.enabled = visible;
    }

    /// <summary>True if the hotbar canvas exists and is currently shown. Lets callers
    /// (e.g. the boss-intro cinematic) capture the prior state and restore it exactly,
    /// instead of force-revealing a hotbar that was intentionally hidden (e.g. a downed player).</summary>
    public bool HudVisible => _canvas != null && _canvas.enabled;

    void BuildCanvas()
    {
        int idx = _playerRef != null ? _playerRef.PlayerIndex : 0;
        var go = new GameObject($"WeaponRoll_Canvas_P{idx}");
        _canvas = go.AddComponent<Canvas>();

        // Co-op: keep ScreenSpaceOverlay (always on top of the world, so the
        // grass can't draw over the hotbar and menus behave as before). To put
        // each player's hotbar in its own half we offset the roots by the
        // owning camera's viewport rect (see PositionRootsForViewport). Single
        // player has a full-screen camera, so the offset is zero — unchanged.
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();

        // Weapon root: active icon at Y=0 (top of weapon roll), inactive go DOWN
        var wGo = new GameObject("WeaponRoot", typeof(RectTransform));
        wGo.transform.SetParent(_canvas.transform, false);
        _weaponRoot = wGo.GetComponent<RectTransform>();
        _weaponRoot.anchorMin = _weaponRoot.anchorMax = _weaponRoot.pivot = Vector2.zero;
        // base (set every frame in PositionRootsForViewport with the viewport offset)
        _weaponRoot.anchoredPosition = bottomLeftPadding;

        // Tool root: active icon at Y=0 (bottom of tool roll), inactive go UP
        var tGo = new GameObject("ToolRoot", typeof(RectTransform));
        tGo.transform.SetParent(_canvas.transform, false);
        _toolRoot = tGo.GetComponent<RectTransform>();
        _toolRoot.anchorMin = _toolRoot.anchorMax = _toolRoot.pivot = Vector2.zero;
        _toolRoot.anchoredPosition = new Vector2(
            bottomLeftPadding.x,
            bottomLeftPadding.y + circleSize + gapBetweenRolls
        );

        PositionRootsForViewport();
    }

    // Co-op: shift both roots into the owning player's split-screen half using
    // the camera's pixel viewport. Single player (full-screen camera) → zero
    // offset, so the hotbar stays exactly where it was. Recomputed each frame
    // so it survives window/viewport resizes.
    void PositionRootsForViewport()
    {
        if (_weaponRoot == null || _toolRoot == null) return;

        Vector2 off = Vector2.zero;
        Camera cam = _playerRef != null ? _playerRef.Camera : null;
        if (cam != null)
        {
            Rect pr = cam.pixelRect;
            off = new Vector2(pr.xMin, pr.yMin);
        }

        _weaponRoot.anchoredPosition = bottomLeftPadding + off;
        _toolRoot.anchoredPosition = new Vector2(
            bottomLeftPadding.x,
            bottomLeftPadding.y + circleSize + gapBetweenRolls
        ) + off;
    }

    //  REFRESH
    public void Refresh(int weaponIndex, int toolIndex, ScrollTarget scrollTarget)
    {
        if (_canvas == null || _ctrl == null) return;

        if (_weaponRects.Count != _ctrl.WeaponCount || _toolRects.Count != _ctrl.ToolCount)
            RebuildAll();

        UpdateWeaponVisuals();
        UpdateToolVisuals();

        if (scrollTarget == ScrollTarget.Weapon)
        {
            StartWeaponScrollAnim();
            SnapToolPositions();
        }
        else if (scrollTarget == ScrollTarget.Tool)
        {
            SnapWeaponPositions();
            StartToolScrollAnim();
        }
        else
        {
            SnapAll();
        }
    }

    public void Refresh(int newIndex, bool animate)
    {
        int toolIdx = _ctrl != null ? _ctrl.CurrentToolIndex : 0;
        Refresh(newIndex, toolIdx, animate ? ScrollTarget.Weapon : ScrollTarget.None);
    }

    void OnUnlocks()
    {
        if (_canvas == null) return;
        int bW = _weaponRects.Count, bT = _toolRects.Count;
        RebuildAll();
        SnapAll();
        for (int i = bW; i < _weaponRects.Count; i++) StartCoroutine(PopRect(_weaponRects[i]));
        for (int i = bT; i < _toolRects.Count; i++) StartCoroutine(PopRect(_toolRects[i]));
    }

    //  GAUGE OVERLAY (cooldown / fuel) — driven every frame
    void Update()
    {
        if (_canvas == null || _ctrl == null) return;

        // Resolve THIS player's Weapon lazily (sibling under the player).
        if (_weapon == null)
            _weapon = _playerRef != null ? _playerRef.GetComponentInChildren<Weapon>()
                                         : FindFirstObjectByType<Weapon>();

        PositionRootsForViewport();
        UpdateGauges();
    }

    void UpdateGauges()
    {
        // Tick down the ready-flash timers regardless.
        if (_weaponReadyFlashTimer > 0f) _weaponReadyFlashTimer -= Time.unscaledDeltaTime;
        if (_toolReadyFlashTimer > 0f) _toolReadyFlashTimer -= Time.unscaledDeltaTime;

        UpdateWeaponFuelGauge();
        UpdateToolCooldownGauge();
    }

    void UpdateWeaponFuelGauge()
    {
        int sel = _ctrl.CurrentWeaponIndex;
        float fuel = 1f;
        bool haveFuel = _weapon != null && _weapon.TryGetWeaponFuel(out fuel);
        if (!haveFuel) fuel = 1f;

        // Ready-flash when fuel climbs back to full (after a refuel).
        if (haveFuel && _lastWeaponGaugeValue < 0.999f && fuel >= 0.999f)
            _weaponReadyFlashTimer = gaugeReadyFlashDuration;
        _lastWeaponGaugeValue = haveFuel ? fuel : 1f;

        // Ease the displayed value toward the real one. On an equipped-weapon
        // change the gauge is now a different item — snap, don't animate.
        float target = haveFuel ? fuel : 1f;
        if (sel != _lastWeaponSel)
        {
            _weaponGaugeDisplay = target;
            _lastWeaponSel = sel;
        }
        else
        {
            float k = 1f - Mathf.Exp(-gaugeSmoothing * Time.unscaledDeltaTime);
            _weaponGaugeDisplay = Mathf.Lerp(_weaponGaugeDisplay, target, k);
        }

        for (int i = 0; i < _weaponGaugeMasks.Count; i++)
        {
            bool isEquipped = (i == sel);
            WeaponData wd = _ctrl.WeaponDataAt(i);
            bool isFuelSlot = isEquipped && haveFuel && wd != null && wd.isFlamethrower;

            if (!isFuelSlot)
            {
                HideGauge(_weaponGaugeMasks[i]);
                continue;
            }

            // FUEL - a rising/falling FILL gauge. Below the warn threshold the dimmed portion shifts toward red. 
            bool lowFuel = _weaponGaugeDisplay <= gaugeLowFuelWarn;
            Color dim = gaugeDimTint;
            if (lowFuel)
            {
                float warn = 1f - Mathf.Clamp01(_weaponGaugeDisplay / Mathf.Max(0.0001f, gaugeLowFuelWarn));
                warn *= Mathf.Clamp01(gaugeLowFuelMaxBlend); // cap the saturation
                dim = Color.Lerp(gaugeDimTint, gaugeLowFuelColor, warn);
            }

            ApplyGauge(_weaponGaugeMasks[i], GaugeStyle.Fill,
                       progress: _weaponGaugeDisplay, dimColor: dim,
                       flashTimer: _weaponReadyFlashTimer);
        }
    }

    void UpdateToolCooldownGauge()
    {
        int sel = _ctrl.CurrentToolIndex;
        Weapon.ToolGaugeInfo info = _weapon != null ? _weapon.GetToolGauge()
                                                    : default;

        // `displayValue` is the raw 0..1 the gauge should show this frame.
        // For Ready we treat it as 1 (full / no chrome).
        float rawValue = !info.has ? 1f
                       : (info.phase == Weapon.ToolGaugePhase.Ready ? 1f : info.value);

        // Ready-flash fires when the COOLDOWN-FILL phase completes (value→1).
        // The active-clock phase ending does NOT flash — it flows straight
        // into the cooldown phase.
        bool inCooldownFill = info.has && info.phase == Weapon.ToolGaugePhase.CooldownFill;
        if (inCooldownFill && _lastToolGaugeValue < 0.999f && rawValue >= 0.999f)
            _toolReadyFlashTimer = gaugeReadyFlashDuration;
        _lastToolGaugeValue = inCooldownFill ? rawValue : 1f;

        // Ease the displayed value. Snap on equipped-tool change, AND snap on
        // a phase change (clock→fill jumps 0→0 conceptually but the fill
        // origin flips, so a lerp across the change looks wrong).
        bool phaseChanged = info.phase != _lastToolPhase;
        if (sel != _lastToolSel || phaseChanged)
        {
            _toolGaugeDisplay = rawValue;
            _lastToolSel = sel;
            _lastToolPhase = info.phase;
        }
        else
        {
            float k = 1f - Mathf.Exp(-gaugeSmoothing * Time.unscaledDeltaTime);
            _toolGaugeDisplay = Mathf.Lerp(_toolGaugeDisplay, rawValue, k);
        }

        for (int i = 0; i < _toolGaugeMasks.Count; i++)
        {
            bool isEquipped = (i == sel);
            bool isGaugeSlot = isEquipped && info.has
                               && info.phase != Weapon.ToolGaugePhase.Ready;

            if (!isGaugeSlot)
            {
                HideGauge(_toolGaugeMasks[i]);
                continue;
            }

            // ACTIVE  → a depleting radial CLOCK (counts the effect down),
            //           cooler-grey 'active' tint so it reads as running.
            // COOLDOWN → a rising vertical FILL gauge (same as flamethrower),
            //           neutral mid-grey tint so it reads as spent / recharging.
            bool isClock = info.phase == Weapon.ToolGaugePhase.ActiveClock;
            GaugeStyle style = isClock ? GaugeStyle.Clock : GaugeStyle.Fill;
            Color tint = isClock ? gaugeActiveTint : gaugeDimTint;

            ApplyGauge(_toolGaugeMasks[i], style,
                       progress: _toolGaugeDisplay, dimColor: tint,
                       flashTimer: _toolReadyFlashTimer);
        }
    }

    enum GaugeStyle
    {
        /// Rising vertical fill — the greyed portion shrinks from the top as
        /// `progress` (0→1) climbs. Used for fuel and for cooldown recharge.
        Fill,
        /// Cooldown-clock wipe — the icon starts fully greyed (progress 1)
        /// and the greyed wedge sweeps away clockwise as `progress` falls to
        /// 0. Used for an active-effect countdown (cloak / book aura).
        Clock,
    }

    // Draws one gauge slot's overlay
    void ApplyGauge(Image mask, GaugeStyle style, float progress,
                    Color dimColor, float flashTimer)
    {
        if (mask == null) return;
        progress = Mathf.Clamp01(progress);

        if (flashTimer > 0f)
        {
            // Ready flash — a brief soft glow pulse across the whole icon.
            float f = Mathf.Clamp01(flashTimer / Mathf.Max(0.01f, gaugeReadyFlashDuration));
            mask.type = Image.Type.Filled;
            mask.fillMethod = Image.FillMethod.Vertical;
            mask.fillOrigin = (int)Image.OriginVertical.Top;
            mask.fillAmount = 1f;
            Color fc = gaugeReadyFlash;
            fc.a = gaugeReadyFlash.a * f;
            mask.color = fc;
            return;
        }

        // "Nothing to draw" cases differ by style:
        //  • Fill  — progress 1 means full fuel / ready cooldown → no overlay.
        //  • Clock — progress 0 means the effect is about to end → no overlay
        //            (it has fully wiped away). progress 1 here means JUST
        //            activated = fully dimmed, which we DO draw.
        bool nothingToDraw = (style == GaugeStyle.Fill && progress >= 0.999f)
                          || (style == GaugeStyle.Clock && progress <= 0.001f);
        if (nothingToDraw)
        {
            HideGauge(mask);
            return;
        }

        mask.color = new Color(dimColor.r, dimColor.g, dimColor.b, dimColor.a);
        mask.type = Image.Type.Filled;

        if (style == GaugeStyle.Clock)
        {
            // Active-effect clock
            mask.fillMethod = Image.FillMethod.Radial360;
            mask.fillOrigin = (int)Image.Origin360.Top;
            mask.fillClockwise = true;
            mask.fillAmount = progress;
        }
        else
        {
            // Cooldown / fuel: rising vertical fill. The dimmed region is the
            // TOP (1 - progress) — it shrinks upward as the gauge charges.
            mask.fillMethod = Image.FillMethod.Vertical;
            mask.fillOrigin = (int)Image.OriginVertical.Top;
            mask.fillAmount = 1f - progress;
        }
    }

    static void HideGauge(Image mask)
    {
        if (mask == null) return;
        var c = mask.color;
        if (c.a != 0f) { c.a = 0f; mask.color = c; }
        mask.fillAmount = 0f;
    }

    //  BUILD SLOTS
    void RebuildAll()
    {
        foreach (var rt in _weaponRects) if (rt) Destroy(rt.gameObject);
        _weaponRects.Clear(); _weaponBgs.Clear(); _weaponIcons.Clear();
        _weaponGaugeMasks.Clear();
        foreach (var rt in _toolRects) if (rt) Destroy(rt.gameObject);
        _toolRects.Clear(); _toolBgs.Clear(); _toolIcons.Clear();
        _toolGaugeMasks.Clear();
        if (_emptyToolRect != null) { Destroy(_emptyToolRect.gameObject); _emptyToolRect = null; }
        if (_ctrl == null) return;

        for (int i = 0; i < _ctrl.WeaponCount; i++)
            MakeSlot(_weaponRoot, $"W{i}", LoadIconForData(_ctrl.WeaponDataAt(i)),
                _weaponRects, _weaponBgs, _weaponIcons, _weaponGaugeMasks);

        if (_ctrl.ToolCount > 0)
        {
            for (int i = 0; i < _ctrl.ToolCount; i++)
                MakeSlot(_toolRoot, $"T{i}", LoadIconForData(_ctrl.ToolDataAt(i)),
                    _toolRects, _toolBgs, _toolIcons, _toolGaugeMasks);
        }
        else
            BuildEmptyToolPlaceholder();
    }

    void MakeSlot(RectTransform parent, string name, Sprite iconSprite,
        List<RectTransform> rects, List<Image> bgs, List<Image> icons,
        List<Image> gaugeMasks)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = Vector2.one * circleSize;
        rects.Add(rt);

        // BG = the decorative ring frame. Starts as the inactive ring;
        // UpdateWeaponVisuals/UpdateToolVisuals swap in the active ring for the
        // equipped slot. preserveAspect keeps the ~404x403 sprite undistorted
        // inside the square slot rect.
        var bg = MakeImg(go, "BG", RingSpriteFor(active: false));
        bg.preserveAspect = true;
        bg.color = UsingRingSprites ? Color.white : inactiveBg;
        bg.rectTransform.sizeDelta = Vector2.one * circleSize;
        bgs.Add(bg);

        float iconSize = circleSize * 0.58f;

        var ic = MakeImg(go, "Icon", iconSprite);
        ic.preserveAspect = true;
        ic.rectTransform.sizeDelta = Vector2.one * iconSize;
        icons.Add(ic);

        // GAUGE MASK
        var maskGo = new GameObject("GaugeMask", typeof(Image));
        maskGo.transform.SetParent(ic.transform, false);
        var mask = maskGo.GetComponent<Image>();
        mask.raycastTarget = false;
        mask.sprite = SolidDiscSprite();
        var mrt = mask.rectTransform;
        mrt.anchorMin = Vector2.zero;             // stretch to fill the icon
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;
        mask.type = Image.Type.Filled;
        mask.fillMethod = Image.FillMethod.Vertical;
        mask.fillOrigin = (int)Image.OriginVertical.Top;
        mask.fillAmount = 0f;
        mask.color = new Color(0f, 0f, 0f, 0f);
        gaugeMasks.Add(mask);
    }

    void BuildEmptyToolPlaceholder()
    {
        float sz = circleSize * 0.7f;
        var go = new GameObject("EmptyTool", typeof(RectTransform));
        go.transform.SetParent(_toolRoot, false);
        _emptyToolRect = go.GetComponent<RectTransform>();
        _emptyToolRect.anchorMin = _emptyToolRect.anchorMax = _emptyToolRect.pivot = new Vector2(0.5f, 0f);
        _emptyToolRect.sizeDelta = Vector2.one * sz;
        _emptyToolRect.anchoredPosition = Vector2.zero;
        // Empty-tool placeholder uses the inactive ring frame (dimmed) so an
        // unlocked-but-empty tool slot matches the rest of the hotbar.
        _emptyToolBg = MakeImg(go, "BG", RingSpriteFor(active: false));
        if (UsingRingSprites)
        {
            _emptyToolBg.preserveAspect = true;
            // Extra-dim so it reads as 'no tool here', not just inactive.
            _emptyToolBg.color = new Color(1f, 1f, 1f, 0.45f);
        }
        else
        {
            _emptyToolBg.color = emptyToolBg;
        }
        _emptyToolBg.rectTransform.sizeDelta = Vector2.one * sz;
    }

    Image MakeImg(GameObject parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent.transform, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        return img;
    }

    //  RANK COMPUTATION
    int[] WeaponRanks(int sel, int n)
    {
        int[] rank = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = i - sel;
            if (d < 0) d += n;
            rank[i] = d;
        }
        return rank;
    }

    int[] ToolRanks(int sel, int n)
    {
        int[] rank = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = sel - i;
            if (d < 0) d += n;
            rank[i] = d;
        }
        return rank;
    }

    float WeaponYForRank(int rank) => -rank * rollStep;
    float ToolYForRank(int rank) => rank * rollStep;

    //  WEAPON VISUALS
    void UpdateWeaponVisuals()
    {
        int n = _weaponRects.Count, sel = _ctrl.CurrentWeaponIndex;
        int[] ranks = WeaponRanks(sel, n);

        for (int i = 0; i < n; i++)
        {
            int r = ranks[i];
            bool on = r == 0;
            bool visible = r <= VisibleRadius;
            float sz = visible ? circleSize * Mathf.Pow(shrinkPerStep, r) : 0f;
            float alpha = visible ? (on ? 1f : Mathf.Pow(shrinkPerStep, r)) : 0f;

            Color ico = on ? activeIcon : inactiveIcon;
            ico.a *= alpha;

            ApplyBgFrame(_weaponBgs[i], on, alpha, weaponActiveBg);
            _weaponIcons[i].color = ico;
            _weaponBgs[i].rectTransform.sizeDelta = Vector2.one * sz;
            _weaponIcons[i].rectTransform.sizeDelta = Vector2.one * (sz * 0.58f);
            _weaponRects[i].SetSiblingIndex(Mathf.Max(0, n - 1 - r));
            _weaponRects[i].gameObject.SetActive(visible);
        }
    }

    //  TOOL VISUALS
    void UpdateToolVisuals()
    {
        int n = _toolRects.Count, sel = _ctrl.CurrentToolIndex;
        int[] ranks = ToolRanks(sel, n);

        for (int i = 0; i < n; i++)
        {
            int r = ranks[i];
            bool on = r == 0;
            bool visible = r <= VisibleRadius;
            float sz = visible ? circleSize * Mathf.Pow(shrinkPerStep, r) : 0f;
            float alpha = visible ? (on ? 1f : Mathf.Pow(shrinkPerStep, r)) : 0f;

            Color ico = on ? activeIcon : inactiveIcon;
            ico.a *= alpha;

            ApplyBgFrame(_toolBgs[i], on, alpha, toolActiveBg);
            _toolIcons[i].color = ico;
            _toolBgs[i].rectTransform.sizeDelta = Vector2.one * sz;
            _toolIcons[i].rectTransform.sizeDelta = Vector2.one * (sz * 0.58f);
            _toolRects[i].SetSiblingIndex(Mathf.Max(0, n - 1 - r));
            _toolRects[i].gameObject.SetActive(visible);
        }
    }

    // Sets a slot's BG frame for its active/inactive state.
    // With ring sprites: swaps active↔inactive ring sprite, BG colour is
    //   plain white (the art carries the look) faded by `alpha`.
    // Fallback (no ring asset): keeps the legacy procedural-circle colour
    //   tint behaviour.
    void ApplyBgFrame(Image bg, bool active, float alpha, Color fallbackActiveBg)
    {
        if (bg == null) return;

        if (UsingRingSprites)
        {
            Sprite ring = RingSpriteFor(active);
            if (bg.sprite != ring) bg.sprite = ring;
            bg.color = new Color(1f, 1f, 1f, alpha);
        }
        else
        {
            Color c = active ? fallbackActiveBg : inactiveBg;
            c.a = alpha;
            bg.color = c;
        }
    }

    //  SNAP POSITIONS
    void SnapWeaponPositions()
    {
        int n = _weaponRects.Count, sel = _ctrl.CurrentWeaponIndex;
        int[] ranks = WeaponRanks(sel, n);
        for (int i = 0; i < n; i++)
            _weaponRects[i].anchoredPosition = new Vector2(0f, WeaponYForRank(ranks[i]));
    }

    void SnapToolPositions()
    {
        int n = _toolRects.Count, sel = _ctrl.CurrentToolIndex;
        int[] ranks = ToolRanks(sel, n);
        for (int i = 0; i < n; i++)
            _toolRects[i].anchoredPosition = new Vector2(0f, ToolYForRank(ranks[i]));
    }

    void SnapAll()
    {
        UpdateWeaponVisuals();
        SnapWeaponPositions();
        UpdateToolVisuals();
        SnapToolPositions();
    }

    //  SCROLL ANIMATIONS
    void StartWeaponScrollAnim()
    {
        if (_weaponScrollCo != null) StopCoroutine(_weaponScrollCo);
        _weaponScrollCo = StartCoroutine(AnimateWeaponScroll());
    }

    void StartToolScrollAnim()
    {
        if (_toolScrollCo != null) StopCoroutine(_toolScrollCo);
        _toolScrollCo = StartCoroutine(AnimateToolScroll());
    }

    IEnumerator AnimateWeaponScroll()
    {
        int n = _weaponRects.Count, sel = _ctrl.CurrentWeaponIndex;
        if (n == 0) yield break;

        int[] ranks = WeaponRanks(sel, n);
        float[] from = new float[n], to = new float[n];
        for (int i = 0; i < n; i++)
        {
            from[i] = _weaponRects[i].anchoredPosition.y;
            to[i] = WeaponYForRank(ranks[i]);
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / scrollSpeed;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            for (int i = 0; i < n; i++)
                _weaponRects[i].anchoredPosition = new Vector2(0f, Mathf.Lerp(from[i], to[i], e));
            yield return null;
        }
        SnapWeaponPositions();
    }

    IEnumerator AnimateToolScroll()
    {
        int n = _toolRects.Count, sel = _ctrl.CurrentToolIndex;
        if (n == 0) yield break;

        int[] ranks = ToolRanks(sel, n);
        float[] from = new float[n], to = new float[n];
        for (int i = 0; i < n; i++)
        {
            from[i] = _toolRects[i].anchoredPosition.y;
            to[i] = ToolYForRank(ranks[i]);
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / scrollSpeed;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            for (int i = 0; i < n; i++)
                _toolRects[i].anchoredPosition = new Vector2(0f, Mathf.Lerp(from[i], to[i], e));
            yield return null;
        }
        SnapToolPositions();
    }

    //  POP
    IEnumerator PopRect(RectTransform rt)
    {
        if (rt == null) yield break;
        rt.localScale = Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / popSpeed;
            rt.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t));
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    // HELPERS

    public static Sprite LoadIconForData(WeaponData wd)
    {
        if (wd == null) return null;
        string path;
        if (wd.isGrapplingHook) path = "Icons/WeaponIconGrapplingHook";
        else if (wd.isObstacleDrawer) path = "Icons/WeaponIconObstacleDrawer";
        else if (wd.isFlamethrower) path = "Icons/WeaponIconFlamethrower";
        else if (wd.isBombLauncher) path = "Icons/WeaponIconBomb";
        else if (wd.isTrap) path = "Icons/WeaponIconTrap";
        else if (wd.isTurret) path = "Icons/WeaponIconTurret";
        else if (wd.isDecoy) path = "Icons/WeaponIconDecoy";
        else if (wd.isCloak) path = "Icons/WeaponIconCloak";
        else if (wd.isTorch) path = "Icons/WeaponIconTorch";
        else if (wd.armorBonus > 0f) path = "Icons/WeaponIconShield";
        else if (wd.isBoomerang) path = "Icons/WeaponIconBoomerang";
        else if (wd.isBook) path = "Icons/WeaponIconBook";
        else if (wd.isHammer) path = "Icons/WeaponIconHammer";
        else if (wd.isRanged) path = "Icons/WeaponIconRanged";
        else if (wd.isClock) path = "Icons/WeaponIconClock";
        else if (wd.isMortar) path = "Icons/WeaponIconMortar";
        else if (wd.isSmoke) path = "Icons/WeaponIconSmoke";
        else path = "Icons/WeaponIconMelee";
        return Resources.Load<Sprite>(path)
            ?? Resources.Load<Sprite>($"Icons/WeaponIcon{wd.weaponName.Replace(" ", "")}");
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    static Sprite _cachedCircle;
    static Sprite CircleSprite()
    {
        if (_cachedCircle != null) return _cachedCircle;
        const int S = 512;
        // Third arg `mipChain: true` enables mipmaps.
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, mipChain: true, linear: false)
        {
            filterMode = FilterMode.Trilinear,   // Trilinear blends between mip levels — smoothest
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 4                        // Aniso filtering now active because mipmaps exist
        };
        var px = new Color[S * S];
        float cx = S * 0.5f;
        float cy = S * 0.5f;
        float r = S * 0.5f - 3f;
        const float aa = 3f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01((r - d) / aa);
                float alpha = t * t * (3f - 2f * t);
                px[y * S + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(px);
        // First arg `updateMipmaps: true` regenerates the mip chain from the new pixel data.
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        _cachedCircle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _cachedCircle;
    }

    // A 4x4 fully-opaque white square. Used as the gauge mask sprite
    static Sprite _cachedDisc;
    static Sprite SolidDiscSprite()
    {
        if (_cachedDisc != null) return _cachedDisc;
        const int S = 512;
        // mipChain:true generates mipmaps so the disc stays smooth when the
        // GPU downscales it from 512px to the ~88px on-screen icon size —
        // without mipmaps that downscale aliases into jagged pixels.
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, mipChain: true, linear: false)
        {
            filterMode = FilterMode.Trilinear,  // blends between mip levels
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 4
        };
        var px = new Color[S * S];
        float c = S * 0.5f;
        float r = S * 0.5f - 3f;
        // Edge anti-aliasing band, in pixels. A multi-pixel smooth falloff
        // (not a 1px hard cut) is what makes the downscaled circle read as a
        // clean curve instead of a stair-stepped edge.
        const float aa = 3.0f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01((r - d) / aa);
                float a = t * t * (3f - 2f * t); // smoothstep — soft, clean rim
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        _cachedDisc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _cachedDisc;
    }

    // RING FRAME SPRITES

    Sprite _activeRing;
    Sprite _inactiveRing;
    bool _ringsResolved;

    void ResolveRingSprites()
    {
        if (_ringsResolved) return;
        _ringsResolved = true;

        _activeRing = Resources.Load<Sprite>(activeRingPath);
        _inactiveRing = Resources.Load<Sprite>(inactiveRingPath);

        if (_activeRing == null)
            Debug.LogWarning($"[WeaponRollUI] Active ring sprite not found at " +
                             $"'Resources/{activeRingPath}'. " +
                             (ringFallbackToCircle ? "Falling back to procedural circle."
                                                   : "Slots will have no frame."));
        if (_inactiveRing == null)
            Debug.LogWarning($"[WeaponRollUI] Inactive ring sprite not found at " +
                             $"'Resources/{inactiveRingPath}'. " +
                             (ringFallbackToCircle ? "Falling back to procedural circle."
                                                   : "Slots will have no frame."));
    }

    // The frame sprite for a slot, given whether it's the equipped (active)
    // one. Falls back to the procedural circle if the asset is missing and
    // fallback is enabled.
    Sprite RingSpriteFor(bool active)
    {
        ResolveRingSprites();
        Sprite s = active ? _activeRing : _inactiveRing;
        if (s != null) return s;
        return ringFallbackToCircle ? CircleSprite() : null;
    }

    // True when a real ring sprite (not the fallback) is in use — the ring
    // art already encodes the active/inactive look, so the BG must NOT be
    // colour-tinted in that case (only alpha-faded).
    bool UsingRingSprites => (_activeRing != null || _inactiveRing != null);

}
