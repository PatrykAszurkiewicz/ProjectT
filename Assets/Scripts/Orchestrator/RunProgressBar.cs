using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Dynamic run-progress bar.
// Drag sprites into the Inspector, one per phase + state (bright/dark).
// REQUIRED PREFAB STRUCTURE (minimal):
//   StagesTest (this script attaches here)
//     ├── PanelIcons   (empty RectTransform — generated icons go here)
//     ├── PanelLines   (empty RectTransform — generated lines go here)
//     └── HighLight    (Image with your highlight sprite — moved over current node)

[DisallowMultipleComponent]
public class RunProgressBar : MonoBehaviour
{
    //   PHASES  

    public enum SlotPhase
    {
        Wave,
        Augment,
        Chest,
        HeartChoice,
        MiniBoss,
        Boss,
        FinalBoss,
    }

    [System.Serializable]
    public class PhaseSprites
    {
        [Tooltip("Sprite shown when this phase is upcoming or current (full colour).")]
        public Sprite bright;

        [Tooltip("Sprite shown when this phase is already completed (faded). Optional — leave empty to auto-tint the bright sprite grey.")]
        public Sprite dark;
    }

    //   CONFIG  

    [Header("═══ ICON SPRITES (drag in Inspector) ═══")]
    [Tooltip("Sprites for each phase. Leave empty to auto-load by filename from Resources/Sprites/HUD/Stages.")]
    public PhaseSprites waveSprites;
    public PhaseSprites augmentSprites;
    public PhaseSprites chestSprites;
    public PhaseSprites heartChoiceSprites;
    public PhaseSprites miniBossSprites;
    public PhaseSprites bossSprites;
    public PhaseSprites finalBossSprites;

    [Header("═══ CONNECTOR / HIGHLIGHT SPRITES ═══")]
    public Sprite lineBright;
    public Sprite lineDark;
    [Tooltip("Optional override — if the HighLight child in the prefab has a sprite already, leave this empty.")]
    public Sprite highlightSpriteOverride;

    [Tooltip("If false, completed icons stay bright (no dark/bright swap). Useful if you only want one set of sprites.")]
    public bool useDarkVariants = true;

    [Header(" TIMING ")]
    [Min(0f)] public float fadeInDuration = 0.35f;
    [Min(0f)] public float fadeOutDuration = 0.35f;

    [Header(" LAYOUT ")]
    [Tooltip("Maximum total width the bar may occupy.")]
    public float maxBarWidth = 1700f;

    [Tooltip("Default icon size when the run is small enough to fit comfortably.")]
    public float preferredIconSize = 138f;

    [Tooltip("Minimum icon size — won't shrink below this.")]
    public float minIconSize = 52f;

    [Tooltip("Spacing between icons as a fraction of icon size.")]
    [Range(0.1f, 1.5f)] public float spacingFraction = 0.5f;

    [Tooltip("Line connector size relative to icon size (height multiplier).")]
    [Range(0.05f, 2f)] public float lineHeightFraction = 0.28f;

    [Tooltip("How wide each line is, as a fraction of the EDGE-TO-EDGE gap between two icons.\n" +
             "1.0 = line exactly fills the gap, touching both icons.\n" +
             "0.85 = line is 85% of the gap, leaving small breathing room (recommended).\n" +
             "1.15 = line slightly tucks under each icon (creates an 'embrace' effect).")]
    [Range(0.3f, 1.5f)] public float lineWidthFraction = 0.85f;

    [Tooltip("If true, line sprites preserve their natural aspect ratio (recommended — prevents stretching).")]
    public bool linePreserveAspect = true;

    [Tooltip("Boss / final-boss icons drawn this much larger than regular icons.")]
    [Min(1f)] public float bossSizeMultiplier = 1.15f;

    [Header("═══ WINDOW / ZOOM ═══")]
    [Tooltip("ON: show only a WINDOW of icons centred on the active node, so a long run stays " +
             "readable and 'zoomed in' on where you are. OFF: lay out the whole timeline at once " +
             "(original behaviour — overflows badly on very long runs).")]
    public bool windowed = true;

    [Tooltip("How many icons to show on EACH side of the active node.\n" +
             "Total visible = 2×radius + 1  (e.g. 4 → 9 icons).")]
    [Min(1)] public int windowRadius = 4;

    [Tooltip("Fade the outermost icon on a side when the timeline continues past the window, " +
             "so the player can tell there's more before/after the visible portion.")]
    public bool fadeWindowEdges = true;

    [Tooltip("Alpha applied to a faded edge icon (when 'Fade Window Edges' is on).")]
    [Range(0.05f, 1f)] public float windowEdgeAlpha = 0.35f;

    [Tooltip("Current node icon scaled up by this factor.")]
    [Min(1f)] public float currentIconScale = 1.18f;

    [Header("═══ HIGHLIGHT ═══")]
    [Tooltip("Highlight size relative to the icon it sits behind. 1.0 = same size; 1.6 = 60% larger.")]
    [Min(0.5f)] public float highlightScale = 1.65f;

    [Tooltip("Gentle scale pulse on the highlight. 0 = off.")]
    [Range(0f, 0.3f)] public float highlightPulseAmplitude = 0.10f;

    [Min(0f)] public float highlightPulseSpeed = 1.2f;

    [Tooltip("Rotation speed in degrees/sec for the highlight. 0 = static.")]
    public float highlightRotationSpeed = 18f;

    [Tooltip("Tint applied to the highlight image.")]
    public Color highlightTint = Color.white;

    [Tooltip("Tint applied to completed (dark) icons that don't have a dark sprite.")]
    public Color completedFallbackTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("═══ RENDER ORDER ═══")]
    [Tooltip("Force the bar to render ON TOP of other UI (e.g. the weapon-roll popup) by giving it " +
             "its own sorting canvas. Turn OFF to fall back to normal hierarchy order.")]
    public bool forceOnTop = true;

    [Tooltip("Sorting order used when 'Force On Top' is on. Higher = drawn later (more on top).\n" +
             "Reference points in this project: ARCHIVE button canvas = 9990, opened Archive menu = 9996, " +
             "lore scroll popup = 9998. Default 9995 sits ABOVE the persistent ARCHIVE button but BELOW " +
             "the modal archive menu / lore scroll (which should cover the HUD when open).")]
    public int sortingOrder = 9995;

    [Tooltip("Optional sorting layer to place the bar on. Leave EMPTY to keep the current/default " +
             "layer (recommended unless your weapon-roll UI lives on a higher sorting layer).")]
    public string sortingLayerName = "";

    [Header(" DEBUG ")]
    public bool debugLog = true;

    [Tooltip("If true, the bar starts visible and stays visible — useful for debugging positioning. " +
             "Wave-start/Hide events are ignored while this is on.")]
    public bool alwaysVisible = false;

    [Tooltip("DIAGNOSTIC: if false, the bar will NOT subscribe to orchestrator events. " +
             "Use this to confirm whether the bar's event handlers are interfering with other systems " +
             "(e.g. the post-stage reward menu). If unticking this fixes a bug elsewhere, the issue is " +
             "in this script's event handling.")]
    public bool enableEventHandling = true;

    //   RUNTIME  

    private RectTransform iconsPanelRT;
    private RectTransform linesPanelRT;
    private RectTransform highlightRT;
    private Image highlightImage;

    // The StagesTest visual root that actually owns the panels — the GameObject we
    // reparent onto our dedicated sorting canvas (may differ from `this`).
    private RectTransform barRootRT;

    // Dedicated root overlay canvas we create so the bar sorts above other root canvases.
    private GameObject ownCanvasGO;

    private struct Node
    {
        public SlotPhase phase;
        public int stageIndex;   // -1 for FinalBoss
        public int waveIndex;    // -1 if N/A
    }

    private List<Node> nodes = new List<Node>();
    private List<Image> nodeIcons = new List<Image>();
    private List<RectTransform> nodeRects = new List<RectTransform>();
    private List<Image> lineIcons = new List<Image>();
    private int currentNodeIndex = -1;
    private float effectiveIconSize;

    // Which slice of `nodes` is currently realised as views (windowed mode).
    // In full mode these span the whole list. nodeIcons[i] ↔ nodes[windowStart + i].
    private int windowStart;
    private int windowCount;
    private bool windowBuilt;

    private CanvasGroup canvasGroup;
    private Coroutine fadeRoutine;
    private bool subscribed;
    private bool builtForRun;
    private bool diagnosedOnce;

    //   LIFECYCLE  

    private void Awake()
    {
        ApplyDefaultsIfUnconfigured();
        DiscoverPrefabChildren();
        if (!enabled) return;          // soft-failed inside DiscoverPrefabChildren
        EnsureCanvasGroup();
        EnsureTopmostCanvas();
        ResolveAutoLoadSprites();
        ClearStockChildren();
        if (!alwaysVisible) FadeTo(0f);
        else FadeTo(1f);
    }

    private void OnEnable() { TrySubscribe(); }
    private void OnDisable() { Unsubscribe(); }

    private void Start()
    {
        TrySubscribe();
        TryBuildTimeline();
        if (!diagnosedOnce) { diagnosedOnce = true; DiagnoseLayering(); }
    }

    private void Update()
    {
        if (!subscribed) TrySubscribe();
        if (!builtForRun) TryBuildTimeline();
        UpdateHighlightAnimation();
    }

    //   PREFAB DISCOVERY  

    private void DiscoverPrefabChildren()
    {
        // Try our own subtree first (script attached directly to the StagesTest root).
        iconsPanelRT = FindChildByName(transform, "PanelIcons") as RectTransform;
        linesPanelRT = FindChildByName(transform, "PanelLines") as RectTransform;
        var hl = FindChildByName(transform, "HighLight");
        if (iconsPanelRT != null) barRootRT = transform as RectTransform;

        // If not found locally, search the entire scene for a StagesTest GameObject and use
        // its children. This makes the script work whether it's attached to the StagesTest
        // root, the GameOrchestrator, or anywhere else.
        if (iconsPanelRT == null)
        {
            var allRects = Resources.FindObjectsOfTypeAll<RectTransform>();
            foreach (var rt in allRects)
            {
                if (rt == null || rt.gameObject == null) continue;
                if (rt.gameObject.scene.IsValid() == false) continue; // skip prefab assets
                if (rt.name != "StagesTest") continue;

                iconsPanelRT = FindChildByName(rt, "PanelIcons") as RectTransform;
                linesPanelRT = FindChildByName(rt, "PanelLines") as RectTransform;
                hl = FindChildByName(rt, "HighLight");
                if (iconsPanelRT != null)
                {
                    barRootRT = rt;
                    if (debugLog)
                        Debug.Log($"[RunProgressBar] Found StagesTest in scene at '{GetPath(rt)}'.");
                    break;
                }
            }
        }

        if (hl != null)
        {
            highlightRT = hl as RectTransform;
            highlightImage = hl.GetComponent<Image>();
        }

        if (iconsPanelRT == null)
        {
            // Soft-fail: log a single warning and disable the bar instead of erroring repeatedly.
            // The bar is non-critical UI; missing it should not break the game.
            Debug.LogWarning("[RunProgressBar] Could not find a 'PanelIcons' child anywhere in the scene. " +
                             "The progress bar will be disabled. " +
                             "To fix: add the StagesTest prefab to your scene, or attach this script to it.");
            enabled = false;
        }
    }

    private static string GetPath(Transform t)
    {
        string p = t.name;
        var cur = t.parent;
        while (cur != null) { p = cur.name + "/" + p; cur = cur.parent; }
        return p;
    }

    private static Transform FindChildByName(Transform parent, string n)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == n) return parent.GetChild(i);
        for (int i = 0; i < parent.childCount; i++)
        {
            var f = FindChildByName(parent.GetChild(i), n);
            if (f != null) return f;
        }
        return null;
    }

    private void EnsureCanvasGroup()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    /// Unity serialization gotcha guard. When these fields were ADDED to the script, an
    /// already-placed component keeps its old serialized data and the NEW fields arrive at
    /// their TYPE defaults (false / 0), ignoring the C# initializers above. The fingerprint
    /// "everything zero at once" means the bar was never reconfigured for these features, so
    /// we apply the intended defaults at runtime instead of silently rendering wrong (full
    /// stretched bar, no on-top canvas). If the user deliberately changed ANY of these, the
    /// fingerprint won't match and we leave their choices alone.
    private void ApplyDefaultsIfUnconfigured()
    {
        bool looksUnconfigured = !forceOnTop && sortingOrder == 0 && !windowed && windowRadius == 0;
        if (!looksUnconfigured) return;

        forceOnTop = true;
        sortingOrder = 9995;
        windowed = true;
        windowRadius = 4;
        fadeWindowEdges = true;
        if (windowEdgeAlpha <= 0f) windowEdgeAlpha = 0.35f;

        Debug.LogWarning(
            "[RunProgressBar] New Inspector fields were at their serialized defaults " +
            "(Force On Top off, Sorting Order 0, Windowed off, Window Radius 0) — Unity kept the " +
            "OLD component data when the fields were added, ignoring the script defaults. " +
            "Applied runtime defaults: on-top @ sortingOrder 9995, windowed (radius 4). " +
            "To make this permanent, set those values in the Inspector on the RunProgressBar component " +
            "(or use the component gear menu → 'Fix Layering + Window Defaults').");
    }

    // Gear-menu helper: fixes ONLY the layering/window fields without touching assigned sprites
    // (unlike the component's built-in Reset, which would wipe sprite references).
    [ContextMenu("Fix Layering + Window Defaults")]
    private void FixLayeringAndWindowDefaults()
    {
        forceOnTop = true;
        sortingOrder = 9995;
        windowed = true;
        windowRadius = Mathf.Max(1, windowRadius == 0 ? 4 : windowRadius);
        fadeWindowEdges = true;
        if (windowEdgeAlpha <= 0f) windowEdgeAlpha = 0.35f;
        Debug.Log("[RunProgressBar] Layering + window defaults applied (on-top @9995, windowed). " +
                  "Save the scene/prefab to persist.");
    }

    /// Move the bar onto its OWN root Screen Space - Overlay canvas so it competes in the
    /// global overlay sort by `sortingOrder`. A nested canvas with overrideSorting can only
    /// reorder the bar WITHIN its parent root canvas — it cannot rise above a *separate* root
    /// canvas (e.g. the ARCHIVE button's canvas at 9990, or the lore popup at 9998). The other
    /// procedural UIs in this project each create their own root canvas for exactly this reason.
    private void EnsureTopmostCanvas()
    {
        if (!forceOnTop)
        {
            Debug.LogWarning(
                "[RunProgressBar] 'Force On Top' is OFF on this component — the bar stays on the main HUD " +
                "canvas and will render UNDER other root canvases (e.g. the ARCHIVE button @9990). " +
                "If that's not what you want: tick 'Force On Top' + set 'Sorting Order' 9995 in the Inspector, " +
                "or use the component gear-menu → 'Fix Layering + Window Defaults', then save.");
            return;
        }

        // Safety: a stale serialized 0 would put us at the bottom of the overlay sort. If we've
        // been asked to force on top, a non-positive order is never intended — use a high default.
        if (sortingOrder <= 0) sortingOrder = 9995;

        // The visual root that owns the panels (normally `this` / the StagesTest object).
        var root = (barRootRT != null ? barRootRT : transform as RectTransform);
        if (root == null) return;

        // Already hosted on our dedicated canvas? Just refresh the order and bail.
        if (ownCanvasGO != null && root.parent == ownCanvasGO.transform)
        {
            var existing = ownCanvasGO.GetComponent<Canvas>();
            if (existing != null)
            {
                existing.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayerName)) existing.sortingLayerName = sortingLayerName;
            }
            return;
        }

        // Build a dedicated root overlay canvas (mirrors LoreArchiveMenu / LoreScrollPopup).
        ownCanvasGO = new GameObject("RunProgressBarCanvas");
        var canvas = ownCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;          // competes globally now (root canvas)
        if (!string.IsNullOrEmpty(sortingLayerName))
            canvas.sortingLayerName = sortingLayerName;

        // Same scaler the rest of the UI uses, so size/responsiveness match.
        var scaler = ownCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster — the bar is display-only (CanvasGroup.blocksRaycasts = false).

        // Reparent the bar under our canvas, KEEPING its current on-screen position and size
        // (worldPositionStays = true), so moving it between canvases doesn't make it jump.
        root.SetParent(ownCanvasGO.transform, worldPositionStays: true);

        if (debugLog)
            Debug.Log($"[RunProgressBar] Hosted on dedicated root canvas (sortingOrder={sortingOrder}" +
                      (string.IsNullOrEmpty(sortingLayerName) ? "" : $", layer='{sortingLayerName}'") +
                      $"). Now competes globally — above ARCHIVE button (9990).");
    }

    // One-shot (and gear-menu) diagnostic that prints the exact state of the bar's layering:
    // how many bars/StagesTest objects exist, this component's live settings, whether the
    // dedicated canvas was created, the full parent→canvas chain, and any Archive canvases'
    // orders — so we can see *why* it is or isn't on top. Always logs (not gated by debugLog).
    [ContextMenu("Diagnose Layering")]
    public void DiagnoseLayering()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("════════ [RunProgressBar] LAYERING DIAGNOSTIC ════════");

        var bars = FindObjectsByType<RunProgressBar>(FindObjectsSortMode.None);
        int stagesCount = 0;
        foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rt == null || !rt.gameObject.scene.IsValid()) continue;
            if (rt.name == "StagesTest") stagesCount++;
        }
        sb.AppendLine($"RunProgressBar instances in scene: {bars.Length}   |   'StagesTest' objects: {stagesCount}" +
                      (bars.Length > 1 || stagesCount > 1 ? "   ⚠ MORE THAN ONE — the wrong copy may be the visible bar!" : ""));

        sb.AppendLine($"This component: GO='{gameObject.name}', enabled={enabled}, activeInHierarchy={gameObject.activeInHierarchy}");
        sb.AppendLine($"Live settings: forceOnTop={forceOnTop}, sortingOrder={sortingOrder}, windowed={windowed}, windowRadius={windowRadius}");
        sb.AppendLine($"barRootRT='{(barRootRT != null ? barRootRT.name : "NULL (panels not found — soft-failed?)")}', " +
                      $"ownCanvasGO={(ownCanvasGO != null ? "CREATED (reparent happened)" : "NULL (NO reparent — not on its own canvas)")}");

        // Walk from the visual root upward, noting every Canvas (this is what actually decides draw order).
        var node = (barRootRT != null ? barRootRT.transform : transform);
        sb.AppendLine("Parent chain (bar → up):");
        int guard = 0;
        while (node != null && guard++ < 50)
        {
            var cv = node.GetComponent<Canvas>();
            string c = cv != null
                ? $"   ◀ Canvas[renderMode={cv.renderMode}, overrideSorting={cv.overrideSorting}, sortingOrder={cv.sortingOrder}, layer='{cv.sortingLayerName}', isRoot={cv.isRootCanvas}]"
                : "";
            sb.AppendLine($"   • {node.name}{c}");
            node = node.parent;
        }

        // List every overlay canvas in the scene with its order, so we can see who wins.
        sb.AppendLine("All scene canvases (name : renderMode : sortingOrder):");
        foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (cv == null || !cv.gameObject.scene.IsValid()) continue;
            if (!cv.isRootCanvas && !cv.overrideSorting) continue; // only sorting-relevant canvases
            sb.AppendLine($"   • '{cv.name}' : {cv.renderMode} : {cv.sortingOrder}" +
                          (cv.name.ToLower().Contains("archive") ? "   ← ARCHIVE" : "") +
                          (cv == (ownCanvasGO != null ? ownCanvasGO.GetComponent<Canvas>() : null) ? "   ← THIS BAR" : ""));
        }
        sb.AppendLine("══════════════════════════════════════════════════════");
        Debug.Log(sb.ToString());
    }

    /// For any sprite slot the user left empty in the Inspector, try to load from Resources by filename.
    /// Naming convention assumed: 1Enemies / 7Enemies (bright/dark pairs); LineBright / LineDark.
    private void ResolveAutoLoadSprites()
    {
        TryAutoFill(ref waveSprites, "1Enemies", "7Enemies");
        TryAutoFill(ref heartChoiceSprites, "2HeartOrCard", "8HeartOrCard");
        TryAutoFill(ref chestSprites, "3Chest", "9Chest");
        TryAutoFill(ref augmentSprites, "4Augments", "10Augments");
        TryAutoFill(ref miniBossSprites, "5Gremlin", "11Gremlin");
        TryAutoFill(ref bossSprites, "6Boss", "12Boss");
        TryAutoFill(ref finalBossSprites, "6Boss", "12Boss"); // fallback to stage boss

        if (lineBright == null) lineBright = LoadFromHud("LineBright");
        if (lineDark == null) lineDark = LoadFromHud("LineDark");

        if (debugLog)
        {
            Debug.Log($"[RunProgressBar] Sprite resolution:" +
                      $"\n  wave: bright={Name(waveSprites?.bright)} dark={Name(waveSprites?.dark)}" +
                      $"\n  augment: bright={Name(augmentSprites?.bright)} dark={Name(augmentSprites?.dark)}" +
                      $"\n  chest: bright={Name(chestSprites?.bright)} dark={Name(chestSprites?.dark)}" +
                      $"\n  heart: bright={Name(heartChoiceSprites?.bright)} dark={Name(heartChoiceSprites?.dark)}" +
                      $"\n  miniBoss: bright={Name(miniBossSprites?.bright)} dark={Name(miniBossSprites?.dark)}" +
                      $"\n  boss: bright={Name(bossSprites?.bright)} dark={Name(bossSprites?.dark)}" +
                      $"\n  finalBoss: bright={Name(finalBossSprites?.bright)} dark={Name(finalBossSprites?.dark)}" +
                      $"\n  lines: bright={Name(lineBright)} dark={Name(lineDark)}");
        }
    }

    private void TryAutoFill(ref PhaseSprites slot, string brightName, string darkName)
    {
        if (slot == null) slot = new PhaseSprites();
        if (slot.bright == null) slot.bright = LoadFromHud(brightName);
        if (slot.dark == null) slot.dark = LoadFromHud(darkName);
    }

    private static Sprite LoadFromHud(string spriteName)
        => Resources.Load<Sprite>("Sprites/HUD/Stages/" + spriteName);

    private static string Name(Sprite s) => s == null ? "<null>" : s.name;

    private void ClearStockChildren()
    {
        // Hide whatever stock children the prefab shipped with — we'll generate our own.
        if (iconsPanelRT != null)
        {
            for (int i = iconsPanelRT.childCount - 1; i >= 0; i--)
                iconsPanelRT.GetChild(i).gameObject.SetActive(false);
            // Disable any layout group — we position manually.
            var grid = iconsPanelRT.GetComponent<LayoutGroup>();
            if (grid != null) grid.enabled = false;
        }

        if (linesPanelRT != null)
        {
            for (int i = linesPanelRT.childCount - 1; i >= 0; i--)
                linesPanelRT.GetChild(i).gameObject.SetActive(false);
            var grid = linesPanelRT.GetComponent<LayoutGroup>();
            if (grid != null) grid.enabled = false;
        }

        if (highlightRT != null)
        {
            highlightRT.gameObject.SetActive(false);
            highlightRT.SetSiblingIndex(0); // render behind PanelIcons / PanelLines
            if (highlightImage != null)
            {
                if (highlightSpriteOverride != null) highlightImage.sprite = highlightSpriteOverride;
                highlightImage.color = highlightTint;
                highlightImage.raycastTarget = false;
            }
        }


        if (highlightRT != null) highlightRT.SetSiblingIndex(0);
        if (linesPanelRT != null) linesPanelRT.SetSiblingIndex(highlightRT != null ? 1 : 0);
        if (iconsPanelRT != null) iconsPanelRT.SetAsLastSibling();
    }

    //   TIMELINE BUILD  

    private void TryBuildTimeline()
    {
        if (builtForRun) return;
        var orch = GameOrchestrator.Instance;
        if (orch == null || orch.runConfig == null) return;
        BuildTimelineFromRunConfig(orch.runConfig);
        builtForRun = true;
    }

    private void BuildTimelineFromRunConfig(RunConfig cfg)
    {
        ClearGeneratedNodes();
        nodes.Clear();
        currentNodeIndex = -1;

        int stages = Mathf.Max(1, cfg.stageCount);
        int wavesPerStage = Mathf.Max(1, cfg.wavesPerStage);
        int augEvery = Mathf.Max(0, cfg.augmentEveryNWaves);

        for (int s = 0; s < stages; s++)
        {
            for (int w = 0; w < wavesPerStage; w++)
            {
                nodes.Add(new Node { phase = SlotPhase.Wave, stageIndex = s, waveIndex = w });

                bool isLastWave = (w == wavesPerStage - 1);
                bool augmentAfter = augEvery > 0 && ((w + 1) % augEvery == 0);
                if (augmentAfter && !isLastWave)
                    nodes.Add(new Node { phase = SlotPhase.Augment, stageIndex = s, waveIndex = w });
            }

            if (cfg.stageBossPrefab != null)
                nodes.Add(new Node { phase = SlotPhase.Boss, stageIndex = s, waveIndex = -1 });

            // The orchestrator's RunStage() fires the post-stage choice menu after
            // EVERY stage, including the last one — independent of whether a final
            // boss follows. Mirror that here so the progress bar shows a reward icon
            // after every stage boss.
            nodes.Add(new Node { phase = SlotPhase.HeartChoice, stageIndex = s, waveIndex = -1 });
        }

        if (cfg.hasFinalBoss && cfg.finalBossPrefab != null)
            nodes.Add(new Node { phase = SlotPhase.FinalBoss, stageIndex = -1, waveIndex = -1 });

        if (debugLog)
            Debug.Log($"[RunProgressBar] Timeline built: {nodes.Count} nodes for {stages} stages × {wavesPerStage} waves");

        windowBuilt = false;
        RebuildViews();

        if (alwaysVisible && nodes.Count > 0 && currentNodeIndex < 0)
        {
            currentNodeIndex = 0;
            RefreshAllVisuals();
        }
    }

    // Build (or rebuild) the icon/line views. In windowed mode this realises only the
    // slice around the active node; in full mode it realises the whole timeline.
    private void RebuildViews()
    {
        if (nodes.Count == 0) return;

        if (!windowed)
        {
            BuildNodeViewsAndLayout(0, nodes.Count);
            windowBuilt = true;
            return;
        }
        EnsureWindowViews(force: true);
    }

    // Compute the desired window around currentNodeIndex and rebuild the views only
    // if that window actually shifted (so per-frame RefreshAllVisuals stays cheap).
    private void EnsureWindowViews(bool force = false)
    {
        int total = nodes.Count;
        if (total == 0) return;

        int size = Mathf.Min(total, windowRadius * 2 + 1);
        int center = Mathf.Clamp(currentNodeIndex < 0 ? 0 : currentNodeIndex, 0, total - 1);
        // Keep a FULL window even near the ends (clamp the start, don't shrink the size).
        int start = Mathf.Clamp(center - windowRadius, 0, Mathf.Max(0, total - size));

        if (!force && windowBuilt && start == windowStart && size == windowCount)
            return; // window unchanged — nothing to rebuild

        BuildNodeViewsAndLayout(start, size);
        windowBuilt = true;
    }

    private void ClearGeneratedNodes()
    {
        foreach (var img in nodeIcons)
            if (img != null) Destroy(img.gameObject);
        foreach (var img in lineIcons)
            if (img != null) Destroy(img.gameObject);
        nodeIcons.Clear();
        nodeRects.Clear();
        lineIcons.Clear();
    }

    //   LAYOUT  

    private void BuildNodeViewsAndLayout(int rangeStart, int rangeCount)
    {
        if (iconsPanelRT == null || nodes.Count == 0) return;

        // Clamp the requested range to the real list, then clear and rebuild views.
        rangeStart = Mathf.Clamp(rangeStart, 0, Mathf.Max(0, nodes.Count - 1));
        rangeCount = Mathf.Clamp(rangeCount, 1, nodes.Count - rangeStart);
        windowStart = rangeStart;
        windowCount = rangeCount;

        ClearGeneratedNodes();

        int n = rangeCount;

        // total width = N*iconSize + (N-1)*iconSize*spacingFraction = iconSize * (N + (N-1)*spacingFraction)
        float spacingTerm = (n - 1) * spacingFraction;
        float widthMultiplier = n + spacingTerm;
        float fitSize = (widthMultiplier > 0f) ? (maxBarWidth / widthMultiplier) : preferredIconSize;
        effectiveIconSize = Mathf.Clamp(Mathf.Min(preferredIconSize, fitSize), minIconSize, preferredIconSize);

        float spacing = effectiveIconSize * spacingFraction;
        float totalWidth = n * effectiveIconSize + (n - 1) * spacing;

        // Position icons centred horizontally inside PanelIcons.
        float xCursor = -totalWidth * 0.5f + effectiveIconSize * 0.5f;

        // Build icons — fresh GameObjects, not template-instantiated.
        for (int i = 0; i < n; i++)
        {
            var node = nodes[windowStart + i];

            var go = new GameObject($"Node_{i}_{node.phase}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(iconsPanelRT, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            float w = effectiveIconSize;
            if (node.phase == SlotPhase.Boss || node.phase == SlotPhase.FinalBoss)
                w *= bossSizeMultiplier;

            rt.sizeDelta = new Vector2(w, w);
            rt.anchoredPosition = new Vector2(xCursor, 0f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            var img = go.GetComponent<Image>();
            img.sprite = SpriteForPhase(node.phase, completed: false);
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;

            nodeIcons.Add(img);
            nodeRects.Add(rt);

            xCursor += effectiveIconSize + spacing;
        }

        // Build lines between adjacent icons.
        if (linesPanelRT != null && n >= 2)
        {
            for (int i = 0; i < n - 1; i++)
            {
                var go = new GameObject($"Line_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(linesPanelRT, false);

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                float midX = (nodeRects[i].anchoredPosition.x + nodeRects[i + 1].anchoredPosition.x) * 0.5f;

                // Edge-to-edge gap: distance between icon centers minus the two half-widths.
                // We use the BASE icon size (not the boss-multiplied size) so all lines are uniform.
                float centerToCenter = nodeRects[i + 1].anchoredPosition.x - nodeRects[i].anchoredPosition.x;
                float edgeGap = centerToCenter - effectiveIconSize;
                if (edgeGap < 1f) edgeGap = centerToCenter * 0.5f; // safety fallback

                rt.sizeDelta = new Vector2(edgeGap * lineWidthFraction, effectiveIconSize * lineHeightFraction);
                rt.anchoredPosition = new Vector2(midX, 0f);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;

                var img = go.GetComponent<Image>();
                img.sprite = lineBright;
                img.color = lineBright != null ? Color.white : new Color(1, 1, 1, 0); // hide if no sprite
                img.preserveAspect = linePreserveAspect;
                img.raycastTarget = false;

                lineIcons.Add(img);
            }
        }

        // Mark the window as realised BEFORE refreshing, so the RefreshAllVisuals
        // below (which calls EnsureWindowViews) sees a matching, up-to-date window
        // and does not recursively rebuild.
        windowBuilt = true;
        RefreshAllVisuals();
    }

    private Sprite SpriteForPhase(SlotPhase phase, bool completed)
    {
        PhaseSprites slot = SlotForPhase(phase);

        if (slot != null)
        {
            if (completed && useDarkVariants && slot.dark != null) return slot.dark;
            if (slot.bright != null) return slot.bright;
            if (slot.dark != null) return slot.dark; // fallback if only dark provided
        }

        // Fallback chain to fill in missing slots.
        switch (phase)
        {
            case SlotPhase.FinalBoss: return SpriteForPhase(SlotPhase.Boss, completed);
            case SlotPhase.MiniBoss: return SpriteForPhase(SlotPhase.Boss, completed);
            case SlotPhase.Chest: return SpriteForPhase(SlotPhase.HeartChoice, completed);
            case SlotPhase.HeartChoice: return SpriteForPhase(SlotPhase.Augment, completed);
            case SlotPhase.Augment: return SpriteForPhase(SlotPhase.Wave, completed);
        }
        return null;
    }

    private PhaseSprites SlotForPhase(SlotPhase phase)
    {
        switch (phase)
        {
            case SlotPhase.Wave: return waveSprites;
            case SlotPhase.Augment: return augmentSprites;
            case SlotPhase.Chest: return chestSprites;
            case SlotPhase.HeartChoice: return heartChoiceSprites;
            case SlotPhase.MiniBoss: return miniBossSprites;
            case SlotPhase.Boss: return bossSprites;
            case SlotPhase.FinalBoss: return finalBossSprites;
        }
        return null;
    }

    /// <summary>True if the resolved sprite is the *dark* variant (so we should NOT tint).</summary>
    private bool HasDarkVariant(SlotPhase phase)
    {
        var slot = SlotForPhase(phase);
        return slot != null && slot.dark != null;
    }

    //   ORCHESTRATOR HOOKUP  

    private void TrySubscribe()
    {
        if (subscribed) return;
        if (!enableEventHandling) return;
        var orch = GameOrchestrator.Instance;
        if (orch == null) return;

        orch.OnStageStarted += HandleStageStarted;
        orch.OnWaveStarted += HandleWaveStarted;
        orch.OnWaveCleared += HandleWaveCleared;
        orch.OnBossSpawned += HandleBossSpawned;
        orch.OnBossKilled += HandleBossKilled;
        orch.OnVictory += HandleVictory;
        orch.OnGameOver += HandleGameOver;
        subscribed = true;
        if (debugLog) Debug.Log("[RunProgressBar] Subscribed to orchestrator events.");
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        var orch = GameOrchestrator.Instance;
        if (orch != null)
        {
            orch.OnStageStarted -= HandleStageStarted;
            orch.OnWaveStarted -= HandleWaveStarted;
            orch.OnWaveCleared -= HandleWaveCleared;
            orch.OnBossSpawned -= HandleBossSpawned;
            orch.OnBossKilled -= HandleBossKilled;
            orch.OnVictory -= HandleVictory;
            orch.OnGameOver -= HandleGameOver;
        }
        subscribed = false;
    }

    private void HandleStageStarted(StageData stage)
    {
        if (debugLog) Debug.Log($"[RunProgressBar] OnStageStarted (stage {stage.stageIndex})");
        SetCurrentToFirstNodeOfStage(stage.stageIndex);
        RefreshAllVisuals();
    }

    private void HandleWaveStarted(int waveIndex, int totalWaves)
    {
        if (debugLog) Debug.Log($"[RunProgressBar] OnWaveStarted ({waveIndex}/{totalWaves})");
        var orch = GameOrchestrator.Instance;
        int stage = orch != null ? orch.CurrentStageIndex : 0;
        int idx = FindNode(SlotPhase.Wave, stage, waveIndex);
        if (idx >= 0) currentNodeIndex = idx;
        RefreshAllVisuals();
        Hide();
    }

    private void HandleWaveCleared(int waveIndex)
    {
        if (debugLog) Debug.Log($"[RunProgressBar] OnWaveCleared ({waveIndex})");
        var orch = GameOrchestrator.Instance;
        if (orch == null) return;

        int stage = orch.CurrentStageIndex;
        int clearedIdx = FindNode(SlotPhase.Wave, stage, waveIndex);
        if (clearedIdx >= 0)
            currentNodeIndex = Mathf.Min(clearedIdx + 1, nodes.Count - 1);

        RefreshAllVisuals();
        Show();
    }

    private void HandleBossSpawned(StageData stage)
    {
        // Final boss fires this with a null stage — point to the FinalBoss node instead.
        if (stage == null)
        {
            if (debugLog) Debug.Log($"[RunProgressBar] OnBossSpawned (FINAL boss)");
            int finalIdx = FindNode(SlotPhase.FinalBoss, -1, -1);
            if (finalIdx >= 0) currentNodeIndex = finalIdx;
            RefreshAllVisuals();
            Hide();
            return;
        }

        if (debugLog) Debug.Log($"[RunProgressBar] OnBossSpawned (stage {stage.stageIndex})");
        int idx = FindNode(SlotPhase.Boss, stage.stageIndex, -1);
        if (idx >= 0) currentNodeIndex = idx;
        RefreshAllVisuals();
        Hide();
    }

    private void HandleBossKilled(int stageIndex)
    {
        if (debugLog) Debug.Log($"[RunProgressBar] OnBossKilled (stage {stageIndex})");
        int bossIdx = FindNode(SlotPhase.Boss, stageIndex, -1);
        if (bossIdx >= 0)
            currentNodeIndex = Mathf.Min(bossIdx + 1, nodes.Count - 1);
        RefreshAllVisuals();
        Show();
    }

    private void HandleVictory()
    {
        if (debugLog) Debug.Log($"[RunProgressBar] OnVictory");
        currentNodeIndex = nodes.Count - 1;
        RefreshAllVisuals();
        Hide();
    }

    private void HandleGameOver()
    {
        if (debugLog) Debug.Log($"[RunProgressBar] OnGameOver");
        Hide();
    }

    private void SetCurrentToFirstNodeOfStage(int stage)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].stageIndex == stage)
            {
                currentNodeIndex = i;
                return;
            }
        }
    }

    private int FindNode(SlotPhase phase, int stage, int wave)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.phase != phase) continue;
            if (node.stageIndex != stage) continue;
            if (wave >= 0 && node.waveIndex != wave) continue;
            return i;
        }
        return -1;
    }

    //   VISUAL REFRESH  

    private void RefreshAllVisuals()
    {
        // Recentre the window on the active node first (cheap no-op if it hasn't moved).
        if (windowed) EnsureWindowViews();

        bool moreLeft = windowed && windowStart > 0;
        bool moreRight = windowed && (windowStart + windowCount < nodes.Count);

        for (int i = 0; i < nodeIcons.Count; i++)
        {
            int global = windowStart + i;
            bool isCurrent = (global == currentNodeIndex);
            // Only the active node is bright. Everything else (past AND future) is dark.
            bool useDarkSprite = !isCurrent;

            var phase = nodes[global].phase;
            nodeIcons[i].sprite = SpriteForPhase(phase, completed: useDarkSprite);

            // If we wanted dark but no dark variant exists, tint grey instead so it still reads as inactive.
            Color baseColor;
            if (useDarkSprite && useDarkVariants && !HasDarkVariant(phase))
                baseColor = completedFallbackTint;
            else
                baseColor = Color.white;

            // Fade the outermost icon on a side that has more timeline beyond the window,
            // hinting the run continues past what's visible. Never fade the active node.
            if (fadeWindowEdges && !isCurrent)
            {
                if (i == 0 && moreLeft) baseColor.a *= windowEdgeAlpha;
                else if (i == nodeIcons.Count - 1 && moreRight) baseColor.a *= windowEdgeAlpha;
            }
            nodeIcons[i].color = baseColor;

            // Current icon scaled up.
            float baseW = effectiveIconSize;
            if (phase == SlotPhase.Boss || phase == SlotPhase.FinalBoss)
                baseW *= bossSizeMultiplier;

            float scale = isCurrent ? currentIconScale : 1f;
            nodeRects[i].sizeDelta = new Vector2(baseW * scale, baseW * scale);
        }

        // Lines: only the two lines flanking the current node are bright.
        // currentLocal is the active node's index within the built window.
        int currentLocal = currentNodeIndex - windowStart;
        for (int i = 0; i < lineIcons.Count; i++)
        {
            bool adjacentToCurrent = (i == currentLocal) || (i == currentLocal - 1);
            Sprite chosen = adjacentToCurrent ? lineBright : lineDark;
            if (chosen == null) chosen = lineIcons[i].sprite;
            lineIcons[i].sprite = chosen;
            lineIcons[i].color = chosen != null ? Color.white : new Color(1, 1, 1, 0);
        }

        UpdateHighlightPosition();
    }

    private void UpdateHighlightPosition()
    {
        if (highlightRT == null) return;

        // The active node may be outside the currently built window for a frame
        // (e.g. mid-rebuild); guard against that by mapping to a local index.
        int local = currentNodeIndex - windowStart;
        bool valid = local >= 0 && local < nodeRects.Count;
        highlightRT.gameObject.SetActive(valid);
        if (!valid) return;

        var target = nodeRects[local];

        Vector3 worldPos = target.position;
        Vector3 localPos = highlightRT.parent.InverseTransformPoint(worldPos);
        highlightRT.localPosition = new Vector3(localPos.x, localPos.y, highlightRT.localPosition.z);

        float targetSize = Mathf.Max(target.sizeDelta.x, target.sizeDelta.y);
        float hlSize = targetSize * highlightScale;
        highlightRT.sizeDelta = new Vector2(hlSize, hlSize);

        if (highlightImage != null)
            highlightImage.color = highlightTint;
    }

    private void UpdateHighlightAnimation()
    {
        if (highlightRT == null || !highlightRT.gameObject.activeSelf) return;

        float baseScale = 1f;
        if (highlightPulseAmplitude > 0f)
        {
            float t = Time.unscaledTime * highlightPulseSpeed * Mathf.PI * 2f;
            baseScale = 1f + Mathf.Sin(t) * highlightPulseAmplitude;
        }
        highlightRT.localScale = new Vector3(baseScale, baseScale, 1f);

        if (Mathf.Abs(highlightRotationSpeed) > 0.01f)
            highlightRT.Rotate(0f, 0f, highlightRotationSpeed * Time.unscaledDeltaTime);
    }

    //   SHOW / HIDE  

    private void Show()
    {
        if (nodes.Count == 0) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(canvasGroup.alpha, 1f, fadeInDuration));
    }

    private void Hide()
    {
        if (alwaysVisible) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(canvasGroup.alpha, 0f, fadeOutDuration));
    }

    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        if (duration <= 0.001f) { FadeTo(to); yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            FadeTo(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        FadeTo(to);
    }

    private void FadeTo(float alpha)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = alpha;
    }

    //   EDITOR DEBUG  

#if UNITY_EDITOR
    [ContextMenu("Show (debug)")]
    private void DebugShow() { if (Application.isPlaying) Show(); }

    [ContextMenu("Hide (debug)")]
    private void DebugHide() { if (Application.isPlaying) Hide(); }

    [ContextMenu("Advance Current (debug)")]
    private void DebugAdvance()
    {
        if (!Application.isPlaying) return;
        currentNodeIndex = Mathf.Min(currentNodeIndex + 1, nodes.Count - 1);
        RefreshAllVisuals();
        Show();
    }

    [ContextMenu("Reset (debug)")]
    private void DebugReset()
    {
        if (!Application.isPlaying) return;
        currentNodeIndex = 0;
        RefreshAllVisuals();
        Show();
    }

    [ContextMenu("Rebuild Timeline (debug)")]
    private void DebugRebuild()
    {
        if (!Application.isPlaying) return;
        builtForRun = false;
        TryBuildTimeline();
    }
#endif
}
