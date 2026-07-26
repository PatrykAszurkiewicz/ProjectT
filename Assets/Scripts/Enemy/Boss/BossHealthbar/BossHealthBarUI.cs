using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// The boss health bar at the top of the screen.
// FILL
// The fill is driven directly rather than through ResourceBarUI, so nothing about
// the player HUD has to change, but the feel is the same: a fast lerp for the live
// value plus a slower "chip" shadow that lags behind after a hit. On reveal the bar
// charges from empty up to the boss's current health, then depletes on every hit.

[DisallowMultipleComponent]
public class BossHealthBarUI : MonoBehaviour
{
    [Header("Wiring - leave empty to auto-resolve from children")]
    [Tooltip("The bar graphic that depletes (the prefab's 'Image', sprite BOSSFILL).")]
    [SerializeField] private Image fillImage;
    [Tooltip("The frame the glow traces (the prefab's 'Ramka', sprite BOSSRamka).")]
    [SerializeField] private Image frameImage;
    [Tooltip("Optional lagging 'damage chip'. Auto-cloned from the fill when enabled below.")]
    [SerializeField] private Image shadowImage;

    [Header("Fill")]
    [SerializeField] private bool autoCreateDamageShadow = true;
    [SerializeField] private Color damageShadowColor = new Color(0.95f, 0.20f, 0.35f, 0.9f);
    [SerializeField] private float fillLerpSpeed = 9f;
    [SerializeField] private float shadowLerpSpeed = 2.2f;
    [SerializeField] private float shadowDelay = 0.45f;
    [Tooltip("Seconds spent charging the bar from empty when it appears.")]
    [SerializeField] private float fillUpDuration = 0.85f;
    [Tooltip("Charge all the way to 100%, then drop to the boss's real health. " +
             "Keeps the reveal reading as 'full bar, then damage' when the boss has already " +
             "been hit during the intro. Off = charge straight to its current health.")]
    [SerializeField] private bool fillUpToFull = true;
    [Tooltip("Seconds to hold the bar at 100% before it settles onto the boss's real health.")]
    [SerializeField] private float fillUpHoldTime = 0.3f;

    [Header("Fill padding (normally leave both at 0)")]
    [Tooltip("Dead zone at the START of the bar. Only useful if your fill art has a wide " +
             "transparent left margin and a sliver pokes out from under the end cap at low health. " +
             "BOSSFILL.png does not need this.")]
    [Range(0f, 0.4f)][SerializeField] private float fillPaddingLeft = 0f;
    [Tooltip("KEEP THIS AT 0 unless you know your fill art has no end cap. Any value here clips " +
             "the right edge of the fill sprite, replacing its authored tapered cap with a flat " +
             "vertical edge - which reads as 'not full' even when the remaining fill still covers " +
             "the frame's window.")]
    [Range(0f, 0.4f)][SerializeField] private float fillPaddingRight = 0f;

    [Header("Colours")]
    [Tooltip("Tint the fill while the boss still has armour, so the armour phase reads at a glance. " +
             "Tints MULTIPLY the prefab's authored colour, so white = untouched.")]
    [SerializeField] private bool tintWhileArmored = true;
    [SerializeField] private Color armoredFillColor = new Color(0.72f, 0.86f, 1f, 1f);
    [SerializeField] private Color healthFillColor = Color.white;

    [Header("Glow")]
    [SerializeField] private bool enableFrameGlow = true;
    [SerializeField] private Color glowColor = new Color(0.78f, 0.30f, 1f, 1f);
    [Tooltip("Glow colour once the armour is gone (the 'real health' phase).")]
    [SerializeField] private Color glowColorNoArmor = new Color(1f, 0.25f, 0.45f, 1f);

    [Header("Reveal")]
    [SerializeField] private float revealDuration = 0.5f;
    [Tooltip("How far above its resting place the bar starts, in pixels.")]
    [SerializeField] private float revealDropDistance = 70f;

    [Header("Feedback")]
    [SerializeField] private float hitShakeAmount = 9f;
    [SerializeField] private float armorBreakShake = 26f;
    [SerializeField] private float shakeDamping = 9f;

    [Header("Death")]
    [Tooltip("Seconds spent draining the last of the bar before it blows apart.")]
    [SerializeField] private float deathDrainTime = 0.35f;
    [SerializeField] private BossBarDisintegrator.Settings disintegration = new BossBarDisintegrator.Settings();

    [Header("Debug")]
    [Tooltip("Log which graphics were picked and what the bar is tracking. " +
             "Turn this on first if the fill isn't showing.")]
    [SerializeField] private bool debugLog = false;

    // -- runtime ---------------------------------------------------------
    private static readonly int FillProp = Shader.PropertyToID("_Fill");

    private RectTransform _rt;
    private CanvasGroup _cg;
    private BossBarFrameGlow _glow;

    private BaseBossStats _boss;
    private float _maxPool = 1f;
    private bool _includeArmor = true;

    private float _target = 1f;
    private float _display;
    private float _shadow;
    private float _shadowTimer;

    private Material _fillMat, _shadowMat;
    private bool _fillUsesShader, _shadowUsesShader;
    private Color _authoredFillColor = Color.white;

    private Vector2 _basePos;
    private Vector2 _authoredPos;
    private Vector2 _shakeOffset;
    private float _revealT;
    private float _revealAlpha;
    private bool _revealing;
    private bool _fillingUp;
    private bool _suppressed;
    private bool _armorWasUp = true;
    private bool _sawAlive;
    private bool _dying;
    private float _debugTimer;

    public bool IsDying => _dying;
    public BaseBossStats Boss => _boss;
    public RectTransform Rect => _rt != null ? _rt : (_rt = (RectTransform)transform);

    /// The position the prefab was authored with, before the manager placed it.
    public Vector2 AuthoredPosition => _authoredPos;

    /// Height of the visible frame, used by the manager to stack multiple bars.
    public float VisualHeight => frameImage != null ? frameImage.rectTransform.rect.height : Rect.rect.height;

    private void Awake()
    {
        _rt = (RectTransform)transform;

        ResolveGraphics();
        if (fillImage != null) _authoredFillColor = fillImage.color;
        SetupMaterials();
        BuildGlow();
        EnforceRenderOrder();

        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        _cg.interactable = false;
        _cg.blocksRaycasts = false;
        _cg.alpha = 0f;

        _basePos = _authoredPos = _rt.anchoredPosition;

        // Start empty - the reveal charges it up.
        _display = 0f;
        _shadow = 0f;
        ApplyFill(0f);
        ApplyShadow(0f);

        if (debugLog) Debug.Log(DescribeWiring());
    }


    private void ResolveGraphics()
    {
        var images = GetComponentsInChildren<Image>(true);

        // FILL: sprite name is the strongest signal, then the Image type, then a
        // shader-driven fill material. Assigning it in the inspector beats all of these.
        if (fillImage == null) fillImage = FindBySpriteName(images, "fill");
        if (fillImage == null)
        {
            foreach (var img in images)
                if (img.type == Image.Type.Filled) { fillImage = img; break; }
        }
        if (fillImage == null)
        {
            foreach (var img in images)
                if (img.material != null && img.material.HasProperty(FillProp)) { fillImage = img; break; }
        }

        // FRAME: by name, else the largest image that isn't the fill.
        if (frameImage == null) frameImage = FindBySpriteName(images, "ramka", "frame", "border");
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

        foreach (var img in images) img.raycastTarget = false;

        if (fillImage == null)
        {
            Debug.LogError("[BossHealthBarUI] No fill graphic found on '" + name + "'. " +
                           "Assign 'Fill Image' in the inspector. " + DescribeChildren(images));
            return;
        }

        // Force the fill into a state that can actually show a value. This is the
        // fix for "the bar appears but there's no health inside the frame": if the
        // Image is Simple, fillAmount does nothing at all and the sprite either
        // draws whole or not at all.
        fillImage.gameObject.SetActive(true);
        fillImage.enabled = true;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillCenter = true;

        var c = fillImage.color;
        if (c.a < 0.05f) { c.a = 1f; fillImage.color = c; }   // rescue a fully transparent fill

        if (shadowImage == null && autoCreateDamageShadow)
            shadowImage = CreateDamageShadow(fillImage);
    }

    private static Image FindBySpriteName(Image[] images, params string[] needles)
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

    // Clone of the fill, tinted, inserted BEHIND it so the lagging value shows
    // through as a chip of freshly lost health.
    private Image CreateDamageShadow(Image source)
    {
        if (source.gameObject == gameObject || source.transform.parent == null) return null;

        var go = Instantiate(source.gameObject, source.transform.parent, false);
        go.name = "DamageShadow";

        // Strip anything the clone shouldn't carry - but NOT its Graphic.
        foreach (var mb in go.GetComponents<MonoBehaviour>())
            if (!(mb is Graphic)) Destroy(mb);
        for (int i = go.transform.childCount - 1; i >= 0; i--) Destroy(go.transform.GetChild(i).gameObject);

        var img = go.GetComponent<Image>();
        if (img == null) { Destroy(go); return null; }

        img.color = damageShadowColor;
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

    // Same trick ResourceBarUI uses: if the material exposes _Fill, instance it and
    // drive the shader; otherwise fall back to fillAmount.
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

    private void BuildGlow()
    {
        if (!enableFrameGlow || frameImage == null) return;

        _glow = GetComponent<BossBarFrameGlow>();
        if (_glow == null) _glow = gameObject.AddComponent<BossBarFrameGlow>();
        _glow.SetFrame(frameImage);
        _glow.SetColor(glowColor);
        _glow.Master = 0f;   // faded in by the reveal
    }

    // Guarantees: glow halo behind the frame, then the frame, then the damage chip,
    // then the fill, with the sheen on top. Prefab authoring order can't break it.
    private void EnforceRenderOrder()
    {
        if (frameImage == null || fillImage == null) return;

        Transform parent = frameImage.transform.parent;
        if (fillImage.transform.parent != parent) return;   // unusual nesting - leave it alone

        int i = 0;
        if (_glow != null && _glow.HaloRoot != null && _glow.HaloRoot.parent == parent)
            _glow.HaloRoot.SetSiblingIndex(i++);

        frameImage.transform.SetSiblingIndex(i++);

        if (shadowImage != null && shadowImage.transform.parent == parent)
            shadowImage.transform.SetSiblingIndex(i++);

        fillImage.transform.SetSiblingIndex(i);

        if (_glow != null && _glow.SweepRoot != null && _glow.SweepRoot.parent == parent)
            _glow.SweepRoot.SetAsLastSibling();
    }


    public void Bind(BaseBossStats boss, bool includeArmorInPool)
    {
        _boss = boss;
        _includeArmor = includeArmorInPool;
        _maxPool = Mathf.Max(1f, TotalMax());

        _target = Mathf.Clamp01(TotalCurrent() / _maxPool);
        _display = 0f;
        _shadow = 0f;
        ApplyFill(0f);
        ApplyShadow(0f);

        _armorWasUp = boss != null && !boss.IsArmorDestroyed;
        UpdateTints(true);

        if (debugLog) Debug.Log(DescribeWiring());
    }

    /// The manager owns placement; shake and the reveal slide ride on top.
    public void SetBasePosition(Vector2 pos)
    {
        _basePos = pos;
        if (_rt == null) _rt = (RectTransform)transform;
        _rt.anchoredPosition = _basePos;
    }

    /// Temporarily hide without cancelling anything (used during boss cinematics).
    public void SetSuppressed(bool suppressed) => _suppressed = suppressed;

    /// Slide in, then charge the bar up from empty to the boss's current health.
    public void Reveal()
    {
        if (_revealing || _revealT >= 1f) return;
        _revealing = true;
        StartCoroutine(RevealRoutine());
    }

    private IEnumerator RevealRoutine()
    {
        _fillingUp = true;
        _display = 0f;
        _shadow = 0f;
        ApplyFill(0f);
        ApplyShadow(0f);

        if (_glow != null) _glow.Burst(16f, 1f);

        // 1) slide + fade in
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, revealDuration);
            _revealT = Mathf.Clamp01(t);
            yield return null;
        }
        _revealT = 1f;

        // 2) Charge up to FULL, then let the normal lerp settle onto the boss's
        //    real health. Bosses usually take a hit or two during the ~1.5s of
        //    slide-in and charge-up, and stopping the charge at that lower value
        //    reads as "the bar never filled" rather than "the boss is hurt".
        //    Filling to full first makes that damage legible as damage.
        float chargeTo = fillUpToFull ? 1f : _target;
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, fillUpDuration);
            float e = EaseOutCubic(Mathf.Clamp01(t));
            _display = chargeTo * e;
            _shadow = _display;
            ApplyFill(_display);
            ApplyShadow(_shadow);
            yield return null;
        }

        _display = chargeTo;
        _shadow = chargeTo;

        // Hold at full for a beat. Without this, a boss that lost a few percent
        // during the intro drops to its real value almost the same frame the
        // charge finishes, so the bar looks like it simply never filled.
        if (fillUpHoldTime > 0f)
            yield return new WaitForSecondsRealtime(fillUpHoldTime);

        _fillingUp = false;
        _revealing = false;

        // Hold the chip at full for a beat so the drop to real health reads as a hit.
        if (_display > _target + 0.001f) _shadowTimer = shadowDelay;

        if (_glow != null) _glow.Burst(8f, 0.6f);
    }

    /// Immediate, no animation - for a bar created mid-fight.
    public void RevealInstant()
    {
        _revealT = 1f;
        _revealing = false;
        _fillingUp = false;
        _display = _shadow = _target;
    }

    /// Quiet exit for cases that aren't a kill (scene teardown, boss despawn).
    public void FadeOutAndDestroy(float duration = 0.25f)
    {
        if (_dying) return;
        _dying = true;
        StartCoroutine(FadeOutRoutine(duration));
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        float t = 0f;
        float start = _revealT;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            _revealT = Mathf.Lerp(start, 0f, t);
            yield return null;
        }
        Destroy(gameObject);
    }

    /// Drain what's left, flash, then blow the bar apart. Safe to call twice.
    public void PlayDeath()
    {
        if (_dying) return;
        _dying = true;
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        _fillingUp = false;
        _target = 0f;
        _shadowTimer = 0f;

        if (_glow != null) _glow.Burst(20f, 1f);
        AddShake(armorBreakShake * 1.4f);

        float t = 0f;
        while (t < deathDrainTime && _display > 0.005f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _display = 0f;
        _shadow = 0f;
        ApplyFill(0f);
        ApplyShadow(0f);

        if (_glow != null) { _glow.Burst(24f, 1f); _glow.Master = 0f; }

        BossBarDisintegrator.Play(Rect, disintegration, null);

        // Hide the real bar the same frame the shards appear, so the shards read
        // as the bar itself coming apart.
        if (_cg != null) _cg.alpha = 0f;
        foreach (var img in GetComponentsInChildren<Image>(true)) img.enabled = false;

        yield return new WaitForSecondsRealtime(
            disintegration.duration * (1f + disintegration.lifeVariance) + 0.3f);
        Destroy(gameObject);
    }


    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (!_dying) TrackBoss();

        if (!_fillingUp)
        {
            _display = Mathf.Lerp(_display, _target, dt * fillLerpSpeed);
            if (Mathf.Abs(_display - _target) < 0.0005f) _display = _target;
            ApplyFill(_display);

            if (_shadowTimer > 0f) _shadowTimer -= dt;
            else
            {
                _shadow = Mathf.Lerp(_shadow, _target, dt * shadowLerpSpeed);
                if (Mathf.Abs(_shadow - _target) < 0.0005f) _shadow = _target;
            }
            ApplyShadow(_shadow);
        }

        float eased = EaseOutBack(_revealT);
        _revealAlpha = Mathf.Clamp01(_revealT * 1.4f);

        _shakeOffset = Vector2.Lerp(_shakeOffset, Vector2.zero, dt * shakeDamping);

        if (_rt != null)
        {
            _rt.anchoredPosition = _basePos
                                 + new Vector2(0f, (1f - eased) * revealDropDistance)
                                 + _shakeOffset;
            float s = Mathf.Lerp(0.94f, 1f, eased);
            _rt.localScale = new Vector3(s, s, 1f);
        }

        if (_cg != null) _cg.alpha = _suppressed ? 0f : _revealAlpha;
        if (_glow != null && !_dying) _glow.Master = _suppressed ? 0f : _revealAlpha;

        if (debugLog && !_dying)
        {
            _debugTimer -= dt;
            if (_debugTimer <= 0f)
            {
                _debugTimer = 1f;
                Debug.Log(string.Format(
                    "[BossHealthBarUI] target={0:F3} display={1:F3} fillAmount={2:F3} pool={3:F0}/{4:F0} armor={5}",
                    _target, _display,
                    fillImage != null ? fillImage.fillAmount : -1f,
                    TotalCurrent(), _maxPool,
                    _boss != null && !_boss.IsArmorDestroyed ? "up" : "broken"));
            }
        }
    }

    private void TrackBoss()
    {
        // Boss destroyed without ever reaching 0 HP (scene change, despawn):
        // leave quietly instead of playing the kill effect.
        if (_boss == null)
        {
            FadeOutAndDestroy();
            return;
        }

        float max = Mathf.Max(1f, TotalMax());
        if (!Mathf.Approximately(max, _maxPool)) _maxPool = max;   // stage / difficulty rescale

        float newTarget = Mathf.Clamp01(Mathf.Max(0f, TotalCurrent()) / _maxPool);

        if (!_fillingUp && newTarget < _target - 0.0001f)
        {
            // Took a hit: hold the chip where the bar was, then let it drain.
            _shadow = Mathf.Max(_shadow, _display);
            _shadowTimer = shadowDelay;
            AddShake(hitShakeAmount * Mathf.Clamp01((_target - newTarget) * 6f));
        }
        _target = newTarget;

        // Armour just broke - big moment.
        bool armorUp = !_boss.IsArmorDestroyed;
        if (_armorWasUp && !armorUp)
        {
            AddShake(armorBreakShake);
            if (_glow != null) _glow.Burst(18f, 1f);
        }
        _armorWasUp = armorUp;
        UpdateTints(false);

        if (_boss.currentHealth > 0f) _sawAlive = true;
        else if (_sawAlive) PlayDeath();
    }

    private float TotalMax()
    {
        if (_boss == null) return _maxPool;
        return _includeArmor ? _boss.maxHealth + _boss.maxArmor : _boss.maxHealth;
    }

    private float TotalCurrent()
    {
        if (_boss == null) return 0f;
        float hp = Mathf.Max(0f, _boss.currentHealth);
        if (!_includeArmor) return hp;
        return hp + (_boss.IsArmorDestroyed ? 0f : Mathf.Max(0f, _boss.CurrentArmor));
    }

    private void UpdateTints(bool force)
    {
        bool armored = _boss != null && !_boss.IsArmorDestroyed;

        if (tintWhileArmored && fillImage != null)
        {
            // Multiply against the authored colour so the art stays intact
            // (healthFillColor = white => visually identical to the prefab).
            Color want = _authoredFillColor * (armored ? armoredFillColor : healthFillColor);
            fillImage.color = force ? want
                                    : Color.Lerp(fillImage.color, want, Time.unscaledDeltaTime * 4f);
        }

        if (_glow != null)
            _glow.SetColor(armored ? glowColor : glowColorNoArmor);
    }

    private void AddShake(float amount)
    {
        if (amount <= 0f) return;
        _shakeOffset += new Vector2(Random.Range(-amount, amount), Random.Range(-amount, amount) * 0.4f);
    }

    // Health 0..1 -> Image.fillAmount.
    // With both paddings at 0 (the default) this is a straight pass-through, so the
    // bar is a direct, procedural read-out of boss health: 73% health = fillAmount
    // 0.73, and 100% health = exactly 1.0.
    // Full health MUST land on 1.0. Clipping any amount off the right destroys the
    // fill sprite's authored end cap and turns it into a flat vertical edge, which
    // reads as "not full" even when the remaining fill still covers the frame's
    // inner window. Coverage is not the same thing as looking full.
    private float MapFill(float value)
    {
        value = Mathf.Clamp01(value);
        if (value <= 0.0001f) return 0f;

        float lo = Mathf.Clamp01(fillPaddingLeft);
        float hi = 1f - Mathf.Clamp01(fillPaddingRight);
        if (hi <= lo) return value;

        return Mathf.Lerp(lo, hi, value);
    }

    private void ApplyFill(float v) => Apply(fillImage, _fillMat, _fillUsesShader, MapFill(v));

    private void ApplyShadow(float v) => Apply(shadowImage, _shadowMat, _shadowUsesShader, MapFill(v));

    private static void Apply(Image img, Material mat, bool usesShader, float v)
    {
        if (img == null) return;
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


    private string DescribeWiring()
    {
        var sb = new StringBuilder("[BossHealthBarUI] ").Append(name).Append(" | fill=");
        sb.Append(fillImage != null
            ? fillImage.name + " (sprite " + (fillImage.sprite != null ? fillImage.sprite.name : "NONE") +
              ", type " + fillImage.type + ")"
            : "NULL");
        sb.Append(" | frame=");
        sb.Append(frameImage != null
            ? frameImage.name + " (sprite " + (frameImage.sprite != null ? frameImage.sprite.name : "NONE") + ")"
            : "NULL");
        sb.Append(" | shadow=").Append(shadowImage != null ? shadowImage.name : "none");
        sb.Append(" | glow=").Append(_glow != null ? "on" : "off");
        if (_boss != null) sb.Append(" | boss=").Append(_boss.name)
                             .Append(" pool=").Append(TotalCurrent().ToString("F0"))
                             .Append('/').Append(TotalMax().ToString("F0"));
        return sb.ToString();
    }

    private static string DescribeChildren(Image[] images)
    {
        var sb = new StringBuilder("Images found: ");
        for (int i = 0; i < images.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(images[i].name).Append('(').Append(images[i].type).Append(')');
        }
        return sb.ToString();
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private static float EaseOutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }

    private void OnDestroy()
    {
        if (_fillMat != null) Destroy(_fillMat);
        if (_shadowMat != null) Destroy(_shadowMat);
    }
}


