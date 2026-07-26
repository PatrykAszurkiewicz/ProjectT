using UnityEngine;
using UnityEngine.UI;

// EnemyBar prefab


[DisallowMultipleComponent]
public class EnemyBarUI : MonoBehaviour
{
    [Header("Wiring - leave empty to auto-resolve from children")]
    [Tooltip("The bar graphic that depletes (the prefab's 'Image', sprite EnemyFILLHP).")]
    [SerializeField] private Image fillImage;
    [Tooltip("The frame drawn over the fill (the prefab's 'Ramka', sprite EnemyRamkaHP).")]
    [SerializeField] private Image frameImage;
    [Tooltip("Optional lagging 'damage chip'. Auto-cloned from the fill when enabled below.")]
    [SerializeField] private Image shadowImage;

    [Header("Fill")]
    [SerializeField] private bool autoCreateDamageShadow = true;
    [Tooltip("Colour of the lagging 'shadow of health' chip. This MULTIPLIES the fill sprite, " +
             "so it can only darken it - a dim shade reads as the fill's ghost, white would be " +
             "invisible (identical to the fill). It's kept near-neutral so it dims whatever the " +
             "fill art is: swap the fill to green and the chip becomes a darker green on its own.")]
    [SerializeField] private Color damageShadowColor = new Color(0.50f, 0.40f, 0.58f, 1f);
    [Tooltip("How fast the visible fill chases the real health. Higher = snappier. " +
             "20 settles in roughly 0.15s, which still reads as a fall rather than a snap.")]
    [SerializeField] private float fillLerpSpeed = 20f;
    [Tooltip("How fast the shadow chip drains once it starts moving. Kept below the fill speed " +
             "on purpose - the gap between the two is what you see as the receding chip.")]
    [SerializeField] private float shadowLerpSpeed = 5f;
    [Tooltip("Seconds the chip holds still after a hit before it starts draining. A short hold " +
             "lets the fresh chip register before it recedes, without feeling sluggish.")]
    [SerializeField] private float shadowDelay = 0.15f;

    [Header("Feedback")]
    [Tooltip("Shake distance per hit, in the bar's own local pixels, scaled by hit size. 0 = off.")]
    [SerializeField] private float hitShakeAmount = 12f;
    [SerializeField] private float shakeDamping = 14f;
    [Tooltip("Extra scale punched in on a hit, as a fraction. 0 = off.")]
    [Range(0f, 0.5f)][SerializeField] private float hitPunchScale = 0.10f;
    [SerializeField] private float punchDamping = 9f;

    [Header("Low health")]
    [SerializeField] private bool tintOnLowHealth = true;
    [Range(0.05f, 1f)][SerializeField] private float lowHealthThreshold = 0.3f;
    [Tooltip("Multiplied into the fill's authored colour, so white = untouched.")]
    [SerializeField] private Color lowHealthTint = new Color(1f, 0.45f, 0.5f, 1f);
    [SerializeField] private float lowHealthPulseSpeed = 4f;
    [Range(0f, 0.6f)][SerializeField] private float lowHealthPulseAmount = 0.25f;

    [Header("Visibility")]
    [Tooltip("Hide the bar while the enemy is at full health, show it on the first hit.")]
    [SerializeField] private bool hideWhenFull = false;
    [Tooltip("Seconds of no damage before the bar fades out again (Hide When Full only).")]
    [SerializeField] private float hideDelay = 3f;
    [SerializeField] private float fadeSpeed = 8f;

    [Header("Size")]
    [Tooltip("Scale the bar so the frame ends up this wide in WORLD units, whatever the " +
             "canvas scale happens to be. The art is 640x85 px, so it needs scaling down " +
             "hard on a world-space canvas. This measures at runtime, so you don't have to " +
             "work out the scale factor yourself. Set to 0 to disable and use the prefab's " +
             "own scale.")]
    [SerializeField] private float matchWorldWidth = 1.56f;
    [Tooltip("Zero the prefab root's anchored position on Awake. The authored EnemyBar sits " +
             "at y = 309, which is an offset from the canvas it was designed in.")]
    [SerializeField] private bool resetAnchoredPosition = true;

    [Header("Time")]
    [Tooltip("Keep animating during hit-stop / pause. Recommended on.")]
    [SerializeField] private bool useUnscaledTime = true;

    private static readonly int FillProp = Shader.PropertyToID("_Fill");

    private RectTransform _rt;
    private CanvasGroup _cg;

    private Material _fillMat, _shadowMat;
    private bool _fillUsesShader, _shadowUsesShader;
    private Color _authoredFillColor = Color.white;

    private float _target = 1f;
    private float _display = 1f;
    private float _shadow = 1f;
    private float _shadowTimer;

    private Vector2 _basePos;
    private float _baseScale = 1f;
    private Vector2 _shakeOffset;
    private float _punch;
    private float _visibleTimer;
    private float _alpha = 1f;
    private bool _hasValue;

    // Current health as 0..1, as the bar understands it (not the animated value).
    public float Normalized => _target;

    public RectTransform Rect => _rt != null ? _rt : (_rt = (RectTransform)transform);

    private void Awake()
    {
        _rt = (RectTransform)transform;

        ResolveGraphics();
        if (fillImage != null) _authoredFillColor = fillImage.color;
        SetupMaterials();
        EnforceRenderOrder();
        ApplyLayout();

        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        _cg.interactable = false;
        _cg.blocksRaycasts = false;

        _alpha = hideWhenFull ? 0f : 1f;
        _cg.alpha = _alpha;

        ApplyFill(_display);
        ApplyShadow(_shadow);
    }

    private void OnDestroy()
    {
        if (_fillMat != null) Destroy(_fillMat);
        if (_shadowMat != null) Destroy(_shadowMat);
    }



    /// Main entry point. Matches ResourceBarUI so this is a drop-in replacement.
    public void SetValue(float current, float max)
    {
        SetNormalized(max > 0.0001f ? current / max : 0f);
    }

    /// Set health directly as 0..1.
    public void SetNormalized(float value)
    {
        value = Mathf.Clamp01(value);

        // First value ever: snap, don't animate a drain from full.
        if (!_hasValue)
        {
            _hasValue = true;
            _target = _display = _shadow = value;
            ApplyFill(_display);
            ApplyShadow(_shadow);
            if (value < 0.999f) _visibleTimer = hideDelay;
            return;
        }

        if (value < _target - 0.0001f)
        {
            // Took a hit: pin the chip where the bar currently is, hold it, then
            // let it drain down to the new value.
            _shadow = Mathf.Max(_shadow, _display);
            _shadowTimer = shadowDelay;

            float severity = Mathf.Clamp01((_target - value) * 5f);
            AddShake(hitShakeAmount * severity);
            _punch = Mathf.Max(_punch, hitPunchScale * (0.5f + 0.5f * severity));
            _visibleTimer = hideDelay;
        }
        else if (value > _target + 0.0001f)
        {
            // Healed: the chip must never sit below the live fill.
            _shadow = Mathf.Max(_shadow, value);
            _visibleTimer = hideDelay;
        }

        _target = value;
    }

    /// Jump straight to a value with no lerp (spawn, or a max-health rescale).
    public void SnapTo(float current, float max)
    {
        _target = _display = _shadow = max > 0.0001f ? Mathf.Clamp01(current / max) : 0f;
        _shadowTimer = 0f;
        _hasValue = true;
        ApplyFill(_display);
        ApplyShadow(_shadow);
    }

    /// Quick fade + shrink, then destroy. EnemyStats destroys the bar outright on
    /// death, so this is only used if you opt into it on EnemyHealthBar.
    public void FadeOutAndDestroy(float duration = 0.2f)
    {
        StartCoroutine(FadeRoutine(duration));
    }

    private System.Collections.IEnumerator FadeRoutine(float duration)
    {
        float t = 0f;
        float startAlpha = _alpha;
        Vector3 startScale = _rt.localScale;
        while (t < 1f)
        {
            t += Dt / Mathf.Max(0.01f, duration);
            _alpha = Mathf.Lerp(startAlpha, 0f, t);
            if (_cg != null) _cg.alpha = _alpha;
            _rt.localScale = Vector3.Lerp(startScale, startScale * 0.7f, t);
            yield return null;
        }
        Destroy(gameObject);
    }



    private float Dt => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void LateUpdate()
    {
        float dt = Dt;

        // Live fill chases the real value. Exponential form so the speed means
        // the same thing at any framerate.
        _display = Mathf.Lerp(_display, _target, 1f - Mathf.Exp(-fillLerpSpeed * dt));
        if (Mathf.Abs(_display - _target) < 0.0005f) _display = _target;
        ApplyFill(_display);

        // Chip holds, then follows.
        if (_shadowTimer > 0f) _shadowTimer -= dt;
        else
        {
            _shadow = Mathf.Lerp(_shadow, _target, 1f - Mathf.Exp(-shadowLerpSpeed * dt));
            if (Mathf.Abs(_shadow - _target) < 0.0005f) _shadow = _target;
        }
        ApplyShadow(_shadow);

        UpdateTint(dt);
        UpdateTransform(dt);
        UpdateVisibility(dt);
    }

    private void UpdateTint(float dt)
    {
        if (!tintOnLowHealth || fillImage == null) return;

        Color want = _authoredFillColor;
        if (_target <= lowHealthThreshold)
        {
            // Deeper into the red band = stronger tint and stronger pulse.
            float k = 1f - Mathf.Clamp01(_target / Mathf.Max(0.0001f, lowHealthThreshold));
            float pulse = 1f + Mathf.Sin(Now * lowHealthPulseSpeed * Mathf.PI * 2f) * lowHealthPulseAmount * k;
            want = _authoredFillColor * Color.Lerp(Color.white, lowHealthTint, k) * pulse;
            want.a = _authoredFillColor.a;
        }

        fillImage.color = Color.Lerp(fillImage.color, want, dt * 10f);
    }

    private void UpdateTransform(float dt)
    {
        _shakeOffset = Vector2.Lerp(_shakeOffset, Vector2.zero, dt * shakeDamping);
        _punch = Mathf.Lerp(_punch, 0f, dt * punchDamping);

        if (_rt == null) return;

        _rt.anchoredPosition = _basePos + _shakeOffset;

        if (hitPunchScale > 0f)
        {
            // Squash-and-stretch: wider than it is taller, which reads better on
            // a bar this wide.
            _rt.localScale = new Vector3(_baseScale * (1f + _punch),
                                         _baseScale * (1f + _punch * 0.5f),
                                         1f);
        }
    }

    private void UpdateVisibility(float dt)
    {
        if (_cg == null) return;

        float want = 1f;
        if (hideWhenFull)
        {
            if (_visibleTimer > 0f) _visibleTimer -= dt;
            // Stay up while anything is still animating, even past the timer.
            bool settled = _shadow >= 0.999f && _display >= 0.999f;
            want = (_visibleTimer > 0f || !settled) ? 1f : 0f;
        }

        _alpha = Mathf.MoveTowards(_alpha, want, dt * fadeSpeed);
        _cg.alpha = _alpha;
    }

    private void AddShake(float amount)
    {
        if (amount <= 0f) return;
        _shakeOffset += new Vector2(Random.Range(-amount, amount),
                                    Random.Range(-amount, amount) * 0.5f);
    }



    // Scale the root so the frame comes out `matchWorldWidth` units wide on
    // screen. Measured from the live hierarchy, so it's correct no matter what
    // the enemy bar's world-space canvas is scaled to.
    private void ApplyLayout()
    {
        if (resetAnchoredPosition) _rt.anchoredPosition = Vector2.zero;

        if (matchWorldWidth > 0f)
        {
            RectTransform probe = frameImage != null ? frameImage.rectTransform : _rt;
            float px = probe.rect.width;
            float parentScale = _rt.parent != null ? Mathf.Abs(_rt.parent.lossyScale.x) : 1f;

            if (px > 1f && parentScale > 0.000001f)
            {
                float s = matchWorldWidth / (px * parentScale);
                _rt.localScale = new Vector3(s, s, 1f);
            }
        }

        _baseScale = _rt.localScale.x;
        _basePos = _rt.anchoredPosition;
    }

    /// Re-run the sizing pass. Call this if you reparent the bar or change the
    /// canvas scale at runtime.
    public void RefreshLayout() => ApplyLayout();



    private void ResolveGraphics()
    {
        var images = GetComponentsInChildren<Image>(true);

        // FILL: sprite/object name first, then whichever is already set to
        // Filled, then a shader-driven fill material. Inspector wins over all.
        if (fillImage == null) fillImage = FindByName(images, "fill");
        if (fillImage == null)
            foreach (var img in images)
                if (img.type == Image.Type.Filled) { fillImage = img; break; }
        if (fillImage == null)
            foreach (var img in images)
                if (img.material != null && img.material.HasProperty(FillProp)) { fillImage = img; break; }

        // FRAME: by name, else the largest image that isn't the fill.
        if (frameImage == null) frameImage = FindByName(images, "ramka", "frame", "border");
        if (frameImage == null || frameImage == fillImage)
        {
            float best = -1f;
            Image found = null;
            foreach (var img in images)
            {
                if (img == fillImage) continue;
                var r = img.rectTransform.rect;
                float area = r.width * r.height;
                if (area > best) { best = area; found = img; }
            }
            if (found != null) frameImage = found;
        }

        // A health bar must never eat a click. The EnemyBar prefab ships with
        // Raycast Target ON, so this matters.
        foreach (var img in images) img.raycastTarget = false;

        if (fillImage == null)
        {
            Debug.LogError("[EnemyBarUI] No fill graphic found on '" + name +
                           "'. Assign 'Fill Image' in the inspector.", this);
            return;
        }

        // Force a state that can actually display a value. If the Image is
        // Simple, fillAmount does nothing and the sprite draws whole or not at all.
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillCenter = true;

        var c = fillImage.color;
        if (c.a < 0.05f) { c.a = 1f; fillImage.color = c; }   // rescue a transparent fill

        if (shadowImage == null && autoCreateDamageShadow)
            shadowImage = CreateDamageShadow(fillImage);
    }

    private static Image FindByName(Image[] images, params string[] needles)
    {
        foreach (var img in images)
        {
            string sprite = img.sprite != null ? img.sprite.name.ToLowerInvariant() : "";
            string obj = img.name.ToLowerInvariant();
            foreach (var n in needles)
                if (sprite.Contains(n) || obj.Contains(n)) return img;
        }
        return null;
    }

    // Clone of the fill, tinted to a dimmer shade of the same art, sitting BEHIND
    // it, so freshly lost health shows through as a chip that drains a moment later.
    private Image CreateDamageShadow(Image source)
    {
        if (source.gameObject == gameObject || source.transform.parent == null) return null;

        var go = Instantiate(source.gameObject, source.transform.parent, false);
        go.name = "DamageShadow";

        // Strip anything the clone shouldn't carry - but not its Graphic.
        foreach (var mb in go.GetComponents<MonoBehaviour>())
            if (!(mb is Graphic)) Destroy(mb);
        for (int i = go.transform.childCount - 1; i >= 0; i--)
            Destroy(go.transform.GetChild(i).gameObject);

        var img = go.GetComponent<Image>();
        if (img == null) { Destroy(go); return null; }

        // Force full alpha regardless of the source's tint: a translucent chip
        // lets the dark background bleed through and muddies the colour.
        Color c = damageShadowColor;
        c.a = 1f;
        img.color = c;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        return img;
    }

    private void SetupMaterials()
    {
        _fillMat = TrySetupShaderMaterial(fillImage, out _fillUsesShader);
        _shadowMat = TrySetupShaderMaterial(shadowImage, out _shadowUsesShader);
    }

    // Same trick ResourceBarUI uses: if the material exposes _Fill, instance it
    // and drive the shader; otherwise fall back to fillAmount.
    private static Material TrySetupShaderMaterial(Image img, out bool usesShader)
    {
        usesShader = false;
        if (img == null || img.material == null) return null;
        if (!img.material.HasProperty(FillProp)) return null;

        var mat = new Material(img.material);
        img.material = mat;
        mat.SetFloat(FillProp, 1f);
        usesShader = true;
        return mat;
    }

    // Chip behind the fill, fill behind the frame. Prefab authoring order can't
    // break it, and the auto-created clone always lands in the right slot.
    private void EnforceRenderOrder()
    {
        if (fillImage == null) return;
        Transform parent = fillImage.transform.parent;

        int i = 0;
        if (shadowImage != null && shadowImage.transform.parent == parent)
            shadowImage.transform.SetSiblingIndex(i++);

        fillImage.transform.SetSiblingIndex(i++);

        if (frameImage != null && frameImage.transform.parent == parent)
            frameImage.transform.SetSiblingIndex(i);
    }

    private void ApplyFill(float v) => Apply(fillImage, _fillMat, _fillUsesShader, v);

    private void ApplyShadow(float v) => Apply(shadowImage, _shadowMat, _shadowUsesShader, v);

    private static void Apply(Image img, Material mat, bool usesShader, float v)
    {
        if (img == null) return;
        v = Mathf.Clamp01(v);
        if (usesShader && mat != null)
        {
            if (img.fillAmount != 1f) img.fillAmount = 1f;
            mat.SetFloat(FillProp, v);
        }
        else
        {
            img.fillAmount = v;
        }
    }
}


