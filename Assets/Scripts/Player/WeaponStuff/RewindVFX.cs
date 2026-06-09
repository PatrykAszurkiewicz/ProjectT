using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Self-contained screen effect for the time-rewind clock. Plays a brief "rewind
/// disturbance" — a desaturating/scanline-style flash plus a spinning clock icon
/// that pulses in the centre of the screen — whenever the clock fires.
///
/// Fully runtime-built: it creates its own overlay Canvas, dark vignette, scanline
/// strips, and clock icon (loaded from Resources/Icons/Clock/ClockWhite). Nothing
/// needs to be wired in the scene. Just call RewindVFX.Play().
///
/// Trigger it from ClockSystem right after a successful rewind:
///     RewindVFX.Play();
/// </summary>
public class RewindVFX : MonoBehaviour
{
    private static RewindVFX _instance;

    [Header("Timing")]
    [Tooltip("Total duration of the effect in seconds (uses unscaled time so it plays even if the game is paused/rewinding).")]
    public float duration = 1.1f;

    [Header("Look")]
    [Tooltip("Resources path to the clock icon sprite.")]
    public string iconResourcePath = "Icons/Clock/ClockWhite";
    [Tooltip("Icon size as a fraction of screen height at its peak.")]
    [Range(0.1f, 0.6f)] public float iconScreenFraction = 0.28f;
    [Tooltip("How many full turns the clock hands spin (negative = counter-clockwise / 'rewind').")]
    public float iconSpins = -1.25f;
    [Tooltip("Tint of the dark disturbance vignette.")]
    public Color vignetteColor = new Color(0.05f, 0.10f, 0.20f, 1f);
    [Tooltip("Tint applied to the clock icon + scanlines.")]
    public Color accentColor = new Color(0.6f, 0.85f, 1f, 1f);

    // Runtime UI refs
    private Canvas _canvas;
    private CanvasGroup _group;
    private Image _vignette;
    private RectTransform _iconRT;
    private Image _iconImg;
    private RectTransform _scanlineRoot;
    private Sprite _iconSprite;
    private Coroutine _running;

    /// <summary>Fire the rewind effect. Creates the singleton on first use.</summary>
    public static void Play()
    {
        if (_instance == null)
        {
            var go = new GameObject("RewindVFX");
            _instance = go.AddComponent<RewindVFX>();
        }
        _instance.PlayInternal();
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildOverlay();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void BuildOverlay()
    {
        // ── Canvas on top of everything ──
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760; // above gameplay & most UI
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>().enabled = false; // never eats clicks

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // ── Full-screen vignette / disturbance backdrop ──
        _vignette = NewImage("Vignette", _canvas.transform);
        Stretch(_vignette.rectTransform);
        _vignette.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);
        _vignette.raycastTarget = false;

        // ── Scanline strips (cheap "interference" look) ──
        _scanlineRoot = NewRect("Scanlines", _canvas.transform);
        Stretch(_scanlineRoot);
        const int STRIPS = 14;
        for (int i = 0; i < STRIPS; i++)
        {
            var strip = NewImage($"Scan{i}", _scanlineRoot);
            var rt = strip.rectTransform;
            rt.anchorMin = new Vector2(0f, (float)i / STRIPS);
            rt.anchorMax = new Vector2(1f, (i + 0.5f) / STRIPS);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            strip.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.06f);
            strip.raycastTarget = false;
        }

        // ── Clock icon ──
        _iconSprite = Resources.Load<Sprite>(iconResourcePath);
        if (_iconSprite == null)
            Debug.LogWarning($"[RewindVFX] Could not load clock icon at Resources/{iconResourcePath}. " +
                             $"The disturbance will still play, just without the icon.");

        _iconImg = NewImage("ClockIcon", _canvas.transform);
        _iconImg.sprite = _iconSprite;
        _iconImg.preserveAspect = true;
        _iconImg.raycastTarget = false;
        _iconImg.color = accentColor;
        _iconRT = _iconImg.rectTransform;
        _iconRT.anchorMin = _iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        _iconRT.pivot = new Vector2(0.5f, 0.5f);
        _iconRT.anchoredPosition = Vector2.zero;
        float size = 1080f * iconScreenFraction;
        _iconRT.sizeDelta = new Vector2(size, size);

        gameObject.SetActive(true);
        _group.alpha = 0f;
    }

    private void PlayInternal()
    {
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        float t = 0f;
        float dur = Mathf.Max(0.2f, duration);

        // Phase split: quick punch-in, slower settle-out.
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);

            // Envelope: fast rise to ~0.15, hold, ease out. Peaks early.
            float env;
            if (p < 0.15f) env = p / 0.15f;            // rise
            else env = 1f - Mathf.SmoothStep(0f, 1f, (p - 0.15f) / 0.85f); // decay
            _group.alpha = env;

            // Vignette breathes with the envelope.
            var vc = vignetteColor; vc.a = env * 0.75f;
            _vignette.color = vc;

            // Clock: spin (counter-clockwise = rewind) + a slight scale pulse + flicker.
            float spin = iconSpins * 360f * Mathf.SmoothStep(0f, 1f, p);
            _iconRT.localRotation = Quaternion.Euler(0f, 0f, spin);
            float pulse = 0.9f + 0.12f * Mathf.Sin(p * Mathf.PI);     // gentle in/out
            _iconRT.localScale = Vector3.one * (pulse * (0.85f + 0.15f * env));
            var ic = accentColor; ic.a = env;
            _iconImg.color = ic;

            // Scanline jitter: slide the strip block vertically for a VHS-rewind feel.
            float jitter = Mathf.Sin(t * 60f) * 6f * env;
            _scanlineRoot.anchoredPosition = new Vector2(0f, jitter);

            yield return null;
        }

        _group.alpha = 0f;
        _vignette.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);
        _running = null;
    }

    // ── tiny UI builders ──
    private static Image NewImage(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
