using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Augments Panel automatic population on a grid

public class AugmentGridUI : MonoBehaviour
{
    [Header("Where the grid renders")]
    [Tooltip("Panel the icon cells fill. Leave empty -> uses this.transform. " +
             "Resize this object in the inspector to control the visible area.")]
    public RectTransform gridContainer;

    [Header("Grid layout")]
    [Tooltip("How many cells per row. 4 fits a panel nicely; raise for smaller tiles.")]
    [Range(1, 10)] public int columns = 4;
    [Tooltip("Pixel gap between cells (both directions).")]
    public Vector2 cellSpacing = new Vector2(12, 12);
    [Tooltip("Padding around the whole grid (left, right, top, bottom).")]
    public RectOffset gridPadding;
    [Tooltip("Reserved space at the top of the panel (e.g. for a header label).")]
    public float topReserve = 80f;
    [Tooltip("Empty inset (px) at the bottom of the scrollable area — keeps " +
             "cells from rendering over the panel's bottom decorative frame.")]
    public float viewportBottomInset = 40f;
    [Tooltip("Empty inset (px) on the LEFT and RIGHT of the scrollable area — " +
             "keeps cells from rendering over the panel's side decorative frame.")]
    public float viewportSideInset = 20f;
    [Tooltip("Mouse-wheel scroll speed. Default Unity ScrollRect is ~1.0; " +
             "raise for snappier scrolling on wheel ticks.")]
    public float scrollSensitivity = 30f;
    [Tooltip("Minimum cell size (px). Cells never shrink below this — if the " +
             "panel can't fit them all, content overflows the bottom instead.")]
    public float minCellSize = 90f;
    [Tooltip("Maximum cell size (px). Cells never grow larger than this. Keeps " +
             "tiles from being chunky if you only have a few augments.")]
    public float maxCellSize = 130f;

    [Header("Cell visuals")]
    [Tooltip("Show the augment name under the icon.")]
    public bool showNameLabel = true;
    [Tooltip("Font to use for cell labels AND tooltip text. Leave empty for " +
             "the default TMP font. Drag your Cinzel-ExtraBold SDF (or any " +
             "TMP Font Asset) here in the Inspector.")]
    public TMP_FontAsset fontAsset;
    [Tooltip("Font size for the name under each icon (auto-shrinks if too long).")]
    public float nameFontSize = 34f;
    [Tooltip("Border thickness (px) around each cell, coloured by rarity.")]
    public float borderThickness = 3f;
    [Tooltip("Fraction of the cell devoted to the icon (vs name label). " +
             "0.78 = big icon, small label.")]
    [Range(0.5f, 0.95f)] public float iconAreaFraction = 0.65f;
    [Tooltip("Inset (px) around the icon inside its area — smaller = bigger icon visual.")]
    public float iconInset = 6f;
    [Tooltip("Background color inside the cell (constant — does NOT change " +
             "with rarity). Light backgrounds work best with dark/silhouette " +
             "icon artwork; dark backgrounds work best with light icons.")]
    public Color cellBackground = new Color(0.88f, 0.88f, 0.90f, 0.95f);

    [Header("Rarity glow")]
    [Tooltip("The glow sprite — a soft white circle on transparent background. " +
             "Drag your soft_circle_glow.png sprite here. If left empty, no glow " +
             "is rendered (falls back to plain framed cells).")]
    public Sprite glowSprite;
    [Tooltip("Base alpha for the glow halo behind each icon. Pulse animation " +
             "modulates around this value.")]
    [Range(0f, 1f)] public float glowAlpha = 0.75f;
    [Tooltip("Glow diameter as a fraction of the cell's smaller dimension. " +
             "1.0 = same as cell; 1.2 = extends 10% past cell edges.")]
    [Range(0.5f, 2f)] public float glowRelativeSize = 1.15f;
    [Tooltip("Legacy field, kept so existing Inspector references don't break.")]
    [Range(0f, 24f)] public float glowRadius = 10f;

    [Header("Click to expand")]
    [Tooltip("Clicking a cell opens an expanded detail panel with the full " +
             "description.")]
    public bool clickToExpand = true;

    [Header("Hover effect")]
    [Tooltip("Scale a cell up to this factor while hovered.")]
    [Range(1f, 1.5f)] public float hoverScale = 1.12f;
    [Tooltip("Seconds to lerp into/out of the hover scale.")]
    public float hoverLerpSeconds = 0.08f;

    [Header("Tooltip")]
    [Tooltip("Width of the floating tooltip in pixels.")]
    public float tooltipWidth = 400f;
    [Tooltip("Offset from the cursor (x right, y up).")]
    public Vector2 tooltipOffset = new Vector2(18, 18);

    [Header("Refresh")]
    [Tooltip("How often the grid checks AugmentRegistry for changes (sec).")]
    public float refreshInterval = 0.5f;

    private GridLayoutGroup _grid;
    private readonly List<AugmentCell> _cells = new List<AugmentCell>();
    private TooltipUI _tooltip;
    private float _refreshTimer;
    private int _lastAugmentCountSeen = -1;

    //  Lifecycle
    private void Reset()
    {
        gridPadding = new RectOffset(8, 8, 8, 8);
    }

    private void Awake()
    {
        if (gridPadding == null) gridPadding = new RectOffset(8, 8, 8, 8);
        if (gridContainer == null) gridContainer = transform as RectTransform;

        SetupGrid();
        _tooltip = TooltipUI.GetOrCreate(transform.root as RectTransform);
        _tooltip.Hide();
    }

    private void OnEnable()
    {
        // Force a refresh on every reopen.
        _lastAugmentCountSeen = -1;
        _refreshTimer = refreshInterval; // refresh next Update
    }

    private void Update()
    {
        _refreshTimer += Time.unscaledDeltaTime;
        if (_refreshTimer < refreshInterval) return;
        _refreshTimer = 0f;

        // Cheap change-detection: only repopulate when the applied-augments
        // list size changes. (If you ever need finer detection, hash the IDs.)
        if (AugmentRegistry.Instance == null) return;
        int count = AugmentRegistry.Instance.GetAppliedAugments()?.Count ?? 0;
        if (count == _lastAugmentCountSeen) return;
        _lastAugmentCountSeen = count;
        PopulateFromRegistry();
    }

    //  Public API — call this to force an immediate rebuild
    [Header("Diagnostics")]
    public bool verbose = false;

    public void PopulateFromRegistry()
    {
        if (AugmentRegistry.Instance == null)
        {
            if (verbose) Debug.LogWarning("[AugmentGridUI] AugmentRegistry.Instance is null.");
            return;
        }
        var applied = AugmentRegistry.Instance.GetAppliedAugments();
        var datas = new List<AugmentData>(applied?.Count ?? 0);
        if (applied != null)
        {
            foreach (int id in applied)
            {
                var d = AugmentRegistry.Instance.GetAugmentData(id);
                if (d != null) datas.Add(d);
            }
        }
        if (verbose)
        {
            Debug.Log($"[AugmentGridUI] Populating: {datas.Count} augments. " +
                $"Grid container = '{(gridContainer != null ? gridContainer.name : "NULL")}', " +
                $"active = {(gridContainer != null && gridContainer.gameObject.activeInHierarchy ? "YES" : "NO")}, " +
                $"size = {(gridContainer != null ? $"{gridContainer.rect.width:0}x{gridContainer.rect.height:0}" : "?")}.");
        }
        Render(datas);
    }

    //  Layout
    private void SetupGrid()
    {
        if (gridContainer == null) return;

        // Initialize padding if Unity left it null.
        if (gridPadding == null) gridPadding = new RectOffset(8, 8, 8, 8);

        //  Build a scrollable hierarchy on top of gridContainer 

        EnsureScrollHierarchy();

        // Unity allows only ONE LayoutGroup per GameObject.
        var existing = _gridRoot.GetComponents<LayoutGroup>();
        foreach (var lg in existing)
        {
            if (lg == null) continue;
            if (lg is GridLayoutGroup) continue;
            DestroyImmediate(lg);
        }

        _grid = _gridRoot.GetComponent<GridLayoutGroup>();
        if (_grid == null) _grid = _gridRoot.gameObject.AddComponent<GridLayoutGroup>();
        if (_grid == null)
        {
            Debug.LogError(
                $"[AugmentGridUI] Could not add GridLayoutGroup to '{_gridRoot.name}'. " +
                "Likely a 'Missing (Mono Script)' placeholder on the GameObject — find it " +
                "in the Inspector (yellow component header) and Remove it.");
            return;
        }
        _grid.enabled = true;

        _grid.padding = new RectOffset(
            gridPadding.left,
            gridPadding.right,
            gridPadding.top,        // top padding is on Content, not the topReserve
            gridPadding.bottom);
        _grid.spacing = cellSpacing;
        _grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        _grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        _grid.childAlignment = TextAnchor.UpperCenter;
        _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _grid.constraintCount = Mathf.Max(1, columns);

        // ContentSizeFitter on Content so it grows taller as rows are added.
        var sizeFit = _gridRoot.GetComponent<ContentSizeFitter>();
        if (sizeFit == null) sizeFit = _gridRoot.gameObject.AddComponent<ContentSizeFitter>();
        sizeFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sizeFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RecomputeCellSize();
    }

    // Builds: gridContainer -> Viewport (Mask) -> Content. Idempotent.

    private RectTransform _viewport;
    private RectTransform _gridRoot;
    private ScrollRect _scrollRect;

    private void EnsureScrollHierarchy()
    {
        if (_gridRoot != null) return;

        // ScrollRect on gridContainer (the panel itself).
        _scrollRect = gridContainer.GetComponent<ScrollRect>();
        if (_scrollRect == null) _scrollRect = gridContainer.gameObject.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Viewport child — masked region of the panel below the topReserve.

        var viewportGO = new GameObject("AugmentViewport", typeof(RectTransform));
        var viewport = (RectTransform)viewportGO.transform;
        viewport.SetParent(gridContainer, false);
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(viewportSideInset, viewportBottomInset);
        viewport.offsetMax = new Vector2(-viewportSideInset, -topReserve);
        // Mask cells that scroll outside.
        viewport.gameObject.AddComponent<RectMask2D>();
        // ScrollRect requires viewport to have a Graphic for raycasts in some
        // setups; an invisible Image is fine.
        var vpImage = viewport.gameObject.AddComponent<Image>();
        vpImage.color = new Color(0, 0, 0, 0);
        vpImage.raycastTarget = true;
        _viewport = viewport;

        // Content child — the actual grid root.
        var contentGO = new GameObject("AugmentContent", typeof(RectTransform));
        var content = (RectTransform)contentGO.transform;
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        _gridRoot = content;

        _scrollRect.viewport = viewport;
        _scrollRect.content = content;
        _scrollRect.scrollSensitivity = scrollSensitivity;
    }

    private void RecomputeCellSize()
    {
        if (_grid == null || _gridRoot == null || _viewport == null) return;

        // Cells size by viewport WIDTH only. Height/rows are no longer
        // constrained — Content grows as tall as needed and the user scrolls.
        float availW = _viewport.rect.width
                       - _grid.padding.left - _grid.padding.right
                       - _grid.spacing.x * (columns - 1);
        float cellW = Mathf.Max(32f, availW / Mathf.Max(1, columns));

        // Square icon area + label area below.
        float labelExtra = showNameLabel ? nameFontSize * 2.4f + 6f : 0f;
        float cellH = cellW + labelExtra;

        // Clamp to min/max so cells stay visually consistent across panel sizes.
        float minCell = Mathf.Max(32f, minCellSize);
        if (cellW < minCell)
        {
            float baseCellH = cellW + labelExtra;
            float aspect = baseCellH / Mathf.Max(1f, cellW);
            cellW = minCell;
            cellH = minCell * aspect;
        }
        float maxCell = Mathf.Max(minCell, maxCellSize);
        if (cellW > maxCell)
        {
            float baseCellH = cellW + labelExtra;
            float aspect = baseCellH / Mathf.Max(1f, cellW);
            cellW = maxCell;
            cellH = maxCell * aspect;
        }

        _grid.cellSize = new Vector2(cellW, cellH);
    }

    //  Render
    private void Render(List<AugmentData> augments)
    {
        if (_grid == null || gridContainer == null) return;   // bailed in SetupGrid

        // Re-fit cell size before laying out (panel may have resized).
        RecomputeCellSize();

        // Grow the pool if needed.
        while (_cells.Count < augments.Count)
        {
            _cells.Add(BuildCell());
        }

        // Apply data to each cell; hide overflow.
        Dictionary<string, Color> rarityColors =
            AugmentRegistry.Instance?.GetRarityColors() ?? new Dictionary<string, Color>();

        // Build a case-insensitive view of the rarity dictionary so 'rare'
        // matches 'Rare'.
        var rarityCI = new Dictionary<string, Color>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var kv in rarityColors) rarityCI[kv.Key] = kv.Value;

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (i < augments.Count)
            {
                var d = augments[i];
                Color rarity = new Color(0.7f, 0.7f, 0.75f); // pale neutral default
                if (!string.IsNullOrEmpty(d.Rarity))
                    rarityCI.TryGetValue(d.Rarity, out rarity);

                cell.SetData(d, rarity, this);
                cell.root.SetActive(true);
            }
            else
            {
                cell.root.SetActive(false);
            }
        }
    }

    //  Cell construction (one-time per pooled cell)
    private AugmentCell BuildCell()
    {
        // Root
        var go = new GameObject("AugmentCell", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_gridRoot != null ? _gridRoot : gridContainer, false);
        // GridLayoutGroup will set the size, but starting at a sane value
        // avoids a one-frame flash.
        rt.sizeDelta = _grid != null ? _grid.cellSize : new Vector2(64, 64);

        //  Invisible raycast catcher 

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f);
        bg.raycastTarget = true;

        // Outline placeholders (kept so AugmentCell.outline isn't null; invisible).
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0);
        var outline2 = go.AddComponent<Outline>();
        outline2.effectColor = new Color(0, 0, 0, 0);

        //  Glow (real circular halo using glowSprite) 
        float iconBottom = showNameLabel ? (1f - iconAreaFraction) : 0.02f;
        var glowGO = new GameObject("RarityGlow", typeof(RectTransform));
        var glowRT = (RectTransform)glowGO.transform;
        glowRT.SetParent(rt, false);
        // Center the glow on the icon's vertical center.
        float iconCenterY = (iconBottom + 0.98f) * 0.5f;
        glowRT.anchorMin = new Vector2(0.5f, iconCenterY);
        glowRT.anchorMax = new Vector2(0.5f, iconCenterY);
        glowRT.pivot = new Vector2(0.5f, 0.5f);
        glowRT.sizeDelta = Vector2.zero; // GlowSizer sets actual size
        var glow = glowGO.AddComponent<Image>();
        if (glowSprite != null)
        {
            glow.sprite = glowSprite;
            glow.type = Image.Type.Simple;
            glow.preserveAspect = true;
            glow.raycastTarget = false;
        }
        else
        {
            // No sprite assigned → no glow rendered.
            glow.enabled = false;
        }
        glow.color = new Color(1, 1, 1, 0); // tinted per-rarity in SetData
        var sizer = glowGO.AddComponent<GlowSizer>();
        sizer.parentRect = rt;
        sizer.relativeSize = glowRelativeSize;
        if (glowSprite != null) glowGO.AddComponent<RarityGlowPulse>();

        //  Icon — sized by iconAreaFraction so it dominates the tile 
        var iconGO = new GameObject("Icon", typeof(RectTransform));
        var irt = (RectTransform)iconGO.transform;
        irt.SetParent(rt, false);
        irt.anchorMin = new Vector2(0f, iconBottom);
        irt.anchorMax = new Vector2(1f, 0.98f);
        irt.offsetMin = new Vector2(iconInset, iconInset);
        irt.offsetMax = new Vector2(-iconInset, -iconInset);
        var icon = iconGO.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // Name label — occupies the remainder of the cell below the icon.
        TextMeshProUGUI nameTmp = null;
        if (showNameLabel)
        {
            var labelGO = new GameObject("Name", typeof(RectTransform));
            var lrt = (RectTransform)labelGO.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, iconBottom);
            lrt.offsetMin = new Vector2(3, 3);
            lrt.offsetMax = new Vector2(-3, -1);
            nameTmp = labelGO.AddComponent<TextMeshProUGUI>();
            nameTmp.fontSize = nameFontSize;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color = new Color(0.96f, 0.96f, 0.98f);
            nameTmp.raycastTarget = false;
            nameTmp.enableAutoSizing = true;
            // Lower the auto-shrink floor: TMP only breaks a word mid-character
            // when even the smallest font size can't fit the longest word on
            // one line. A small floor (8pt) guarantees room for words like
            // "flamethrower" or "necronomicon" so wrap happens at spaces only.
            nameTmp.fontSizeMin = 18f;
            nameTmp.fontSizeMax = nameFontSize;
            nameTmp.fontStyle = FontStyles.Bold;
            // Wrapping stays at TMP's default (word-level breaks at spaces).
            if (fontAsset != null) nameTmp.font = fontAsset;
            // Drop shadow for legibility over the dark panel background.
            var shadow = labelGO.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        // Hover handler
        var hover = go.AddComponent<AugmentCellHover>();

        var cell = new AugmentCell
        {
            root = go,
            rect = rt,
            bg = bg,
            icon = icon,
            glowImage = glow,
            outline = outline,
            outline2 = outline2,
            nameLabel = nameTmp,
            hover = hover,
        };
        hover.cell = cell;
        return cell;
    }
}

//  Cell data + hover handler

public class GlowSizer : MonoBehaviour
{
    public RectTransform parentRect;
    [Tooltip("Glow diameter as a fraction of the smaller cell dimension. " +
             "1.0 = same as cell width; 1.1 = slightly larger than the cell.")]
    public float relativeSize = 1.1f;

    private RectTransform _rt;

    private void Awake() => _rt = (RectTransform)transform;

    private void LateUpdate()
    {
        if (_rt == null || parentRect == null) return;
        float size = Mathf.Min(parentRect.rect.width, parentRect.rect.height) * relativeSize;
        _rt.sizeDelta = new Vector2(size, size);
    }
}

// Animates a UI Image's alpha and scale in a slow sine wave so the glow
public class RarityGlowPulse : MonoBehaviour
{
    [Tooltip("Pulse speed (cycles per second). Lower = slower, calmer.")]
    public float frequency = 0.35f;
    [Tooltip("Alpha swing — pulse rises and falls by this much.")]
    public float amplitude = 0.12f;
    [Tooltip("Scale swing — glow breathes slightly larger/smaller.")]
    public float scaleAmplitude = 0.03f;

    private Image _img;
    private float _baseAlpha;
    private Color _baseColor;
    private float _phase;
    private bool _initialised;

    private void OnEnable()
    {
        _img = GetComponent<Image>();
        _initialised = false;
        _phase = Random.value * Mathf.PI * 2f;
    }

    private void LateUpdate()
    {
        if (_img == null) return;

        if (!_initialised && _img.color.a > 0.001f)
        {
            _baseColor = _img.color;
            _baseAlpha = _img.color.a;
            _initialised = true;
        }
        if (!_initialised) return;

        float t = Time.unscaledTime * frequency * Mathf.PI * 2f + _phase;
        float pulse = Mathf.Sin(t) * 0.5f + 0.5f; // 0..1
        float a = Mathf.Clamp01(_baseAlpha + (pulse - 0.5f) * 2f * amplitude);
        _img.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, a);

        float s = 1f + (pulse - 0.5f) * 2f * scaleAmplitude;
        transform.localScale = new Vector3(s, s, 1f);
    }
}

public class AugmentCell
{
    public GameObject root;
    public RectTransform rect;
    public Image bg;
    public Image icon;
    public Image glowImage;
    public Outline outline;
    public TextMeshProUGUI nameLabel;
    public Outline outline2; // second outline for thicker frame
    public AugmentCellHover hover;
    public AugmentData data;

    public void SetData(AugmentData d, Color rarity, AugmentGridUI owner)
    {
        data = d;
        if (icon != null)
        {
            icon.sprite = d.Icon;
            icon.enabled = d.Icon != null;
        }
        if (nameLabel != null)
        {
            nameLabel.text = d.Name ?? "";
        }
        Color rarityOpaque = new Color(rarity.r, rarity.g, rarity.b, 1f);
        // Frameless now: outlines invisible (kept as fields so they aren't null).
        if (glowImage != null && glowImage.enabled && owner != null)
        {
            // Rarity colour at the configured base alpha. RarityGlowPulse modulates around this value.
            glowImage.color = new Color(rarity.r, rarity.g, rarity.b, owner.glowAlpha);
        }
        if (bg != null)
        {
            // Frameless: cell background is transparent (just a raycast catcher).
            bg.color = new Color(0, 0, 0, 0);
        }
        if (hover != null)
        {
            hover.owner = owner;
        }
    }
}

public class AugmentCellHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
{
    [System.NonSerialized] public AugmentCell cell;
    [System.NonSerialized] public AugmentGridUI owner;

    private Coroutine _scaleCo;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (cell == null || cell.data == null || owner == null) return;
        StartScale(owner.hoverScale);
        var tt = TooltipUI.GetOrCreate(transform.root as RectTransform);
        tt.SetFont(owner.fontAsset);
        tt.Show(cell.data, owner.tooltipWidth, AugmentRegistry.Instance?.GetRarityColors());
        tt.Follow(eventData.position, owner.tooltipOffset);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        StartScale(1f);
        var tt = TooltipUI.GetOrCreate(transform.root as RectTransform);
        if (!tt.IsPinned) tt.Hide();
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (owner == null) return;
        var tt = TooltipUI.GetOrCreate(transform.root as RectTransform);
        if (tt != null && tt.IsVisible && !tt.IsPinned)
            tt.Follow(eventData.position, owner.tooltipOffset);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (cell == null || cell.data == null || owner == null) return;
        if (!owner.clickToExpand) return;
        var tt = TooltipUI.GetOrCreate(transform.root as RectTransform);
        // Toggle pin: clicking the same cell again unpins; clicking a different
        // cell pins that one.
        if (tt.IsPinned && tt.PinnedAugmentId == cell.data.ID)
        {
            tt.Unpin();
            tt.Hide();
        }
        else
        {
            tt.SetFont(owner.fontAsset);
            tt.Show(cell.data, owner.tooltipWidth, AugmentRegistry.Instance?.GetRarityColors());
            tt.Follow(eventData.position, owner.tooltipOffset);
            tt.Pin(cell.data.ID);
        }
    }

    private void StartScale(float target)
    {
        if (_scaleCo != null) StopCoroutine(_scaleCo);
        _scaleCo = StartCoroutine(ScaleTo(target, owner != null ? owner.hoverLerpSeconds : 0.08f));
    }

    private System.Collections.IEnumerator ScaleTo(float target, float dur)
    {
        Vector3 from = transform.localScale;
        Vector3 to = Vector3.one * target;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            transform.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.0001f, dur)));
            yield return null;
        }
        transform.localScale = to;
    }
}

//  Procedural tooltip (a singleton built once under the Canvas root)
public class TooltipUI : MonoBehaviour
{
    private static TooltipUI _instance;

    public bool IsVisible { get; private set; }
    public bool IsPinned { get; private set; }
    public int PinnedAugmentId { get; private set; } = -1;

    public void Pin(int augmentId)
    {
        IsPinned = true;
        PinnedAugmentId = augmentId;
    }

    public void Unpin()
    {
        IsPinned = false;
        PinnedAugmentId = -1;
    }

    private RectTransform _rt;
    private CanvasGroup _cg;
    private Image _bg;
    private TextMeshProUGUI _titleTmp;
    private TextMeshProUGUI _bodyTmp;

    public static TooltipUI GetOrCreate(RectTransform canvasRoot)
    {
        if (_instance != null) return _instance;
        if (canvasRoot == null) return null;

        var go = new GameObject("AugmentTooltip", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvasRoot, false);
        // Anchor at canvas center; pivot at bottom-left so the tooltip's
        // bottom-left corner sits at the cursor (i.e. tooltip appears
        // up-and-to-the-right of the mouse, which is conventional).
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(280, 160);

        var cg = go.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;
        cg.alpha = 0f;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.06f, 0.09f, 0.94f);
        var outline = go.AddComponent<Outline>();
        outline.effectDistance = new Vector2(2, 2);
        outline.effectColor = new Color(0.6f, 0.6f, 0.7f, 1f);

        // Title
        var titleGO = new GameObject("Title", typeof(RectTransform));
        var trt = (RectTransform)titleGO.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = new Vector2(0, 1);
        trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1);
        trt.sizeDelta = new Vector2(0, 46);
        trt.anchoredPosition = new Vector2(0, -8);
        trt.offsetMin = new Vector2(12, trt.offsetMin.y);
        trt.offsetMax = new Vector2(-12, trt.offsetMax.y);
        var title = titleGO.AddComponent<TextMeshProUGUI>();
        title.fontSize = 26f;
        title.alignment = TextAlignmentOptions.TopLeft;
        title.color = Color.white;
        title.raycastTarget = false;
        title.enableAutoSizing = false;

        // Body
        var bodyGO = new GameObject("Body", typeof(RectTransform));
        var brt = (RectTransform)bodyGO.transform;
        brt.SetParent(rt, false);
        brt.anchorMin = new Vector2(0, 0);
        brt.anchorMax = new Vector2(1, 1);
        brt.pivot = new Vector2(0.5f, 1);
        brt.offsetMin = new Vector2(12, 12);
        brt.offsetMax = new Vector2(-14, -60);
        var body = bodyGO.AddComponent<TextMeshProUGUI>();
        body.fontSize = 20f;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.color = new Color(0.9f, 0.9f, 0.92f, 1f);
        body.raycastTarget = false;

        // ContentSizeFitter on the tooltip so it grows for long descriptions.
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var inst = go.AddComponent<TooltipUI>();
        inst._rt = rt;
        inst._cg = cg;
        inst._bg = bg;
        inst._titleTmp = title;
        inst._bodyTmp = body;

        // Ensure tooltip renders above siblings.
        rt.SetAsLastSibling();
        _instance = inst;
        return inst;
    }

    /// <summary>
    /// Apply a custom font to both the title and body. Called per-Show by the
    /// AugmentCellHover so the tooltip picks up whatever font the grid owner
    /// has assigned in its Inspector field.
    /// </summary>
    public void SetFont(TMP_FontAsset font)
    {
        if (font == null) return;
        if (_titleTmp != null) _titleTmp.font = font;
        if (_bodyTmp != null) _bodyTmp.font = font;
    }

    public void Show(AugmentData data, float width, Dictionary<string, Color> rarityColors)
    {
        if (data == null) return;

        _rt.sizeDelta = new Vector2(width, _rt.sizeDelta.y);

        // Title: just the augment name. The rarity is already conveyed by the
        // halo colour on the cell, so spelling it out here is redundant noise.
        _titleTmp.text = $"<b>{data.Name}</b>";

        // Body: just the description. Category was also redundant noise.
        _bodyTmp.text = string.IsNullOrEmpty(data.Description)
            ? "<i>(no description)</i>"
            : data.Description;

        // Adjust the title rect to its actual rendered height so a two-line
        // title doesn't bleed into the body. ForceMeshUpdate makes preferredHeight
        // reflect the current text, then we resize the title and push the body
        // down by the matching amount.
        if (_titleTmp != null && _bodyTmp != null)
        {
            _titleTmp.ForceMeshUpdate();
            float titleHeight = Mathf.Max(_titleTmp.preferredHeight, 32f);

            var trt = _titleTmp.rectTransform;
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, titleHeight);

            // Body offsetMax.y is negative — it's the distance the body's TOP
            // edge sits BELOW the parent's top edge. So we want:
            //   topInset = titleHeight + topPadding + gap
            const float topPadding = 8f;
            const float gap = 6f;
            var brt = _bodyTmp.rectTransform;
            brt.offsetMax = new Vector2(brt.offsetMax.x, -(titleHeight + topPadding + gap));
        }

        _cg.alpha = 1f;
        IsVisible = true;
        _rt.SetAsLastSibling();
    }

    public void Hide()
    {
        if (_cg != null) _cg.alpha = 0f;
        IsVisible = false;
    }

    public void Follow(Vector3 mouseScreenPos, Vector2 offset)
    {
        if (_rt == null) return;
        var canvas = _rt.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            (Vector2)mouseScreenPos + offset,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            out local);
        _rt.anchoredPosition = local;

        // Clamp inside the canvas so the tooltip never goes off-screen.
        var canvasRT = canvas.transform as RectTransform;
        if (canvasRT != null)
        {
            Vector2 ttSize = _rt.rect.size;
            Vector2 canvasSize = canvasRT.rect.size;
            float maxX = canvasSize.x * 0.5f - ttSize.x;
            float maxY = canvasSize.y * 0.5f - ttSize.y;
            float minX = -canvasSize.x * 0.5f;
            float minY = -canvasSize.y * 0.5f;
            _rt.anchoredPosition = new Vector2(
                Mathf.Clamp(_rt.anchoredPosition.x, minX, maxX),
                Mathf.Clamp(_rt.anchoredPosition.y, minY, maxY));
        }
    }
}
