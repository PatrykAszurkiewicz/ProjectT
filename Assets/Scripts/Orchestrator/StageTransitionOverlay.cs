using System.Collections;
using UnityEngine;
using UnityEngine.UI;


// Full-screen overlay for smooth biome transitions.
// The orchestrator calls:
//   yield return FadeOut()      — screen goes dark
//   yield return ShowBanner()   — "Stage 2: Desert" text appears over black
//   yield return FadeIn()       — screen fades back to gameplay
public class StageTransitionOverlay : MonoBehaviour
{
    [Header("Fade")]
    public float fadeOutDuration = 0.4f;
    public float fadeInDuration = 0.4f;
    public Color fadeColor = Color.black;

    [Tooltip("Start with screen fully black (hides the initial biome load).")]
    public bool startBlack = true;

    [Tooltip("Safety net: if the very first reveal has not happened within this many " +
             "seconds, force the screen to clear so a startup error can't strand the " +
             "player on a black screen. Set to 0 to disable.")]
    public float initialRevealFailsafeSeconds = 8f;

    [Tooltip("HARD backstop: no matter what (a resume that never completes, a swallowed " +
         "exception, or a latched intro flag), if the screen is STILL fully black this " +
         "many seconds after boot it is force-cleared. Must exceed GameOrchestrator's " +
         "DeferredResume max wait (15s) so it never clips a legitimately slow resume.")]
    public float hardRevealCeilingSeconds = 20f;

    [Tooltip("TEMP: log a timestamped trace of the intro reveal to the Console. Turn off once fixed.")]
    public bool debugIntroSequence = false;
    private float _dbgT0 = -1f;
    private float _dbgLastAlpha = -999f;
    private float _dbgLastFadeA = -999f;
    void Update()
    {
        // Spinner + caption fade. Runs on unscaled time, so it keeps animating while
        // Time.timeScale is 0. Kept above the debug early-return on purpose.
        TickLoadingIndicator();

        if (!debugIntroSequence) return;
        if (_dbgT0 < 0f) _dbgT0 = Time.realtimeSinceStartup;
        float t = Time.realtimeSinceStartup - _dbgT0;
        if (t > 15f) return; // only trace the first 15 seconds
        float a = canvasGroup != null ? canvasGroup.alpha : -1f;
        float fa = fadeImage != null ? fadeImage.color.a : -1f;
        if (Mathf.Abs(a - _dbgLastAlpha) > 0.02f || Mathf.Abs(fa - _dbgLastFadeA) > 0.02f)
        {
            _dbgLastAlpha = a; _dbgLastFadeA = fa;
            //Debug.Log($"[IntroTrace] t={t:F2}s  coverGroupAlpha={a:F2}  fadeImageAlpha={fa:F2}  " +
            //          $"canvasSort={(canvas != null ? canvas.sortingOrder : -1)}  enabledCanvas={(canvas != null && canvas.enabled)}");
        }
    }

    [Header("Banner")]
    public float bannerDuration = 0.8f;
    public int bannerFontSize = 52;
    public int subtitleFontSize = 28;

    [Header("Biome Screens (optional)")]
    [Tooltip("Parent GameObject that holds one child per biome (named after the BiomeType, e.g. 'Snow', 'Desert', 'GrassCartoon'). " +
             "During a stage transition, the child matching the biome is activated; all others are hidden. " +
             "If no child matches the biome, all are hidden and the default black fade background is shown behind the banner text.\n" +
             "Drag Canvas/StageStart/Image here.")]
    public GameObject biomeScreensRoot;

    [Tooltip("Optional TextMeshPro font asset for the stage counter text (e.g. 'Lagu Sans Medium SDF').\n" +
             "Only used when a biome image is showing. If left empty or TMP is not installed, falls back to the legacy UI.Text counter.")]
    public TMPro.TMP_FontAsset counterFont;

    [Tooltip("Font size for the TMP stage counter. Only used if counterFont is set.")]
    public float counterFontSize = 42f;

    [Header("External Wave Counter (optional)")]
    [Tooltip("Drag your Canvas/WaveCounter TMP text here to drive it with 'Wave X/Y'.\n" +
             "If assigned, the script will NOT create its own auto-generated wave counter.\n" +
             "If left empty, the script tries to auto-find a scene object named 'WaveCounter' " +
             "with a TextMeshProUGUI; if none is found, falls back to the legacy auto-generated counter.")]
    public TMPro.TextMeshProUGUI externalWaveCounterTMP;

    [Header("Boss Name Styling (Cinzel)")]
    [Tooltip("Font for the boss name flash, the boss wave counter and the final-boss banner. " +
             "Drag a .ttf straight in (Cinzel-Bold works nicely). Normally set from " +
             "GameOrchestrator's 'Boss Name Font' field, which forwards it here. " +
             "Leave empty to keep the default font - nothing else breaks.")]
    public Font bossNameFont;

    [Tooltip("Optional. Only needed if your wave counter is an EXTERNAL TextMeshPro text, and " +
             "only if you want to hand-tune the atlas. Left empty, a TMP font asset is generated " +
             "at runtime from 'Boss Name Font' automatically - no Font Asset Creator required.")]
    public TMPro.TMP_FontAsset bossNameFontTMP;

    [Tooltip("Tint for boss names. Pale gold by default. Overwritten by GameOrchestrator's " +
             "matching field at startup.")]
    public Color bossNameColor = new Color(0.94f, 0.85f, 0.60f, 1f);

    [Tooltip("Size multiplier on top of the normal wave text size - Cinzel's capitals read " +
             "smaller than the default font at the same point size.")]
    [Range(0.5f, 2.5f)]
    public float bossNameSizeMultiplier = 1.15f;

    [Tooltip("UPPERCASE boss names. Cinzel is a Roman-inscription face, so caps look right. " +
             "Turn off for names with accented or non-Latin characters.")]
    public bool bossNameUppercase = true;

    [Tooltip("Extra letter spacing for a TMP boss counter. Ignored by the legacy Text counter.")]
    public float bossNameTMPCharacterSpacing = 6f;

    [Tooltip("Supersampling for the overlay's text. Legacy UI.Text rasterises each glyph into " +
             "a bitmap atlas, so a big headline like 'Wave 1 Starts' can look chunky at 1x. " +
             "3 renders the glyphs at 3x resolution and scales them down, which is what makes " +
             "a fine serif face like Cinzel look clean. Costs a little texture memory - drop to " +
             "1-2 if you are tight, raise to 4 for 4K displays.")]
    [Range(1f, 8f)]
    public float textSharpness = 3f;

    [Tooltip("Thickness of the black rim behind the 'Wave N Starts' flash, in pixels. The legacy " +
             "Outline effect just stamps the glyph mesh 4 times at whole-pixel offsets, so a fat " +
             "rim reads as stair-stepped edges. 1-2 looks clean; 0 removes it entirely. Ignored " +
             "once the crisp TMP flash below is active - that uses a real shader outline.")]
    [Range(0f, 4f)]
    public float flashOutlineThickness = 1.5f;

    [Tooltip("ON: draw the big centre-screen flash with TextMeshPro instead of legacy UI.Text " +
             "whenever a font is assigned. TMP renders from a signed-distance field, so the " +
             "headline stays razor sharp at any size and gets a real anti-aliased outline rather " +
             "than a stamped one. This is the actual fix for pixelated edges. OFF: keep the " +
             "original legacy Text (still fine, just softer at large sizes).")]
    public bool useCrispFlashText = true;

    [Tooltip("ON: use the font above for EVERY text this overlay draws - the stage/biome banner " +
             "and subtitle, the wave counter, the 'Wave N Starts' flash, the stage counter and " +
             "the 'Loading' caption - not just boss names. Sizes, colours and outlines are left " +
             "alone; only the typeface changes. Boss names still get their own tint and size on " +
             "top. OFF: only boss names use it. Set from GameOrchestrator.")]
    public bool useFontForAllText = false;

    [Header("Loading Indicator (bottom-right)")]
    [Tooltip("Show a small 'Loading' caption + rotating spiral in the bottom-right corner " +
             "while the screen is covered (boot black AND the biome title card). " +
             "Built entirely in code — no prefab, no sprite, no scene setup.")]
    public bool showLoadingIndicator = true;

    [Tooltip("Also show it during mid-run stage transitions (every FadeOut), not just at boot.")]
    public bool loadingIndicatorOnStageTransitions = true;

    [Tooltip("Caption text. Change at runtime with SetLoadingLabel(\"Resuming run\").")]
    public string loadingLabel = "Loading";

    [Tooltip("Cycle 'Loading' -> 'Loading.' -> 'Loading..' -> 'Loading...' as well as spinning the spiral.")]
    public bool loadingAnimateDots = false;

    [Tooltip("Spiral box size in px at the 1920x1080 reference resolution.")]
    public float loadingSpiralSize = 28f;

    [Tooltip("Spin rate. Negative = clockwise.")]
    public float loadingSpinDegreesPerSecond = -260f;

    [Tooltip("Caption font size at the 1920x1080 reference resolution.")]
    public int loadingFontSize = 20;

    [Tooltip("Distance in from the bottom-right screen corner, in reference px.")]
    public Vector2 loadingCornerMargin = new Vector2(34f, 26f);

    [Tooltip("Tint for both the caption and the spiral.")]
    public Color loadingTint = new Color(1f, 1f, 1f, 0.85f);

    [Tooltip("Fade in/out time for the whole indicator.")]
    public float loadingFadeSeconds = 0.18f;

    // UI references (auto-created)
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private Image fadeImage;
    private Text bannerText;
    private Text subtitleText;
    private Outline bannerOutline;
    private Outline subtitleOutline;
    private Text waveCounterText;
    private Text waveFlashText;
    private TMPro.TextMeshProUGUI waveFlashTmp;   // crisp SDF twin, used when available
    private bool flashIsBoss;                     // which styling the current flash is using
    private float defaultFlashTmpSize;
    private TMPro.TextMeshProUGUI tmpCounterText; // biome-image stage counter, created lazily if counterFont assigned

    // Dedicated stage-counter overlay. Lives on its OWN canvas at a sort order ABOVE the biome
    // art (which renders at 10000). The old counter sat on the transition canvas (9999), i.e.
    // BELOW the biome image, so the biome art covered it and the stage text vanished. This
    // canvas (10001) guarantees the "Stage X/Y" text always draws on top of the biome screen.
    private Canvas persistentUICanvas;   // holds the wave counter + the "Wave N Starts" flash
    private Canvas stageCounterCanvas;
    private Text stageCounterText;                 // legacy fallback, always created
    private TMPro.TextMeshProUGUI stageCounterTmp; // styled, only if counterFont assigned
    // Loading indicator (bottom-right "Loading" + rotating spiral). Own canvas at 10002 so it
    // draws above the cover (9999), the biome art (10000) and the stage counter (10001).
    private Canvas loadingCanvas;
    private CanvasGroup loadingGroup;
    private Text loadingText;
    private RectTransform loadingSpiralRect;
    private float loadingAlpha;       // current fade value
    private float loadingTargetAlpha; // 0 or 1
    private float loadingAngle;
    private float loadingDotTimer;
    private int loadingLastDots = -1;

    private Coroutine flashCoroutine;

    // Boss-name styling state. The defaults are captured the first time a boss style is
    // applied, so switching back to normal wave text is exact - no hardcoded "restore to
    // LegacyRuntime.ttf size 80" that would silently undo a future re-style.
    private Color flashTint = Color.white;
    private bool textDefaultsCaptured = false;
    private Font defaultFlashFont; private int defaultFlashSize;
    private Font defaultCounterFont; private int defaultCounterSize; private Color defaultCounterColor;
    private TMPro.TMP_FontAsset defaultExtCounterFont; private float defaultExtCounterSize;
    private Color defaultExtCounterColor; private float defaultExtCounterSpacing;
    private Font defaultBannerFont; private int defaultBannerSize; private Color defaultBannerColor;
    private TMPro.TMP_FontAsset _resolvedBossFontTMP; private bool _bossFontTMPLookupDone;

    private bool initialized = false;
    private bool everRevealed = false;  // set true once the screen has been revealed at least once
    private bool failsafeArmed = false; // ensures the reveal watchdog starts at most once
    private bool introActive = false; // true once the orchestrator's stage intro is running


    // Ensures the UI elements exist. Called lazily on first use.

    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        //  Canvas (Screen Space - Overlay, renders on top of everything) 
        GameObject canvasObj = new GameObject("TransitionCanvas");
        // Parent to scene root (not to `transform`). ScreenSpaceOverlay canvases render the same
        // regardless of parent, but parenting to the orchestrator's GameObject makes the canvas
        // vulnerable to whatever happens to that GameObject (re-parented under another canvas,
        // disabled temporarily, scaled by an animator, etc.). Scene-rooted = bulletproof.
        canvasObj.transform.SetParent(null, false);

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999; // on top of everything

        CanvasScaler mainScaler = canvasObj.AddComponent<CanvasScaler>();
        mainScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        mainScaler.referenceResolution = new Vector2(1920, 1080);
        mainScaler.matchWidthOrHeight = 0.5f;
        mainScaler.referencePixelsPerUnit = 100;

        canvasObj.AddComponent<GraphicRaycaster>();

        //  CanvasGroup for fading the whole thing ─
        canvasGroup = canvasObj.AddComponent<CanvasGroup>();
        canvasGroup.alpha = startBlack ? 1f : 0f;
        canvasGroup.blocksRaycasts = startBlack;
        canvasGroup.interactable = false;

        //  Full-screen fade image 
        GameObject imgObj = new GameObject("FadeImage");
        imgObj.transform.SetParent(canvasObj.transform, false);

        fadeImage = imgObj.AddComponent<Image>();
        fadeImage.color = fadeColor;
        fadeImage.raycastTarget = false;

        // Stretch to fill entire screen. Give it a large bleed past the screen edges so the
        // cover is guaranteed to fill the viewport on the very first frame, before the
        // CanvasScaler has evaluated its layout. ScreenSpaceOverlay clips the overflow, so
        // the extra size costs nothing.
        RectTransform imgRect = imgObj.GetComponent<RectTransform>();
        imgRect.anchorMin = Vector2.zero;
        imgRect.anchorMax = Vector2.one;
        imgRect.offsetMin = new Vector2(-5000f, -5000f);
        imgRect.offsetMax = new Vector2(5000f, 5000f);

        //  Stage banner text (centered) 
        GameObject textObj = new GameObject("BannerText");
        textObj.transform.SetParent(canvasObj.transform, false);

        bannerText = textObj.AddComponent<Text>();
        bannerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bannerText.fontSize = bannerFontSize;
        bannerText.alignment = TextAnchor.MiddleCenter;
        bannerText.color = Color.white;
        bannerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bannerText.verticalOverflow = VerticalWrapMode.Overflow;
        bannerText.text = "";
        bannerText.raycastTarget = false; // never clickable

        // Add outline for readability
        bannerOutline = textObj.AddComponent<Outline>();
        bannerOutline.effectColor = new Color(0, 0, 0, 0.8f);
        bannerOutline.effectDistance = new Vector2(2, -2);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0.45f);
        textRect.anchorMax = new Vector2(1, 0.65f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Subtitle text (biome modifiers: night, fog, etc.) 
        GameObject subObj = new GameObject("SubtitleText");
        subObj.transform.SetParent(canvasObj.transform, false);

        subtitleText = subObj.AddComponent<Text>();
        subtitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subtitleText.fontSize = subtitleFontSize;
        subtitleText.alignment = TextAnchor.MiddleCenter;
        subtitleText.color = new Color(0.75f, 0.75f, 0.75f, 1f);
        subtitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        subtitleText.verticalOverflow = VerticalWrapMode.Overflow;
        subtitleText.text = "";
        subtitleText.raycastTarget = false; // never clickable

        Outline subOutline = subObj.AddComponent<Outline>();
        subOutline.effectColor = new Color(0, 0, 0, 0.6f);
        subOutline.effectDistance = new Vector2(1, -1);
        subtitleOutline = subOutline;

        RectTransform subRect = subObj.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0, 0.35f);
        subRect.anchorMax = new Vector2(1, 0.45f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;

        //  Optional TMP counter text (used for the biome-image stage counter) 
        // Only created if a TMP font asset is assigned in the inspector.
        if (counterFont != null)
        {
            GameObject tmpObj = new GameObject("TMPCounterText");
            tmpObj.transform.SetParent(canvasObj.transform, false);

            tmpCounterText = tmpObj.AddComponent<TMPro.TextMeshProUGUI>();
            tmpCounterText.font = counterFont;
            tmpCounterText.fontSize = counterFontSize;
            tmpCounterText.alignment = TMPro.TextAlignmentOptions.Center;
            tmpCounterText.color = new Color(1, 1, 1, 0);
            tmpCounterText.text = "";
            tmpCounterText.raycastTarget = false;
            tmpCounterText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            RectTransform tmpRect = tmpObj.GetComponent<RectTransform>();
            tmpRect.anchorMin = new Vector2(0, 0.30f);
            tmpRect.anchorMax = new Vector2(1, 0.40f);
            tmpRect.offsetMin = Vector2.zero;
            tmpRect.offsetMax = Vector2.zero;
        }

        //  Separate canvas for persistent UI (counter + flash).
        //  These must stay visible during fades, so they live OUTSIDE the fading CanvasGroup.
        GameObject counterCanvasObj = new GameObject("PersistentUICanvas");
        // Scene-root parent (same reasoning as TransitionCanvas above).
        counterCanvasObj.transform.SetParent(null, false);
        // Kept in a field, not just a local: the wave counter AND the wave flash live under
        // this canvas, so ApplyGlobalFont needs to be able to find them later.
        Canvas counterCanvas = counterCanvasObj.AddComponent<Canvas>();
        persistentUICanvas = counterCanvas;
        counterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        counterCanvas.sortingOrder = 9998; // below fade canvas

        // Configure scaler for sharp text at 1080p reference resolution
        CanvasScaler counterScaler = counterCanvasObj.AddComponent<CanvasScaler>();
        counterScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        counterScaler.referenceResolution = new Vector2(1920, 1080);
        counterScaler.matchWidthOrHeight = 0.5f;
        counterScaler.referencePixelsPerUnit = 100;

        // Intentionally NOT adding a GraphicRaycaster here.
        // This canvas contains only display-only text (wave counter + flash). Adding a raycaster would make its Text rects (especially the large
        // center-screen WaveFlashText) cover clicks intended for the AugmentsMenu beneath it.

        //  Persistent wave counter (top right, below HUD elements) 
        //  If the user has placed their own TMP text in the scene (typically Canvas/WaveCounter),
        //  drive that instead and skip building our own.
        TryResolveExternalWaveCounter();
        if (externalWaveCounterTMP == null)
        {
            GameObject counterObj = new GameObject("WaveCounterText");
            counterObj.transform.SetParent(counterCanvasObj.transform, false);

            waveCounterText = counterObj.AddComponent<Text>();
            waveCounterText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            waveCounterText.fontSize = 36;
            waveCounterText.fontStyle = FontStyle.Bold;
            waveCounterText.alignment = TextAnchor.UpperRight;
            waveCounterText.color = new Color(1f, 1f, 1f, 0.95f);
            waveCounterText.horizontalOverflow = HorizontalWrapMode.Overflow;
            waveCounterText.text = "";
            waveCounterText.raycastTarget = false; // display-only, must not block clicks

            Outline counterOutline = counterObj.AddComponent<Outline>();
            counterOutline.effectColor = new Color(0, 0, 0, 1f);
            counterOutline.effectDistance = new Vector2(2, -2);

            RectTransform counterRect = counterObj.GetComponent<RectTransform>();
            counterRect.anchorMin = new Vector2(1, 1);
            counterRect.anchorMax = new Vector2(1, 1);
            counterRect.pivot = new Vector2(1f, 1f);
            counterRect.anchoredPosition = new Vector2(-68, -90); // pushed down to avoid overlap with Energy HUD
            counterRect.sizeDelta = new Vector2(400, 60);
        } // end if (externalWaveCounterTMP == null)

        //  Wave flash (center of screen, brief) 
        GameObject flashObj = new GameObject("WaveFlashText");
        flashObj.transform.SetParent(counterCanvasObj.transform, false);

        waveFlashText = flashObj.AddComponent<Text>();
        waveFlashText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        waveFlashText.fontSize = 80;
        waveFlashText.fontStyle = FontStyle.Bold;
        waveFlashText.alignment = TextAnchor.MiddleCenter;
        waveFlashText.color = new Color(1, 1, 1, 0);
        waveFlashText.horizontalOverflow = HorizontalWrapMode.Overflow;
        waveFlashText.text = "";

        waveFlashText.raycastTarget = false;

        // Legacy Outline stamps the glyph mesh four times at whole-pixel offsets. At the old
        // fixed (3,-3) that reads as visible stair-stepping around a big serif headline, so
        // the thickness is an inspector value now and 0 removes the effect entirely.
        if (flashOutlineThickness > 0f)
        {
            Outline flashOutline = flashObj.AddComponent<Outline>();
            flashOutline.effectColor = new Color(0, 0, 0, 1f);
            flashOutline.effectDistance =
                new Vector2(flashOutlineThickness, -flashOutlineThickness);
        }

        RectTransform flashRect = flashObj.GetComponent<RectTransform>();
        flashRect.anchorMin = new Vector2(0, 0.4f);
        flashRect.anchorMax = new Vector2(1, 0.6f);
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;

        BuildCrispFlashText(counterCanvasObj.transform, flashRect);

        //  Dedicated stage-counter canvas (ABOVE the biome art) 
        // Separate top-level overlay canvas so its sort order can sit above the biome title
        // image (10000). The stage text is parented here, not on the transition canvas, so the
        // biome art can never cover it.
        GameObject stageCounterCanvasObj = new GameObject("StageCounterCanvas");
        stageCounterCanvasObj.transform.SetParent(null, false);
        stageCounterCanvas = stageCounterCanvasObj.AddComponent<Canvas>();
        stageCounterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        stageCounterCanvas.sortingOrder = 10001; // above biome art (10000) and transition canvas (9999)

        CanvasScaler stageCounterScaler = stageCounterCanvasObj.AddComponent<CanvasScaler>();
        stageCounterScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        stageCounterScaler.referenceResolution = new Vector2(1920, 1080);
        stageCounterScaler.matchWidthOrHeight = 0.5f;
        stageCounterScaler.referencePixelsPerUnit = 100;
        // No GraphicRaycaster: this is display-only text and must never eat clicks.

        // Legacy Text (always present so the counter shows even without a TMP font).
        GameObject scObj = new GameObject("StageCounterText");
        scObj.transform.SetParent(stageCounterCanvasObj.transform, false);
        stageCounterText = scObj.AddComponent<Text>();
        stageCounterText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        stageCounterText.fontSize = bannerFontSize;
        stageCounterText.fontStyle = FontStyle.Bold;
        stageCounterText.alignment = TextAnchor.MiddleCenter;
        stageCounterText.color = new Color(1, 1, 1, 0);
        stageCounterText.horizontalOverflow = HorizontalWrapMode.Overflow;
        stageCounterText.verticalOverflow = VerticalWrapMode.Overflow;
        stageCounterText.raycastTarget = false;
        Outline scOutline = scObj.AddComponent<Outline>();
        scOutline.effectColor = new Color(0, 0, 0, 0.85f);
        scOutline.effectDistance = new Vector2(2, -2);
        RectTransform scRect = scObj.GetComponent<RectTransform>();
        scRect.anchorMin = new Vector2(0, 0.30f);
        scRect.anchorMax = new Vector2(1, 0.40f);
        scRect.offsetMin = Vector2.zero;
        scRect.offsetMax = Vector2.zero;

        // Optional styled TMP counter (used in preference to the legacy Text when assigned).
        if (counterFont != null)
        {
            GameObject scTmpObj = new GameObject("StageCounterTMP");
            scTmpObj.transform.SetParent(stageCounterCanvasObj.transform, false);
            stageCounterTmp = scTmpObj.AddComponent<TMPro.TextMeshProUGUI>();
            stageCounterTmp.font = counterFont;
            stageCounterTmp.fontSize = counterFontSize;
            stageCounterTmp.alignment = TMPro.TextAlignmentOptions.Center;
            stageCounterTmp.color = new Color(1, 1, 1, 0);
            stageCounterTmp.raycastTarget = false;
            stageCounterTmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            RectTransform scTmpRect = scTmpObj.GetComponent<RectTransform>();
            scTmpRect.anchorMin = new Vector2(0, 0.30f);
            scTmpRect.anchorMax = new Vector2(1, 0.40f);
            scTmpRect.offsetMin = Vector2.zero;
            scTmpRect.offsetMax = Vector2.zero;
        }

        //  Loading indicator canvas (bottom-right) 
        BuildLoadingIndicator();

        // Restyle every text at once, if asked. Runs here - after all the text objects
        // exist and BEFORE CaptureTextDefaults() ever snapshots them - so "restore to
        // normal" later restores to this font rather than reverting to LegacyRuntime.
        ApplyGlobalFont();

        // Unconditional: sharpening applies whether or not a custom font is in use.
        ApplyTextSharpness();

        // Boot safety net: arm the watchdog that clears the screen if the first
        // reveal never happens. (Also armed by SnapToBlack for startBlack=false.)
        if (startBlack)
            ArmInitialRevealFailsafe();

        if (debugIntroSequence)
        {
            int overlayCount = FindObjectsByType<StageTransitionOverlay>(FindObjectsSortMode.None).Length;
            string brName = biomeScreensRoot != null ? biomeScreensRoot.name : "<null>";
            //Debug.Log($"[IntroTrace] EnsureInitialized: created TransitionCanvas (sort={canvas.sortingOrder}, " +
            //          $"mode={canvas.renderMode}). startBlack={startBlack}. biomeScreensRoot={brName}. " +
            //          $"StageTransitionOverlay instances in scene={overlayCount}.");
        }

        // Build the UI geometry NOW (in the caller's Awake), so the cover has a real mesh on
        // the very first rendered frame. Without this, a runtime-created canvas defers its
        // first rebuild and the scene shows through for frame 0 — a flash that is briefly
        // visible in a build and a long, obvious hitch in the editor's first Play frame.
        Canvas.ForceUpdateCanvases();
    }

    // Instantly force the screen to opaque black with NO animation. Called at the
    // start of the very first stage so the biome is built behind black regardless of
    // the startBlack inspector value — otherwise a freshly-built biome can flash on
    // screen for ~a second before the stage banner appears.
    public void SnapToBlack()
    {
        EnsureInitialized();
        if (debugIntroSequence) Debug.Log($"[IntroTrace] SnapToBlack @ {Time.realtimeSinceStartup:F2}s (forcing cover opaque).");
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }
        if (bannerText != null) bannerText.text = "";
        if (subtitleText != null) subtitleText.text = "";
        ShowLoadingIndicator();
        ArmInitialRevealFailsafe();

        // Clear any stray biome card that would otherwise render above this black cover
        // (biome screens live on a canvas at sort 10000, above the 9999 cover). This is
        // what was flashing the biome for ~2s at startup before the intro banner ran.
        HideStrayBiomeScreens();

        // Force the cover's mesh to rebuild immediately so this opaque black is on screen
        // before the next frame renders — no single frame of the biome/player can slip
        // through between asserting black and the render pass.
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// TRUE while the black cover is (even partly) hiding gameplay — the boot cover
    /// asserted in GameOrchestrator.Awake, a stage FadeOut, or a FadeIn still in flight.
    /// FALSE the moment the screen is fully revealed and the arena is actually visible.
    ///
    /// Read-only diagnostic accessor — it changes nothing here. The orchestrator uses it
    /// to hold the first wave of a stage until the player can actually SEE the arena
    /// (see GameOrchestrator.WaitUntilScreenRevealed).
    ///
    /// NOTE ON SCOPE: canvasGroup governs ONLY the fade canvas (cover image + banner +
    /// subtitle). The persistent wave counter and the "Wave X Starts" flash live on their
    /// own canvas, so this is exactly "the cover is visible" and is 0 during normal play —
    /// the gate therefore costs nothing once a stage is running.
    /// </summary>
    public bool IsCovering => canvasGroup != null && canvasGroup.alpha > 0.01f;

    // Called by the orchestrator the instant its stage-intro coroutine begins. Once the
    // intro is running it WILL reach FadeIn (ApplyBiome is wrapped in try/catch), so the
    // boot watchdog must stand down — otherwise a slow biome build (a long blocking frame)
    // can trip the timer mid-intro and reveal the game before the banner.
    public void NotifyIntroStarted() { introActive = true; }

    // Start the boot reveal watchdog at most once.
    private void ArmInitialRevealFailsafe()
    {
        if (failsafeArmed || everRevealed || initialRevealFailsafeSeconds <= 0f) return;
        failsafeArmed = true;
        StartCoroutine(InitialRevealFailsafe());
    }

    // Watchdog for the initial boot reveal only. Stands down the moment a real
    // FadeIn() runs (everRevealed). If it times out with the screen still fully
    // black, it clears the overlay so a startup exception can't strand the player.
    private IEnumerator InitialRevealFailsafe()
    {
        float hard = Mathf.Max(hardRevealCeilingSeconds, initialRevealFailsafeSeconds);
        float t = 0f;
        while (t < hard)
        {
            if (everRevealed) yield break; // a real FadeIn ran — nothing stuck

            // SOFT net (original intent, unchanged): if NO intro/resume is even running yet
            // and the screen is still black after the short window, a startup exception
            // stranded us before any intro began. Clear immediately.
            if (!introActive && t >= initialRevealFailsafeSeconds
                && canvasGroup != null && canvasGroup.alpha > 0.99f)
            {
                ForceClearStuckBlack("soft failsafe (no intro started before window)");
                yield break;
            }

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // HARD ceiling: still fully black this long after boot is ALWAYS a bug — a resume that
        // never completed, a swallowed throw, a do-nothing bail, or a latched intro flag.
        // introActive is intentionally IGNORED so a latched flag can't hold an infinite black.
        if (!everRevealed) ForceClearStuckBlack("hard reveal ceiling");
    }

    // Force the cover transparent only if it is still opaque black. Idempotent.
    private void ForceClearStuckBlack(string reason)
    {
        if (canvasGroup == null || canvasGroup.alpha <= 0.99f) return;
        Debug.LogWarning($"[StageTransitionOverlay] Reveal watchdog fired ({reason}) — force-clearing a stuck black screen.");
        if (bannerText != null) bannerText.text = "";
        if (subtitleText != null) subtitleText.text = "";
        HideBiomeScreens();
        HideLoadingIndicator();   // never strand a spinner on revealed gameplay
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        everRevealed = true; // initial reveal handled; never fire twice
    }


    //  PUBLIC API (called by GameOrchestrator)
    // Set the persistent wave counter at the top of the screen. Pass empty string to clear it.

    public void SetWaveCounter(string text)
    {
        EnsureInitialized();
        RestoreCounterStyle();   // undo any boss styling from a previous fight

        // Prefer the user-styled external TMP text if one is present in the scene.
        if (externalWaveCounterTMP != null)
        {
            externalWaveCounterTMP.text = text;
            return;
        }

        if (waveCounterText != null) waveCounterText.text = text;
    }

    /// Same as SetWaveCounter, but renders the boss's name in the Cinzel boss style.
    /// Pass the raw name ("Ashen Warden"); uppercasing is handled here so the
    /// orchestrator never has to care about presentation.
    public void SetBossCounter(string bossName)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(bossName)) { SetWaveCounter(""); return; }

        ApplyBossCounterStyle();
        string shown = FormatBossName(bossName);

        if (externalWaveCounterTMP != null) { externalWaveCounterTMP.text = shown; return; }
        if (waveCounterText != null) waveCounterText.text = shown;
    }

    // Try to find a TMP text named "WaveCounter" anywhere in the scene (including inactive),
    // unless one has already been assigned via the Inspector.
    private void TryResolveExternalWaveCounter()
    {
        if (externalWaveCounterTMP != null) return;

        // FindObjectsInactive.Include so a hidden HUD that gets enabled later still wires up.
        var allTMP = Resources.FindObjectsOfTypeAll<TMPro.TextMeshProUGUI>();
        for (int i = 0; i < allTMP.Length; i++)
        {
            var t = allTMP[i];
            if (t == null) continue;
            // Skip prefab assets — only accept things actually in a scene.
            if (!t.gameObject.scene.IsValid()) continue;
            if (t.gameObject.name != "WaveCounter") continue;

            externalWaveCounterTMP = t;
            Debug.Log($"[StageTransitionOverlay] Bound external WaveCounter TMP: {GetPath(t.transform)}");
            return;
        }
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "(null)";
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    /// TextMeshPro twin of the wave-flash label. Legacy UI.Text bakes glyphs into a bitmap
    /// atlas, so an 80pt headline is only ever as sharp as that bitmap; TMP renders from a
    /// signed-distance field and stays clean at any size, with a real anti-aliased outline
    /// instead of four stamped copies. Built only if a font is available - otherwise the
    /// legacy label carries on exactly as before.
    private void BuildCrispFlashText(Transform parent, RectTransform copyRectFrom)
    {
        if (!useCrispFlashText) return;

        var tmpFont = BossFontTMP();
        if (tmpFont == null) return;   // no font assigned: keep the legacy label

        GameObject obj = new GameObject("WaveFlashTMP");
        obj.transform.SetParent(parent, false);

        waveFlashTmp = obj.AddComponent<TMPro.TextMeshProUGUI>();
        waveFlashTmp.font = tmpFont;
        waveFlashTmp.fontSize = 80;
        waveFlashTmp.alignment = TMPro.TextAlignmentOptions.Center;
        waveFlashTmp.color = new Color(1, 1, 1, 0);
        waveFlashTmp.raycastTarget = false;
        waveFlashTmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        waveFlashTmp.overflowMode = TMPro.TextOverflowModes.Overflow;
        waveFlashTmp.text = "";
        defaultFlashTmpSize = waveFlashTmp.fontSize;

        // Shader outline. fontMaterial (not fontSharedMaterial) gives this label its own
        // material instance, so the rim never leaks onto other text using the same font.
        if (flashOutlineThickness > 0f)
        {
            Material mat = waveFlashTmp.fontMaterial;
            mat.EnableKeyword(TMPro.ShaderUtilities.Keyword_Outline);
            mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, Color.black);
            // Material outline width is 0-1 relative to the glyph, not pixels.
            mat.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth,
                         Mathf.Clamp(flashOutlineThickness * 0.06f, 0f, 0.35f));
        }

        RectTransform r = obj.GetComponent<RectTransform>();
        r.anchorMin = copyRectFrom.anchorMin;
        r.anchorMax = copyRectFrom.anchorMax;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    // The TMP label only takes over when it exists AND the current flash should use the
    // custom font: boss names always, plain wave text only if you ticked "all text".
    private bool UseTmpFlash => waveFlashTmp != null && (flashIsBoss || useFontForAllText);

    private void SetFlashText(string text)
    {
        bool tmp = UseTmpFlash;
        if (waveFlashTmp != null) waveFlashTmp.text = tmp ? text : "";
        if (waveFlashText != null) waveFlashText.text = tmp ? "" : text;
    }

    private void SetFlashColor(Color c)
    {
        if (UseTmpFlash) { if (waveFlashTmp != null) waveFlashTmp.color = c; return; }
        if (waveFlashText != null) waveFlashText.color = c;
    }

    // Briefly flash "Wave X" in the center of the screen. Non-blocking — returns immediately, animates asynchronously.
    public void FlashWaveStart(string text, float holdDuration = 1.0f)
    {
        EnsureInitialized();
        if (waveFlashText == null) return;

        flashIsBoss = false;
        RestoreFlashStyle();            // plain wave text: default font, white
        StartFlash(text, holdDuration);
    }

    /// Flash the boss's name in the middle of the screen, in Cinzel + the boss tint.
    /// Called by GameOrchestrator the moment a stage boss spawns.
    public void FlashBossName(string bossName, float holdDuration = 1.5f)
    {
        EnsureInitialized();
        if (waveFlashText == null) return;
        if (string.IsNullOrWhiteSpace(bossName)) return;

        flashIsBoss = true;
        ApplyBossFlashStyle();
        StartFlash(FormatBossName(bossName), holdDuration);
    }

    private void StartFlash(string text, float holdDuration)
    {
        // Stop any previous flash and fully reset state
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
        SetFlashText(text);
        SetFlashColor(WithAlpha(flashTint, 0f));

        flashCoroutine = StartCoroutine(FlashWaveRoutine(text, holdDuration));
    }

    private IEnumerator FlashWaveRoutine(string text, float holdDuration)
    {
        SetFlashText(text);

        // Use unscaled time so the flash can complete even if the augment
        // menu opens mid-flash and sets Time.timeScale = 0.
        // Tint comes from flashTint so a boss name fades in gold while a normal wave
        // fades in white, using one shared routine.
        Color tint = flashTint;

        float elapsed = 0f;
        float fadeIn = 0.3f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeIn);
            SetFlashColor(WithAlpha(tint, t));
            yield return null;
        }
        SetFlashColor(WithAlpha(tint, 1f));

        yield return new WaitForSecondsRealtime(holdDuration);

        elapsed = 0f;
        float fadeOut = 0.4f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / fadeOut);
            SetFlashColor(WithAlpha(tint, t));
            yield return null;
        }
        SetFlashColor(WithAlpha(tint, 0f));
        SetFlashText("");
        flashCoroutine = null;
    }

    // Fade screen to black. Call this BEFORE swapping the biome.
    public IEnumerator FadeOut(float? customDuration = null)
    {
        EnsureInitialized();
        if (debugIntroSequence) Debug.Log($"[IntroTrace] FadeOut START @ {Time.realtimeSinceStartup:F2}s (covering).");
        float duration = customDuration ?? fadeOutDuration;

        bannerText.text = "";
        subtitleText.text = "";
        canvasGroup.blocksRaycasts = true; // block clicks during transition

        // Boot always shows it; mid-run stage transitions are opt-out via the inspector.
        if (!everRevealed || loadingIndicatorOnStageTransitions) ShowLoadingIndicator();

        // Same stray-card hygiene as SnapToBlack: if the orchestrator asserts black via
        // FadeOut (e.g. FadeOut(0f) on the first stage), make sure no leftover biome card
        // renders above the cover during the load that follows. No card should be visible
        // when a fade-to-black begins, so this only ever clears a stray one.
        HideStrayBiomeScreens();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    // Show the stage banner over the black screen. Call this AFTER swapping the biome (it's hidden behind the fade).
    public IEnumerator ShowBanner(StageData stage, int totalStages)
    {
        BootProfiler.Mark("[PERF] ShowBanner START  <-- BIOME TITLE VISIBLE HERE");
        EnsureInitialized();

        // Show the biome-specific screen (if one exists).
        bool hasBiomeImage = ShowBiomeScreen(stage.biome);
        if (debugIntroSequence) { string caseLabel = hasBiomeImage ? "A" : "B"; Debug.Log($"[IntroTrace] ShowBanner START @ {Time.realtimeSinceStartup:F2}s  (biomeImage={hasBiomeImage} -> CASE {caseLabel})."); }

        //  CASE A: biome image exists — the image IS the banner title.
        // Show only the stage counter, on the dedicated counter canvas that renders ABOVE the
        // biome art (sort 10001 > 10000), so the stage text is never hidden behind the image.
        if (hasBiomeImage)
        {
            subtitleText.text = "";
            subtitleText.color = new Color(0.75f, 0.75f, 0.75f, 0);
            bannerText.text = ""; // counter lives on its own top-most canvas now

            string counterString = $"Stage {stage.stageIndex + 1}/{totalStages}";
            bool useTmp = (stageCounterTmp != null);

            if (useTmp)
            {
                stageCounterTmp.text = counterString;
                stageCounterTmp.color = new Color(1, 1, 1, 0);
                if (stageCounterText != null) stageCounterText.text = "";
            }
            else if (stageCounterText != null)
            {
                stageCounterText.text = counterString;
                stageCounterText.color = new Color(1, 1, 1, 0);
            }

            // Fade counter in, hold, fade out — same timing as the text-only path.
            float counterFadeIn = 0.4f;
            float counterElapsed = 0f;
            while (counterElapsed < counterFadeIn)
            {
                counterElapsed += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(counterElapsed / counterFadeIn);
                if (useTmp) stageCounterTmp.color = new Color(1, 1, 1, a);
                else if (stageCounterText != null) stageCounterText.color = new Color(1, 1, 1, a);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(bannerDuration);

            float counterFadeOut = 0.3f;
            counterElapsed = 0f;
            while (counterElapsed < counterFadeOut)
            {
                counterElapsed += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(counterElapsed / counterFadeOut);
                if (useTmp) stageCounterTmp.color = new Color(1, 1, 1, a);
                else if (stageCounterText != null) stageCounterText.color = new Color(1, 1, 1, a);
                yield return null;
            }

            ClearStageCounter();
            HideBiomeScreens();
            yield break;
        }

        //  CASE B: no biome image — show plain default white text on black.
        BiomeTextStyle style = BiomeTextStyle.Default();
        ApplyBannerStyle(style);

        // Build title — biome name on top, stage number below
        string biomeName = FormatBiomeName(stage.biome);
        bannerText.text = $"{biomeName}\nStage {stage.stageIndex + 1}/{totalStages}";

        // Build subtitle with modifiers
        var mods = new System.Collections.Generic.List<string>();
        if (stage.nightMode) mods.Add("Night");
        if (stage.fogEnabled) mods.Add("Fog");
        if (stage.rainEnabled) mods.Add("Rain");
        if (stage.snowEnabled) mods.Add("Snow");
        // TODO Add some better description of mods (e.g. rainy day)
        //subtitleText.text = mods.Count > 0 ? string.Join(" · ", mods) : "";
        subtitleText.text = "";

        // Fade text in (starting from transparent, ending at the style's target color)
        Color targetBanner = style.bannerColor;
        Color targetSubtitle = style.subtitleColor;
        bannerText.color = new Color(targetBanner.r, targetBanner.g, targetBanner.b, 0);
        subtitleText.color = new Color(targetSubtitle.r, targetSubtitle.g, targetSubtitle.b, 0);

        float textFadeIn = 0.4f;
        float elapsed = 0f;
        while (elapsed < textFadeIn)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / textFadeIn);
            bannerText.color = new Color(targetBanner.r, targetBanner.g, targetBanner.b, t);
            subtitleText.color = new Color(targetSubtitle.r, targetSubtitle.g, targetSubtitle.b, t);
            yield return null;
        }

        // Hold
        yield return new WaitForSecondsRealtime(bannerDuration);

        // Fade text out
        float textFadeOut = 0.3f;
        elapsed = 0f;
        while (elapsed < textFadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / textFadeOut);
            bannerText.color = new Color(targetBanner.r, targetBanner.g, targetBanner.b, t);
            subtitleText.color = new Color(targetSubtitle.r, targetSubtitle.g, targetSubtitle.b, t);
            yield return null;
        }
        bannerText.text = "";
        subtitleText.text = "";

        // No biome image was shown — nothing to hide, but call for consistency (clears any residual state).
        HideBiomeScreens();
    }

    // Show a simple centered message (for final boss, victory, etc.)
    public IEnumerator ShowMessage(string title, string subtitle = "", float duration = 2f)
    {
        EnsureInitialized();
        RestoreBannerStyle();   // undo any boss styling from a previous banner

        bannerText.text = title;
        subtitleText.text = subtitle;
        bannerText.color = Color.white;
        subtitleText.color = new Color(0.75f, 0.75f, 0.75f, 1f);

        // Realtime so this can't hang if Time.timeScale is 0 (e.g. a menu didn't restore it).
        yield return new WaitForSecondsRealtime(duration);

        bannerText.text = "";
        subtitleText.text = "";
    }

    /// Boss-flavoured banner: the boss's NAME in Cinzel as the headline, with the generic
    /// label ("FINAL BOSS") demoted to the subtitle. Used for the final-boss title card.
    public IEnumerator ShowBossMessage(string bossName, string subtitle = "", float duration = 2f)
    {
        EnsureInitialized();
        ApplyBannerBossStyle();

        bannerText.text = FormatBossName(bossName);
        subtitleText.text = subtitle;
        bannerText.color = bossNameColor;
        subtitleText.color = new Color(0.75f, 0.75f, 0.75f, 1f);

        yield return new WaitForSecondsRealtime(duration);

        bannerText.text = "";
        subtitleText.text = "";
        RestoreBannerStyle();
    }

    //  BOSS NAME STYLING (Cinzel) 
    //  All of this degrades gracefully: if no Cinzel font is assigned and none is found in
    //  Resources, the text simply keeps its normal font and only the tint/size change.

    private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    private string FormatBossName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string n = raw.Trim();
        return bossNameUppercase ? n.ToUpperInvariant() : n;
    }

    /// Swap the typeface on every text this overlay owns. Walks the canvases it created
    /// rather than naming each field, so texts added later are covered automatically.
    /// Only the font changes - sizes, colours, outlines and alignment are untouched.
    private void ApplyGlobalFont()
    {
        if (!useFontForAllText || bossNameFont == null) return;

        ApplyFontUnder(canvas);              // banner + subtitle
        ApplyFontUnder(persistentUICanvas);  // wave counter + "Wave N Starts" flash
        ApplyFontUnder(stageCounterCanvas);  // "Stage 2 / 4"
        ApplyFontUnder(loadingCanvas);       // "Loading" caption

        // The external TMP counter is a scene object of yours, not ours, so it is only
        // touched when you explicitly opt in.
        if (externalWaveCounterTMP != null)
        {
            var tf = BossFontTMP();
            if (tf != null) externalWaveCounterTMP.font = tf;
        }
    }

    /// Raise the resolution legacy UI.Text glyphs are rasterised at.
    /// CanvasScaler.dynamicPixelsPerUnit is the only knob that controls this; the default of
    /// 1 means an 80pt headline is baked into an 80px-tall bitmap, which is where the
    /// pixelated edges come from. Applied to every canvas the overlay owns.
    private void ApplyTextSharpness()
    {
        float ppu = Mathf.Max(1f, textSharpness);
        SetSharpness(canvas, ppu);
        SetSharpness(persistentUICanvas, ppu);
        SetSharpness(stageCounterCanvas, ppu);
        SetSharpness(loadingCanvas, ppu);
    }

    private static void SetSharpness(Canvas c, float ppu)
    {
        if (c == null) return;
        var scaler = c.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.dynamicPixelsPerUnit = ppu;
    }

    private void ApplyFontUnder(Canvas c)
    {
        if (c == null) return;

        foreach (var t in c.GetComponentsInChildren<Text>(true))
            if (t != null) t.font = bossNameFont;

        var tmpFont = BossFontTMP();
        if (tmpFont == null) return;

        foreach (var t in c.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            if (t != null) t.font = tmpFont;
    }

    // Just the inspector field. Null is a valid answer: the caller then leaves the
    // existing font alone and only the tint/size change.
    private Font BossFont() => bossNameFont;

    // TMP needs its own asset type. Rather than making you run the Font Asset Creator,
    // build one at runtime from the same .ttf the first time it is needed, and cache it
    // (including the failure case, so a bad font can't retry every boss).
    private TMPro.TMP_FontAsset BossFontTMP()
    {
        if (_bossFontTMPLookupDone) return _resolvedBossFontTMP;
        _bossFontTMPLookupDone = true;

        if (bossNameFontTMP != null) { _resolvedBossFontTMP = bossNameFontTMP; return _resolvedBossFontTMP; }
        if (bossNameFont == null) return null;

        try
        {
            _resolvedBossFontTMP = TMPro.TMP_FontAsset.CreateFontAsset(bossNameFont);
        }
        catch (System.Exception e)
        {
            // Usually means the .ttf was imported without font data. Not fatal - the
            // counter simply keeps its existing TMP font.
            Debug.LogWarning("[StageTransitionOverlay] Could not build a TMP font asset from " +
                             bossNameFont.name + ": " + e.Message);
            _resolvedBossFontTMP = null;
        }
        return _resolvedBossFontTMP;
    }

    // Snapshot the normal look ONCE, so restoring is exact rather than hardcoded.
    private void CaptureTextDefaults()
    {
        if (textDefaultsCaptured) return;
        textDefaultsCaptured = true;

        if (waveFlashText != null)
        {
            defaultFlashFont = waveFlashText.font;
            defaultFlashSize = waveFlashText.fontSize;
        }
        if (waveCounterText != null)
        {
            defaultCounterFont = waveCounterText.font;
            defaultCounterSize = waveCounterText.fontSize;
            defaultCounterColor = waveCounterText.color;
        }
        if (externalWaveCounterTMP != null)
        {
            defaultExtCounterFont = externalWaveCounterTMP.font;
            defaultExtCounterSize = externalWaveCounterTMP.fontSize;
            defaultExtCounterColor = externalWaveCounterTMP.color;
            defaultExtCounterSpacing = externalWaveCounterTMP.characterSpacing;
        }
        if (bannerText != null)
        {
            defaultBannerFont = bannerText.font;
            defaultBannerSize = bannerText.fontSize;
            defaultBannerColor = bannerText.color;
        }
    }

    private void ApplyBossFlashStyle()
    {
        CaptureTextDefaults();
        flashTint = bossNameColor;
        if (waveFlashText == null) return;

        if (waveFlashTmp != null)
            waveFlashTmp.fontSize = defaultFlashTmpSize * bossNameSizeMultiplier;

        var f = BossFont();
        if (f != null) waveFlashText.font = f;
        waveFlashText.fontSize = Mathf.RoundToInt(defaultFlashSize * bossNameSizeMultiplier);
    }

    private void RestoreFlashStyle()
    {
        CaptureTextDefaults();
        flashTint = Color.white;
        if (waveFlashText == null) return;

        if (waveFlashTmp != null && defaultFlashTmpSize > 0f)
            waveFlashTmp.fontSize = defaultFlashTmpSize;

        if (defaultFlashFont != null) waveFlashText.font = defaultFlashFont;
        if (defaultFlashSize > 0) waveFlashText.fontSize = defaultFlashSize;
    }

    private void ApplyBossCounterStyle()
    {
        CaptureTextDefaults();

        if (externalWaveCounterTMP != null)
        {
            var tf = BossFontTMP();
            if (tf != null) externalWaveCounterTMP.font = tf;
            externalWaveCounterTMP.fontSize = defaultExtCounterSize * bossNameSizeMultiplier;
            externalWaveCounterTMP.color = bossNameColor;
            externalWaveCounterTMP.characterSpacing = bossNameTMPCharacterSpacing;
            return;
        }

        if (waveCounterText == null) return;
        var f = BossFont();
        if (f != null) waveCounterText.font = f;
        waveCounterText.fontSize = Mathf.RoundToInt(defaultCounterSize * bossNameSizeMultiplier);
        waveCounterText.color = bossNameColor;
    }

    private void RestoreCounterStyle()
    {
        CaptureTextDefaults();

        if (externalWaveCounterTMP != null)
        {
            if (defaultExtCounterFont != null) externalWaveCounterTMP.font = defaultExtCounterFont;
            if (defaultExtCounterSize > 0f) externalWaveCounterTMP.fontSize = defaultExtCounterSize;
            externalWaveCounterTMP.color = defaultExtCounterColor;
            externalWaveCounterTMP.characterSpacing = defaultExtCounterSpacing;
            return;
        }

        if (waveCounterText == null) return;
        if (defaultCounterFont != null) waveCounterText.font = defaultCounterFont;
        if (defaultCounterSize > 0) waveCounterText.fontSize = defaultCounterSize;
        waveCounterText.color = defaultCounterColor;
    }

    private void ApplyBannerBossStyle()
    {
        CaptureTextDefaults();
        if (bannerText == null) return;

        var f = BossFont();
        if (f != null) bannerText.font = f;
        bannerText.fontSize = Mathf.RoundToInt(defaultBannerSize * bossNameSizeMultiplier);
    }

    private void RestoreBannerStyle()
    {
        CaptureTextDefaults();
        if (bannerText == null) return;

        if (defaultBannerFont != null) bannerText.font = defaultBannerFont;
        if (defaultBannerSize > 0) bannerText.fontSize = defaultBannerSize;
        bannerText.color = defaultBannerColor;
    }

    // Fade screen back in from black. Call this AFTER the banner.
    public IEnumerator FadeIn(float? customDuration = null)
    {
        EnsureInitialized();
        everRevealed = true; // a real reveal is happening — the boot failsafe can stand down
        HideLoadingIndicator();
        if (debugIntroSequence) Debug.Log($"[IntroTrace] FadeIn START @ {Time.realtimeSinceStartup:F2}s  <<< THIS REVEALS GAMEPLAY.");
        float duration = customDuration ?? fadeInDuration;

        // Defensive safety net: if anything bailed out of ShowBanner early
        // (coroutine stopped, exception, etc.) we'd have stale biome screens and
        // a lingering banner. Clear both before fading gameplay back in.
        if (bannerText != null) bannerText.text = "";
        if (subtitleText != null) subtitleText.text = "";
        ClearStageCounter();
        HideBiomeScreens();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
    }

    // Full transition sequence: fade out → swap biome → banner → fade in.

    public IEnumerator DoFullTransition(StageData stage, int totalStages, System.Action onBiomeSwap)
    {
        yield return FadeOut();

        // Swap biome while screen is black
        onBiomeSwap?.Invoke();

        // Brief pause for biome to settle (overlays, particles, etc.)
        yield return new WaitForSecondsRealtime(0.3f);

        yield return ShowBanner(stage, totalStages);
        yield return FadeIn();
    }


    //  Biome screen activation tracking
    // We snapshot the original active state of every direct child of biomeScreensRoot
    // and every ancestor we touch, so HideBiomeScreens can restore them exactly.
    // This way: StageNum and any other unrelated children keep whatever state they had.
    private readonly System.Collections.Generic.Dictionary<GameObject, bool> originalActiveState
        = new System.Collections.Generic.Dictionary<GameObject, bool>();
    private GameObject activeBiomeChild = null;

    // Runtime Canvas override on biomeScreensRoot so it renders above gameplay UI
    // (weapon selection, HUD buttons, etc.) but below the transition's own fade/banner canvas.
    private Canvas biomeScreensCanvas = null;
    private bool biomeScreensCanvasWasOverriding = false;
    private int biomeScreensCanvasOriginalOrder = 0;
    private GraphicRaycaster biomeScreensRaycaster = null;
    private bool biomeScreensRaycasterWasEnabled = true;

    // Names of children that are NOT biome screens and should be left untouched.
    // Extend this list if you add more non-biome siblings under biomeScreensRoot.
    private static readonly System.Collections.Generic.HashSet<string> nonBiomeChildren
        = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "StageNum",
        };

    // Activates the child of biomeScreensRoot whose name matches the biome enum,
    // hides the other biome siblings, and walks up to ensure ancestors are active.
    // Returns true if a matching child was found, false otherwise.
    private bool ShowBiomeScreen(BiomeType biome)
    {
        if (biomeScreensRoot == null) return false;

        // Look for a matching child (case-insensitive).
        string target = biome.ToString();
        Transform parent = biomeScreensRoot.transform;
        Transform matchedChild = null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, target, System.StringComparison.OrdinalIgnoreCase))
            {
                matchedChild = child;
                break;
            }
        }

        // If no matching child exists for this biome, don't touch the hierarchy at all.
        // The styled-text fallback will carry the biome mood instead.
        if (matchedChild == null)
        {
            if (fadeImage != null) fadeImage.color = fadeColor; // keep black background
            return false;
        }

        originalActiveState.Clear();

        // 1) For every DIRECT CHILD of biomeScreensRoot:
        //    - remember its current state
        //    - if it's a biome screen, activate only the match
        //    - leave "non-biome" children (e.g. StageNum) untouched
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            GameObject childGo = child.gameObject;

            if (nonBiomeChildren.Contains(child.name))
                continue; // never touch these — user owns their state

            // Record original state for restoration
            originalActiveState[childGo] = childGo.activeSelf;

            // Activate only the match; deactivate all other biome siblings
            bool shouldBeActive = (child == matchedChild);
            if (childGo.activeSelf != shouldBeActive)
                childGo.SetActive(shouldBeActive);
        }

        // 2) Walk UP from biomeScreensRoot and activate any inactive ancestor.
        //    (e.g. StageStart if it's off). Remember original state.
        Transform cursor = biomeScreensRoot.transform;
        while (cursor != null)
        {
            GameObject go = cursor.gameObject;
            if (!originalActiveState.ContainsKey(go))
                originalActiveState[go] = go.activeSelf;
            if (!go.activeSelf)
                go.SetActive(true);
            cursor = cursor.parent;
        }

        activeBiomeChild = matchedChild.gameObject;

        // 3) Elevate biomeScreensRoot above gameplay UI (weapon selection, HUD, etc.) by giving
        //    it a Canvas override at a sort order just below our transition canvas.
        //    This is the fix for gameplay UI (e.g. weapon roll) showing through the biome screen.
        if (biomeScreensCanvas == null)
        {
            biomeScreensCanvas = biomeScreensRoot.GetComponent<Canvas>();
            if (biomeScreensCanvas == null)
            {
                biomeScreensCanvas = biomeScreensRoot.AddComponent<Canvas>();
                biomeScreensCanvasWasOverriding = false;
            }
            else
            {
                biomeScreensCanvasWasOverriding = true;
                biomeScreensCanvasOriginalOrder = biomeScreensCanvas.overrideSorting
                    ? biomeScreensCanvas.sortingOrder : 0;
            }
            biomeScreensRaycaster = biomeScreensRoot.GetComponent<GraphicRaycaster>();
            if (biomeScreensRaycaster == null)
            {
                biomeScreensRaycaster = biomeScreensRoot.AddComponent<GraphicRaycaster>();
                biomeScreensRaycasterWasEnabled = false; // we just added it — treat as "off originally"
            }
            else
            {
                biomeScreensRaycasterWasEnabled = biomeScreensRaycaster.enabled;
            }
        }
        biomeScreensCanvas.overrideSorting = true;
        // Render the biome title screen ABOVE the transition canvas (9999) so it sits ON
        // TOP of the opaque black cover. The old value (9998) put it BELOW the cover, which
        // forced the code to make the black fade image transparent to reveal it — and that
        // transparency let the live map show through wherever the title image wasn't fully
        // opaque. Rendering on top means the cover never has to be lifted.
        biomeScreensCanvas.sortingOrder = 10000;
        if (biomeScreensRaycaster != null) biomeScreensRaycaster.enabled = false; // purely visual, no input

        // Keep the black fade image FULLY OPAQUE. The biome title now renders above it, so
        // the live game stays completely hidden behind black until FadeIn. The title image's
        // own transparent areas reveal black (and the stage counter), never the gameplay.
        if (fadeImage != null)
            fadeImage.color = fadeColor;

        return true;
    }

    // Clears and hides the dedicated stage-counter overlay text.
    private void ClearStageCounter()
    {
        if (stageCounterTmp != null) { stageCounterTmp.text = ""; stageCounterTmp.color = new Color(1, 1, 1, 0); }
        if (stageCounterText != null) { stageCounterText.text = ""; stageCounterText.color = new Color(1, 1, 1, 0); }
    }

    // Restores every GameObject we touched to its original active state.
    // Also restores the fade image to its default black color for future fades.
    // Boot / snap-to-black hygiene: force every biome-screen CARD off immediately.
    // Biome screens (the full-screen stage-intro art under biomeScreensRoot) are shown
    // by ShowBiomeScreen on a canvas at sort 10000 — ABOVE this overlay's 9999 black
    // cover. So a card left active in the scene, or carried over from a previous Play
    // session when the editor's Enter Play Mode "Reload Scene" is disabled, renders
    // THROUGH the black and flashes the biome before the intro's ShowBanner runs.
    // HideBiomeScreens() only restores cards it tracked during a transition (via
    // originalActiveState), so it can't clear a stray one at boot. This does — statelessly
    // — and is safe to call whenever we assert black (no card should be visible then).
    private void HideStrayBiomeScreens()
    {
        if (biomeScreensRoot == null) return;

        Transform parent = biomeScreensRoot.transform;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            // Never touch non-biome children (StageNum, etc.) — the user owns their state.
            if (nonBiomeChildren != null && nonBiomeChildren.Contains(child.name))
                continue;
            if (child.gameObject.activeSelf)
                child.gameObject.SetActive(false);
        }

        activeBiomeChild = null;
    }

    private void HideBiomeScreens()
    {
        if (fadeImage != null)
            fadeImage.color = fadeColor;

        // Revert the Canvas override before deactivating, so we leave the hierarchy
        // in the same state we found it (no leftover Canvas components silently bumping sort order).
        if (biomeScreensCanvas != null)
        {
            if (biomeScreensCanvasWasOverriding)
            {
                biomeScreensCanvas.overrideSorting = true;
                biomeScreensCanvas.sortingOrder = biomeScreensCanvasOriginalOrder;
            }
            else
            {
                biomeScreensCanvas.overrideSorting = false;
                // If WE added the Canvas component at runtime, destroy it so the scene stays clean.
                if (biomeScreensRoot != null
                    && biomeScreensCanvas.gameObject == biomeScreensRoot
                    && !biomeScreensCanvasWasOverriding)
                {
                    // Safe-destroy the raycaster first (Canvas must outlive its raycaster or warnings log)
                    if (biomeScreensRaycaster != null && !biomeScreensRaycasterWasEnabled)
                    {
                        Destroy(biomeScreensRaycaster);
                        biomeScreensRaycaster = null;
                    }
                    Destroy(biomeScreensCanvas);
                    biomeScreensCanvas = null;
                }
            }
        }
        if (biomeScreensRaycaster != null)
        {
            biomeScreensRaycaster.enabled = biomeScreensRaycasterWasEnabled;
        }

        foreach (var kvp in originalActiveState)
        {
            if (kvp.Key != null && kvp.Key.activeSelf != kvp.Value)
                kvp.Key.SetActive(kvp.Value);
        }
        originalActiveState.Clear();
        activeBiomeChild = null;
    }

    //  Biome-styled fallback banner
    // When a biome has no matching image in Canvas/StageStart/Image, the banner text itself
    // carries the atmosphere — desaturated greens for Wasteland, cold icy blue for Snow, etc.

    private struct BiomeTextStyle
    {
        public Color bannerColor;
        public Color subtitleColor;
        public Color outlineColor;
        public Vector2 outlineDistance;
        public FontStyle fontStyle;
        public int bannerSizeDelta;   // added to base bannerFontSize
        public int subtitleSizeDelta; // added to base subtitleFontSize

        public static BiomeTextStyle Default()
        {
            return new BiomeTextStyle
            {
                bannerColor = Color.white,
                subtitleColor = new Color(0.75f, 0.75f, 0.75f, 1f),
                outlineColor = new Color(0, 0, 0, 0.8f),
                outlineDistance = new Vector2(2, -2),
                fontStyle = FontStyle.Normal,
                bannerSizeDelta = 0,
                subtitleSizeDelta = 0,
            };
        }
    }

    private BiomeTextStyle GetBiomeTextStyle(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Wasteland:
                // Decrepit, toxic, heavy. Sickly green on dark, thick ragged outline.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(0.70f, 0.85f, 0.30f, 1f),      // radioactive green
                    subtitleColor = new Color(0.55f, 0.55f, 0.35f, 1f),    // moldy khaki
                    outlineColor = new Color(0.10f, 0.15f, 0.05f, 1f),     // deep toxic black-green
                    outlineDistance = new Vector2(4, -4),                  // heavy, decaying
                    fontStyle = FontStyle.BoldAndItalic,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.Stones:
                // Carved, weighty, monolithic. Stone-grey with deep chisel shadow.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(0.82f, 0.80f, 0.74f, 1f),      // weathered limestone
                    subtitleColor = new Color(0.60f, 0.58f, 0.54f, 1f),    // darker stone
                    outlineColor = new Color(0.15f, 0.13f, 0.10f, 1f),     // deep shadow crack
                    outlineDistance = new Vector2(5, -5),                  // chiseled relief
                    fontStyle = FontStyle.Bold,
                    bannerSizeDelta = 4,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.Grass:
                // Fresh, natural, bright. Warm green, gentle outline.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(0.95f, 1f, 0.85f, 1f),         // sun-warmed cream-green
                    subtitleColor = new Color(0.55f, 0.75f, 0.40f, 1f),    // grass green
                    outlineColor = new Color(0.10f, 0.25f, 0.08f, 0.9f),   // forest shadow
                    outlineDistance = new Vector2(2, -2),
                    fontStyle = FontStyle.Bold,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.Night:
                // Dark, eerie, starry. Pale moonlight on ink, wispy outline.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(0.85f, 0.88f, 1f, 1f),         // pale moonlight
                    subtitleColor = new Color(0.55f, 0.60f, 0.80f, 1f),    // twilight indigo
                    outlineColor = new Color(0.05f, 0.05f, 0.15f, 1f),     // deep night blue
                    outlineDistance = new Vector2(3, -3),
                    fontStyle = FontStyle.Italic,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.Snow:
                // Crisp, icy, cold. Pale blue-white with cyan frost outline.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(0.92f, 0.98f, 1f, 1f),         // icy white
                    subtitleColor = new Color(0.70f, 0.85f, 0.95f, 1f),    // pale cyan
                    outlineColor = new Color(0.20f, 0.45f, 0.60f, 1f),     // frost blue
                    outlineDistance = new Vector2(3, -3),
                    fontStyle = FontStyle.Bold,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.Desert:
                // Hot, sun-bleached, bold. Sandy gold with rust-red outline.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(1f, 0.92f, 0.65f, 1f),         // sun-bleached gold
                    subtitleColor = new Color(0.85f, 0.65f, 0.35f, 1f),    // dune orange
                    outlineColor = new Color(0.45f, 0.15f, 0.05f, 1f),     // scorched rust
                    outlineDistance = new Vector2(3, -3),
                    fontStyle = FontStyle.Bold,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.GrassCartoon:
                // Playful, storybook, soft. Pastel pink-cream with lavender shadow.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(1f, 0.95f, 0.88f, 1f),         // soft cream
                    subtitleColor = new Color(0.90f, 0.70f, 0.85f, 1f),    // fairy pink
                    outlineColor = new Color(0.40f, 0.25f, 0.45f, 1f),     // lavender shadow
                    outlineDistance = new Vector2(2, -2),
                    fontStyle = FontStyle.BoldAndItalic,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            case BiomeType.Marsh:
                // Murky, damp, oppressive. Swampy green-brown with sludge outline.
                return new BiomeTextStyle
                {
                    bannerColor = new Color(0.75f, 0.80f, 0.55f, 1f),      // pale mossy green
                    subtitleColor = new Color(0.50f, 0.55f, 0.35f, 1f),    // murky olive
                    outlineColor = new Color(0.15f, 0.20f, 0.10f, 1f),     // bog sludge
                    outlineDistance = new Vector2(3, -3),
                    fontStyle = FontStyle.Italic,
                    bannerSizeDelta = 0,
                    subtitleSizeDelta = 0,
                };

            default:
                return BiomeTextStyle.Default();
        }
    }

    private void ApplyBannerStyle(BiomeTextStyle style)
    {
        if (bannerText != null)
        {
            bannerText.fontStyle = style.fontStyle;
            bannerText.fontSize = bannerFontSize + style.bannerSizeDelta;
        }
        if (subtitleText != null)
        {
            subtitleText.fontStyle = style.fontStyle;
            subtitleText.fontSize = subtitleFontSize + style.subtitleSizeDelta;
        }
        if (bannerOutline != null)
        {
            bannerOutline.effectColor = style.outlineColor;
            bannerOutline.effectDistance = style.outlineDistance;
        }
        if (subtitleOutline != null)
        {
            // Subtitle outline is slightly softer than banner's
            subtitleOutline.effectColor = new Color(
                style.outlineColor.r, style.outlineColor.g, style.outlineColor.b,
                style.outlineColor.a * 0.75f);
            subtitleOutline.effectDistance = style.outlineDistance * 0.5f;
        }
    }

    private string FormatBiomeName(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Grass: return "Grasslands";
            case BiomeType.Snow: return "Frozen Tundra";
            case BiomeType.Desert: return "Scorched Desert";
            case BiomeType.Wasteland: return "Toxic Wasteland";
            case BiomeType.Stones: return "Stone Ruins";
            case BiomeType.GrassCartoon: return "Enchanted Meadow";
            case BiomeType.Marsh: return "Murky Swamp";
            case BiomeType.Night: return "Eternal Night";
            default: return biome.ToString();
        }
    }

    //  LOADING INDICATOR (bottom-right "Loading" + rotating spiral)
    // Fully programmatic: no prefab, no sprite, no scene setup. Lives on its own
    // ScreenSpaceOverlay canvas at sort 10002 so it stays visible over the boot black
    // AND over the biome title card. No GraphicRaycaster and raycastTarget=false
    // everywhere, so it can never eat a click.

    // Show the indicator. Safe to call repeatedly.
    public void ShowLoadingIndicator()
    {
        if (!showLoadingIndicator) return;
        if (loadingCanvas == null) return;   // not built yet (EnsureInitialized hasn't run)
        loadingTargetAlpha = 1f;
    }

    // Hide the indicator. Safe to call when it was never shown.
    public void HideLoadingIndicator()
    {
        loadingTargetAlpha = 0f;
    }

    // Swap the caption at runtime, e.g. SetLoadingLabel("Resuming run") on the
    // Continue Previous Game path where DeferredResume can wait a while.
    public void SetLoadingLabel(string label)
    {
        loadingLabel = label;
        loadingLastDots = -1;                 // force a refresh next tick
        if (loadingText != null) loadingText.text = label;
    }

    private void BuildLoadingIndicator()
    {
        if (!showLoadingIndicator || loadingCanvas != null) return;

        GameObject root = new GameObject("LoadingIndicatorCanvas");
        root.transform.SetParent(null, false);   // scene root, same reasoning as the other canvases

        loadingCanvas = root.AddComponent<Canvas>();
        loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        loadingCanvas.sortingOrder = 10002;      // above biome art (10000) and stage counter (10001)

        CanvasScaler loadingScaler = root.AddComponent<CanvasScaler>();
        loadingScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        loadingScaler.referenceResolution = new Vector2(1920, 1080);
        loadingScaler.matchWidthOrHeight = 0.5f;
        loadingScaler.referencePixelsPerUnit = 100;

        // Deliberately NO GraphicRaycaster — display-only, must never block input.

        loadingGroup = root.AddComponent<CanvasGroup>();
        loadingGroup.alpha = 0f;
        loadingGroup.interactable = false;
        loadingGroup.blocksRaycasts = false;

        //  Container pinned to the bottom-right corner 
        GameObject holder = new GameObject("LoadingGroup");
        holder.transform.SetParent(root.transform, false);
        RectTransform holderRect = holder.AddComponent<RectTransform>();
        holderRect.anchorMin = new Vector2(1f, 0f);
        holderRect.anchorMax = new Vector2(1f, 0f);
        holderRect.pivot = new Vector2(1f, 0f);
        holderRect.anchoredPosition = new Vector2(-loadingCornerMargin.x, loadingCornerMargin.y);
        holderRect.sizeDelta = new Vector2(320f, Mathf.Max(loadingSpiralSize, loadingFontSize * 1.6f));

        //  Spiral (right-most element) 
        GameObject spiralObj = new GameObject("Spiral");
        spiralObj.transform.SetParent(holder.transform, false);

        UISpiralGraphic spiral = spiralObj.AddComponent<UISpiralGraphic>();
        spiral.color = loadingTint;
        spiral.raycastTarget = false;

        loadingSpiralRect = spiralObj.GetComponent<RectTransform>();
        loadingSpiralRect.anchorMin = new Vector2(1f, 0.5f);
        loadingSpiralRect.anchorMax = new Vector2(1f, 0.5f);
        // Pivot MUST be centred: a RectTransform rotates about its pivot, so a right-edge
        // pivot makes the spiral orbit its own edge (a wide sway) instead of spinning in
        // place. Offset by half the size instead, to keep it flush with the right edge.
        loadingSpiralRect.pivot = new Vector2(0.5f, 0.5f);
        loadingSpiralRect.anchoredPosition = new Vector2(-loadingSpiralSize * 0.5f, 0f);
        loadingSpiralRect.sizeDelta = new Vector2(loadingSpiralSize, loadingSpiralSize);

        //  "Loading" caption, right-aligned just left of the spiral 
        GameObject textObj = new GameObject("LoadingText");
        textObj.transform.SetParent(holder.transform, false);

        loadingText = textObj.AddComponent<Text>();
        loadingText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        loadingText.fontSize = loadingFontSize;
        loadingText.alignment = TextAnchor.MiddleRight;
        loadingText.color = loadingTint;
        loadingText.horizontalOverflow = HorizontalWrapMode.Overflow;
        loadingText.verticalOverflow = VerticalWrapMode.Overflow;
        loadingText.text = loadingLabel;
        loadingText.raycastTarget = false;

        // Outline so it reads on a bright biome card as well as on black.
        Outline loadingOutline = textObj.AddComponent<Outline>();
        loadingOutline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        loadingOutline.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform loadingTextRect = textObj.GetComponent<RectTransform>();
        loadingTextRect.anchorMin = new Vector2(1f, 0.5f);
        loadingTextRect.anchorMax = new Vector2(1f, 0.5f);
        loadingTextRect.pivot = new Vector2(1f, 0.5f);
        loadingTextRect.anchoredPosition = new Vector2(-(loadingSpiralSize + 10f), 0f);
        loadingTextRect.sizeDelta = new Vector2(260f, loadingFontSize * 1.6f);

        loadingCanvas.enabled = false;   // nothing to draw until the first Show
    }

    // Called every frame from Update(). Fades toward the target and spins the spiral.
    private void TickLoadingIndicator()
    {
        if (loadingCanvas == null) return;

        float dt = Time.unscaledDeltaTime;

        float step = dt / Mathf.Max(0.0001f, loadingFadeSeconds);
        loadingAlpha = Mathf.MoveTowards(loadingAlpha, loadingTargetAlpha, step);

        bool visible = loadingAlpha > 0.001f;
        if (loadingCanvas.enabled != visible) loadingCanvas.enabled = visible;
        if (loadingGroup != null) loadingGroup.alpha = loadingAlpha;
        if (!visible) return;

        // Spin. Stored positive-CCW and negated on apply, so a negative
        // loadingSpinDegreesPerSecond reads as clockwise.
        loadingAngle -= loadingSpinDegreesPerSecond * dt;
        if (loadingAngle > 360f || loadingAngle < -360f) loadingAngle %= 360f;
        if (loadingSpiralRect != null)
            loadingSpiralRect.localRotation = Quaternion.Euler(0f, 0f, -loadingAngle);

        if (loadingText == null) return;

        if (loadingAnimateDots)
        {
            loadingDotTimer += dt;
            int dots = Mathf.FloorToInt(loadingDotTimer * 2.5f) % 4;
            if (dots != loadingLastDots)
            {
                loadingLastDots = dots;
                loadingText.text = loadingLabel + new string('.', dots);
            }
        }
        else if (loadingText.text != loadingLabel)
        {
            loadingText.text = loadingLabel;
        }
    }
}

// A tapering Archimedean-spiral ribbon generated as a UI mesh.
//
// Width and opacity both ramp from a nearly-invisible tail to a solid, round-capped
// head, which is what reads as motion once the RectTransform is rotated. Because it's
// a generated mesh rather than a texture it stays crisp at any resolution, costs one
// draw call, and needs no art asset.
//
// Lives in this file on purpose (no extra script to manage). Unity only requires the
// filename to match ONE MonoBehaviour — the one you drag onto a GameObject. This one is
// only ever added from code via AddComponent, which works from any file.
[RequireComponent(typeof(CanvasRenderer))]
public class UISpiralGraphic : MaskableGraphic
{
    [Tooltip("How many full revolutions the spiral makes.")]
    public float turns = 2.1f;

    [Tooltip("Where the tail starts / the head ends, as a fraction of the half-size.")]
    public float innerRadius = 0.14f;
    public float outerRadius = 0.88f;

    [Tooltip("Ribbon thickness at the tail / head, as a fraction of the half-size.")]
    public float tailThickness = 0.03f;
    public float headThickness = 0.20f;

    [Tooltip("Opacity at the very tail (the head is always fully opaque).")]
    public float tailAlpha = 0.04f;

    [Tooltip("Cross-sections generated per revolution. Higher = smoother curve.")]
    public int segmentsPerTurn = 96;

    [Tooltip("Width of the soft antialiasing edge, in SCREEN pixels. UI meshes get no " +
             "hardware AA, so the shape is drawn with a feathered alpha-0 border instead. " +
             "~1.5 is a crisp edge; raise to 2 for a softer one.")]
    public float edgeFeatherPixels = 1.5f;

    // Feathering is computed in screen pixels, so the mesh must be rebuilt whenever the
    // canvas scale changes (resolution change, window resize, editor Game-view rescale).
    private float _lastScaleFactor = -1f;

    private void Update()
    {
        float s = (canvas != null) ? canvas.scaleFactor : 1f;
        if (!Mathf.Approximately(s, _lastScaleFactor))
        {
            _lastScaleFactor = s;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        // rectTransform.rect, NOT GetPixelAdjustedRect(): pixel snapping is meaningless on a
        // continuously rotating object and makes the edges shimmer as it spins.
        Rect rect = rectTransform.rect;
        float half = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (half <= 0f) return;

        // Draw around the LOCAL ORIGIN — that is the point the RectTransform rotates about,
        // so the spiral always spins around its own true centre no matter what pivot the
        // rect happens to have.
        Vector2 center = Vector2.zero;

        // One screen pixel expressed in local canvas units.
        float scaleFactor = (canvas != null) ? canvas.scaleFactor : 1f;
        float onePixel = 1f / Mathf.Max(0.01f, scaleFactor);
        float feather = Mathf.Max(0.01f, edgeFeatherPixels) * onePixel;

        float thetaMax = Mathf.Max(0.1f, turns) * Mathf.PI * 2f;
        float rIn = innerRadius * half;
        float rOut = outerRadius * half;
        float b = (rOut - rIn) / thetaMax;          // radial growth per radian
        int segments = Mathf.Max(24, Mathf.RoundToInt(segmentsPerTurn * Mathf.Max(0.1f, turns)));

        Color32 baseColor = color;
        UIVertex vert = UIVertex.simpleVert;

        Vector2 headPoint = Vector2.zero;
        float headHalfWidth = 0f;

        // Four vertices per cross-section: feather / core / core / feather.
        // That gives three quad strips — a solid centre with a transparent gradient on
        // each flank, which is what antialiases the silhouette.
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float theta = t * thetaMax;
            float radius = rIn + b * theta;

            Vector2 dir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
            Vector2 point = center + dir * radius;

            // True tangent of r(theta) = rIn + b*theta, so the ribbon width stays
            // perpendicular to the curve instead of shearing on the tight inner turns.
            Vector2 tangent = new Vector2(
                b * Mathf.Cos(theta) - radius * Mathf.Sin(theta),
                b * Mathf.Sin(theta) + radius * Mathf.Cos(theta)).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);

            // Ease the taper so most of the growth happens near the head.
            float taper = Mathf.Pow(t, 1.3f);
            float halfWidth = Mathf.Lerp(tailThickness, headThickness, taper) * half * 0.5f;
            // Never let the core collapse below a pixel — a sub-pixel core flickers as it
            // spins. Below that width the tail is carried by alpha instead.
            halfWidth = Mathf.Max(halfWidth, onePixel * 0.4f);

            byte a = (byte)Mathf.RoundToInt(
                baseColor.a * Mathf.Lerp(tailAlpha, 1f, Mathf.Pow(t, 1.15f)));
            Color32 core = new Color32(baseColor.r, baseColor.g, baseColor.b, a);
            Color32 edge = new Color32(baseColor.r, baseColor.g, baseColor.b, 0);

            vert.color = edge;
            vert.position = point + normal * (halfWidth + feather);
            vh.AddVert(vert);

            vert.color = core;
            vert.position = point + normal * halfWidth;
            vh.AddVert(vert);
            vert.position = point - normal * halfWidth;
            vh.AddVert(vert);

            vert.color = edge;
            vert.position = point - normal * (halfWidth + feather);
            vh.AddVert(vert);

            if (i > 0)
            {
                int prev = (i - 1) * 4;
                int cur = i * 4;
                for (int k = 0; k < 3; k++)
                {
                    vh.AddTriangle(prev + k, prev + k + 1, cur + k + 1);
                    vh.AddTriangle(prev + k, cur + k + 1, cur + k);
                }
            }

            headPoint = point;
            headHalfWidth = halfWidth;
        }

        // Round, feathered cap on the head so the leading edge isn't a hard chop.
        AddFeatheredDisc(vh, headPoint, headHalfWidth, feather, baseColor, 28);
    }

    // Solid disc plus a transparent outer ring, matching the ribbon's antialiasing.
    private static void AddFeatheredDisc(VertexHelper vh, Vector2 center, float radius,
                                         float feather, Color32 c, int sides)
    {
        if (radius <= 0f) return;

        int start = vh.currentVertCount;
        UIVertex vert = UIVertex.simpleVert;
        Color32 edge = new Color32(c.r, c.g, c.b, 0);

        vert.color = c;
        vert.position = center;
        vh.AddVert(vert);                       // start + 0 = hub

        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

            vert.color = c;
            vert.position = center + dir * radius;
            vh.AddVert(vert);                   // inner ring

            vert.color = edge;
            vert.position = center + dir * (radius + feather);
            vh.AddVert(vert);                   // outer feather ring
        }

        for (int i = 0; i < sides; i++)
        {
            int inner = start + 1 + i * 2;
            int outer = inner + 1;
            int innerNext = inner + 2;
            int outerNext = inner + 3;

            vh.AddTriangle(start, inner, innerNext);        // solid wedge
            vh.AddTriangle(inner, outer, outerNext);        // feather quad
            vh.AddTriangle(inner, outerNext, innerNext);
        }
    }
}

