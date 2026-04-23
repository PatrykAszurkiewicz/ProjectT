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
    private TMPro.TextMeshProUGUI tmpCounterText; // biome-image stage counter, created lazily if counterFont assigned
    private Coroutine flashCoroutine;
    private bool initialized = false;


    // Ensures the UI elements exist. Called lazily on first use.

    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        //  Canvas (Screen Space - Overlay, renders on top of everything) 
        GameObject canvasObj = new GameObject("TransitionCanvas");
        canvasObj.transform.SetParent(transform, false);

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

        // Stretch to fill entire screen
        RectTransform imgRect = imgObj.GetComponent<RectTransform>();
        imgRect.anchorMin = Vector2.zero;
        imgRect.anchorMax = Vector2.one;
        imgRect.offsetMin = Vector2.zero;
        imgRect.offsetMax = Vector2.zero;

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
        counterCanvasObj.transform.SetParent(transform, false);
        Canvas counterCanvas = counterCanvasObj.AddComponent<Canvas>();
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

        Outline flashOutline = flashObj.AddComponent<Outline>();
        flashOutline.effectColor = new Color(0, 0, 0, 1f);
        flashOutline.effectDistance = new Vector2(3, -3);

        RectTransform flashRect = flashObj.GetComponent<RectTransform>();
        flashRect.anchorMin = new Vector2(0, 0.4f);
        flashRect.anchorMax = new Vector2(1, 0.6f);
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;
    }

    //  PUBLIC API (called by GameOrchestrator)
    // Set the persistent wave counter at the top of the screen. Pass empty string to clear it.

    public void SetWaveCounter(string text)
    {
        EnsureInitialized();
        if (waveCounterText != null) waveCounterText.text = text;
    }

    // Briefly flash "Wave X" in the center of the screen. Non-blocking — returns immediately, animates asynchronously.
    public void FlashWaveStart(string text, float holdDuration = 1.0f)
    {
        EnsureInitialized();
        if (waveFlashText == null) return;

        // Stop any previous flash and fully reset state
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
        waveFlashText.text = text;
        waveFlashText.color = new Color(1, 1, 1, 0);

        flashCoroutine = StartCoroutine(FlashWaveRoutine(text, holdDuration));
    }

    private IEnumerator FlashWaveRoutine(string text, float holdDuration)
    {
        waveFlashText.text = text;

        // Use unscaled time so the flash can complete even if the augment
        // menu opens mid-flash and sets Time.timeScale = 0.
        float elapsed = 0f;
        float fadeIn = 0.3f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeIn);
            waveFlashText.color = new Color(1, 1, 1, t);
            yield return null;
        }
        waveFlashText.color = Color.white;

        yield return new WaitForSecondsRealtime(holdDuration);

        elapsed = 0f;
        float fadeOut = 0.4f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / fadeOut);
            waveFlashText.color = new Color(1, 1, 1, t);
            yield return null;
        }
        waveFlashText.color = new Color(1, 1, 1, 0);
        waveFlashText.text = "";
        flashCoroutine = null;
    }

    // Fade screen to black. Call this BEFORE swapping the biome.
    public IEnumerator FadeOut(float? customDuration = null)
    {
        EnsureInitialized();
        float duration = customDuration ?? fadeOutDuration;

        bannerText.text = "";
        subtitleText.text = "";
        canvasGroup.blocksRaycasts = true; // block clicks during transition

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
        EnsureInitialized();

        // Show the biome-specific screen (if one exists).
        bool hasBiomeImage = ShowBiomeScreen(stage.biome);

        //  CASE A: biome image exists — the image IS the banner title.
        // Show only the stage counter, moved down to just below the image's title art.
        if (hasBiomeImage)
        {
            subtitleText.text = "";
            subtitleText.color = new Color(0.75f, 0.75f, 0.75f, 0);

            string counterString = $"Stage {stage.stageIndex + 1}/{totalStages}";
            bool useTmp = (tmpCounterText != null);

            // Prepare whichever text object we're using; hide the other so they don't double up.
            RectTransform rect = bannerText.GetComponent<RectTransform>();
            Vector2 origMin = rect.anchorMin;
            Vector2 origMax = rect.anchorMax;

            if (useTmp)
            {
                tmpCounterText.text = counterString;
                tmpCounterText.color = new Color(1, 1, 1, 0);
                bannerText.text = "";
            }
            else
            {
                rect.anchorMin = new Vector2(0, 0.30f);
                rect.anchorMax = new Vector2(1, 0.40f);
                ApplyBannerStyle(BiomeTextStyle.Default());
                bannerText.text = counterString;
                bannerText.color = new Color(1, 1, 1, 0);
            }

            // Fade counter in, hold, fade out — same timing as the text-only path
            float counterFadeIn = 0.4f;
            float counterElapsed = 0f;
            while (counterElapsed < counterFadeIn)
            {
                counterElapsed += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(counterElapsed / counterFadeIn);
                if (useTmp) tmpCounterText.color = new Color(1, 1, 1, a);
                else bannerText.color = new Color(1, 1, 1, a);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(bannerDuration);

            float counterFadeOut = 0.3f;
            counterElapsed = 0f;
            while (counterElapsed < counterFadeOut)
            {
                counterElapsed += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(counterElapsed / counterFadeOut);
                if (useTmp) tmpCounterText.color = new Color(1, 1, 1, a);
                else bannerText.color = new Color(1, 1, 1, a);
                yield return null;
            }

            if (useTmp) tmpCounterText.text = "";
            else bannerText.text = "";

            // Restore original anchors for CASE B if we touched them
            if (!useTmp)
            {
                rect.anchorMin = origMin;
                rect.anchorMax = origMax;
            }

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

        bannerText.text = title;
        subtitleText.text = subtitle;
        bannerText.color = Color.white;
        subtitleText.color = new Color(0.75f, 0.75f, 0.75f, 1f);

        yield return new WaitForSeconds(duration);

        bannerText.text = "";
        subtitleText.text = "";
    }

    // Fade screen back in from black. Call this AFTER the banner.
    public IEnumerator FadeIn(float? customDuration = null)
    {
        EnsureInitialized();
        float duration = customDuration ?? fadeInDuration;

        // Defensive safety net: if anything bailed out of ShowBanner early
        // (coroutine stopped, exception, etc.) we'd have stale biome screens and
        // a lingering banner. Clear both before fading gameplay back in.
        if (bannerText != null) bannerText.text = "";
        if (subtitleText != null) subtitleText.text = "";
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
        biomeScreensCanvas.sortingOrder = 9998; // under transition (9999), above main Canvas (usually 0)
        if (biomeScreensRaycaster != null) biomeScreensRaycaster.enabled = false; // purely visual, no input

        // Hide the black fade image so the biome image shows through.
        // (The fade CanvasGroup is still at alpha 1 — gameplay behind it stays hidden.)
        if (fadeImage != null)
            fadeImage.color = new Color(0, 0, 0, 0);

        return true;
    }

    // Restores every GameObject we touched to its original active state.
    // Also restores the fade image to its default black color for future fades.
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
}
