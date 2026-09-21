using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Compact screen-space readout of the Central Core's health (plus shield state),
/// so the player can keep an eye on the core while far away from it on the map.
///
/// ZERO SETUP: copy this file anywhere under Assets/ (not inside an "Editor" folder).
/// It spawns itself when Play starts, finds the live CentralCore (re-binding when
/// TowerDefenseMap rebuilds the core between stages), and hides itself in scenes
/// without a core.
///
/// Placement: it looks for the player health element on the HUD canvas (screen-space UI
/// in the upper half whose name contains "Health", "Life" or "HP") and measures it from
/// its VISIBLE graphics, not its RectTransform (containers are often stretched full-screen).
/// If nothing is found, it uses the top-left fallback offset. Either way, a final pass
/// pushes the indicator down below any visible HUD graphic it would overlap, so it can
/// never sit on top of the player health bar. The Console prints one "[CoreHealthHUD]"
/// line with the final placement and what it avoided.
///
/// Look: matches the existing HUD (thick black pill frame like the player health bar,
/// purple crystal gradient taken from the Energy crystal, a faceted crystal icon, and
/// the Energy text font). All graphics are generated at the exact on-screen pixel size
/// and pixel-snapped, so they stay sharp at any resolution.
/// </summary>
[DisallowMultipleComponent]
public class CoreHealthHUD : MonoBehaviour
{
    #region Auto Bootstrap
    static readonly bool AutoCreateAtRuntime = true;
    static CoreHealthHUD sInstance;

    // Statics survive Play Mode re-entry when Domain Reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => sInstance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!AutoCreateAtRuntime) return;
        if (sInstance != null || FindFirstObjectByType<CoreHealthHUD>() != null) return;

        var go = new GameObject("[CoreHealthHUD]");
        DontDestroyOnLoad(go);
        go.AddComponent<CoreHealthHUD>();
    }
    #endregion

    #region Configuration
    public enum HudPlacement { BelowPlayerHealth, TopLeft, TopCenter, TopRight, BottomLeft, BottomRight }

    [Header("Visibility")]
    public bool show = true;
    [Tooltip("Fade the indicator out while the core itself is visible on screen.")]
    public bool hideWhenCoreOnScreen = false;
    [Range(0f, 0.3f)] public float onScreenMargin = 0.05f;
    [Tooltip("Log once to the Console which element the indicator anchored to.")]
    public bool logPlacement = true;

    [Header("Placement")]
    public HudPlacement placement = HudPlacement.BelowPlayerHealth;
    [Tooltip("Optional: the exact player health RectTransform to sit under. Empty = auto-detect by name.")]
    public RectTransform anchorOverride;
    [Tooltip("X: offset from the left edge of the player health UI. Y: gap between the bottom of the " +
             "player health UI and the TOP OF THE CORE BAR (the crystal tip may poke higher). Canvas units.")]
    public Vector2 anchorGap = new Vector2(-5f, 6f);
    [Tooltip("Used when no player health element can be found.")]
    public HudPlacement fallbackPlacement = HudPlacement.TopLeft;
    [Tooltip("Edge offset for corner placements. The default puts it under the player health bar " +
             "on a 1920x1080 reference canvas.")]
    public Vector2 screenEdgeOffset = new Vector2(29f, 112f);
    [Tooltip("Never overlap other visible HUD elements in the top half of the screen: " +
             "the indicator moves down below them instead.")]
    public bool avoidOverlappingHud = true;
    [Tooltip("Minimum gap kept between the core bar and HUD elements above it, canvas units. " +
             "Lower = the core bar sits closer to the player health bar.")]
    public float hudGap = 6f;

    [Header("Size (canvas units)")]
    public float frameWidth = 290f;
    public float frameHeight = 40f;
    public float frameThickness = 6f;
    public Vector2 iconSize = new Vector2(42f, 60f);
    [Tooltip("How far the crystal icon overlaps the left end of the frame.")]
    public float iconOverlap = 18f;
    [Tooltip("Nudges the crystal vertically against the bar (positive = up), canvas units.")]
    public float iconVerticalOffset = 3f;
    public float fontSize = 23f;
    [Range(0.2f, 1f)] public float shieldBarWidthFraction = 0.6f;
    public float shieldBarHeight = 16f;

    [Header("Text")]
    public string label = "Core";
    [Tooltip("Use the Energy counter's FONT so the text matches the HUD.")]
    public bool matchEnergyTextStyle = true;

    [Header("Text Sharpness")]
    [Tooltip("Use a private text material tuned for small sizes: no outline (at ~10-20px an outline " +
             "eats the thin serif strokes and turns letters into blobs), a slightly bolder face and a " +
             "sharper SDF edge, plus a pixel-exact drop shadow for contrast. " +
             "Off = copy the Energy counter's material (outline/glow) as-is.")]
    public bool crispSmallText = true;
    [Tooltip("SDF edge sharpness (TMP '_Sharpness'). Higher = crisper, too high = jaggy.")]
    [Range(-1f, 1f)] public float textSharpness = 0.25f;
    [Tooltip("Thickens the letters (TMP '_FaceDilate') so hairline serifs survive at small sizes.")]
    [Range(0f, 0.4f)] public float textFaceDilate = 0.1f;
    public Color textShadowColor = new Color32(0x0E, 0x00, 0x16, 0xE6);
    [Tooltip("Drop shadow offset in whole screen pixels (x right, y down). (0,0) = no shadow.")]
    public Vector2Int textShadowOffsetPx = new Vector2Int(1, 1);

    [Header("Palette (sampled from the HUD)")]
    public Color fillLeft = new Color32(0x4A, 0x00, 0x88, 0xFF);
    public Color fillMid = new Color32(0x9C, 0x00, 0xD0, 0xFF);
    public Color fillRight = new Color32(0xE6, 0x00, 0xFA, 0xFF);
    public Color criticalLeft = new Color32(0x5A, 0x00, 0x1C, 0xFF);
    public Color criticalRight = new Color32(0xFF, 0x2A, 0x5A, 0xFF);
    public Color frameColor = new Color32(0x05, 0x00, 0x08, 0xFF);
    public Color trackColor = new Color32(0x16, 0x00, 0x22, 0xFF);
    public Color ghostColor = new Color32(0xFF, 0xC4, 0xFF, 0xC0);
    public Color labelTop = new Color32(0xF6, 0xB8, 0xEE, 0xFF);   // "Wave 1/8" pink...
    public Color labelBottom = new Color32(0xE6, 0xB4, 0x58, 0xFF); // ...to gold
    public Color depletedTextColor = new Color32(0x9A, 0x90, 0xA0, 0xFF);
    public Color shieldColor = new Color32(0x4C, 0xC8, 0xFF, 0xFF);
    public Color hitFlashColor = new Color32(0xFF, 0x5A, 0xE6, 0xFF);
    public Color criticalFrameColor = new Color32(0xC0, 0x10, 0x30, 0xFF);

    [Header("Feedback")]
    public float hitFeedbackDuration = 0.3f;
    public float hitShakeStrength = 4f;
    public float criticalPulseSpeed = 6f;
    #endregion

    #region State

    CentralCore core;
    TowerDefenseMap map;
    float coreSearchTimer;

    Canvas hostCanvas;
    Canvas ownCanvas;
    RectTransform anchor;
    float anchorSearchTimer;
    int failedAnchorScans;
    bool hasAnchorPosition;
    Rect anchorBounds;              // visible extent of the player health UI, host-canvas space
    float layoutScanTimer;          // anchor bounds + obstacles are re-measured on this timer
    readonly List<Rect> obstacleRects = new List<Rect>();
    readonly List<string> obstacleNames = new List<string>();
    readonly Rect[] shapeBuffer = new Rect[3];
    bool placementLogged;
    readonly Vector3[] corners = new Vector3[4];
    readonly List<Graphic> graphicsBuffer = new List<Graphic>();

    RectTransform panel;
    CanvasGroup canvasGroup;
    Image frameImage;
    Image fillImage;
    Image ghostImage;
    int barLayerInset, barLayerWidth, barTrackWidth;
    int shieldLayerInset, shieldLayerWidth, shieldTrackWidth;
    Image criticalImage;
    RectTransform iconRect;
    RectTransform shieldBar;
    Image shieldFill;
    TextMeshProUGUI labelText;
    TextMeshProUGUI labelShadow;


    float displayedPct = -1f;
    float ghostPct;
    float ghostHold;
    float hitTimer;
    Color currentHitColor;
    float lastShield = -1f;
    bool shieldBarVisible;
    #endregion

    #region Lifecycle
    void Awake()
    {
        if (sInstance != null && sInstance != this)
        {
            Destroy(this);
            return;
        }
        sInstance = this;
    }

    void OnDestroy()
    {
        UnbindCore();
        DestroyPanel();
        if (sInstance == this) sInstance = null;
    }

    // LateUpdate: the player health UI has already been laid out for this frame.
    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        EnsureCore(dt);

        if (!show || core == null)
        {
            if (panel != null && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
            return;
        }

        EnsureHost(dt);
        if (panel == null) return;

        // Resolution / canvas scale changed: rebuild so textures stay pixel-exact.
        float currentScale = ComputeUiScale(hostCanvas);
        if (Mathf.Abs(currentScale - uiScale) > uiScale * 0.01f)
        {
            DestroyPanel();
            EnsureHost(dt);
            if (panel == null) return;
        }

        if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);

        UpdateValues(dt);
        UpdatePosition(dt);
        UpdateFade(dt);
    }
    #endregion

    #region Core Binding
    void EnsureCore(float dt)
    {
        // The core GameObject was destroyed (e.g. the map rebuilt it for a new stage).
        if ((object)core != null && core == null) UnbindCore();

        bool needSearch = core == null || core.IsDestroyed();
        if (!needSearch) return;

        coreSearchTimer -= dt;
        if (coreSearchTimer > 0f) return;
        coreSearchTimer = core == null ? 0.5f : 1f;

        CentralCore found = FindCore();
        if (found != null && found != core) BindCore(found);
    }

    CentralCore FindCore()
    {
        if (map == null) map = FindFirstObjectByType<TowerDefenseMap>();
        if (map != null)
        {
            CentralCore mapCore = map.GetCentralCore();
            if (mapCore != null && !mapCore.IsDestroyed()) return mapCore;
        }

        // Prefer a living core; a replaced one may linger until end of frame.
        CentralCore fallback = null;
        foreach (CentralCore c in FindObjectsByType<CentralCore>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            if (!c.IsDestroyed()) return c;
            if (fallback == null) fallback = c;
        }
        return fallback;
    }

    void BindCore(CentralCore newCore)
    {
        UnbindCore();
        core = newCore;
        core.OnDamageTaken += OnCoreDamaged;

        displayedPct = -1f; // snap on first update, no ghost sweep for a fresh core
        lastShield = -1f;
        hitTimer = 0f;
    }

    void UnbindCore()
    {
        if ((object)core != null) core.OnDamageTaken -= OnCoreDamaged;
        core = null;
    }

    void OnCoreDamaged(float damage, GameObject source)
    {
        TriggerHit(hitFlashColor);
        ghostHold = 0f;
    }

    void TriggerHit(Color color)
    {
        hitTimer = hitFeedbackDuration;
        currentHitColor = color;
    }
    #endregion

    #region Host Canvas & Anchor
    bool WantsAnchor => anchorOverride != null || placement == HudPlacement.BelowPlayerHealth;

    void EnsureHost(float dt)
    {
        bool hostLost = hostCanvas == null || panel == null;
        bool anchorMissing = WantsAnchor && anchor == null;

        if (!hostLost)
        {
            if (!anchorMissing) return;
            anchorSearchTimer -= dt;
            if (anchorSearchTimer > 0f) return;
        }

        RectTransform newAnchor = anchorOverride != null ? anchorOverride
            : placement == HudPlacement.BelowPlayerHealth ? FindHealthAnchor() : null;

        if (WantsAnchor && newAnchor == null)
        {
            failedAnchorScans++;
            anchorSearchTimer = failedAnchorScans < 15 ? 2f : 10f; // HUD may spawn late
        }
        else
        {
            failedAnchorScans = 0;
        }

        if (newAnchor != anchor)
        {
            hasAnchorPosition = false;
            layoutScanTimer = 0f;
            placementLogged = false;
        }
        anchor = newAnchor;

        Canvas newHost = ResolveHostCanvas(anchor);
        if (newHost != hostCanvas || panel == null)
        {
            DestroyPanel();
            hostCanvas = newHost;
            BuildPanel((RectTransform)hostCanvas.transform);
        }
    }

    static string GetPath(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    Canvas ResolveHostCanvas(RectTransform anchorRect)
    {
        if (anchorRect != null)
        {
            Canvas c = anchorRect.GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }

        // No health element: still prefer the game's HUD canvas so scaling matches.
        TextMeshProUGUI energyText = GetEnergyText();
        if (energyText != null)
        {
            Canvas c = energyText.canvas;
            if (c != null && c.rootCanvas.renderMode != RenderMode.WorldSpace) return c.rootCanvas;
        }

        return GetOrCreateOwnCanvas();
    }

    static TextMeshProUGUI GetEnergyText()
    {
        EnergyManager em = EnergyManager.Instance;
        return em != null ? em.playerEnergyText : null;
    }

    RectTransform FindHealthAnchor()
    {
        RectTransform best = null;
        int bestDepth = int.MaxValue;
        float bestTop = float.MinValue;
        float bestLeft = float.MaxValue;

        foreach (RectTransform rt in FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!LooksLikePlayerHealth(rt.name)) continue;

            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) continue;
            Canvas root = canvas.rootCanvas;
            if (root.renderMode == RenderMode.WorldSpace) continue;
            if (rt == root.transform) continue;
            if (rt.rect.width * rt.rect.height < 1f) continue;

            var rootRect = (RectTransform)root.transform;
            // Full-screen / huge containers are layout roots, not the health bar itself.
            if (rt.rect.width * rt.lossyScale.x > rootRect.rect.width * rootRect.lossyScale.x * 0.5f ||
                rt.rect.height * rt.lossyScale.y > rootRect.rect.height * rootRect.lossyScale.y * 0.35f) continue;

            rt.GetWorldCorners(corners);
            Vector3 topLeft = rootRect.InverseTransformPoint(corners[1]);

            // The player bar lives in the top half of the screen.
            if (topLeft.y < rootRect.rect.center.y) continue;

            // Shallowest match wins (the "PlayerHealth" container rather than its fill),
            // then top-most, then left-most (player 1 in co-op).
            int depth = 0;
            for (Transform t = rt.parent; t != null && t != root.transform; t = t.parent) depth++;

            bool better = depth < bestDepth ||
                          (depth == bestDepth && (topLeft.y > bestTop + 0.5f ||
                          (Mathf.Abs(topLeft.y - bestTop) <= 0.5f && topLeft.x < bestLeft)));
            if (!better) continue;

            best = rt;
            bestDepth = depth;
            bestTop = topLeft.y;
            bestLeft = topLeft.x;
        }

        return best;
    }

    static bool LooksLikePlayerHealth(string n)
    {
        if (n.StartsWith("CoreHUD")) return false;

        string lower = n.ToLowerInvariant();
        bool isHealth = lower.Contains("health") ||
                        (lower.Contains("life") && !lower.Contains("lifetime")) ||
                        n.Contains("HP") || n.Contains("Hp") || lower.StartsWith("hp");
        if (!isHealth) return false;

        string[] excluded = { "core", "enemy", "boss", "tower", "shield", "potion", "pickup" };
        foreach (string word in excluded)
            if (lower.Contains(word)) return false;

        return true;
    }

    // Rect of a RectTransform in the HOST canvas's local space. Works across canvases
    // (Overlay / Screen Space - Camera) by going through screen coordinates.
    bool TryGetHostRect(RectTransform rt, Canvas sourceRoot, out Rect rect)
    {
        rect = default;
        if (sourceRoot == null || sourceRoot.renderMode == RenderMode.WorldSpace) return false;

        var hostRect = (RectTransform)hostCanvas.transform;
        bool sameCanvas = sourceRoot == hostCanvas;
        Camera sourceCam = sourceRoot.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceRoot.worldCamera;
        Camera hostCam = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;

        rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < 4; i++)
        {
            Vector2 p;
            if (sameCanvas)
            {
                p = hostRect.InverseTransformPoint(corners[i]);
            }
            else
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCam, corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hostRect, screen, hostCam, out p)) return false;
            }
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    bool IsVisibleHudGraphic(Graphic g)
    {
        if (g == null || !g.enabled || !g.gameObject.activeInHierarchy) return false;
        if (panel != null && g.transform.IsChildOf(panel)) return false;
        if (g.canvasRenderer.cull) return false;
        if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < 0.05f) return false;
        if (g is TMP_Text text && string.IsNullOrWhiteSpace(text.text)) return false;
        return true;
    }

    // Vignettes, fades, blockers etc. are not HUD widgets to dodge.
    static bool IsOversized(Rect r, Rect canvas) =>
        r.width > canvas.width * 0.5f || r.height > canvas.height * 0.35f;

    static Rect Union(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // Measures the player health UI from what is actually drawn (the anchor and its
    // children), ignoring the anchor's own RectTransform when it is just a big container.
    bool TryMeasureAnchor(out Rect bounds)
    {
        bounds = default;
        Canvas c = anchor.GetComponentInParent<Canvas>();
        if (c == null) return false;
        Canvas sourceRoot = c.rootCanvas;
        Rect canvasRect = ((RectTransform)hostCanvas.transform).rect;

        bool any = false;
        anchor.GetComponentsInChildren(false, graphicsBuffer);
        foreach (Graphic g in graphicsBuffer)
        {
            if (!IsVisibleHudGraphic(g)) continue;
            if (!TryGetHostRect(g.rectTransform, sourceRoot, out Rect r) || IsOversized(r, canvasRect)) continue;
            bounds = any ? Union(bounds, r) : r;
            any = true;
        }
        graphicsBuffer.Clear();

        if (!any && TryGetHostRect(anchor, sourceRoot, out Rect own) && !IsOversized(own, canvasRect))
        {
            bounds = own;
            any = true;
        }
        return any;
    }

    // Every visible graphic in the top half of any screen-space canvas (player bars, the
    // purple under-bar, buttons...). The indicator is pushed below whichever it would overlap.
    void RefreshObstacles()
    {
        obstacleRects.Clear();
        obstacleNames.Clear();
        Rect canvasRect = ((RectTransform)hostCanvas.transform).rect;

        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isRootCanvas || !c.isActiveAndEnabled || c.renderMode == RenderMode.WorldSpace) continue;

            c.GetComponentsInChildren(false, graphicsBuffer);
            foreach (Graphic g in graphicsBuffer)
            {
                if (!IsVisibleHudGraphic(g)) continue;
                if (!TryGetHostRect(g.rectTransform, c, out Rect r) || IsOversized(r, canvasRect)) continue;
                if (r.yMax < canvasRect.center.y) continue; // top half only
                obstacleRects.Add(r);
                obstacleNames.Add(g.name);
            }
        }
        graphicsBuffer.Clear();
    }

    // Only ever moves DOWN, so it always converges. Collision uses the real shapes rather
    // than the whole panel box: the bar frame, the shield under-bar when shown, and just
    // the narrow upper tip of the crystal. Using the full box (as before) counted empty
    // space above the bar and left the core bar sitting needlessly low.
    float PushBelowObstacles(float left, float top, out string pushedBy)
    {
        pushedBy = null;

        int shapeCount = 0;
        // x, y (down from panel top), width, height
        shapeBuffer[shapeCount++] = new Rect(FrameX, FrameY, frameWidth, frameHeight);
        if (shieldBarVisible)
            shapeBuffer[shapeCount++] = new Rect(FrameX + 28f, ShieldBarY, frameWidth * shieldBarWidthFraction, shieldBarHeight);
        shapeBuffer[shapeCount++] = new Rect(iconSize.x * 0.35f, -iconVerticalOffset, iconSize.x * 0.3f, iconSize.y * 0.35f);

        for (int iteration = 0; iteration < 16; iteration++)
        {
            bool moved = false;
            for (int i = 0; i < obstacleRects.Count; i++)
            {
                Rect r = obstacleRects[i];
                for (int k = 0; k < shapeCount; k++)
                {
                    Rect shape = shapeBuffer[k];
                    float shapeLeft = left + shape.x;
                    float shapeTop = top - shape.y;

                    bool overlapX = r.xMax > shapeLeft + 1f && r.xMin < shapeLeft + shape.width - 1f;
                    bool overlapY = r.yMin < shapeTop + hudGap - 0.5f && r.yMax > shapeTop - shape.height;
                    if (!overlapX || !overlapY) continue;

                    float newTop = r.yMin - hudGap + shape.y;
                    if (newTop < top - 0.01f)
                    {
                        top = newTop;
                        pushedBy = obstacleNames[i];
                        moved = true;
                    }
                }
            }
            if (!moved) break;
        }
        return top;
    }

    Canvas GetOrCreateOwnCanvas()
    {
        if (ownCanvas != null) return ownCanvas;

        var go = new GameObject("CoreHUD_Canvas", typeof(RectTransform));
        go.layer = 5; // UI
        go.transform.SetParent(transform, false);

        ownCanvas = go.AddComponent<Canvas>();
        ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        ownCanvas.sortingOrder = 100;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return ownCanvas;
    }
    #endregion

    #region Build UI
    // Layout (panel space, origin top-left, y down):
    //   [crystal icon] overlapping the left end of a black pill frame, a pill track inside the
    //   frame holding ghost / fill / critical layers, a "Core" label over the bar, and an
    //   optional shield under-bar tucked beneath the frame (echoing the player's purple bar).
    //
    // CRISP RENDERING: every shape is rendered into its own texture at its EXACT on-screen
    // pixel size, with analytic anti-aliased edges (the crystal is 4x4 supersampled), and every
    // rect is snapped to whole screen pixels, so one texel lands on exactly one pixel. There is
    // no stencil Mask (its clipping is hard-edged, which caused jaggy bar ends) and no scaled
    // 9-slicing. The panel rebuilds itself when the resolution / canvas scale changes.
    float FrameX => iconSize.x - iconOverlap;
    float FrameY => Mathf.Max(0f, (iconSize.y - frameHeight) * 0.5f);
    float ShieldBarY => FrameY + frameHeight - 6f;

    float uiScale = 1f;            // screen pixels per host-canvas unit, captured at build time
    float panelWidthUnits;
    float panelHeightUnits;
    float panelHeightShieldUnits;
    readonly List<Object> generatedAssets = new List<Object>();

    int Px(float units) => Mathf.RoundToInt(units * uiScale);
    float U(int px) => px / uiScale;

    static float ComputeUiScale(Canvas canvas)
    {
        var rt = (RectTransform)canvas.transform;
        float width = rt.rect.width;
        float scale = width > 1f ? Screen.width / width : canvas.scaleFactor;
        return Mathf.Clamp(scale, 0.05f, 20f);
    }

    void DestroyPanel()
    {
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
        foreach (Object asset in generatedAssets)
            if (asset != null) Destroy(asset);
        generatedAssets.Clear();
    }

    void BuildPanel(RectTransform host)
    {
        uiScale = ComputeUiScale(hostCanvas);

        // Everything below is in whole screen pixels.
        int frameX = Px(FrameX), frameY = Px(FrameY);
        int frameW = Mathf.Max(8, Px(frameWidth)), frameH = Mathf.Max(6, Px(frameHeight));
        int thick = Mathf.Clamp(Px(frameThickness), 2, Mathf.Max(2, frameH / 2 - 2));
        int trackH = frameH - thick * 2;

        int iconW = Mathf.Max(4, Px(iconSize.x)), iconH = Mathf.Max(4, Px(iconSize.y));
        int iconY = -Px(iconVerticalOffset);

        int shieldX = frameX + Px(28f), shieldY = frameY + frameH - Px(6f);
        int shieldW = Mathf.Max(8, Px(frameWidth * shieldBarWidthFraction)), shieldH = Mathf.Max(5, Px(shieldBarHeight));
        int shieldThick = Mathf.Clamp(Px(3.5f), 2, Mathf.Max(2, shieldH / 2 - 1));

        panelWidthUnits = U(frameX + frameW);
        panelHeightUnits = U(Mathf.Max(iconH, frameY + frameH));
        panelHeightShieldUnits = U(Mathf.Max(iconH, shieldY + shieldH));

        panel = NewRect("CoreHUD_Panel", host);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(panelWidthUnits, panelHeightUnits);
        panel.SetAsLastSibling();
        panel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        canvasGroup = panel.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        // HALO-FREE LAYERING: stacking several anti-aliased pills of the same size makes their
        // soft edges add up (the light ghost layer bled through as a pale rim). Instead, the
        // inner layers are slightly LARGER pills whose edges hide under the frame, and the frame
        // is a ring drawn ON TOP, so the only visible edges are the ring's own clean AA edges.

        // Shield under-bar (behind the main frame)
        shieldBar = NewRect("CoreHUD_ShieldBar", panel);
        PlacePx(shieldBar, shieldX, shieldY, shieldW, shieldH);
        shieldFill = BuildRingBar(shieldBar, shieldW, shieldH, shieldThick, null, shieldColor, null, out _, out _,
                                  out shieldLayerInset, out shieldLayerWidth, out shieldTrackWidth);
        shieldBar.gameObject.SetActive(false);
        shieldBarVisible = false;

        // Main frame
        RectTransform frame = NewRect("CoreHUD_Frame", panel);
        PlacePx(frame, frameX, frameY, frameW, frameH);
        fillImage = BuildRingBar(frame, frameW, frameH, thick,
            GradientPillSprite(frameW - thick * 2 + LayerBleed(thick) * 2, trackH + LayerBleed(thick) * 2, fillLeft, fillMid, fillRight),
            Color.white,
            GradientPillSprite(frameW - thick * 2 + LayerBleed(thick) * 2, trackH + LayerBleed(thick) * 2,
                               criticalLeft, Color.Lerp(criticalLeft, criticalRight, 0.55f), criticalRight),
            out criticalImage, out frameImage,
            out barLayerInset, out barLayerWidth, out barTrackWidth);

        // Text over the bar; starts right of the crystal
        int textX = iconW + Px(6f);
        int textRight = frameX + frameW - thick - Px(12f);
        int textY = frameY + thick;

        TextMeshProUGUI energyText = GetEnergyText();
        TMP_FontAsset font = matchEnergyTextStyle && energyText != null && energyText.font != null
            ? energyText.font : TMP_Settings.defaultFontAsset;
        Material textMaterial = CreateTextMaterial(font, energyText);
        int fontPx = Mathf.Max(6, Px(fontSize)); // whole-pixel font size
        bool shadows = crispSmallText && textShadowOffsetPx != Vector2Int.zero;
        int textW = textRight - textX;

        if (shadows)
        {
            labelShadow = NewText("CoreHUD_LabelShadow", panel, TextAlignmentOptions.MidlineLeft, font, textMaterial, fontPx, null);
            PlacePx(labelShadow.rectTransform, textX + textShadowOffsetPx.x, textY + textShadowOffsetPx.y, textW, trackH);
        }
        else
        {
            labelShadow = null;
        }

        labelText = NewText("CoreHUD_Label", panel, TextAlignmentOptions.MidlineLeft, font, textMaterial, fontPx,
                            new VertexGradient(labelTop, labelTop, labelBottom, labelBottom));
        PlacePx(labelText.rectTransform, textX, textY, textW, trackH);

        SetText(labelText, labelShadow, label, label);
        SnapTextToPixels(labelText, labelShadow, snapX: true);
        LogFontDiagnostics(font, fontPx);

        // Crystal icon on top of everything, pivot centred so the hit "punch" scales in place
        iconRect = NewRect("CoreHUD_Crystal", panel);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(U(iconW), U(iconH));
        iconRect.anchoredPosition = new Vector2(U(iconW) * 0.5f, -(U(iconY) + U(iconH) * 0.5f));
        AddShape(iconRect.gameObject, Color.white, CrystalSprite(iconW, iconH));

        hasAnchorPosition = false;
        layoutScanTimer = 0f;
        placementLogged = false;
    }

    // gradient == null -> shadow copy (solid textShadowColor).
    TextMeshProUGUI NewText(string name, Transform parent, TextAlignmentOptions align, TMP_FontAsset font,
                            Material material, int fontPx, VertexGradient? gradient)
    {
        var tmp = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();

        if (font != null) tmp.font = font;
        if (material != null) tmp.fontSharedMaterial = material;
        tmp.extraPadding = true; // keeps dilated / sharpened glyphs from clipping at the quad edge

        tmp.fontSize = U(fontPx);
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.richText = true;

        if (gradient.HasValue)
        {
            tmp.color = Color.white;
            tmp.enableVertexGradient = true;
            tmp.colorGradient = gradient.Value;
        }
        else
        {
            tmp.enableVertexGradient = false;
            tmp.color = textShadowColor;
        }
        return tmp;
    }

    // Small-size text material: a copy of the font's own SDF material with the outline, glow
    // and underlay switched off, a little dilation and extra edge sharpness.
    Material CreateTextMaterial(TMP_FontAsset font, TextMeshProUGUI energyText)
    {
        if (font == null) return null;

        if (!crispSmallText)
            return energyText != null && energyText.font == font ? energyText.fontSharedMaterial : null;

        Material source = font.material;
        if (source == null) return null;

        var mat = new Material(source) { name = "CoreHUD_Text (runtime)" };
        mat.DisableKeyword("OUTLINE_ON");
        mat.DisableKeyword("UNDERLAY_ON");
        mat.DisableKeyword("UNDERLAY_INNER");
        mat.DisableKeyword("GLOW_ON");
        mat.DisableKeyword("BEVEL_ON");
        SetFloatIfPresent(mat, "_OutlineWidth", 0f);
        SetFloatIfPresent(mat, "_OutlineSoftness", 0f);
        SetFloatIfPresent(mat, "_FaceDilate", textFaceDilate);
        SetFloatIfPresent(mat, "_Sharpness", textSharpness); // not present on Mobile SDF shaders

        generatedAssets.Add(mat);
        return mat;
    }

    static void SetFloatIfPresent(Material mat, string property, float value)
    {
        if (mat.HasProperty(property)) mat.SetFloat(property, value);
    }

    static void SetText(TextMeshProUGUI main, TextMeshProUGUI shadow, string richText, string plainText)
    {
        main.text = richText;
        if (shadow != null) shadow.text = plainText;
    }

    // TMP centres a line inside its rect using font metrics, so the baseline usually lands
    // between two screen pixels and every horizontal stroke gets smeared over two rows.
    // Shift the text rects by that fraction so the baseline (and for left-aligned text, the
    // first glyph's origin) sits exactly on a pixel. Metrics don't change with the content,
    // so this is done once per build.
    void SnapTextToPixels(TextMeshProUGUI main, TextMeshProUGUI shadow, bool snapX)
    {
        main.ForceMeshUpdate();
        TMP_TextInfo info = main.textInfo;
        if (info == null || info.characterCount == 0 || info.lineCount == 0) return;

        float baselinePx = info.lineInfo[0].baseline * uiScale;
        float dy = (Mathf.Round(baselinePx) - baselinePx) / uiScale;

        float dx = 0f;
        if (snapX)
        {
            float originPx = info.characterInfo[0].origin * uiScale;
            dx = (Mathf.Round(originPx) - originPx) / uiScale;
        }

        Vector2 shift = new Vector2(dx, dy);
        main.rectTransform.anchoredPosition += shift;
        if (shadow != null) shadow.rectTransform.anchoredPosition += shift;
    }

    void LogFontDiagnostics(TMP_FontAsset font, int fontPx)
    {
        if (!logPlacement || font == null) return;

        string mode = font.atlasRenderMode.ToString();
        if (!mode.Contains("SDF"))
            Debug.LogWarning($"[CoreHealthHUD] Font '{font.name}' uses bitmap render mode '{mode}'. Bitmap atlases " +
                             $"can't scale cleanly and will look pixelated at {fontPx}px. Regenerate it in " +
                             "Window > TextMeshPro > Font Asset Creator with Render Mode = SDFAA.");

        Texture2D atlas = font.atlasTexture;
        if (atlas != null && atlas.filterMode == FilterMode.Point)
            Debug.LogWarning($"[CoreHealthHUD] Font atlas of '{font.name}' uses Point filtering, which makes text " +
                             "blocky. Set it to Bilinear.");
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image AddImage(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // Plain image showing a pixel-exact generated sprite.
    static Image AddShape(GameObject go, Color color, Sprite sprite)
    {
        Image img = AddImage(go, color);
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
        return img;
    }

    // Horizontally filled layer covering the whole track. fillAmount reveals the gradient
    // (rather than squashing it), and the pill shape is baked into the sprite's alpha, so the
    // left end stays round and smooth without any stencil mask.
    Image AddFilledShape(RectTransform rt, Color color, Sprite sprite, int w, int h)
    {
        Image img = AddShape(rt.gameObject, color, sprite);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        PlacePx(rt, 0, 0, w, h);
        return img;
    }

    void PlacePx(RectTransform rt, int x, int y, int w, int h) => Place(rt, U(x), U(y), U(w), U(h));

    // How far inner layers extend under the ring: far enough past the ring's inner AA edge
    // to be hidden, never past its outer edge.
    static int LayerBleed(int thick) => Mathf.Max(1, thick / 2);

    // Builds [track bg, ghost?, fill, critical?] as oversized pills under a ring on top.
    // Returns the fill image. Layer metrics let fillAmount map to the VISIBLE track width.
    Image BuildRingBar(RectTransform parent, int w, int h, int thick, Sprite fillSprite, Color fillColor,
                       Sprite criticalSprite, out Image critical, out Image ring,
                       out int layerInset, out int layerWidth, out int trackWidth)
    {
        int bleed = LayerBleed(thick);
        int layerX = thick - bleed;
        int layerW = w - layerX * 2, layerH = h - layerX * 2;

        Sprite layerShape = PillSprite(layerW, layerH);

        RectTransform layers = NewRect("CoreHUD_Layers", parent);
        PlacePx(layers, layerX, layerX, layerW, layerH);

        RectTransform trackRect = NewRect("CoreHUD_Track", layers);
        PlacePx(trackRect, 0, 0, layerW, layerH);
        AddShape(trackRect.gameObject, trackColor, layerShape);

        if (criticalSprite != null) // main bar only
            ghostImage = AddFilledShape(NewRect("CoreHUD_Ghost", layers), ghostColor, layerShape, layerW, layerH);

        Image fill = AddFilledShape(NewRect("CoreHUD_Fill", layers), fillColor, fillSprite != null ? fillSprite : layerShape, layerW, layerH);

        critical = criticalSprite != null
            ? AddFilledShape(NewRect("CoreHUD_Critical", layers), new Color(1f, 1f, 1f, 0f), criticalSprite, layerW, layerH)
            : null;

        RectTransform ringRect = NewRect("CoreHUD_Ring", parent);
        PlacePx(ringRect, 0, 0, w, h);
        ring = AddShape(ringRect.gameObject, frameColor, RingSprite(w, h, thick));

        layerInset = bleed;
        layerWidth = layerW;
        trackWidth = w - thick * 2;
        return fill;
    }

    // fillAmount that shows `pct` of the visible track, given the hidden bleed on each side.
    static float LayerFill(float pct, int inset, int layerWidth, int trackWidth) =>
        layerWidth > 0 ? Mathf.Clamp01((inset + pct * trackWidth) / layerWidth) : pct;

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }
    #endregion

    #region Procedural Sprites
    Sprite RegisterSprite(Texture2D tex, Color[] pixels, int w, int h)
    {
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels(pixels);
        tex.Apply(false, true); // upload and free the CPU copy

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        generatedAssets.Add(tex);
        generatedAssets.Add(sprite);
        return sprite;
    }

    // Anti-aliased coverage of a w x h pill (radius = half the short side) at a pixel centre.
    static float PillCoverage(float x, float y, int w, int h)
    {
        float r = Mathf.Min(w, h) * 0.5f;
        float qx = Mathf.Max(Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r), 0f);
        float qy = Mathf.Max(Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r), 0f);
        float distance = Mathf.Sqrt(qx * qx + qy * qy) - r; // signed, in pixels
        return Mathf.Clamp01(0.5f - distance);
    }

    // Pill outline of the given thickness; both edges anti-aliased.
    Sprite RingSprite(int w, int h, int thick)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        int innerW = w - thick * 2, innerH = h - thick * 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float outer = PillCoverage(x + 0.5f, y + 0.5f, w, h);
                float inner = PillCoverage(x + 0.5f - thick, y + 0.5f - thick, innerW, innerH);
                px[y * w + x] = new Color(1f, 1f, 1f, outer * (1f - inner));
            }
        return RegisterSprite(tex, px, w, h);
    }

    Sprite PillSprite(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color(1f, 1f, 1f, PillCoverage(x + 0.5f, y + 0.5f, w, h));
        return RegisterSprite(tex, px, w, h);
    }

    // 3-stop horizontal gradient with a soft vertical shade (gem look), pill-shaped alpha.
    Sprite GradientPillSprite(int w, int h, Color left, Color mid, Color right)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = h > 1 ? y / (float)(h - 1) : 1f; // 0 bottom, 1 top
            float shade = Mathf.Lerp(0.72f, 1.12f, v);
            for (int x = 0; x < w; x++)
            {
                float t = w > 1 ? x / (float)(w - 1) : 0f;
                Color c = t < 0.5f ? Color.Lerp(left, mid, t * 2f) : Color.Lerp(mid, right, (t - 0.5f) * 2f);
                px[y * w + x] = new Color(Mathf.Clamp01(c.r * shade), Mathf.Clamp01(c.g * shade), Mathf.Clamp01(c.b * shade),
                                          PillCoverage(x + 0.5f, y + 0.5f, w, h));
            }
        }
        return RegisterSprite(tex, px, w, h);
    }

    // Faceted purple crystal modelled on the Energy counter's gem, rendered at the exact
    // on-screen size with 4x4 supersampling per pixel for smooth facet edges.
    Sprite CrystalSprite(int w, int h)
    {
        const int ss = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        // Map into the 68 x 100 design space the shape was authored in.
                        float u = (x + (sx + 0.5f) / ss) / w * CrystalDesignW;
                        float v = (y + (sy + 0.5f) / ss) / h * CrystalDesignH;
                        Color c = CrystalColorAt(u, v);
                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a; // premultiplied average
                    }

                float n = ss * ss;
                a /= n;
                px[y * w + x] = a > 0.0001f ? new Color(r / n / a, g / n / a, b / n / a, a) : Color.clear;
            }
        return RegisterSprite(tex, px, w, h);
    }

    const float CrystalDesignW = 68f, CrystalDesignH = 100f;
    static readonly Color CrystalRim = new Color32(0x0A, 0x00, 0x10, 0xFF);
    static readonly Color CrystalFacetDarkL = new Color32(0x2A, 0x00, 0x3C, 0xFF);
    static readonly Color CrystalFacetDarkR = new Color32(0x4C, 0x00, 0x62, 0xFF);
    static readonly Color CrystalFacetLightL = new Color32(0x40, 0x00, 0x58, 0xFF);
    static readonly Color CrystalFacetLightR = new Color32(0x6C, 0x00, 0x88, 0xFF);
    static readonly Color CrystalHeartEdge = new Color32(0xB0, 0x00, 0xE0, 0xFF);
    static readonly Color CrystalHeartCenter = new Color32(0xFF, 0xB4, 0xFF, 0xFF);
    static readonly Color CrystalHeartRim = new Color32(0xF0, 0x40, 0xFF, 0xFF);
    static readonly Color CrystalLine = new Color32(0xC8, 0x00, 0xF0, 0xFF);
    static readonly Color CrystalGlow = new Color32(0xD8, 0x00, 0xF4, 0xFF);

    static Color CrystalColorAt(float u, float v)
    {
        const float cx = 34f, cy = 50f, halfW = 26f, halfH = 44f;
        float fx = u - cx, fy = v - cy;
        float dx = fx / halfW, dy = fy / halfH;
        float d = Mathf.Abs(dx) + Mathf.Abs(dy);

        if (d > 1f)
        {
            float glow = Mathf.Clamp01(1f - (d - 1f) / 0.3f);
            return new Color(CrystalGlow.r, CrystalGlow.g, CrystalGlow.b, glow * glow * 0.45f);
        }
        if (d > 0.86f) return CrystalRim;
        if (d < 0.40f)
        {
            float t = 1f - d / 0.40f;
            Color heart = Color.Lerp(CrystalHeartEdge, CrystalHeartCenter, t * t);
            if (dx > -0.2f && dx < -0.04f && dy > 0.04f && dy < 0.26f) heart = Color.Lerp(heart, Color.white, 0.55f);
            return heart;
        }
        if (d < 0.46f) return CrystalHeartRim;
        if (Mathf.Abs(fx) < 1.1f || Mathf.Abs(fy) < 1.1f) return CrystalLine;

        bool left = dx < 0f, upper = dy > 0f;
        Color facet = upper ? (left ? CrystalFacetLightL : CrystalFacetLightR) : (left ? CrystalFacetDarkL : CrystalFacetDarkR);
        float lift = Mathf.Lerp(1.35f, 1f, (d - 0.46f) / 0.4f); // brighter toward the heart
        return new Color(Mathf.Clamp01(facet.r * lift), Mathf.Clamp01(facet.g * lift), Mathf.Clamp01(facet.b * lift), 1f);
    }
    #endregion

    #region Update UI
    void UpdateValues(float dt)
    {
        bool dead = core.IsDestroyed();
        float pct = dead ? 0f : Mathf.Clamp01(core.GetEnergyPercentage());

        // Fill drops instantly on damage, rises smoothly on heal; the ghost trails behind.
        if (displayedPct < 0f) { displayedPct = pct; ghostPct = pct; }
        else if (pct < displayedPct) displayedPct = pct;
        else displayedPct = Mathf.MoveTowards(displayedPct, pct, dt * 0.8f);

        if (displayedPct >= ghostPct) { ghostPct = displayedPct; ghostHold = 0f; }
        else
        {
            ghostHold += dt;
            if (ghostHold > 0.35f) ghostPct = Mathf.MoveTowards(ghostPct, displayedPct, dt * 0.7f);
        }

        fillImage.fillAmount = LayerFill(displayedPct, barLayerInset, barLayerWidth, barTrackWidth);
        criticalImage.fillAmount = fillImage.fillAmount;

        // Only show the ghost while it actually extends past the fill (at least half a pixel).
        bool showGhost = (ghostPct - displayedPct) * barTrackWidth > 0.5f;
        if (ghostImage.enabled != showGhost) ghostImage.enabled = showGhost;
        if (showGhost) ghostImage.fillAmount = LayerFill(ghostPct, barLayerInset, barLayerWidth, barTrackWidth);

        EnergyManager em = EnergyManager.Instance;
        float critT = em != null ? em.GetCoreCriticalThreshold() : 0.3f;
        bool critical = !dead && pct <= critT;

        float t = Time.unscaledTime;
        Color frame = frameColor;
        Color labelTint = Color.white;
        float criticalAlpha = 0f;

        if (critical)
        {
            float pulse = Mathf.Sin(t * criticalPulseSpeed) * 0.5f + 0.5f;
            criticalAlpha = Mathf.Lerp(0.55f, 1f, pulse);
            frame = Color.Lerp(frameColor, criticalFrameColor, pulse * 0.75f);
            labelTint = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.6f, 1f), pulse);
        }

        if (hitTimer > 0f)
        {
            hitTimer -= dt;
            float k = Mathf.Clamp01(hitTimer / Mathf.Max(0.01f, hitFeedbackDuration));
            frame = Color.Lerp(frame, currentHitColor, k * 0.85f);
            iconRect.localScale = Vector3.one * (1f + 0.18f * k);
        }
        else if (iconRect.localScale != Vector3.one)
        {
            iconRect.localScale = Vector3.one;
        }

        fillImage.color = dead ? new Color(0.45f, 0.4f, 0.5f, 1f) : Color.white;
        criticalImage.color = new Color(1f, 1f, 1f, criticalAlpha);
        frameImage.color = frame;
        labelText.color = dead ? depletedTextColor : labelTint;

        UpdateShield();
    }

    void UpdateShield()
    {
        CoreShieldMatrix shield = core.GetComponent<CoreShieldMatrix>();
        bool visible = shield != null && shield.GetMaxShieldStrength() > 0f &&
                       (shield.IsShieldActive() || shield.enableShieldRecharge);

        if (visible != shieldBarVisible)
        {
            shieldBarVisible = visible;
            shieldBar.gameObject.SetActive(visible);
            panel.sizeDelta = new Vector2(panelWidthUnits, visible ? panelHeightShieldUnits : panelHeightUnits);
        }

        if (shield == null) { lastShield = -1f; return; }

        float strength = shield.GetShieldStrength();
        if (lastShield >= 0f && strength < lastShield - 0.01f) TriggerHit(shieldColor);
        lastShield = strength;

        if (visible) shieldFill.fillAmount = LayerFill(shield.GetShieldPercentage(), shieldLayerInset, shieldLayerWidth, shieldTrackWidth);
    }

    void UpdatePosition(float dt)
    {
        var hostRect = (RectTransform)hostCanvas.transform;
        Rect bounds = hostRect.rect;
        Vector2 size = panel.sizeDelta;

        // Measuring walks the HUD's graphics, so do it on a short timer, not every frame.
        layoutScanTimer -= dt;
        bool rescanned = false;
        if (layoutScanTimer <= 0f)
        {
            layoutScanTimer = 0.5f;
            rescanned = true;

            // An inactive anchor (e.g. player dead) keeps its last measured spot.
            if (WantsAnchor && anchor != null && anchor.gameObject.activeInHierarchy &&
                TryMeasureAnchor(out Rect measured))
            {
                anchorBounds = measured;
                hasAnchorPosition = true;
            }

            if (avoidOverlappingHud) RefreshObstacles();
            else obstacleRects.Clear();
        }

        Vector2 topLeft;
        if (WantsAnchor && hasAnchorPosition)
        {
            // anchorGap.y is measured to the top of the bar frame, not the panel box.
            topLeft = new Vector2(anchorBounds.xMin + anchorGap.x, anchorBounds.yMin - anchorGap.y + FrameY);
        }
        else
        {
            HudPlacement mode = WantsAnchor ? fallbackPlacement : placement;
            Vector2 o = screenEdgeOffset;
            switch (mode)
            {
                case HudPlacement.TopCenter: topLeft = new Vector2(bounds.center.x - size.x * 0.5f, bounds.yMax - o.y); break;
                case HudPlacement.TopRight: topLeft = new Vector2(bounds.xMax - o.x - size.x, bounds.yMax - o.y); break;
                case HudPlacement.BottomLeft: topLeft = new Vector2(bounds.xMin + o.x, bounds.yMin + o.y + size.y); break;
                case HudPlacement.BottomRight: topLeft = new Vector2(bounds.xMax - o.x - size.x, bounds.yMin + o.y + size.y); break;
                default: topLeft = new Vector2(bounds.xMin + o.x, bounds.yMax - o.y); break;
            }
        }

        topLeft.x = Mathf.Clamp(topLeft.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
        topLeft.y = Mathf.Min(topLeft.y, bounds.yMax);

        string pushedBy = null;
        if (avoidOverlappingHud && obstacleRects.Count > 0)
        {
            topLeft.y = PushBelowObstacles(topLeft.x, topLeft.y, out pushedBy);
        }

        topLeft.y = Mathf.Max(topLeft.y, bounds.yMin + size.y);

        if (logPlacement && rescanned && !placementLogged)
        {
            placementLogged = true;
            string source = WantsAnchor && hasAnchorPosition
                ? $"below '{(anchor != null ? GetPath(anchor) : "(removed anchor)")}' (visible bounds {anchorBounds})"
                : $"{(WantsAnchor ? fallbackPlacement : placement)} fallback" +
                  (WantsAnchor ? " - no player health UI found by name; drag it into Anchor Override if needed" : "");
            Debug.Log($"[CoreHealthHUD] Placed {source} on canvas '{hostCanvas.name}' at {topLeft}" +
                      (pushedBy != null ? $", moved down to clear '{pushedBy}'." : "."));
        }

        if (hitTimer > 0f && hitShakeStrength > 0f)
        {
            float k = hitTimer / Mathf.Max(0.01f, hitFeedbackDuration);
            topLeft += Random.insideUnitCircle * hitShakeStrength * k;
        }

        // Snap to whole screen pixels so the pixel-exact textures stay 1:1 (no resampling blur).
        topLeft.x = bounds.xMin + Mathf.Round((topLeft.x - bounds.xMin) * uiScale) / uiScale;
        topLeft.y = bounds.yMax - Mathf.Round((bounds.yMax - topLeft.y) * uiScale) / uiScale;

        Vector3 target = new Vector3(topLeft.x, topLeft.y, 0f);
        if (panel.localPosition != target) panel.localPosition = target;
    }

    void UpdateFade(float dt)
    {
        float targetAlpha = 1f;

        if (hideWhenCoreOnScreen && !core.IsDestroyed())
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(core.transform.position);
                bool onScreen = vp.z > 0f &&
                                vp.x > onScreenMargin && vp.x < 1f - onScreenMargin &&
                                vp.y > onScreenMargin && vp.y < 1f - onScreenMargin;
                if (onScreen) targetAlpha = 0f;
            }
        }

        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, dt * 4f);
    }
    #endregion
}



