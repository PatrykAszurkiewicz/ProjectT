using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

    [Header("Isolation")]
    [Tooltip("Volume layer for THIS camera. -1 = auto from player index (P0->31, P1->30).")]
    public int volumeLayer = -1;

    [Header("Debug")]
    public bool debugLog = true;

    public bool IsEngaged { get; private set; }

    // Volume (desaturation + vignette)
    private Camera _cam;
    private Volume _volume;
    private VolumeProfile _profile;
    private ColorAdjustments _color;
    private Vignette _vignette;
    private int _camOriginalMask;
    private bool _maskApplied;

    // Icon UI
    private Canvas _canvas;
    private Image _iconImage;

    private float _weight;   // 0..1 eased engagement
    private float _phase;    // pulse phase, cycles
    private float _logT;     // heartbeat log throttle

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        enabled = false;   // idle until placement mode begins
    }

    /// <summary>Turn the effect on/off for this camera. Eases in and out smoothly.</summary>
    public void SetEngaged(bool on)
    {
        IsEngaged = on;
        if (on)
        {
            EnsureVolume();
            EnsureUI();
            ApplyMask(true);
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            enabled = true;
            if (debugLog)
                Debug.Log($"[PlacementModeScreenEffect] engaged on '{name}' " +
                          $"(volumeLayer={volumeLayer}, rect={_cam.rect}).");
        }
        // Turning off: stay enabled so Update plays the fade-out, then idles.
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
            var imgGO = new GameObject("PlacementModeIcon");
            imgGO.transform.SetParent(canvasGO.transform, false);
            _iconImage = imgGO.AddComponent<Image>();
            _iconImage.sprite = sp;
            _iconImage.raycastTarget = false;
            _iconImage.preserveAspect = true;
            var rt = _iconImage.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);  // bottom-right
            rt.sizeDelta = new Vector2(iconSize, iconSize);
            rt.anchoredPosition = new Vector2(-iconPadding, iconPadding);
        }

        canvasGO.SetActive(false);
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

    private void Update()
    {
        float target = IsEngaged ? 1f : 0f;
        _weight = Mathf.MoveTowards(_weight, target, fadeSpeed * Time.unscaledDeltaTime);
        _phase += Time.unscaledDeltaTime * pulseSpeed;

        float wave = Mathf.Sin(_phase * Mathf.PI * 2f);   // -1..1, symmetric
        float pulse = 0.5f + 0.5f * wave;                  // 0..1

        // The Volume's overall weight carries the smooth fade in/out.
        if (_volume != null) _volume.weight = _weight;

        // Desaturation (+ optional breaths). Steady saturation by default.
        if (_color != null)
        {
            float grey = maxGrey * (1f - greyPulseDepth * pulse);
            _color.saturation.value = -100f * grey;
            _color.postExposure.value = brightnessPulseDepth * wave;
        }

        // PULSE: the vignette intensity breathes every frame. The Volume composites
        // this into the camera image, so it darkens the screen edges you actually see.
        if (_vignette != null)
        {
            _vignette.intensity.value = Mathf.Clamp01(vignetteBase + vignettePulseDepth * pulse);
            _vignette.smoothness.value = vignetteSmoothness;
            _vignette.color.value = vignetteColor;
        }

        // Icon: stays bright so it reads on top of the dark frame; gentle beat.
        if (_iconImage != null)
        {
            float a = _weight * Mathf.Lerp(0.85f, 1f, pulse);
            var c = _iconImage.color; c.a = a; _iconImage.color = c;
            float s = Mathf.Lerp(1f, 1.10f, pulse);
            _iconImage.rectTransform.localScale = new Vector3(s, s, 1f);
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
                Debug.Log($"[PMSE] hb '{name}' engaged={IsEngaged} w={_weight:0.00} " +
                          $"pulse={pulse:0.00} vigIntensity={vigI:0.00} volWeight={volW:0.00} " +
                          $"sat={(_color != null ? _color.saturation.value : 0):0} " +
                          $"baseV={vignetteBase:0.00} depth={vignettePulseDepth:0.00} speed={pulseSpeed:0.00}");
            }
        }

        if (!IsEngaged && _weight <= 0.0001f)
        {
            _weight = 0f;
            if (_volume != null) _volume.weight = 0f;
            ApplyMask(false);
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        ApplyMask(false);
        if (_volume != null) Destroy(_volume.gameObject);
        if (_profile != null) Destroy(_profile);
        if (_canvas != null) Destroy(_canvas.gameObject);
    }
}

