using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Boss bar glow

[DisallowMultipleComponent]
public class BossBarFrameGlow : MonoBehaviour
{
    public const string ShaderName = "UI/BossBarGlowSilhouette";

    [Header("Source")]
    [Tooltip("The frame graphic to glow (the prefab's 'Ramka'). BossHealthBarUI assigns this.")]
    [SerializeField] private Image frameImage;

    [Header("Glow")]
    [SerializeField] private Color glowColor = new Color(0.78f, 0.30f, 1f, 1f);
    [Tooltip("Master brightness. Keep this low - the point is a subtle rim, not a lamp.")]
    [Range(0f, 3f)][SerializeField] private float intensity = 0.85f;
    [Tooltip("How many rings of offset copies. More = wider, softer falloff.")]
    [Range(1, 4)][SerializeField] private int rings = 3;
    [Tooltip("Copies per ring, spread evenly around the compass.")]
    [Range(4, 12)][SerializeField] private int directions = 8;
    [Tooltip("Pixels of spread added per ring.")]
    [SerializeField] private float radiusStep = 4.5f;

    [Header("Breathing")]
    [SerializeField] private float pulseSpeed = 0.8f;
    [Range(0f, 1f)][SerializeField] private float pulseAmount = 0.35f;

    [Header("Sheen sweep")]
    [Tooltip("A highlight band that travels along the frame.")]
    [SerializeField] private bool sweepEnabled = true;
    [Tooltip("Seconds of REST between passes. Total cycle = this + Sweep Travel Time.")]
    [SerializeField] private float sweepPeriod = 1.9f;
    [Tooltip("Seconds one pass takes to cross the frame.")]
    [SerializeField] private float sweepTravelTime = 0.85f;
    [Range(0.02f, 0.4f)][SerializeField] private float sweepWidth = 0.09f;
    [Range(0f, 3f)][SerializeField] private float sweepStrength = 1.1f;
    [SerializeField] private Color sweepColor = new Color(1f, 0.85f, 1f, 1f);

    // -- runtime ---------------------------------------------------------
    private static readonly int IntensityProp = Shader.PropertyToID("_Intensity");
    private static readonly int AlphaPowerProp = Shader.PropertyToID("_AlphaPower");
    private static readonly int SweepProp = Shader.PropertyToID("_Sweep");
    private static readonly int SweepWidthProp = Shader.PropertyToID("_SweepWidth");
    private static readonly int SweepStrengthProp = Shader.PropertyToID("_SweepStrength");

    private static bool _warnedMissingShader;

    private RectTransform _haloRoot;
    private RectTransform _sweepRoot;
    private readonly List<Image> _copies = new List<Image>();
    private readonly List<Vector2> _dirs = new List<Vector2>();
    private readonly List<int> _ringOf = new List<int>();
    private Image _sweepImage;

    private Material _glowMat;
    private Material _sweepMat;
    private bool _hasShader;
    private bool _built;

    private float _master = 1f;      // fade owned by BossHealthBarUI
    private float _flash;            // decaying burst brightness
    private float _radiusBoost;      // decaying burst spread
    private float _sweepTimer;

    /// 0..1 fade applied on top of everything (reveal / suppression).
    public float Master { get => _master; set => _master = Mathf.Clamp01(value); }

    /// The aura container - belongs BEHIND the frame.
    public RectTransform HaloRoot => _haloRoot;

    /// The sheen container - belongs IN FRONT of the frame.
    public RectTransform SweepRoot => _sweepRoot;

    public void SetFrame(Image frame)
    {
        if (frame == frameImage && _built) return;
        frameImage = frame;
        if (_built) Teardown();
        Build();
    }

    public void SetColor(Color c) => glowColor = c;

    /// Momentary flare - reveal, armour break, death.
    public void Burst(float radiusBoost = 10f, float flash = 1f)
    {
        _radiusBoost = Mathf.Max(_radiusBoost, radiusBoost);
        _flash = Mathf.Max(_flash, Mathf.Clamp01(flash));
        _sweepTimer = Mathf.Min(_sweepTimer, 0.05f);   // kick off a sweep too
    }

    private void Awake()
    {
        if (frameImage != null) Build();
    }

    private void OnDestroy()
    {
        Teardown();
        if (_glowMat != null) Destroy(_glowMat);
        if (_sweepMat != null) Destroy(_sweepMat);
    }

    private void Teardown()
    {
        if (_haloRoot != null) Destroy(_haloRoot.gameObject);
        if (_sweepRoot != null) Destroy(_sweepRoot.gameObject);
        _haloRoot = null;
        _sweepRoot = null;
        _copies.Clear();
        _dirs.Clear();
        _ringOf.Clear();
        _sweepImage = null;
        _built = false;
    }

    // -- build -----------------------------------------------------------

    private void Build()
    {
        if (_built || frameImage == null) return;

        var parent = frameImage.transform.parent as RectTransform;
        if (parent == null) return;

        var shader = Shader.Find(ShaderName);
        _hasShader = shader != null;

        if (!_hasShader && !_warnedMissingShader)
        {
            _warnedMissingShader = true;
            Debug.LogWarning("[BossBarFrameGlow] Shader \"" + ShaderName + "\" not found. Add " +
                             "UIGlowSilhouette.shader to the project (and to Always Included Shaders " +
                             "if you strip shaders on build). Falling back to a plain tinted glow, " +
                             "which looks muddy on art with a dark baked outline.");
        }

        if (_hasShader)
        {
            _glowMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _glowMat.SetFloat(IntensityProp, 1f);
            _glowMat.SetFloat(AlphaPowerProp, 1f);
            _glowMat.SetFloat(SweepStrengthProp, 0f);

            _sweepMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _sweepMat.SetFloat(IntensityProp, 1f);
            _sweepMat.SetFloat(AlphaPowerProp, 1f);
            _sweepMat.SetFloat(SweepStrengthProp, 1f);
            _sweepMat.SetFloat(SweepWidthProp, sweepWidth);
            _sweepMat.SetFloat(SweepProp, -1f);
        }

        _haloRoot = NewContainer(parent, "FrameGlow_Halo");
        _haloRoot.SetSiblingIndex(frameImage.transform.GetSiblingIndex());   // behind the frame

        int dirCount = Mathf.Max(4, directions);
        for (int r = 1; r <= Mathf.Max(1, rings); r++)
        {
            for (int d = 0; d < dirCount; d++)
            {
                float ang = (Mathf.PI * 2f) * d / dirCount;
                _dirs.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)));
                _ringOf.Add(r);
                _copies.Add(NewFrameCopy(_haloRoot, "Glow_r" + r + "_d" + d, _glowMat));
            }
        }

        if (sweepEnabled)
        {
            _sweepRoot = NewContainer(parent, "FrameGlow_Sweep");
            _sweepRoot.SetAsLastSibling();                                   // in front of the frame
            _sweepImage = NewFrameCopy(_sweepRoot, "Sheen", _sweepMat);
            _sweepTimer = sweepPeriod * 0.5f;
        }

        _built = true;
    }

    private RectTransform NewContainer(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(BossBarExcludeFromShatter));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private Image NewFrameCopy(RectTransform parent, string name, Material mat)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite = frameImage.sprite;
        img.type = frameImage.type;
        img.preserveAspect = frameImage.preserveAspect;
        img.raycastTarget = false;
        img.maskable = false;
        if (mat != null) img.material = mat;
        return img;
    }

    // -- per-frame -------------------------------------------------------

    private void LateUpdate()
    {
        if (!_built || frameImage == null) return;

        float dt = Time.unscaledDeltaTime;

        _flash = Mathf.Max(0f, _flash - dt * 1.8f);
        _radiusBoost = Mathf.Lerp(_radiusBoost, 0f, dt * 3.5f);

        MatchRect(_haloRoot);
        MatchRect(_sweepRoot);

        // Gentle breathing, plus whatever burst is still decaying.
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
        float a = intensity * _master * pulse * (1f + _flash * 2f);
        Color c = _flash > 0f ? Color.Lerp(glowColor, Color.white, _flash * 0.5f) : glowColor;

        int dirCount = Mathf.Max(4, directions);
        for (int i = 0; i < _copies.Count; i++)
        {
            var img = _copies[i];
            if (img == null) continue;

            int ring = _ringOf[i];
            float radius = ring * radiusStep + _radiusBoost * ring * 0.35f;

            // Inner rings are brightest. Divided by the direction count so the
            // overlapping copies sum to roughly `intensity` rather than 8x it.
            float falloff = 1f / (ring * ring);
            float alpha = a * falloff / dirCount;

            img.rectTransform.anchoredPosition = _dirs[i] * radius;

            Color cc = c;
            cc.a = Mathf.Clamp01(alpha);
            img.color = cc;
            img.enabled = cc.a > 0.002f;
        }

        UpdateSweep(dt);
    }

    private void UpdateSweep(float dt)
    {
        if (_sweepImage == null) return;

        if (_master <= 0.01f)
        {
            _sweepImage.enabled = false;
            return;
        }

        _sweepTimer -= dt;

        if (_sweepTimer > 0f)
        {
            _sweepImage.enabled = false;      // waiting for the next pass
            return;
        }

        // _sweepTimer counts from 0 down to -sweepTravelTime across one pass.
        float k = -_sweepTimer / Mathf.Max(0.05f, sweepTravelTime);
        if (k >= 1f)
        {
            _sweepTimer = sweepPeriod;
            _sweepImage.enabled = false;
            return;
        }

        _sweepImage.enabled = true;

        Color sc = sweepColor;
        // Fade in and out across the travel so it never pops at the edges.
        sc.a = Mathf.Sin(k * Mathf.PI) * sweepStrength * _master;
        _sweepImage.color = sc;

        if (_sweepMat != null)
        {
            _sweepMat.SetFloat(SweepProp, Mathf.Lerp(-sweepWidth * 2f, 1f + sweepWidth * 2f, k));
            _sweepMat.SetFloat(SweepWidthProp, sweepWidth);
        }
    }

    // Keep a container aligned with the frame, whatever anchors the prefab uses.
    private void MatchRect(RectTransform rt)
    {
        if (rt == null) return;
        var f = frameImage.rectTransform;
        rt.anchorMin = f.anchorMin;
        rt.anchorMax = f.anchorMax;
        rt.pivot = f.pivot;
        rt.anchoredPosition = f.anchoredPosition;
        rt.sizeDelta = f.sizeDelta;
        rt.localScale = f.localScale;
        rt.localRotation = f.localRotation;
    }
}


