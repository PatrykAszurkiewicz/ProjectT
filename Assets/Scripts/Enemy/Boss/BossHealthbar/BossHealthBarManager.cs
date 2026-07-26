using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Owns every top-of-screen boss health bar.
//
// SPLIT SCREEN
// The bars live on a dedicated Screen-Space-OVERLAY canvas that this manager
// creates. Overlay canvases are drawn once, over the whole backbuffer, and are
// completely independent of camera viewport rects - so a single bar spans the
// full width and looks identical in 1P and 2P, with no duplication and nothing
// to keep in sync between the two halves.
//
// AVOIDING THE PLAYER HUD
// In co-op the second player's health/stamina bars appear in the top-RIGHT, which
// is exactly where a centred boss bar wants to be. Rather than hard-coding a
// co-op offset, the manager measures the player HUD's screen rect and drops the
// boss bar below whatever is actually up there. Single player is unaffected,
// because P2's bars are hidden with CanvasGroup alpha and the visibility check
// skips them.
//
// TIMING
// A bar is created the moment its boss registers (BaseBossStats.Start) but stays
// hidden until BossZoomController has finished its intro cinematic and handed
// control back to the gameplay cameras. If no cinematic plays the bar simply
// reveals after a short window, so nothing depends on the cinematic existing.
[DisallowMultipleComponent]
public class BossHealthBarManager : MonoBehaviour
{
    public static BossHealthBarManager Instance { get; private set; }

    public enum HudAvoidMode
    {
        Off,                 // always use topMargin
        ExtraMarginInCoop,   // add a fixed offset when 2+ players are registered
        AutoMeasure          // measure the player HUD and sit below it (default)
    }

    [Header("Prefab")]
    [Tooltip("The BossBar prefab. If left empty, it is loaded from Resources using the path below.")]
    [SerializeField] private GameObject bossBarPrefab;
    [Tooltip("Fallback Resources path, e.g. Assets/Resources/UI/BossBar.prefab -> \"UI/BossBar\".")]
    [SerializeField] private string resourcesFallbackPath = "UI/BossBar";

    [Header("Canvas")]
    [Tooltip("Optional. Assign an existing Screen Space - Overlay canvas to host the bars. " +
             "Leave empty and a dedicated one is created at runtime.")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private int canvasSortingOrder = 500;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

    [Header("Placement")]
    [Tooltip("Anchor the bar to the top-centre of the screen. Off = keep the prefab's own " +
             "anchors and position exactly as authored.")]
    [SerializeField] private bool anchorToTop = true;
    [Tooltip("Minimum pixels (at reference resolution) between the top of the screen and the " +
             "top of the bar. HUD avoidance can push the bar further down, never higher.")]
    [SerializeField] private float topMargin = 24f;
    [Tooltip("Vertical gap between stacked bars when more than one boss is alive.")]
    [SerializeField] private float stackSpacing = 14f;

    [Header("Avoid the player HUD")]
    [Tooltip("Off = fixed topMargin. ExtraMarginInCoop = add a set offset with 2+ players. " +
             "AutoMeasure = measure the player health/stamina bars and sit below them.")]
    [SerializeField] private HudAvoidMode hudAvoidance = HudAvoidMode.AutoMeasure;
    [Tooltip("Gap left between the bottom of the player HUD and the top of the boss bar.")]
    [SerializeField] private float hudClearance = 18f;
    [Tooltip("ExtraMarginInCoop only: pixels added to topMargin when 2+ players are registered.")]
    [SerializeField] private float coopExtraMargin = 130f;
    [Tooltip("AutoMeasure only: ignore HUD elements that don't overlap the boss bar horizontally. " +
             "This is what keeps single player unchanged - P1's bars sit off to the left.")]
    [SerializeField] private bool onlyOverlappingHud = true;
    [Tooltip("AutoMeasure only: ignore anything whose bottom edge is below this fraction of the " +
             "screen height, so bottom-of-screen HUD never pushes the boss bar down.")]
    [Range(0f, 1f)][SerializeField] private float hudScanTopFraction = 0.5f;
    [Tooltip("AutoMeasure only: seconds between re-measurements. Players can join mid-run.")]
    [SerializeField] private float hudRescanInterval = 0.5f;
    [Tooltip("AutoMeasure only: extra HUD roots to clear that aren't a HealthBarUI or StaminaBarUI.")]
    [SerializeField] private List<RectTransform> additionalHudElements = new List<RectTransform>();

    [Header("Boss intro")]
    [Tooltip("Hold the bar back until the BossZoomController cinematic has finished.")]
    [SerializeField] private bool waitForBossIntro = true;
    [Tooltip("How long to wait for a cinematic to START before giving up and showing the bar.")]
    [SerializeField] private float introDetectWindow = 0.75f;
    [Tooltip("Beat between the camera settling back and the bar dropping in.")]
    [SerializeField] private float revealDelayAfterIntro = 0.15f;
    [Tooltip("Hide the bars again if another cinematic plays later (e.g. a second boss's intro).")]
    [SerializeField] private bool hideDuringCinematics = true;

    [Header("Bar behaviour")]
    [Tooltip("Count the boss's armour pool as part of the bar, so it drains through armour and " +
             "then health as one continuous track.")]
    [SerializeField] private bool includeArmorInPool = true;

    private readonly List<BossHealthBarUI> _bars = new List<BossHealthBarUI>();
    private RectTransform _canvasRoot;
    private Canvas _canvas;
    private Camera _uiCamera;

    private float _hudMargin;          // extra top margin from HUD avoidance
    private float _hudRescanTimer;

    private static readonly List<RectTransform> _hudScratch = new List<RectTransform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // -- entry points ----------------------------------------------------

    /// Called by BaseBossStats.Start. Creates the manager on demand.
    public static void Show(BaseBossStats boss, GameObject prefabOverride = null)
    {
        if (boss == null) return;

        if (Instance == null)
        {
            var existing = FindAnyObjectByType<BossHealthBarManager>();
            Instance = existing != null
                ? existing
                : new GameObject("BossHealthBarManager").AddComponent<BossHealthBarManager>();
        }

        Instance.ShowInternal(boss, prefabOverride);
    }

    /// Remove a boss's bar early (without the death effect).
    public static void Hide(BaseBossStats boss)
    {
        if (Instance == null || boss == null) return;
        foreach (var bar in Instance._bars)
            if (bar != null && bar.Boss == boss) bar.FadeOutAndDestroy();
    }

    private void ShowInternal(BaseBossStats boss, GameObject prefabOverride)
    {
        foreach (var b in _bars)
            if (b != null && b.Boss == boss) return;     // already tracked

        GameObject prefab = prefabOverride != null ? prefabOverride : ResolvePrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[BossHealthBarManager] No BossBar prefab assigned and none found at " +
                             $"Resources/{resourcesFallbackPath}. The top-of-screen boss bar is disabled.");
            return;
        }

        var root = EnsureCanvas();
        if (root == null) return;

        var go = Instantiate(prefab, root, false);
        go.name = $"BossBar_{boss.name}";

        var ui = go.GetComponent<BossHealthBarUI>();
        if (ui == null) ui = go.AddComponent<BossHealthBarUI>();

        ui.Bind(boss, includeArmorInPool);
        _bars.Add(ui);

        _hudMargin = ComputeHudMargin();   // measure before the first placement
        _hudRescanTimer = hudRescanInterval;
        Layout();

        StartCoroutine(RevealWhenReady(ui));
    }

    private GameObject ResolvePrefab()
    {
        if (bossBarPrefab != null) return bossBarPrefab;
        if (string.IsNullOrEmpty(resourcesFallbackPath)) return null;
        bossBarPrefab = Resources.Load<GameObject>(resourcesFallbackPath);
        return bossBarPrefab;
    }

    // -- canvas ----------------------------------------------------------

    private RectTransform EnsureCanvas()
    {
        if (_canvasRoot != null) return _canvasRoot;

        if (targetCanvas != null)
        {
            _canvas = targetCanvas;
            _canvasRoot = targetCanvas.transform as RectTransform;
            CacheUiCamera();
            return _canvasRoot;
        }

        var go = new GameObject("BossBarCanvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);

        _canvas = go.GetComponent<Canvas>();
        // Overlay: one draw over the whole screen, immune to split-screen camera rects.
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = canvasSortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // No GraphicRaycaster on purpose: the bar is pure decoration.

        _canvasRoot = (RectTransform)go.transform;
        CacheUiCamera();
        return _canvasRoot;
    }

    private void CacheUiCamera()
    {
        _uiCamera = null;
        if (_canvas == null) return;
        var root = _canvas.rootCanvas;
        if (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay)
            _uiCamera = root.worldCamera;
    }

    // -- reveal timing ---------------------------------------------------

    private IEnumerator RevealWhenReady(BossHealthBarUI ui)
    {
        if (waitForBossIntro)
        {
            float t = 0f;
            bool sawCinematic = false;
            while (t < introDetectWindow)
            {
                if (IsCinematicRunning()) { sawCinematic = true; break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (sawCinematic)
                while (IsCinematicRunning()) yield return null;
        }

        if (revealDelayAfterIntro > 0f)
            yield return new WaitForSecondsRealtime(revealDelayAfterIntro);

        if (ui != null) ui.Reveal();
    }

    private static bool IsCinematicRunning()
    {
        if (BossZoomController.CinematicActive) return true;
        var c = BossZoomController.Instance;
        return c != null && c.IsPlaying;
    }

    // -- per-frame -------------------------------------------------------

    private void LateUpdate()
    {
        bool dirty = false;
        for (int i = _bars.Count - 1; i >= 0; i--)
        {
            if (_bars[i] == null) { _bars.RemoveAt(i); dirty = true; }
        }

        if (_bars.Count > 0 && hudAvoidance != HudAvoidMode.Off)
        {
            _hudRescanTimer -= Time.unscaledDeltaTime;
            if (_hudRescanTimer <= 0f)
            {
                _hudRescanTimer = Mathf.Max(0.1f, hudRescanInterval);
                float m = ComputeHudMargin();
                // Small deadband so a 1px HUD wobble doesn't re-lay-out every scan.
                if (Mathf.Abs(m - _hudMargin) > 1f) { _hudMargin = m; dirty = true; }
            }
        }

        if (dirty) Layout();

        if (hideDuringCinematics)
        {
            bool suppress = IsCinematicRunning();
            foreach (var bar in _bars)
                if (bar != null) bar.SetSuppressed(suppress);
        }
    }

    // Stack the bars downward from the top of the screen, below the player HUD.
    private void Layout()
    {
        float y = Mathf.Max(topMargin, _hudMargin);

        foreach (var bar in _bars)
        {
            if (bar == null) continue;
            var rt = bar.Rect;
            float h = Mathf.Max(1f, bar.VisualHeight);

            if (anchorToTop)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                bar.SetBasePosition(new Vector2(0f, -(y + h * 0.5f)));
            }
            else
            {
                // Keep the prefab's authored placement; offset for stacking and for
                // however far the HUD pushed us down (both already folded into y).
                bar.SetBasePosition(new Vector2(bar.AuthoredPosition.x,
                                                bar.AuthoredPosition.y - (y - topMargin)));
            }

            y += h + stackSpacing;
        }
    }

    // -- HUD avoidance ---------------------------------------------------

    private float ComputeHudMargin()
    {
        switch (hudAvoidance)
        {
            case HudAvoidMode.ExtraMarginInCoop:
                return PlayerRegistry.Count >= 2 ? topMargin + coopExtraMargin : topMargin;

            case HudAvoidMode.AutoMeasure:
                return MeasureHudMargin();

            default:
                return topMargin;
        }
    }

    /// Finds the lowest visible player-HUD element in the top of the screen and
    /// returns the top margin (in canvas units) needed to clear it.
    private float MeasureHudMargin()
    {
        if (_canvasRoot == null) return topMargin;

        CollectHudRects(_hudScratch);
        if (_hudScratch.Count == 0) return topMargin;

        TryGetBarScreenXRange(out float barMinX, out float barMaxX);

        float cutoffY = Screen.height * hudScanTopFraction;
        float lowestBottom = float.MaxValue;
        bool found = false;

        foreach (var rt in _hudScratch)
        {
            if (rt == null) continue;
            if (!IsEffectivelyVisible(rt)) continue;      // P2's bars in single player

            Rect r = GetScreenRect(rt);
            if (r.height <= 1f || r.width <= 1f) continue;
            if (r.yMin < cutoffY) continue;               // not part of the top HUD
            if (onlyOverlappingHud && (r.xMax < barMinX || r.xMin > barMaxX)) continue;

            if (r.yMin < lowestBottom) { lowestBottom = r.yMin; found = true; }
        }

        if (!found) return topMargin;

        // Convert that screen Y into "distance below the canvas top".
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRoot, new Vector2(Screen.width * 0.5f, lowestBottom), _uiCamera, out Vector2 local))
            return topMargin;

        float canvasTop = _canvasRoot.rect.yMax;
        return Mathf.Max(topMargin, canvasTop - local.y + hudClearance);
    }

    private void CollectHudRects(List<RectTransform> into)
    {
        into.Clear();

        foreach (var hb in FindObjectsByType<HealthBarUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (hb != null && hb.transform is RectTransform hbRect) into.Add(hbRect);
        }

        foreach (var sb in FindObjectsByType<StaminaBarUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sb != null && sb.transform is RectTransform sbRect) into.Add(sbRect);
        }

        foreach (var extra in additionalHudElements)
        {
            if (extra != null) into.Add(extra);
        }
    }

    /// Screen-space rect of a RectTransform INCLUDING its children, so the whole
    /// bar group (frame, shadow, blur, particles) is measured, not just the root.
    private static Rect GetScreenRect(RectTransform rt)
    {
        Camera cam = null;
        var canvas = rt.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var root = canvas.rootCanvas;
            if (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = root.worldCamera;
        }

        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(rt);
        Vector3 min = b.min, max = b.max;

        float x0 = float.MaxValue, y0 = float.MaxValue;
        float x1 = float.MinValue, y1 = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            Vector3 localCorner = new Vector3(
                (i == 0 || i == 1) ? min.x : max.x,
                (i == 0 || i == 2) ? min.y : max.y,
                0f);
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(localCorner));
            x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
            y0 = Mathf.Min(y0, sp.y); y1 = Mathf.Max(y1, sp.y);
        }

        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// Walks up the parents multiplying CanvasGroup alphas. HealthBarUI and
    /// StaminaBarUI hide absent players with alpha rather than SetActive, so this
    /// is what keeps a hidden P2 bar from pushing the boss bar down in 1P.
    private static bool IsEffectivelyVisible(RectTransform rt)
    {
        if (rt == null || !rt.gameObject.activeInHierarchy) return false;

        float alpha = 1f;
        Transform t = rt;
        while (t != null)
        {
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                alpha *= cg.alpha;
                if (alpha <= 0.05f) return false;
                if (cg.ignoreParentGroups) break;
            }

            var canvas = t.GetComponent<Canvas>();
            if (canvas != null && canvas.isRootCanvas) break;

            t = t.parent;
        }

        return alpha > 0.05f;
    }

    private bool TryGetBarScreenXRange(out float minX, out float maxX)
    {
        minX = 0f;
        maxX = Screen.width;

        foreach (var bar in _bars)
        {
            if (bar == null) continue;
            Rect r = GetScreenRect(bar.Rect);
            if (r.width <= 1f) continue;
            minX = r.xMin;
            maxX = r.xMax;
            return true;
        }
        return false;
    }
}


