using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// LORE SCROLL POPUP (TextMeshPro) 
// Runtime-built popup that shows a single lore fragment on a torn, partly-burned
// sheet of paper. No prefab needed.
public class LoreScrollPopup : MonoBehaviour
{
    private static LoreScrollPopup _instance;
    public static LoreScrollPopup Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<LoreScrollPopup>();
                if (_instance == null)
                {
                    var go = new GameObject("LoreScrollPopup");
                    _instance = go.AddComponent<LoreScrollPopup>();
                }
            }
            return _instance;
        }
    }

    [Header("Palette")]
    public Color backdropColor = new Color(0.02f, 0.02f, 0.05f, 0.80f);
    public Color parchmentColor = new Color(0.86f, 0.78f, 0.58f, 1f);
    public Color parchmentEdgeColor = new Color(0.55f, 0.44f, 0.26f, 1f);
    public Color titleColor = new Color(0.20f, 0.12f, 0.05f, 1f);
    public Color bodyColor = new Color(0.16f, 0.10f, 0.05f, 1f);
    public Color hintColor = new Color(0.30f, 0.20f, 0.10f, 0.8f);
    [Tooltip("Colour of the '+10 Energy recovered' line. Ink-blue so it reads as a stamp on the page.")]
    public Color rewardColor = new Color(0.10f, 0.32f, 0.46f, 1f);

    [Header("Animation")]
    public float entranceDuration = 0.4f;

    [Tooltip("Optional TMP font for the scroll text (assign your menu font, e.g. Cinzel-Black SDF). " +
             "Falls back to TextMeshPro's default font asset.")]
    public TMP_FontAsset overrideFont;

    // runtime
    private Canvas canvas;
    private GameObject root;
    private CanvasGroup rootGroup;
    private RectTransform panelRect;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;
    private TextMeshProUGUI rewardText;
    private bool built;
    private bool isOpen;
    private bool dismissRequested;

    private Sprite procPaper;
    private Sprite procSolid;
    private TMP_FontAsset uiFont;

    public bool IsOpen => isOpen;

    /// Returns true if the scroll took the fragment. False means it was rejected (another
    /// scroll is already up) — the caller must NOT treat the fragment as read.
    public bool ShowFragment(LoreFragment fragment, string rewardNote = null)
    {
        if (fragment == null) return false;
        if (isOpen) return false; // modal — one scroll at a time
        StartCoroutine(ShowRoutine(fragment, rewardNote));
        return isOpen;            // ShowRoutine runs to its first yield synchronously
    }

    public IEnumerator ShowRoutine(LoreFragment fragment, string rewardNote = null)
    {
        if (built && (root == null || canvas == null)) built = false;
        EnsureBuilt();

        titleText.text = fragment.title;
        bodyText.text = fragment.body;

        bool hasReward = !string.IsNullOrEmpty(rewardNote);
        if (rewardText != null)
        {
            rewardText.text = rewardNote ?? "";
            rewardText.gameObject.SetActive(hasReward);
        }

        dismissRequested = false;
        root.SetActive(true);
        isOpen = true;

        CombatJuice.StopAllShake();
        float prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        bool prevCursorVisible = Cursor.visible;
        Cursor.visible = true;
        bool prevInputSuppressed = PlayerAttack.InputSuppressed;
        PlayerAttack.InputSuppressed = true;

        if (AudioManager.instance != null && FMODEvents.instance != null)
            AudioManager.instance.PlayOneShot(FMODEvents.instance.towerCreation, Vector3.zero);

        yield return StartCoroutine(PlayEntrance());

        // Stamp the reward line on a beat after the page settles.
        if (hasReward) StartCoroutine(PopReward());

        yield return null; // swallow the opening click
        while (!dismissRequested && !DismissInputDown())
            yield return null;

        Time.timeScale = prevTimeScale;
        PlayerAttack.InputSuppressed = prevInputSuppressed;
        Cursor.visible = prevCursorVisible;
        root.SetActive(false);
        isOpen = false;
    }

    private bool DismissInputDown()
    {
#if ENABLE_INPUT_SYSTEM
        bool mouse = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool key = Keyboard.current != null &&
                   (Keyboard.current.spaceKey.wasPressedThisFrame
                    || Keyboard.current.enterKey.wasPressedThisFrame
                    || Keyboard.current.escapeKey.wasPressedThisFrame);
        return mouse || key;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0)
            || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    private IEnumerator PlayEntrance()
    {
        float t = 0f;
        rootGroup.alpha = 0f;
        Vector3 from = Vector3.one * 0.85f;
        Vector3 to = Vector3.one;
        panelRect.localScale = from;

        while (t < entranceDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / entranceDuration);
            float e = 1f - Mathf.Pow(1f - u, 3f);
            rootGroup.alpha = e;
            panelRect.localScale = Vector3.Lerp(from, to, e);
            yield return null;
        }
        rootGroup.alpha = 1f;
        panelRect.localScale = to;
    }

    // Reward line fades in and overshoots slightly — unscaled, since the game is paused.
    private IEnumerator PopReward()
    {
        if (rewardText == null) yield break;

        var rt = rewardText.rectTransform;
        const float dur = 0.32f;
        float t = 0f;

        Color c = rewardColor; c.a = 0f;
        rewardText.color = c;
        rt.localScale = Vector3.one * 0.75f;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = 1f - Mathf.Pow(1f - u, 3f);
            float overshoot = 1f + 0.12f * Mathf.Sin(e * Mathf.PI);   // small pop past 1
            rt.localScale = Vector3.one * (Mathf.Lerp(0.75f, 1f, e) * overshoot);
            c.a = rewardColor.a * e;
            rewardText.color = c;
            yield return null;
        }

        rt.localScale = Vector3.one;
        rewardText.color = rewardColor;
    }

    // UI CONSTRUCTION 
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        uiFont = overrideFont;     // null → TMP default font asset
        procPaper = LorePaperArt.MakePaperSprite();
        procSolid = MakeSolidSprite();

        var canvasObj = new GameObject("LoreScrollCanvas");
        canvasObj.transform.SetParent(null, false);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9998;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        rootGroup = canvasObj.AddComponent<CanvasGroup>();
        root = canvasObj;

        var backdrop = CreateImage("Backdrop", root.transform, procSolid, backdropColor);
        StretchFull(backdrop.rectTransform);
        var backdropBtn = backdrop.gameObject.AddComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(() => dismissRequested = true);

        var panel = CreateImage("PaperSheet", root.transform, procPaper, Color.white);
        panel.type = Image.Type.Simple;
        panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(900, 620);
        panelRect.anchoredPosition = Vector2.zero;

        titleText = CreateText("Title", panelRect, "", 36, FontStyles.Bold, TextAlignmentOptions.Center, titleColor);
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 26;
        titleText.fontSizeMax = 36;
        var tr = titleText.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.offsetMin = new Vector2(80, -178); tr.offsetMax = new Vector2(-80, -54);

        var divider = CreateImage("Divider", panelRect, procSolid, new Color(titleColor.r, titleColor.g, titleColor.b, 0.40f));
        var dr = divider.rectTransform;
        dr.anchorMin = new Vector2(0.5f, 1f); dr.anchorMax = new Vector2(0.5f, 1f);
        dr.pivot = new Vector2(0.5f, 1f);
        dr.sizeDelta = new Vector2(480, 2);
        dr.anchoredPosition = new Vector2(0, -192);

        bodyText = CreateText("Body", panelRect, "", 29, FontStyles.Italic, TextAlignmentOptions.TopLeft, bodyColor);
        bodyText.enableAutoSizing = true;       // long fragments shrink to fit — never spill off the paper
        bodyText.fontSizeMin = 15;
        bodyText.fontSizeMax = 29;
        bodyText.lineSpacing = 6f;
        var bodyR = bodyText.rectTransform;
        bodyR.anchorMin = new Vector2(0f, 0f); bodyR.anchorMax = new Vector2(1f, 1f);
        // Bottom padding raised from 126 → 168 to reserve a lane for the reward line.
        bodyR.offsetMin = new Vector2(110, 168); bodyR.offsetMax = new Vector2(-110, -212);

        rewardText = CreateText("Reward", panelRect, "", 24, FontStyles.Bold, TextAlignmentOptions.Center, rewardColor);
        var rr = rewardText.rectTransform;
        rr.anchorMin = new Vector2(0f, 0f); rr.anchorMax = new Vector2(1f, 0f);
        rr.pivot = new Vector2(0.5f, 0f);
        rr.offsetMin = new Vector2(80, 122); rr.offsetMax = new Vector2(-80, 160);
        rewardText.gameObject.SetActive(false);

        var hint = CreateText("Hint", panelRect, "— click to continue —", 20, FontStyles.Normal, TextAlignmentOptions.Bottom, hintColor);
        var hr = hint.rectTransform;
        hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
        hr.pivot = new Vector2(0.5f, 0f);
        hr.offsetMin = new Vector2(60, 74); hr.offsetMax = new Vector2(-60, 116);

        root.SetActive(false);
    }

    // UI HELPERS 
    private Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        return img;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string content, float size,
                                       FontStyles style, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) txt.font = uiFont;     // else TMP default
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.alignment = align;
        txt.color = color;
        txt.textWrappingMode = TextWrappingModes.Normal;
        txt.richText = false;
        txt.raycastTarget = false;
        return txt;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private Sprite MakeSolidSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
