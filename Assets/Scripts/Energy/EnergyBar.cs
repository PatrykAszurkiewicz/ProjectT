using UnityEngine;
using System.Collections.Generic;

public class EnergyBar : MonoBehaviour
{
    #region Configuration
    [Header("Energy Bar Settings")]
    public bool showEnergyBar = true;
    public float energyBarHeight = 0.1f;
    public float energyBarWidth = 1f;
    public float energyBarOffset = 1.5f;
    public bool showEnergyText = true;

    [Header("Colors")]
    public Color backgroundBarColor = Color.black;
    public Color normalEnergyColor = Color.lightSteelBlue;
    public Color lowEnergyColor = Color.yellow;
    public Color criticalEnergyColor = Color.red;
    public Color depletedEnergyColor = Color.gray;

    [Header("Text Outline")]
    [Tooltip("Adds a contrasting outline behind the energy text so it stays readable on light biomes (Snow, Stones) and dark ones alike.")]
    public bool useTextOutline = true;

    [Tooltip("Color of the outline drawn behind the main energy text.")]
    public Color textOutlineColor = Color.black;

    [Tooltip("Outline thickness in local units. Around 0.015–0.03 looks right for the default characterSize (0.18).")]
    public float textOutlineThickness = 0.022f;
    #endregion

    #region Level Marker Configuration
    // The upgrade level is drawn as a vertical stack of UPWARD chevrons beside the bar:
    // level 1 = one "^", level 2 = two stacked, level 3 = three, growing upward like
    // rank insignia. The glyphs are built from two thin rotated quads each rather than
    // from font characters, so they stay crisp at any tower scale and don't depend on
    // which font Unity falls back to (and "^" as a character sits badly off-centre).

    public enum LevelMarkerSide { Right, Left }
    public enum ChevronDirection { Left, Right, Up, Down }

    [Header("Tower Level Marker")]
    [Tooltip("Draw the upgrade-level chevrons next to the energy bar. Nothing is drawn " +
             "for towers whose max upgrade level is 1 (no upgrade path to communicate).")]
    public bool showLevelMarker = true;

    [Tooltip("Which side of the bar the chevron stack sits on.")]
    public LevelMarkerSide levelMarkerSide = LevelMarkerSide.Right;

    [Tooltip("Which way each chevron points. Up is the default rank-insignia look.")]
    public ChevronDirection chevronDirection = ChevronDirection.Up;

    [Tooltip("Size of ONE chevron across its opening, in the bar's local units (bar " +
             "width is 1). An upward chevron is this wide and half this tall.")]
    [Min(0.02f)]
    public float chevronSize = 0.13f;

    [Tooltip("Stroke thickness of the chevron arms, in local units.")]
    [Min(0.005f)]
    public float chevronThickness = 0.032f;

    [Tooltip("Gap between stacked chevrons, edge to edge. The centre-to-centre step is " +
             "derived from this plus the glyph's real height, which differs between an " +
             "upward chevron and a sideways one — so the stack stays tight either way.")]
    [Min(0f)]
    public float chevronGap = 0.026f;

    [Tooltip("Force an exact centre-to-centre step instead of deriving it. 0 = auto.")]
    [Min(0f)]
    public float chevronSpacingOverride = 0f;

    [Tooltip("Horizontal gap between the end of the energy bar and the chevron stack.")]
    public float levelMarkerGap = 0.08f;

    [Tooltip("Colour of an earned level chevron.")]
    public Color levelMarkerColor = new Color(1f, 0.86f, 0.35f, 1f);

    [Tooltip("Also draw faint placeholder chevrons for levels not yet reached, so the " +
             "player can see how much upgrade headroom is left.")]
    public bool showEmptyLevelSlots = false;

    [Tooltip("Colour of a not-yet-earned placeholder chevron.")]
    public Color emptyLevelSlotColor = new Color(1f, 1f, 1f, 0.18f);

    [Tooltip("Outline the chevrons the same way the energy text is outlined, so they " +
             "stay readable on light biomes.")]
    public bool useMarkerOutline = true;

    [Tooltip("Colour of the chevron outline.")]
    public Color markerOutlineColor = Color.black;

    [Tooltip("Chevron outline thickness in local units.")]
    public float markerOutlineThickness = 0.016f;
    #endregion

    #region Core Components
    private GameObject energyBarContainer;
    private SpriteRenderer energyBarBackground;
    private SpriteRenderer energyBarFill;
    private TextMesh energyText;
    private TextMesh[] energyTextOutlines; // 4-direction outline stamps drawn behind energyText
    private IEnergyConsumer energyConsumer;
    private SpriteRenderer parentSpriteRenderer;

    // Level marker
    private sealed class ChevronGlyph
    {
        public GameObject root;
        public SpriteRenderer[] fills;
        public SpriteRenderer[] outlines;
    }

    private GameObject levelMarkerContainer;
    private List<ChevronGlyph> chevrons;
    private Sprite unitSprite;          // shared 1x1-unit white quad, scaled per arm
    private Tower levelSourceTower;     // resolved once in Initialize
    private int builtMaxLevel = -1;
    private int shownLevel = -1;
    private float stackStep = 0.1f;     // centre-to-centre, resolved in CreateLevelMarker
    #endregion

    #region Initialization
    public void Initialize(IEnergyConsumer consumer, SpriteRenderer parentRenderer)
    {
        energyConsumer = consumer;
        parentSpriteRenderer = parentRenderer;

        // The energy bar is added to the tower's own GameObject, so the level comes
        // straight off the Tower component. Non-tower consumers simply get no marker.
        levelSourceTower = consumer as Tower;
        if (levelSourceTower == null) levelSourceTower = GetComponent<Tower>();

        CreateEnergyBar();
    }

    void CreateEnergyBar()
    {
        if (!showEnergyBar || energyConsumer == null) return;

        // Create energy bar container
        energyBarContainer = new GameObject("EnergyBar");
        energyBarContainer.transform.SetParent(transform);
        energyBarContainer.transform.localPosition = Vector3.up * energyBarOffset;

        CreateBackgroundBar();
        CreateFillBar();

        if (showEnergyText)
            CreateEnergyText();

        CreateLevelMarker();
    }

    void CreateBackgroundBar()
    {
        GameObject backgroundObj = new GameObject("EnergyBarBackground");
        backgroundObj.transform.SetParent(energyBarContainer.transform);
        backgroundObj.transform.localPosition = Vector3.zero;

        energyBarBackground = backgroundObj.AddComponent<SpriteRenderer>();
        energyBarBackground.sprite = CreateColoredSprite(backgroundBarColor, (int)(energyBarWidth * 100), (int)(energyBarHeight * 100));

        if (parentSpriteRenderer != null)
        {
            energyBarBackground.sortingLayerName = parentSpriteRenderer.sortingLayerName;
        }
        // Fixed high value so bars always render above grass Y-sort range (400-1600)
        energyBarBackground.sortingOrder = 4000;
    }

    void CreateFillBar()
    {
        GameObject fillObj = new GameObject("EnergyBarFill");
        fillObj.transform.SetParent(energyBarContainer.transform);
        fillObj.transform.localPosition = Vector3.zero;

        energyBarFill = fillObj.AddComponent<SpriteRenderer>();
        energyBarFill.sprite = CreateColoredSprite(normalEnergyColor, (int)(energyBarWidth * 100), (int)(energyBarHeight * 100));

        if (parentSpriteRenderer != null)
        {
            energyBarFill.sortingLayerName = parentSpriteRenderer.sortingLayerName;
        }
        // Above background (4000)
        energyBarFill.sortingOrder = 4001;
    }

    void CreateEnergyText()
    {
        // Outline stamps must be created first so they render BEHIND the main text
        // (lower sortingOrder). The four offsets give a balanced N/S/E/W outline that
        // reads as a clean stroke at this character size while staying cheap.
        if (useTextOutline)
        {
            Vector2[] offsets = new Vector2[]
            {
                new Vector2(-textOutlineThickness, 0f),
                new Vector2( textOutlineThickness, 0f),
                new Vector2( 0f, -textOutlineThickness),
                new Vector2( 0f,  textOutlineThickness),
            };

            energyTextOutlines = new TextMesh[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject outlineObj = new GameObject($"EnergyTextOutline_{i}");
                outlineObj.transform.SetParent(energyBarContainer.transform);
                outlineObj.transform.localPosition = Vector3.up * 0.3f + (Vector3)offsets[i];

                TextMesh outlineMesh = outlineObj.AddComponent<TextMesh>();
                outlineMesh.text = $"{energyConsumer.GetEnergy():F0}/{energyConsumer.GetMaxEnergy():F0}";
                outlineMesh.fontSize = 22;
                outlineMesh.characterSize = 0.18f;
                outlineMesh.anchor = TextAnchor.MiddleCenter;
                outlineMesh.color = textOutlineColor;

                MeshRenderer outlineRenderer = outlineObj.GetComponent<MeshRenderer>();
                if (outlineRenderer != null)
                {
                    if (parentSpriteRenderer != null)
                        outlineRenderer.sortingLayerName = parentSpriteRenderer.sortingLayerName;
                    // One below the main text (4002), still above fill (4001)
                    outlineRenderer.sortingOrder = 4002;
                }

                energyTextOutlines[i] = outlineMesh;
            }
        }

        GameObject textObj = new GameObject("EnergyText");
        textObj.transform.SetParent(energyBarContainer.transform);
        textObj.transform.localPosition = Vector3.up * 0.3f;

        energyText = textObj.AddComponent<TextMesh>();
        energyText.text = $"{energyConsumer.GetEnergy():F0}/{energyConsumer.GetMaxEnergy():F0}";
        energyText.fontSize = 22;
        energyText.characterSize = 0.18f;
        energyText.anchor = TextAnchor.MiddleCenter;
        energyText.color = normalEnergyColor;

        MeshRenderer textRenderer = textObj.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            if (parentSpriteRenderer != null)
                textRenderer.sortingLayerName = parentSpriteRenderer.sortingLayerName;
            // Above fill (4001) and above outline stamps (4002)
            textRenderer.sortingOrder = 4003;
        }
    }
    #endregion

    #region Level Marker
    private int GetCurrentLevel()
    {
        return levelSourceTower != null ? levelSourceTower.GetUpgradeLevel() : 0;
    }

    private int GetMaxLevel()
    {
        return levelSourceTower != null ? levelSourceTower.GetMaxUpgradeLevel() : 0;
    }

    void CreateLevelMarker()
    {
        if (!showLevelMarker || energyBarContainer == null) return;

        int maxLevel = GetMaxLevel();
        // A tower that can never be upgraded has nothing to communicate — drawing a
        // lone chevron there would just be noise next to the bar.
        if (maxLevel <= 1) return;

        builtMaxLevel = maxLevel;

        // Geometry: the chevron is built pointing left, spanning chevronSize across its
        // opening and half that in depth — 45-degree arms, which is what makes it read
        // as a clean arrowhead rather than a squashed or stretched one. Rotating it to
        // point up swaps those two, so the footprint has to be swapped as well.
        float glyphSpan = chevronSize;
        float glyphDepth = chevronSize * 0.5f;
        bool pointsVertically = chevronDirection == ChevronDirection.Up || chevronDirection == ChevronDirection.Down;

        float glyphWidth = pointsVertically ? glyphSpan : glyphDepth;
        float glyphVerticalExtent = pointsVertically ? glyphDepth : glyphSpan;

        // An upward chevron is only half as tall as a sideways one, so a fixed step
        // would scatter them up the side of the bar. Derive it from the real height.
        stackStep = chevronSpacingOverride > 0.0001f
            ? chevronSpacingOverride
            : glyphVerticalExtent + chevronGap;

        levelMarkerContainer = new GameObject("LevelMarker");
        levelMarkerContainer.transform.SetParent(energyBarContainer.transform);
        levelMarkerContainer.transform.localRotation = Quaternion.identity;
        levelMarkerContainer.transform.localScale = Vector3.one;

        float sideSign = levelMarkerSide == LevelMarkerSide.Right ? 1f : -1f;
        levelMarkerContainer.transform.localPosition = new Vector3(
            sideSign * (energyBarWidth * 0.5f + levelMarkerGap + glyphWidth * 0.5f), 0f, 0f);

        if (unitSprite == null) unitSprite = CreateUnitSprite();

        chevrons = new List<ChevronGlyph>(maxLevel);
        for (int i = 0; i < maxLevel; i++)
            chevrons.Add(BuildChevron(i, glyphSpan, glyphDepth));

        shownLevel = -1;
        LayoutLevelMarker(GetCurrentLevel());
    }

    ChevronGlyph BuildChevron(int index, float height, float depth)
    {
        var glyph = new ChevronGlyph();

        glyph.root = new GameObject($"Chevron_{index + 1}");
        glyph.root.transform.SetParent(levelMarkerContainer.transform);
        glyph.root.transform.localPosition = Vector3.zero;
        glyph.root.transform.localScale = Vector3.one;
        // Only the glyph itself rotates; the stack always grows along local Y.
        glyph.root.transform.localRotation = Quaternion.Euler(0f, 0f, RotationFor(chevronDirection));

        float halfHeight = height * 0.5f;
        float armLength = Mathf.Sqrt(depth * depth + halfHeight * halfHeight);

        glyph.fills = new SpriteRenderer[2];
        glyph.outlines = useMarkerOutline ? new SpriteRenderer[2] : null;

        for (int arm = 0; arm < 2; arm++)
        {
            // arm 0 = upper stroke (tip -> up-right), arm 1 = lower stroke (tip -> down-right).
            float dir = arm == 0 ? 1f : -1f;
            Vector3 armPos = new Vector3(0f, dir * height * 0.25f, 0f);
            float angle = Mathf.Atan2(dir * halfHeight, depth) * Mathf.Rad2Deg;

            if (useMarkerOutline)
            {
                // A slightly fatter dark stroke behind the arm: cheaper and cleaner at
                // this size than four offset stamps like the text uses.
                glyph.outlines[arm] = MakeQuad(glyph.root.transform, $"ChevronOutline_{arm}",
                    armPos, angle,
                    armLength + markerOutlineThickness * 2f,
                    chevronThickness + markerOutlineThickness * 2f,
                    markerOutlineColor, 4002);
            }

            glyph.fills[arm] = MakeQuad(glyph.root.transform, $"ChevronArm_{arm}",
                armPos, angle, armLength, chevronThickness, levelMarkerColor, 4003);
        }

        return glyph;
    }

    SpriteRenderer MakeQuad(Transform parent, string name, Vector3 localPos, float angleDeg,
                            float length, float thickness, Color color, int sortingOrder)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
        obj.transform.localScale = new Vector3(length, thickness, 1f);

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = unitSprite;
        sr.color = color;
        if (parentSpriteRenderer != null)
            sr.sortingLayerName = parentSpriteRenderer.sortingLayerName;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    static float RotationFor(ChevronDirection direction)
    {
        switch (direction)
        {
            case ChevronDirection.Right: return 180f;
            case ChevronDirection.Up: return -90f;
            case ChevronDirection.Down: return 90f;
            default: return 0f; // Left — the "<" shape as built
        }
    }

    // Positions and colours the stack for a given level. The visible chevrons are always
    // re-centred vertically on the bar, so a level-1 tower shows one centred "<" rather
    // than one chevron floating at the bottom of an invisible three-slot column.
    void LayoutLevelMarker(int level)
    {
        if (chevrons == null || chevrons.Count == 0) return;

        int count = chevrons.Count;
        level = Mathf.Clamp(level, 0, count);
        int visible = showEmptyLevelSlots ? count : level;

        float startY = -(visible - 1) * 0.5f * stackStep;
        int slot = 0;

        for (int i = 0; i < count; i++)
        {
            ChevronGlyph glyph = chevrons[i];
            if (glyph == null || glyph.root == null) continue;

            bool earned = i < level;                       // level 1 fills the bottom chevron
            bool active = earned || showEmptyLevelSlots;

            if (glyph.root.activeSelf != active) glyph.root.SetActive(active);
            if (!active) continue;

            glyph.root.transform.localPosition = new Vector3(0f, startY + slot * stackStep, 0f);
            slot++;

            Color fill = earned ? levelMarkerColor : emptyLevelSlotColor;
            for (int a = 0; a < glyph.fills.Length; a++)
                if (glyph.fills[a] != null) glyph.fills[a].color = fill;

            if (glyph.outlines != null)
            {
                // Fade the outline with the placeholder so ghost chevrons stay ghostly.
                Color outline = markerOutlineColor;
                outline.a *= fill.a;
                for (int a = 0; a < glyph.outlines.Length; a++)
                    if (glyph.outlines[a] != null) glyph.outlines[a].color = outline;
            }
        }

        shownLevel = level;
    }

    void UpdateLevelMarker()
    {
        if (!showLevelMarker || energyBarContainer == null) return;

        if (levelMarkerContainer == null)
        {
            // Built lazily if the tower only became upgradeable after the bar was made.
            if (GetMaxLevel() > 1) CreateLevelMarker();
            return;
        }

        int maxLevel = GetMaxLevel();
        if (maxLevel != builtMaxLevel)
        {
            RebuildLevelMarker();
            return;
        }

        int level = GetCurrentLevel();
        if (level != shownLevel) LayoutLevelMarker(level);   // only touches renderers on change
    }

    void RebuildLevelMarker()
    {
        if (levelMarkerContainer != null) Destroy(levelMarkerContainer);
        levelMarkerContainer = null;
        chevrons = null;
        builtMaxLevel = -1;
        shownLevel = -1;
        CreateLevelMarker();
    }

    /// Force the marker to re-read the tower's level right now. Update() already polls,
    /// so this is only needed if you want the chevron to pop in the same frame as the
    /// upgrade (e.g. straight after Tower.ApplyUpgrade()).
    public void RefreshLevelMarker()
    {
        UpdateLevelMarker();
    }

    public void SetLevelMarkerVisible(bool visible)
    {
        showLevelMarker = visible;
        if (levelMarkerContainer != null) levelMarkerContainer.SetActive(visible);
        else if (visible) CreateLevelMarker();
    }
    #endregion

    #region Update Logic
    void Update()
    {
        if (energyConsumer != null && showEnergyBar)
        {
            UpdateEnergyBarVisuals();
            UpdateLevelMarker();
        }
    }

    void UpdateEnergyBarVisuals()
    {
        if (energyBarFill == null || energyBarBackground == null || energyConsumer == null) return;
        if (EnergyManager.Instance == null) return;

        float energyPercentage = energyConsumer.GetEnergyPercentage();

        // Determine energy bar color based on energy state
        Color energyColor = GetEnergyColor(energyPercentage);
        energyBarFill.color = energyColor;

        // Update energy bar fill scale to represent energy percentage
        Vector3 fillScale = new Vector3(energyPercentage, 1f, 1f);
        energyBarFill.transform.localScale = fillScale;

        // Adjust fill position to align with background
        Vector3 fillPosition = Vector3.left * (energyBarWidth * (1f - energyPercentage) * 0.5f);
        energyBarFill.transform.localPosition = fillPosition;

        // Update energy text
        if (energyText != null && showEnergyText)
        {
            string label = $"{energyConsumer.GetEnergy():F0}/{energyConsumer.GetMaxEnergy():F0}";
            energyText.text = label;
            energyText.color = energyColor;

            // Keep outline stamps in lockstep with the main text. Outline color stays
            // constant — it's the contrast layer, not part of the energy state cue.
            if (energyTextOutlines != null)
            {
                for (int i = 0; i < energyTextOutlines.Length; i++)
                {
                    if (energyTextOutlines[i] != null)
                    {
                        energyTextOutlines[i].text = label;
                    }
                }
            }
        }
    }

    Color GetEnergyColor(float energyPercentage)
    {
        if (energyConsumer.IsEnergyDepleted())
            return depletedEnergyColor;

        if (energyConsumer.IsEnergyLow())
        {
            float criticalThreshold = EnergyManager.Instance.GetCriticalThreshold(energyConsumer);
            return Color.Lerp(criticalEnergyColor, lowEnergyColor, energyPercentage / criticalThreshold);
        }

        return normalEnergyColor;
    }
    #endregion

    #region Utility Methods
    Sprite CreateColoredSprite(Color color, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
    }

    // A white 1x1-world-unit quad. Every chevron arm shares it and gets its shape from
    // localScale + colour from SpriteRenderer.color, so N chevrons cost one texture.
    Sprite CreateUnitSprite()
    {
        Texture2D texture = new Texture2D(4, 4);
        texture.filterMode = FilterMode.Point;
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();

        // pixelsPerUnit == texture size, so the sprite measures exactly 1x1 units.
        return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }
    #endregion

    #region Public Methods
    public void SetVisibility(bool visible)
    {
        showEnergyBar = visible;
        if (energyBarContainer != null)
        {
            energyBarContainer.SetActive(visible);
        }
    }

    public void SetColors(Color normal, Color low, Color critical, Color depleted)
    {
        normalEnergyColor = normal;
        lowEnergyColor = low;
        criticalEnergyColor = critical;
        depletedEnergyColor = depleted;
    }
    #endregion

    #region Cleanup
    void OnDestroy()
    {
        if (energyBarContainer != null)
        {
            DestroyImmediate(energyBarContainer);
        }
    }
    #endregion
}



