using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Per-camera screen-state effect (URP). Handles TWO mutually-exclusive screen
// states on one camera, because they need the exact same machinery (a private
// Volume with ColorAdjustments + Vignette) and only differ in their presets:
//
//   PLACEMENT (build mode)  — light desaturation, slow breathing vignette, corner
//                             maintenance icon. Unchanged from before.
//   DOWNED (co-op revive /  — heavy greyscale, dimmed, faster and darker pulsing
//    solo respawn)            damage vignette, no icon. DOWNED WINS when both are
//                             active, and the two crossfade rather than pop.
//
// Keeping both in ONE component is deliberate. Two components on a camera would each
// snapshot and restore the whole volumeLayerMask, and whichever released second would
// silently clear the other's layer bit — which happens every time you are killed while
// in build mode. One component means one Volume, one layer, one mask owner, and that
// class of bug cannot occur.
//
// Placement behaviour is bit-for-bit what it was: the downed blend defaults to 0, and
// every value below reduces to its original expression at blend 0.
//
// Original notes:
// Per-camera placement-mode screen effect (URP):
//   DESATURATION  — URP ColorAdjustments override on a private Volume,
//   PULSING edge VIGNETTE — URP Vignette override on the SAME Volume, its
//     intensity animated every frame so the screen edges darken and breathe,
//   Maintenance ICON (bottom-right) on a per-camera canvas that beats with the pulse.
// CO-OP SPLIT-SCREEN isolation: the Volume sits on a private layer only this
// camera samples (P0->31, P1->30), so the desaturation + vignette affect only
// this player's half. The icon canvas is bound to this camera. Single player:
// one full-screen camera → whole view.
[DisallowMultipleComponent]
public class PlacementModeScreenEffect : MonoBehaviour
{
    [Header("Desaturation")]
    [Tooltip("How far to drain colour. 1 = full greyscale, 0.4 = lightly desaturated " +
             "(colours still show subtly). 0 = no desaturation.")]
    [Range(0f, 1f)] public float maxGrey = 0.25f;

    [Tooltip("Ease in/out speed of the whole effect (0..1 weight per second). 6 ≈ ~0.17s.")]
    public float fadeSpeed = 6f;

    [Header("Pulse")]
    [Tooltip("Pulse rate in Hz (breaths per second). ~0.24 ≈ one breath every ~4s.")]
    public float pulseSpeed = 0.24f;

    [Tooltip("Resting vignette intensity at the edges (0..1). Kept clearly visible so the " +
             "darkened frame is always on screen, even at the trough of the breath.")]
    [Range(0f, 1f)] public float vignetteBase = 0.30f;

    [Tooltip("Extra vignette intensity added at the peak of each breath (0..1). Total edge " +
             "intensity = vignetteBase + this*pulse. NOTE: in URP, intensity sets BOTH how dark " +
             "AND how far inward the darkening reaches — so this is kept modest so the breath " +
             "changes darkness without GROWING the dark area over the corner icon.")]
    [Range(0f, 1f)] public float vignettePulseDepth = 0.22f;

    [Tooltip("Vignette softness (URP smoothness). 0.4 keeps a soft, clearly-visible frame. " +
             "Too low (≤0.3) tightens it into a thin corner sliver that's easy to miss.")]
    [Range(0.01f, 1f)] public float vignetteSmoothness = 0.40f;

    [Tooltip("Vignette colour. Usually black; a very dark blue/teal can read as 'maintenance'.")]
    public Color vignetteColor = Color.black;

    [Header("Pulse — extra cues")]
    [Tooltip("Symmetric brightness breath in EV (post-exposure). 0 = off.")]
    [Range(0f, 1f)] public float brightnessPulseDepth = 0.0f;

    [Tooltip("Colour-saturation breath on each pulse. 0 = steady desaturation.")]
    [Range(0f, 0.5f)] public float greyPulseDepth = 0.0f;

    [Header("Maintenance icon")]
    [Tooltip("Resources path (no extension) to the icon sprite.")]
    public string iconResourcePath = "Sprites/PlacementMode";
    [Tooltip("Icon size in reference pixels (1080p reference).")]
    public float iconSize = 110f;
    [Tooltip("Padding from the bottom-right corner, in reference pixels. Pulled in a bit so the " +
             "icon sits where the vignette frame is lighter, not in the darkest corner.")]
    public float iconPadding = 90f;

    [Header("Maintenance icon — purple styling")]
    [Tooltip("Icon tint at the trough of the pulse. Multiplies the sprite, so it reads truest " +
             "on a white / light-grey sprite.")]
    public Color iconColor = new Color32(0xEB, 0xA9, 0xFF, 0xFF);       // soft lilac
    [Tooltip("Icon tint at the peak of the pulse.")]
    public Color iconPulseColor = new Color32(0xF7, 0xDB, 0xFF, 0xFF);  // pale lavender highlight

    [Tooltip("Draw a dark purple disc with a bright rim behind the icon. Keeps it readable " +
             "on light maps AND inside the darkened vignette corner.")]
    public bool iconBadge = true;
    [Tooltip("Badge diameter relative to iconSize.")]
    [Range(1f, 2.5f)] public float badgeScale = 1.5f;
    public Color badgeFillColor = new Color32(0x37, 0x00, 0x43, 0xE6); // deep plum, slightly see-through
    public Color badgeRimColor = new Color32(0xD2, 0x00, 0xFF, 0xFF);  // vivid violet

    [Tooltip("Soft violet glow that breathes behind the icon. 0 = off.")]
    [Range(0f, 1f)] public float glowStrength = 0.6f;
    public Color glowColor = new Color32(0xD2, 0x00, 0xFF, 0xFF);

    //  DOWNED state preset 
    // Same knobs as above, tuned for "you are out of the fight". Every one is lerped
    // from its placement counterpart by _downedBlend, so at blend 0 the placement look
    // is reproduced exactly.

    [Header("Downed — grey screen")]
    [Tooltip("How far to drain colour while downed. Much heavier than placement mode: " +
             "0.85 leaves just a trace of colour so the scene stays readable.")]
    [Range(0f, 1f)] public float downedMaxGrey = 0.85f;

    [Tooltip("Extra dimming of the whole image while downed, in EV (post-exposure). " +
             "Negative exposure; keep small, the vignette carries most of the darkness.")]
    [Range(0f, 1.5f)] public float downedScreenDarkening = 0.25f;

    [Tooltip("Colour-saturation breath on top of the downed grey. 0 = flat grey.")]
    [Range(0f, 0.5f)] public float downedGreyPulseDepth = 0.12f;

    [Header("Downed — pulsing damage vignette")]
    [Tooltip("Downed pulse rate in Hz — a slow, laboured heartbeat, faster than the " +
             "placement breath. HARD-CLAMPED below 2.9 Hz at runtime, well under the " +
             "photosensitive-seizure threshold, matching PlayerDamageVignette.")]
    [Range(0.05f, 2.9f)] public float downedPulseSpeed = 0.55f;

    [Tooltip("Resting vignette intensity while downed.")]
    [Range(0f, 1f)] public float downedVignetteBase = 0.34f;

    [Tooltip("Extra vignette intensity at the peak of each downed breath.")]
    [Range(0f, 1f)] public float downedVignettePulseDepth = 0.26f;

    [Tooltip("Vignette softness while downed. Tighter than placement so the frame reads " +
             "as pressure closing in rather than a gentle fade.")]
    [Range(0.01f, 1f)] public float downedVignetteSmoothness = 0.35f;

    [Tooltip("Downed vignette colour. Near-black with a red push reads as 'damage'; " +
             "pure black reads as 'blacking out'.")]
    public Color downedVignetteColor = new Color(0.14f, 0.012f, 0.02f, 1f);

    [Header("Downed — fade")]
    [Tooltip("Ease-in speed when going down. Slower than the fade-out so going down " +
             "feels like a slump, not a snap.")]
    public float downedFadeInSpeed = 2.5f;

    [Tooltip("Ease-out speed on revive. Faster, so getting back up feels immediate.")]
    public float downedFadeOutSpeed = 5f;

    [Tooltip("How quickly the look crossfades between the placement and downed presets.")]
    public float downedBlendSpeed = 6f;

    [Header("Isolation")]
    [Tooltip("Volume layer for THIS camera. -1 = auto from player index (P0->31, P1->30).")]
    public int volumeLayer = -1;

    [Tooltip("If the camera has post-processing disabled, URP renders no Volume overrides " +
             "at all and this effect is silently invisible. When true it is switched on " +
             "for as long as the effect runs, then restored to whatever it was. A no-op " +
             "on cameras that already have post-processing on.")]
    public bool ensurePostProcessing = true;

    [Header("Debug")]
    public bool debugLog = true;

    /// <summary>True while PLACEMENT mode is engaged on this camera.</summary>
    public bool IsEngaged { get; private set; }

    /// <summary>True while the DOWNED look is showing (held state or timed pulse).</summary>
    public bool IsDownedActive => _downedEngaged || Time.unscaledTime < _downedUntil;

    private bool _downedEngaged;
    private float _downedUntil = float.NegativeInfinity;
    private float _downedBlend;              // 0 = placement look, 1 = downed look
    private bool _downedShowVignette = true; // cleared when the player disabled damage vignettes

    // Volume (desaturation + vignette)
    private Camera _cam;
    private Volume _volume;
    private VolumeProfile _profile;
    private ColorAdjustments _color;
    private Vignette _vignette;
    private int _camOriginalMask;
    private bool _maskApplied;
    private bool _postFxOriginal;
    private bool _postFxApplied;

    // Icon UI
    private Canvas _canvas;
    private Image _iconImage;
    private RectTransform _iconRoot;   // pulses as one unit; holds glow + badge + icon
    private Image _iconGlow;
    private Image _badgeFill;
    private Image _badgeRim;
    private Sprite _glowSprite, _discSprite, _rimSprite;   // generated at runtime, destroyed with us

    private float _weight;   // 0..1 eased engagement
    private float _phase;    // pulse phase, cycles
    private float _logT;     // heartbeat log throttle

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        enabled = false;   // idle until placement mode begins
    }

    /// <summary>Turn PLACEMENT mode on/off for this camera. Eases in and out smoothly.</summary>
    public void SetEngaged(bool on)
    {
        IsEngaged = on;
        if (on)
        {
            Engage();
            if (debugLog)
                Debug.Log($"[PlacementModeScreenEffect] engaged on '{name}' " +
                          $"(volumeLayer={volumeLayer}, rect={_cam.rect}).");
        }
        // Turning off: stay enabled so Update plays the fade-out, then idles.
    }

    /// <summary>
    /// Turn the DOWNED look on/off for this camera. Driven by PlayerDownedState, which
    /// covers both co-op (awaiting a teammate revive) and single player (awaiting the
    /// respawn countdown), since both routes go through PlayerDownedState.EnterDowned().
    /// </summary>
    /// <param name="showVignette">
    /// False when the player has set the damage-vignette option to Off. The grey screen
    /// still shows — it is state information, not a damage flash — but the pulsing edge
    /// is suppressed.
    /// </param>
    public void SetDowned(bool on, bool showVignette = true)
    {
        if (on)
        {
            _downedShowVignette = showVignette;
            if (!IsDownedActive) _phase = -0.25f;   // start the breath at its trough
            _downedEngaged = true;
            Engage();
        }
        else
        {
            _downedEngaged = false;
            _downedUntil = float.NegativeInfinity;
            // Stay enabled so Update plays the fade-out.
        }
    }

    /// <summary>
    /// Show the downed look for a fixed duration, independent of any held state. Used by
    /// QuickReviveEffect, whose self-revive window (reviveDelay, ~0.1s) is far too short
    /// to read on screen. Extends an in-flight pulse rather than restarting it.
    /// </summary>
    public void PulseDowned(float seconds, bool showVignette = true)
    {
        if (seconds <= 0f) return;
        _downedShowVignette = showVignette;
        if (!IsDownedActive) _phase = -0.25f;
        _downedUntil = Mathf.Max(_downedUntil, Time.unscaledTime + seconds);
        Engage();
    }

    /// <summary>
    /// Get-or-add the effect on <paramref name="cam"/>, with the volume layer bound to
    /// this player. Saves every caller repeating the GetComponent/AddComponent dance.
    /// </summary>
    public static PlacementModeScreenEffect Ensure(Camera cam, int playerIndex)
    {
        if (cam == null) return null;
        var fx = cam.GetComponent<PlacementModeScreenEffect>();
        if (fx == null) fx = cam.gameObject.AddComponent<PlacementModeScreenEffect>();
        if (fx.volumeLayer < 0) fx.volumeLayer = Mathf.Clamp(31 - Mathf.Max(0, playerIndex), 8, 31);
        return fx;
    }

    // Shared start-up for both states.
    private void Engage()
    {
        EnsureVolume();
        EnsureUI();
        ApplyMask(true);
        ApplyPostFx(true);
        if (_canvas != null) _canvas.gameObject.SetActive(true);
        enabled = true;
    }

    private void EnsureVolume()
    {
        if (_volume != null) return;
        if (_cam == null) _cam = GetComponent<Camera>();

        if (volumeLayer < 0)
            volumeLayer = Mathf.Clamp(31 - ResolvePlayerIndex(), 8, 31);

        var go = new GameObject($"PlacementFXVolume_{name}");
        go.transform.SetParent(transform, false);
        go.layer = volumeLayer;

        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 1000f;
        _volume.weight = 0f;

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volume.sharedProfile = _profile;

        // Desaturation (+ optional brightness breath).
        _color = _profile.Add<ColorAdjustments>(true);
        _color.saturation.overrideState = true;
        _color.postExposure.overrideState = true;

        // Pulsing edge vignette — rendered INTO the camera image, so it is always
        // aligned with what the player sees (unlike a screen-space canvas here).
        _vignette = _profile.Add<Vignette>(true);
        _vignette.intensity.overrideState = true;
        _vignette.smoothness.overrideState = true;
        _vignette.color.overrideState = true;
        _vignette.rounded.overrideState = true;
        _vignette.intensity.value = 0f;
        _vignette.smoothness.value = vignetteSmoothness;
        _vignette.color.value = vignetteColor;
        _vignette.rounded.value = false;   // follow the screen aspect (rectangular framing)

        if (debugLog)
            Debug.Log($"[PlacementModeScreenEffect] Volume (desat+vignette) created on " +
                      $"layer {volumeLayer} for '{name}'.");
    }

    private void EnsureUI()
    {
        if (_canvas != null) return;
        if (_cam == null) _cam = GetComponent<Camera>();

        // Screen Space - CAMERA, bound to THIS camera. The icon rendered correctly
        // this way in earlier builds (it tracks the camera's presented image), so
        // we keep it — only the vignette moved to the Volume.
        var canvasGO = new GameObject("PlacementModeCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _cam;
        _canvas.planeDistance = 1f;
        _canvas.sortingOrder = 32760;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        // Maintenance icon (bottom-right).
        Sprite sp = Resources.Load<Sprite>(iconResourcePath);
        if (sp == null)
        {
            Debug.LogWarning($"[PlacementModeScreenEffect] Icon sprite not found at " +
                             $"Resources/{iconResourcePath}. Make sure PlacementMode.png is under a " +
                             $"'Resources' folder and imported as a Sprite (2D and UI).");
        }
        else
        {
            // Root sits exactly where the old icon did (bottom-right, same size and
            // padding), but with a CENTRE pivot so the beat pulses in place instead of
            // growing up-and-left from the corner.
            var rootGO = new GameObject("PlacementModeIcon", typeof(RectTransform));
            rootGO.transform.SetParent(canvasGO.transform, false);
            _iconRoot = (RectTransform)rootGO.transform;
            _iconRoot.anchorMin = _iconRoot.anchorMax = new Vector2(1f, 0f);  // bottom-right
            _iconRoot.pivot = new Vector2(0.5f, 0.5f);
            _iconRoot.sizeDelta = new Vector2(iconSize, iconSize);
            _iconRoot.anchoredPosition = new Vector2(-iconPadding - iconSize * 0.5f,
                                                      iconPadding + iconSize * 0.5f);

            float badgeD = iconSize * badgeScale;

            // Back to front: glow, badge disc, badge rim, icon.
            if (glowStrength > 0f)
            {
                _glowSprite = MakeRadialSprite(128, r =>
                {
                    float t = 1f - Mathf.Clamp01(r);
                    return t * t;                                   // soft quadratic falloff
                });
                _iconGlow = AddIconLayer("Glow", _glowSprite, badgeD * 1.9f);
            }

            if (iconBadge)
            {
                const int res = 128;
                float px = res * 0.5f;                              // 1 texel in radius units = 1/px
                _discSprite = MakeRadialSprite(res, r => Mathf.Clamp01((0.92f - r) * px));
                _rimSprite = MakeRadialSprite(res, r =>
                    Mathf.Clamp01((0.97f - r) * px) * Mathf.Clamp01((r - 0.85f) * px));
                _badgeFill = AddIconLayer("BadgeFill", _discSprite, badgeD);
                _badgeRim = AddIconLayer("BadgeRim", _rimSprite, badgeD);
            }

            _iconImage = AddIconLayer("Icon", sp, iconSize);
            _iconImage.preserveAspect = true;
            _iconImage.color = iconColor;
        }

        canvasGO.SetActive(false);
    }

    // One centred, non-interactive Image under the icon root.
    private Image AddIconLayer(string layerName, Sprite sprite, float size)
    {
        var go = new GameObject(layerName, typeof(RectTransform));
        go.transform.SetParent(_iconRoot, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;
        return img;
    }

    // White, circularly-symmetric sprite; alphaAt(r) gives alpha for normalised radius
    // r (0 = centre, 1 = edge). Tinted per layer via Image.color, so no art assets needed.
    private static Sprite MakeRadialSprite(int size, System.Func<float, float> alphaAt)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PlacementIconRadial",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var px = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            float dy = (y + 0.5f - half) / half;
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(r)) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);   // upload and free the CPU copy
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static void DestroySprite(Sprite s)
    {
        if (s == null) return;
        if (s.texture != null) Destroy(s.texture);
        Destroy(s);
    }

    private int ResolvePlayerIndex()
    {
        var coopCam = GetComponent<ICoopCamera>();
        if (coopCam != null && coopCam.Owner != null) return coopCam.Owner.PlayerIndex;
        foreach (var pr in FindObjectsByType<PlayerRef>(FindObjectsSortMode.None))
            if (pr != null && pr.Camera == _cam) return pr.PlayerIndex;
        return 0;
    }

    private void ApplyMask(bool include)
    {
        if (_cam == null) return;
        var data = _cam.GetUniversalAdditionalCameraData();
        if (data == null) return;

        if (include)
        {
            if (!_maskApplied) { _camOriginalMask = data.volumeLayerMask; _maskApplied = true; }
            data.volumeLayerMask = _camOriginalMask | (1 << volumeLayer);
        }
        else if (_maskApplied)
        {
            data.volumeLayerMask = _camOriginalMask;
            _maskApplied = false;
        }
    }

    // URP renders no Volume overrides on a camera with post-processing switched off, so
    // the whole effect would be invisible with no obvious cause. Turn it on while the
    // effect runs and put it back afterwards.
    private void ApplyPostFx(bool on)
    {
        if (!ensurePostProcessing || _cam == null) return;
        var data = _cam.GetUniversalAdditionalCameraData();
        if (data == null) return;

        if (on)
        {
            if (!_postFxApplied) { _postFxOriginal = data.renderPostProcessing; _postFxApplied = true; }
            if (!data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
                if (debugLog)
                    Debug.Log($"[PlacementModeScreenEffect] enabled post-processing on " +
                              $"'{name}' so the screen effect can render.");
            }
        }
        else if (_postFxApplied)
        {
            data.renderPostProcessing = _postFxOriginal;
            _postFxApplied = false;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Which state(s) want the screen. Downed outranks placement, and the two
        // crossfade through _downedBlend instead of popping.
        bool downed = IsDownedActive;
        bool active = IsEngaged || downed;

        // Blend toward the downed preset. Stays pinned at 0 for a camera that is only
        // ever used for placement mode, which is what makes every expression below
        // reduce to its original form.
        _downedBlend = Mathf.MoveTowards(_downedBlend, downed ? 1f : 0f, downedBlendSpeed * dt);
        float b = _downedBlend;

        float target = active ? 1f : 0f;
        float speed;
        if (active) speed = downed ? downedFadeInSpeed : fadeSpeed;
        else speed = (b > 0.01f) ? downedFadeOutSpeed : fadeSpeed;
        _weight = Mathf.MoveTowards(_weight, target, speed * dt);

        // Pulse rate crossfades too. Clamped under 3 Hz on the downed side.
        float rate = Mathf.Lerp(pulseSpeed, Mathf.Clamp(downedPulseSpeed, 0.05f, 2.9f), b);
        _phase += dt * rate;

        float wave = Mathf.Sin(_phase * Mathf.PI * 2f);   // -1..1, symmetric
        float pulse = 0.5f + 0.5f * wave;                  // 0..1

        // The Volume's overall weight carries the smooth fade in/out.
        if (_volume != null) _volume.weight = _weight;

        // Desaturation (+ optional breaths). Steady saturation by default.
        if (_color != null)
        {
            float greyMax = Mathf.Lerp(maxGrey, downedMaxGrey, b);
            float greyDepth = Mathf.Lerp(greyPulseDepth, downedGreyPulseDepth, b);
            float grey = greyMax * (1f - greyDepth * pulse);
            _color.saturation.value = -100f * grey;
            _color.postExposure.value = Mathf.Lerp(brightnessPulseDepth * wave,
                                                   -downedScreenDarkening, b);
        }

        // PULSE: the vignette intensity breathes every frame. The Volume composites
        // this into the camera image, so it darkens the screen edges you actually see.
        if (_vignette != null)
        {
            // Downed side collapses to zero when the player has damage vignettes off,
            // leaving the grey screen on its own.
            float dBase = _downedShowVignette ? downedVignetteBase : 0f;
            float dDepth = _downedShowVignette ? downedVignettePulseDepth : 0f;

            float vBase = Mathf.Lerp(vignetteBase, dBase, b);
            float vDepth = Mathf.Lerp(vignettePulseDepth, dDepth, b);

            _vignette.intensity.value = Mathf.Clamp01(vBase + vDepth * pulse);
            _vignette.smoothness.value = Mathf.Lerp(vignetteSmoothness, downedVignetteSmoothness, b);
            _vignette.color.value = Color.Lerp(vignetteColor, downedVignetteColor, b);
        }

        // Icon: stays bright so it reads on top of the dark frame; gentle beat.
        // Faded out by the downed blend — the maintenance icon has no business on
        // screen while you are lying on the floor.
        if (_iconRoot != null)
        {
            float vis = _weight * (1f - b);
            float a = vis * Mathf.Lerp(0.85f, 1f, pulse);

            // Icon shimmers from lilac to pale lavender on each beat.
            if (_iconImage != null)
                _iconImage.color = WithAlpha(Color.Lerp(iconColor, iconPulseColor, pulse), a);

            if (_badgeFill != null)
                _badgeFill.color = WithAlpha(badgeFillColor, a);

            // Rim brightens slightly toward the highlight at the peak.
            if (_badgeRim != null)
                _badgeRim.color = WithAlpha(Color.Lerp(badgeRimColor, iconPulseColor, pulse * 0.35f), a);

            // Glow breathes harder than the rest so the corner visibly "throbs".
            if (_iconGlow != null)
            {
                _iconGlow.color = WithAlpha(glowColor, vis * glowStrength * Mathf.Lerp(0.35f, 1f, pulse));
                float gs = Mathf.Lerp(0.85f, 1.1f, pulse);
                _iconGlow.rectTransform.localScale = new Vector3(gs, gs, 1f);
            }

            float s = Mathf.Lerp(1f, 1.10f, pulse);
            _iconRoot.localScale = new Vector3(s, s, 1f);
        }

        // Heartbeat: prints once/sec. vigI is the live vignette intensity feeding URP;
        // if it oscillates and you still see no edge darkening, post-processing is off
        // on this camera (see notes) — otherwise it should be visible.
        if (debugLog)
        {
            _logT += Time.unscaledDeltaTime;
            if (_logT >= 1f)
            {
                _logT = 0f;
                float vigI = _vignette != null ? _vignette.intensity.value : -1f;
                float volW = _volume != null ? _volume.weight : -1f;
                Debug.Log($"[PMSE] hb '{name}' engaged={IsEngaged} downed={IsDownedActive} " +
                          $"blend={_downedBlend:0.00} w={_weight:0.00} " +
                          $"pulse={pulse:0.00} vigIntensity={vigI:0.00} volWeight={volW:0.00} " +
                          $"sat={(_color != null ? _color.saturation.value : 0):0} " +
                          $"baseV={vignetteBase:0.00} depth={vignettePulseDepth:0.00} speed={pulseSpeed:0.00}");
            }
        }

        if (!active && _weight <= 0.0001f)
        {
            _weight = 0f;
            _downedBlend = 0f;
            if (_volume != null) _volume.weight = 0f;
            ApplyMask(false);
            ApplyPostFx(false);
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        ApplyMask(false);
        ApplyPostFx(false);
        if (_volume != null) Destroy(_volume.gameObject);
        if (_profile != null) Destroy(_profile);
        if (_canvas != null) Destroy(_canvas.gameObject);
        DestroySprite(_glowSprite);
        DestroySprite(_discSprite);
        DestroySprite(_rimSprite);
    }

    private static Color WithAlpha(Color c, float alphaMul)
    {
        c.a *= alphaMul;
        return c;
    }
}


