using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;


// Post-stage reward choice menu.
// Drop the script on any GameObject (e.g. the one that holds GameOrchestrator) and call ShowChoice() from a coroutine.
// Sprites can optionally be dragged in via the

public class PostStageChoiceMenu : MonoBehaviour
{
    public enum Choice { None, Heal, Augment }

    //  INSPECTOR — SPRITE OVERRIDES (all optional)
    [Header("Sprite Overrides (optional — leave null for procedural fallback)")]
    [Tooltip("Background sprite for the WHOLE popup panel (parchment/wood). Null = procedural dark leather texture.")]
    public Sprite panelBackgroundSprite;

    [Tooltip("Background sprite for the HEAL (left) button. Null = procedural golden parchment.")]
    public Sprite healButtonSprite;

    [Tooltip("Background sprite for the AUGMENT (right) button. Null = procedural dark runic panel.")]
    public Sprite augmentButtonSprite;

    [Tooltip("Icon/emblem shown on the HEAL button (e.g. chalice, cross, heart). Null = procedural cross.")]
    public Sprite healIconSprite;

    [Tooltip("Icon/emblem shown on the AUGMENT button (e.g. rune, star, sigil). Null = procedural rune-star.")]
    public Sprite augmentIconSprite;

    [Tooltip("Optional corner ornament sprite for the panel (flourish / filigree). Null = skipped.")]
    public Sprite cornerOrnamentSprite;

    //  INSPECTOR — TEXT
    [Header("Labels")]
    public string headerText = "THE STAGE IS CLEARED";
    public string subheaderText = "Choose your reward";

    public string healButtonTitle = "RESTORE";
    public string healButtonSubtitleFormat = "Restore the Core, Towers and your Health\n\n<size=22><color=#ffffff>+{0} Energy</color></size>";

    public string augmentButtonTitle = "EMPOWER";
    public string augmentButtonSubtitleFormat = "Receive a new power\n\n<size=22><color=#a890d8>+{0} Energy</color></size>";

    //  INSPECTOR — COLORS

    [Header("Heal Card Palette (warm / golden)")]
    public Color healBaseColor = new Color(0.82f, 0.70f, 0.42f, 1f);
    public Color healAccentColor = new Color(0.95f, 0.82f, 0.45f, 1f);
    public Color healGlowColor = new Color(1.0f, 0.75f, 0.25f, 0.5f);

    [Header("Augment Card Palette (cool / arcane)")]
    public Color augmentBaseColor = new Color(0.18f, 0.15f, 0.32f, 1f);
    public Color augmentAccentColor = new Color(0.55f, 0.40f, 0.85f, 1f);
    public Color augmentGlowColor = new Color(0.45f, 0.30f, 0.90f, 0.55f);

    [Header("Frame & Backdrop")]
    public Color backdropColor = new Color(0.02f, 0.02f, 0.05f, 0.85f);
    public Color panelBaseColor = new Color(0.14f, 0.10f, 0.07f, 0.98f);
    public Color goldTrimColor = new Color(0.76f, 0.60f, 0.28f, 1f);

    [Header("Animation")]
    [Tooltip("How long the entrance animation takes (seconds, unscaled).")]
    public float entranceDuration = 0.45f;

    [Tooltip("How much cards slide in from the sides (pixels).")]
    public float slideDistance = 60f;

    //  RUNTIME STATE
    private Canvas canvas;
    private GameObject root;
    private RectTransform healCardRect;
    private RectTransform augmentCardRect;
    private CanvasGroup rootGroup;
    private Text headerLabel;
    private Text subheaderLabel;
    private Text healSubtitleText;
    private Text augmentSubtitleText;
    private Text healTitleText;
    private Text augmentTitleText;
    private Choice pickedChoice = Choice.None;
    private bool isOpen = false;
    private bool built = false;

    // Cached procedural sprites
    private Sprite procParchment;
    private Sprite procDarkRune;
    private Sprite procPanel;
    private Sprite procHealIcon;
    private Sprite procAugmentIcon;
    private Sprite procGlow;
    private Sprite procSolid;

    // Show the choice popup and wait until the player picks one. Use from a coroutine:
    //     yield return StartCoroutine(menu.ShowChoice(300, 100, c => { chosen = c; }));

    public IEnumerator ShowChoice(int healEnergyBonus, int augmentEnergyBonus, System.Action<Choice> onChosen)
    {
        // Self-heal: if the canvas was destroyed (scene reload, manual delete, etc.) but `built`
        // is still true, force a rebuild. Without this, root.SetActive(true) silently does nothing
        // and the menu never appears.
        if (built && (root == null || canvas == null))
        {
            Debug.LogWarning("[PostStageChoiceMenu] Canvas was destroyed; rebuilding.");
            built = false;
        }

        EnsureBuilt();

        headerLabel.text = headerText;
        subheaderLabel.text = subheaderText;
        healTitleText.text = healButtonTitle;
        augmentTitleText.text = augmentButtonTitle;
        healSubtitleText.text = string.Format(healButtonSubtitleFormat, healEnergyBonus);
        augmentSubtitleText.text = string.Format(augmentButtonSubtitleFormat, augmentEnergyBonus);

        pickedChoice = Choice.None;
        root.SetActive(true);
        isOpen = true;

        // Kill any in-flight camera shake so the reward popup sits still.
        // CameraShake uses unscaledDeltaTime and would keep shaking through the pause otherwise.
        CombatJuice.StopAllShake();

        float prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Cursor.visible = true;
        bool prevInputSuppressed = PlayerAttack.InputSuppressed;
        PlayerAttack.InputSuppressed = true;

        yield return StartCoroutine(PlayEntranceAnimation());

        while (pickedChoice == Choice.None)
        {
            yield return null;
        }

        Time.timeScale = prevTimeScale;
        PlayerAttack.InputSuppressed = prevInputSuppressed;
        Cursor.visible = false;
        root.SetActive(false);
        isOpen = false;

        onChosen?.Invoke(pickedChoice);
    }

    private IEnumerator PlayEntranceAnimation()
    {
        float t = 0f;
        Vector2 healStart = new Vector2(-slideDistance, 0f);
        Vector2 augmentStart = new Vector2(slideDistance, 0f);
        Vector2 endPos = Vector2.zero;

        rootGroup.alpha = 0f;
        healCardRect.anchoredPosition = healStart;
        augmentCardRect.anchoredPosition = augmentStart;

        while (t < entranceDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / entranceDuration);
            float e = 1f - Mathf.Pow(1f - u, 3f); // ease-out cubic

            rootGroup.alpha = e;
            healCardRect.anchoredPosition = Vector2.Lerp(healStart, endPos, e);
            augmentCardRect.anchoredPosition = Vector2.Lerp(augmentStart, endPos, e);

            yield return null;
        }
        rootGroup.alpha = 1f;
        healCardRect.anchoredPosition = endPos;
        augmentCardRect.anchoredPosition = endPos;
    }

    public bool IsOpen() => isOpen;

    //  UI CONSTRUCTION
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        GenerateProceduralSprites();

        // Canvas
        GameObject canvasObj = new GameObject("PostStageChoiceCanvas");
        // Parent to scene root (no parent) instead of `transform`. This prevents the popup from
        // being affected by anything happening to the parent GameObject (e.g. it being parented
        // under another Canvas, having its scale changed, being temporarily disabled, etc.).
        // ScreenSpaceOverlay canvases work the same regardless of parent.
        canvasObj.transform.SetParent(null, false);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9997;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // Make sure there's an EventSystem in the scene — without one, no UI buttons work.
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        rootGroup = canvasObj.AddComponent<CanvasGroup>();
        rootGroup.alpha = 1f;

        root = canvasObj;

        BuildBackdrop(canvasObj.transform);
        RectTransform panel = BuildPanel(canvasObj.transform);
        BuildHeader(panel);

        healCardRect = BuildCard(
            panel, "HealCard",
            new Vector2(0.05f, 0.08f), new Vector2(0.49f, 0.70f),
            healButtonSprite != null ? healButtonSprite : procParchment,
            healBaseColor, healAccentColor, healGlowColor,
            healIconSprite != null ? healIconSprite : procHealIcon,
            out healTitleText, out healSubtitleText,
            new Color(0.35f, 0.22f, 0.08f, 1f),
            () => OnPicked(Choice.Heal)
        );

        augmentCardRect = BuildCard(
            panel, "AugmentCard",
            new Vector2(0.51f, 0.08f), new Vector2(0.95f, 0.70f),
            augmentButtonSprite != null ? augmentButtonSprite : procDarkRune,
            augmentBaseColor, augmentAccentColor, augmentGlowColor,
            augmentIconSprite != null ? augmentIconSprite : procAugmentIcon,
            out augmentTitleText, out augmentSubtitleText,
            new Color(0.92f, 0.88f, 1f, 1f),
            () => OnPicked(Choice.Augment)
        );

        headerLabel.transform.SetAsLastSibling();
        subheaderLabel.transform.SetAsLastSibling();

        root.SetActive(false);
    }

    private void BuildBackdrop(Transform parent)
    {
        GameObject bd = new GameObject("Backdrop");
        bd.transform.SetParent(parent, false);
        Image img = bd.AddComponent<Image>();
        img.color = backdropColor;
        img.raycastTarget = true;
        RectTransform rt = bd.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private RectTransform BuildPanel(Transform parent)
    {
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(parent, false);
        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400, 780);
        rt.anchoredPosition = Vector2.zero;

        // Soft outer shadow
        CreateBgLayer(panelObj.transform, "Shadow",
            procSolid, new Color(0f, 0f, 0f, 0.65f),
            new Vector2(-30, -40), new Vector2(30, 20));

        // Gold outer trim
        CreateBgLayer(panelObj.transform, "GoldTrim",
            procSolid, goldTrimColor,
            new Vector2(-8, -8), new Vector2(8, 8));

        // Dark leather body
        CreateBgLayer(panelObj.transform, "LeatherBack",
            panelBackgroundSprite != null ? panelBackgroundSprite : procPanel,
            panelBackgroundSprite != null ? Color.white : panelBaseColor,
            Vector2.zero, Vector2.zero);

        // Inner gold pinstripe glow (subtle highlight near edge)
        CreateBgLayer(panelObj.transform, "InnerTrimGlow",
            procSolid, new Color(goldTrimColor.r, goldTrimColor.g, goldTrimColor.b, 0.18f),
            new Vector2(16, 16), new Vector2(-16, -16));

        if (cornerOrnamentSprite != null)
        {
            // args: parent, anchor (corner), pivot (matches anchor), scale (flip to face inward)
            AddCornerOrnament(panelObj.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(1, -1));   // Top-Left
            AddCornerOrnament(panelObj.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-1, -1)); // Top-Right
            AddCornerOrnament(panelObj.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(1, 1));   // Bottom-Left
            AddCornerOrnament(panelObj.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-1, 1));  // Bottom-Right
        }

        return rt;
    }

    private void CreateBgLayer(Transform parent, string name, Sprite sprite, Color color,
                               Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
    }

    private void AddCornerOrnament(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 scale)
    {
        GameObject go = new GameObject("Corner");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = cornerOrnamentSprite;
        img.color = goldTrimColor;
        img.raycastTarget = false;
        img.preserveAspect = true;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(96, 96);
        rt.anchoredPosition = new Vector2(12 * scale.x, 12 * scale.y);
        rt.localScale = new Vector3(scale.x, scale.y, 1f);
    }

    private void BuildHeader(Transform parent)
    {
        // Title
        GameObject hObj = new GameObject("Header");
        hObj.transform.SetParent(parent, false);
        headerLabel = hObj.AddComponent<Text>();
        headerLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerLabel.fontSize = 56;
        headerLabel.fontStyle = FontStyle.Bold;
        headerLabel.alignment = TextAnchor.MiddleCenter;
        headerLabel.color = goldTrimColor;
        headerLabel.raycastTarget = false;
        headerLabel.supportRichText = true;
        headerLabel.text = headerText;

        Shadow topShadow = hObj.AddComponent<Shadow>();
        topShadow.effectColor = new Color(1f, 0.92f, 0.60f, 0.6f);
        topShadow.effectDistance = new Vector2(0, 2);
        Outline carved = hObj.AddComponent<Outline>();
        carved.effectColor = new Color(0.12f, 0.07f, 0.02f, 1f);
        carved.effectDistance = new Vector2(3, -3);

        RectTransform hRect = hObj.GetComponent<RectTransform>();
        hRect.anchorMin = new Vector2(0f, 0.88f);
        hRect.anchorMax = new Vector2(1f, 0.98f);
        hRect.offsetMin = Vector2.zero; hRect.offsetMax = Vector2.zero;

        // Subheader
        GameObject sObj = new GameObject("Subheader");
        sObj.transform.SetParent(parent, false);
        subheaderLabel = sObj.AddComponent<Text>();
        subheaderLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subheaderLabel.fontSize = 26;
        subheaderLabel.fontStyle = FontStyle.Italic;
        subheaderLabel.alignment = TextAnchor.MiddleCenter;
        subheaderLabel.color = new Color(0.85f, 0.78f, 0.62f, 1f);
        subheaderLabel.raycastTarget = false;
        subheaderLabel.text = subheaderText;
        Outline subOutline = sObj.AddComponent<Outline>();
        subOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        subOutline.effectDistance = new Vector2(1, -1);

        RectTransform sRect = sObj.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0f, 0.80f);
        sRect.anchorMax = new Vector2(1f, 0.87f);
        sRect.offsetMin = Vector2.zero; sRect.offsetMax = Vector2.zero;
    }

    private RectTransform BuildCard(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Sprite bgSprite,
        Color baseColor,
        Color accentColor,
        Color glowColor,
        Sprite iconSprite,
        out Text titleText,
        out Text subtitleText,
        Color titleColor,
        System.Action onClick)
    {
        GameObject cardObj = new GameObject(name);
        cardObj.transform.SetParent(parent, false);
        RectTransform rt = cardObj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        // Outer gold frame
        CreateBgLayer(cardObj.transform, "CardGoldFrame",
            procSolid, goldTrimColor,
            Vector2.zero, Vector2.zero);

        // Inset body — this is the clickable surface
        GameObject bodyObj = new GameObject("CardBody");
        bodyObj.transform.SetParent(cardObj.transform, false);
        Image bodyImg = bodyObj.AddComponent<Image>();
        bodyImg.sprite = bgSprite;
        bodyImg.color = (bgSprite == procParchment || bgSprite == procDarkRune) ? baseColor : Color.white;
        bodyImg.raycastTarget = true;
        RectTransform bodyRt = bodyObj.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero; bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(6, 6); bodyRt.offsetMax = new Vector2(-6, -6);

        // Ambient glow behind icon
        GameObject glowObj = new GameObject("Glow");
        glowObj.transform.SetParent(bodyObj.transform, false);
        Image glowImg = glowObj.AddComponent<Image>();
        glowImg.sprite = procGlow;
        glowImg.color = glowColor;
        glowImg.raycastTarget = false;
        RectTransform glowRt = glowObj.GetComponent<RectTransform>();
        glowRt.anchorMin = new Vector2(0.5f, 0.65f);
        glowRt.anchorMax = new Vector2(0.5f, 0.65f);
        glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.sizeDelta = new Vector2(380, 380);

        // Icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(bodyObj.transform, false);
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = iconSprite;
        iconImg.color = accentColor;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        RectTransform iconRt = iconObj.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.65f);
        iconRt.anchorMax = new Vector2(0.5f, 0.65f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(200, 200);

        // Thin top-edge highlight
        GameObject topEdge = new GameObject("TopHighlight");
        topEdge.transform.SetParent(bodyObj.transform, false);
        Image teImg = topEdge.AddComponent<Image>();
        teImg.sprite = procSolid;
        teImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.4f);
        teImg.raycastTarget = false;
        RectTransform teRt = topEdge.GetComponent<RectTransform>();
        teRt.anchorMin = new Vector2(0, 1); teRt.anchorMax = new Vector2(1, 1);
        teRt.pivot = new Vector2(0.5f, 1f);
        teRt.sizeDelta = new Vector2(-40, 2);
        teRt.anchoredPosition = new Vector2(0, -20);

        // Title
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(bodyObj.transform, false);
        titleText = titleObj.AddComponent<Text>();
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 52;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = titleColor;
        titleText.raycastTarget = false;
        Outline tOut = titleObj.AddComponent<Outline>();
        tOut.effectColor = new Color(0f, 0f, 0f, 0.75f);
        tOut.effectDistance = new Vector2(2, -2);
        RectTransform tRect = titleObj.GetComponent<RectTransform>();
        tRect.anchorMin = new Vector2(0.05f, 0.33f);
        tRect.anchorMax = new Vector2(0.95f, 0.46f);
        tRect.offsetMin = Vector2.zero; tRect.offsetMax = Vector2.zero;

        // Subtitle
        GameObject subObj = new GameObject("Subtitle");
        subObj.transform.SetParent(bodyObj.transform, false);
        subtitleText = subObj.AddComponent<Text>();
        subtitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subtitleText.fontSize = 24;
        subtitleText.alignment = TextAnchor.UpperCenter;
        subtitleText.color = new Color(titleColor.r, titleColor.g, titleColor.b, 0.92f);
        subtitleText.raycastTarget = false;
        subtitleText.supportRichText = true;
        subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        subtitleText.verticalOverflow = VerticalWrapMode.Overflow;
        subtitleText.lineSpacing = 1.15f;
        Outline sOut = subObj.AddComponent<Outline>();
        sOut.effectColor = new Color(0f, 0f, 0f, 0.55f);
        sOut.effectDistance = new Vector2(1, -1);
        RectTransform sRect = subObj.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0.08f, 0.06f);
        sRect.anchorMax = new Vector2(0.92f, 0.32f);
        sRect.offsetMin = Vector2.zero; sRect.offsetMax = Vector2.zero;

        // Button
        Button btn = bodyObj.AddComponent<Button>();
        btn.targetGraphic = bodyImg;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick?.Invoke());

        // Hover lift + glow
        var hover = cardObj.AddComponent<CardHoverEffect>();
        hover.Initialize(rt, glowImg, iconImg, accentColor, glowColor);

        return rt;
    }

    private void OnPicked(Choice choice)
    {
        if (!isOpen) return;
        pickedChoice = choice;
    }

    //  PROCEDURAL SPRITE GENERATION
    private void GenerateProceduralSprites()
    {
        procSolid = MakeSolidSprite();
        procParchment = MakeParchmentSprite(warm: true);
        procDarkRune = MakeParchmentSprite(warm: false);
        procPanel = MakePanelSprite();
        procHealIcon = MakeHealIconSprite();
        procAugmentIcon = MakeAugmentIconSprite();
        procGlow = MakeGlowSprite();
    }

    private Sprite MakeSolidSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                tex.SetPixel(x, y, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
    }

    private Sprite MakeParchmentSprite(bool warm)
    {
        int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color baseC = warm ? new Color(0.95f, 0.87f, 0.68f, 1f) : new Color(0.22f, 0.18f, 0.38f, 1f);
        Color darkC = warm ? new Color(0.70f, 0.55f, 0.30f, 1f) : new Color(0.08f, 0.06f, 0.18f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size - 0.5f;
                float ny = y / (float)size - 0.5f;
                float r = Mathf.Sqrt(nx * nx + ny * ny) * 2f;
                float vignette = Mathf.Clamp01(1f - r * 0.6f);

                float n1 = Mathf.PerlinNoise(x * 0.035f, y * 0.035f);
                float n2 = Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                float noise = 0.65f * n1 + 0.35f * n2;
                noise = Mathf.Lerp(0.80f, 1.05f, noise);

                Color c = Color.Lerp(darkC, baseC, vignette);
                c *= noise;
                c.a = 1f;

                float fleck = Mathf.PerlinNoise(x * 0.6f + 100f, y * 0.6f + 100f);
                if (fleck > 0.88f) c *= warm ? 0.75f : 1.2f;

                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private Sprite MakePanelSprite()
    {
        int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color baseC = new Color(0.14f, 0.10f, 0.07f, 1f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size - 0.5f;
                float ny = y / (float)size - 0.5f;
                float r = Mathf.Sqrt(nx * nx + ny * ny) * 2f;
                float v = Mathf.Clamp01(1f - r * 0.8f);

                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                n = Mathf.Lerp(0.85f, 1.12f, n);

                Color c = baseC * (0.55f + v * 0.55f) * n;
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private Sprite MakeGlowSprite()
    {
        int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - size / 2f) / (size / 2f);
                float ny = (y - size / 2f) / (size / 2f);
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Clamp01(1f - r);
                alpha = alpha * alpha;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // Stylized cross / holy symbol for HEAL
    private Sprite MakeHealIconSprite()
    {
        int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, new Color(0, 0, 0, 0));

        int cx = size / 2;
        int barHalfW = 10;
        int topY = 16;
        int botY = size - 16;

        // Vertical bar
        for (int y = topY; y <= botY; y++)
            for (int x = cx - barHalfW; x <= cx + barHalfW; x++)
                SetPixelSafe(tex, x, y, Color.white);

        // Horizontal bar
        int hMidY = size / 2 + 8;
        int hHalfLen = 36;
        int hHalfH = 10;
        for (int y = hMidY - hHalfH; y <= hMidY + hHalfH; y++)
            for (int x = cx - hHalfLen; x <= cx + hHalfLen; x++)
                SetPixelSafe(tex, x, y, Color.white);

        // Tapered ends on the vertical bar
        for (int dy = 0; dy < 8; dy++)
        {
            float shrink = 1f - dy / 8f;
            int w = Mathf.RoundToInt(barHalfW * shrink);
            for (int x = cx - w; x <= cx + w; x++)
            {
                SetPixelSafe(tex, x, topY - dy, Color.white);
                SetPixelSafe(tex, x, botY + dy, Color.white);
            }
        }

        // Soft glow at intersection
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - cx) / (float)size;
                float ny = (y - hMidY) / (float)size;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                if (d < 0.18f)
                {
                    Color existing = tex.GetPixel(x, y);
                    float glow = Mathf.Clamp01(1f - d / 0.18f) * 0.35f;
                    existing.a = Mathf.Max(existing.a, glow);
                    if (existing.a > 0) { existing.r = 1f; existing.g = 1f; existing.b = 1f; }
                    tex.SetPixel(x, y, existing);
                }
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // 8-point runic star for AUGMENT
    private Sprite MakeAugmentIconSprite()
    {
        int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, new Color(0, 0, 0, 0));

        Vector2 c = new Vector2(size / 2f, size / 2f);
        float outerR = size * 0.42f;
        float innerR = size * 0.16f;
        int points = 8;

        Vector2[] verts = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float angle = (i / (float)(points * 2)) * Mathf.PI * 2f - Mathf.PI / 2f;
            float r = (i % 2 == 0) ? outerR : innerR;
            verts[i] = c + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), verts))
                    tex.SetPixel(x, y, Color.white);
            }
        }

        // Central gem
        float gemR = size * 0.08f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                if (d < gemR)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, 1f));
                else if (d < gemR + 2f)
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0.6f));
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
            {
                inside = !inside;
            }
            j = i;
        }
        return inside;
    }

    private static void SetPixelSafe(Texture2D tex, int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) return;
        tex.SetPixel(x, y, c);
    }

    //  INNER CLASS — hover lift + glow pulse on each card
    private class CardHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform cardRt;
        private Image glowImg;
        private Image iconImg;
        private Color baseGlowColor;
        private Color baseAccentColor;
        private Vector3 baseScale;
        private Coroutine anim;

        public void Initialize(RectTransform rt, Image glow, Image icon, Color accent, Color glow0)
        {
            cardRt = rt;
            glowImg = glow;
            iconImg = icon;
            baseAccentColor = accent;
            baseGlowColor = glow0;
            baseScale = rt.localScale;
        }

        public void OnPointerEnter(PointerEventData e) => StartAnim(true);
        public void OnPointerExit(PointerEventData e) => StartAnim(false);

        private void StartAnim(bool hover)
        {
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Animate(hover));
        }

        private IEnumerator Animate(bool hover)
        {
            float dur = 0.18f;
            float t = 0f;
            Vector3 fromScale = cardRt.localScale;
            Vector3 toScale = hover ? baseScale * 1.04f : baseScale;
            Color fromGlow = glowImg.color;
            Color toGlow = hover
                ? new Color(baseGlowColor.r, baseGlowColor.g, baseGlowColor.b, Mathf.Min(1f, baseGlowColor.a * 1.8f))
                : baseGlowColor;
            Color fromIcon = iconImg.color;
            Color toIcon = hover
                ? new Color(
                    Mathf.Min(1f, baseAccentColor.r * 1.25f),
                    Mathf.Min(1f, baseAccentColor.g * 1.25f),
                    Mathf.Min(1f, baseAccentColor.b * 1.25f),
                    baseAccentColor.a)
                : baseAccentColor;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                float e = 1f - Mathf.Pow(1f - u, 3f);
                cardRt.localScale = Vector3.Lerp(fromScale, toScale, e);
                glowImg.color = Color.Lerp(fromGlow, toGlow, e);
                iconImg.color = Color.Lerp(fromIcon, toIcon, e);
                yield return null;
            }
            cardRt.localScale = toScale;
            glowImg.color = toGlow;
            iconImg.color = toIcon;
            anim = null;
        }
    }
}