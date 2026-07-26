using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Disintegrates the boss bar apart when the boss dies.

public class BossBarDisintegrator : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shards")]
        [Tooltip("Horizontal cuts per source image. The bar is very wide, so this is high.")]
        [Range(2, 48)] public int columns = 20;
        [Range(1, 12)] public int rows = 3;
        [Tooltip("Total lifetime of the effect in seconds (unscaled).")]
        public float duration = 1.5f;
        [Tooltip("Extra random lifetime variation per shard, 0..1 of duration.")]
        [Range(0f, 0.8f)] public float lifeVariance = 0.35f;

        [Header("Shard physics (pixels/second)")]
        public float outwardSpeed = 300f;
        public float upwardSpeed = 210f;
        public float randomSpeed = 170f;
        public float gravity = -1700f;
        public float drag = 0.7f;
        public float spin = 300f;
        [Tooltip("How far into its life a shard starts fading. 0 = fades the whole time.")]
        [Range(0f, 0.9f)] public float fadeStart = 0.25f;

        [Header("Embers")]
        [Range(0, 200)] public int emberCount = 70;
        public Color emberColor = new Color(1f, 0.66f, 0.22f, 1f);
        public float emberSize = 22f;
        public float emberRise = 210f;
        public float emberSpread = 170f;

        [Header("Flash")]
        public bool whiteFlash = true;
        public float flashDuration = 0.22f;
        [Range(0f, 1f)] public float flashAlpha = 0.85f;
    }

    private struct Piece
    {
        public RectTransform rt;
        public Graphic gfx;
        public Vector2 vel;
        public float angVel;
        public float life;
        public float maxLife;
        public Color baseColor;
        public float startScale;
        public bool isEmber;
        public float flicker;
    }

    private readonly List<Piece> _pieces = new List<Piece>();
    private Settings _s;
    private Image _flash;
    private float _elapsed;
    private float _total;
    private System.Action _onComplete;
    private bool _finished;

    private static readonly Vector3[] _corners = new Vector3[4];


    public static BossBarDisintegrator Play(RectTransform bar, Settings settings, System.Action onComplete = null)
    {
        if (bar == null) { onComplete?.Invoke(); return null; }
        settings ??= new Settings();

        var parent = bar.parent as RectTransform;
        if (parent == null) { onComplete?.Invoke(); return null; }

        var go = new GameObject("BossBarDebris", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetSiblingIndex(bar.GetSiblingIndex());

        var d = go.AddComponent<BossBarDisintegrator>();
        d.Build(bar, rt, settings, onComplete);
        return d;
    }

    private void Build(RectTransform bar, RectTransform debris, Settings s, System.Action onComplete)
    {
        _s = s;
        _onComplete = onComplete;
        _total = s.duration * (1f + s.lifeVariance) + 0.1f;

        // PASS 1 — collect every piece of bar art and measure the real visual
        // bounds. (The prefab root's own rect is a small placeholder, so it can't
        // be used for the blast centre or the ember spread.)
        var sources = new List<Source>();
        Vector2 unionMin = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 unionMax = new Vector2(float.MinValue, float.MinValue);
        Rect flashRect = new Rect();
        float flashArea = 0f;

        var images = bar.GetComponentsInChildren<Image>(false);
        foreach (var img in images)
        {
            if (img == null || !img.isActiveAndEnabled) continue;
            if (img.sprite == null || img.sprite.texture == null) continue;
            if (img.color.a <= 0.01f) continue;
            // The frame glow is a live effect, not bar art — it must not shatter.
            if (img.GetComponentInParent<BossBarExcludeFromShatter>() != null) continue;

            // Sprites packed with rotation would need swapped UVs; skip rather
            // than draw garbage (they just won't contribute shards).
            if (img.sprite.packed && img.sprite.packingRotation != SpritePackingRotation.None) continue;

            img.rectTransform.GetWorldCorners(_corners);
            Vector2 bl = debris.InverseTransformPoint(_corners[0]);
            Vector2 tr = debris.InverseTransformPoint(_corners[2]);
            if (tr.x - bl.x < 1f || tr.y - bl.y < 1f) continue;

            float area = (tr.x - bl.x) * (tr.y - bl.y);
            if (area > flashArea)
            {
                flashArea = area;
                flashRect = new Rect(bl.x, bl.y, tr.x - bl.x, tr.y - bl.y);
            }

            ResolveVisibleRegion(img, out float fx0, out float fx1, out float fy0, out float fy1);
            if (fx1 - fx0 < 0.02f || fy1 - fy0 < 0.02f) continue;   // fully drained bar → nothing to shatter

            unionMin = Vector2.Min(unionMin, bl);
            unionMax = Vector2.Max(unionMax, tr);

            sources.Add(new Source
            {
                img = img,
                bl = bl,
                tr = tr,
                fx0 = fx0,
                fx1 = fx1,
                fy0 = fy0,
                fy1 = fy1
            });
        }

        if (sources.Count == 0)
        {
            // Nothing to shatter (bar already empty / hidden): finish immediately.
            _total = 0.01f;
            return;
        }

        // PASS 2 — shards blast outward from the centre of the real bar.
        Vector2 barCentre = (unionMin + unionMax) * 0.5f;
        foreach (var src in sources)
            SpawnShards(debris, src.img, src.bl, src.tr, barCentre, src.fx0, src.fx1, src.fy0, src.fy1);

        SpawnEmbers(debris, unionMin, unionMax);

        if (_s.whiteFlash && flashArea > 0f)
            CreateFlash(debris, flashRect);
    }

    private struct Source
    {
        public Image img;
        public Vector2 bl, tr;
        public float fx0, fx1, fy0, fy1;
    }

    // Which fraction of the image is actually drawn right now: handles both
    // Image.Type.Filled and ResourceBarUI-style "_Fill" shader materials, so a
    // half-empty bar shatters as a half-empty bar.
    private static void ResolveVisibleRegion(Image img, out float x0, out float x1, out float y0, out float y1)
    {
        x0 = 0f; x1 = 1f; y0 = 0f; y1 = 1f;

        float f = 1f;
        bool horizontal = true;
        bool fromStart = true;

        if (img.type == Image.Type.Filled)
        {
            f = Mathf.Clamp01(img.fillAmount);
            if (img.fillMethod == Image.FillMethod.Horizontal)
            {
                horizontal = true;
                fromStart = img.fillOrigin == (int)Image.OriginHorizontal.Left;
            }
            else if (img.fillMethod == Image.FillMethod.Vertical)
            {
                horizontal = false;
                fromStart = img.fillOrigin == (int)Image.OriginVertical.Bottom;
            }
            else return;   // radial fills: just shatter the whole quad
        }
        else if (img.material != null && img.material.HasProperty("_Fill"))
        {
            f = Mathf.Clamp01(img.material.GetFloat("_Fill"));
        }
        else return;

        if (horizontal) { if (fromStart) x1 = f; else x0 = 1f - f; }
        else { if (fromStart) y1 = f; else y0 = 1f - f; }
    }

    private void SpawnShards(RectTransform debris, Image src, Vector2 bl, Vector2 tr, Vector2 barCentre,
                             float fx0, float fx1, float fy0, float fy1)
    {
        Sprite sp = src.sprite;
        Texture tex = sp.texture;
        Rect texRect = sp.textureRect;
        float tw = tex.width, th = tex.height;

        int cols = Mathf.Max(1, _s.columns);
        int rows = Mathf.Max(1, _s.rows);
        Vector2 full = tr - bl;

        for (int c = 0; c < cols; c++)
        {
            float u0 = Mathf.Lerp(fx0, fx1, c / (float)cols);
            float u1 = Mathf.Lerp(fx0, fx1, (c + 1) / (float)cols);

            for (int r = 0; r < rows; r++)
            {
                float v0 = Mathf.Lerp(fy0, fy1, r / (float)rows);
                float v1 = Mathf.Lerp(fy0, fy1, (r + 1) / (float)rows);

                var go = new GameObject("Shard", typeof(RectTransform), typeof(RawImage));
                var rt = (RectTransform)go.transform;
                rt.SetParent(debris, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(full.x * (u1 - u0), full.y * (v1 - v0));

                Vector2 pos = new Vector2(
                    bl.x + full.x * (u0 + u1) * 0.5f,
                    bl.y + full.y * (v0 + v1) * 0.5f);
                rt.anchoredPosition = pos;

                var raw = go.GetComponent<RawImage>();
                raw.texture = tex;
                raw.uvRect = new Rect(
                    (texRect.x + u0 * texRect.width) / tw,
                    (texRect.y + v0 * texRect.height) / th,
                    (u1 - u0) * texRect.width / tw,
                    (v1 - v0) * texRect.height / th);
                raw.color = src.color;
                raw.raycastTarget = false;
                raw.maskable = false;

                Vector2 away = pos - barCentre;
                // Mostly horizontal separation (the bar is wide), always some lift.
                Vector2 dir = away.sqrMagnitude > 0.001f ? away.normalized : Random.insideUnitCircle.normalized;

                var p = new Piece
                {
                    rt = rt,
                    gfx = raw,
                    vel = dir * _s.outwardSpeed
                          + Vector2.up * _s.upwardSpeed * Random.Range(0.4f, 1.2f)
                          + Random.insideUnitCircle * _s.randomSpeed,
                    angVel = Random.Range(-_s.spin, _s.spin),
                    maxLife = _s.duration * Random.Range(1f - _s.lifeVariance, 1f + _s.lifeVariance),
                    baseColor = src.color,
                    startScale = 1f,
                    isEmber = false
                };
                _pieces.Add(p);
            }
        }
    }

    private void SpawnEmbers(RectTransform debris, Vector2 bl, Vector2 tr)
    {
        for (int i = 0; i < _s.emberCount; i++)
        {
            var go = new GameObject("Ember", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(debris, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            float size = _s.emberSize * Random.Range(0.35f, 1.2f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(
                Random.Range(bl.x, tr.x),
                Random.Range(bl.y, tr.y));

            var img = go.GetComponent<Image>();
            img.sprite = UIProceduralSprites.SoftDot;
            img.color = _s.emberColor;
            img.raycastTarget = false;
            img.maskable = false;

            _pieces.Add(new Piece
            {
                rt = rt,
                gfx = img,
                vel = Vector2.up * _s.emberRise * Random.Range(0.5f, 1.5f)
                      + Vector2.right * Random.Range(-_s.emberSpread, _s.emberSpread),
                angVel = 0f,
                maxLife = _s.duration * Random.Range(0.5f, 1.1f),
                baseColor = _s.emberColor,
                startScale = 1f,
                isEmber = true,
                flicker = Random.Range(0f, 10f)
            });
        }
    }

    private void CreateFlash(RectTransform debris, Rect area)
    {
        var go = new GameObject("Flash", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(debris, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(area.width * 1.06f, area.height * 1.6f);
        rt.anchoredPosition = new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f);

        _flash = go.GetComponent<Image>();
        _flash.sprite = UIProceduralSprites.SoftDot;
        _flash.color = new Color(1f, 1f, 1f, _s.flashAlpha);
        _flash.raycastTarget = false;
        _flash.maskable = false;
        rt.SetAsLastSibling();
    }

    private void Update()
    {
        if (_finished) return;

        float dt = Time.unscaledDeltaTime;
        _elapsed += dt;

        if (_flash != null)
        {
            float k = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _s.flashDuration));
            var c = _flash.color;
            c.a = _s.flashAlpha * (1f - k) * (1f - k);
            _flash.color = c;
            if (k >= 1f) { Destroy(_flash.gameObject); _flash = null; }
        }

        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (p.rt == null) continue;

            p.life += dt;
            float k = Mathf.Clamp01(p.life / Mathf.Max(0.01f, p.maxLife));

            // Embers float; shards fall.
            float g = p.isEmber ? _s.gravity * 0.06f : _s.gravity;
            p.vel += Vector2.up * g * dt;
            p.vel *= 1f - Mathf.Clamp01(_s.drag * dt);
            p.rt.anchoredPosition += p.vel * dt;

            if (!p.isEmber)
            {
                p.rt.localEulerAngles += new Vector3(0f, 0f, p.angVel * dt);
                float s = Mathf.Lerp(1f, 0.55f, k * k);
                p.rt.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                float s = Mathf.Lerp(1f, 0.1f, k);
                p.rt.localScale = new Vector3(s, s, 1f);
            }

            float fadeK = Mathf.InverseLerp(_s.fadeStart, 1f, k);
            float alpha = p.baseColor.a * (1f - fadeK);
            if (p.isEmber)
                alpha *= 0.6f + 0.4f * Mathf.Abs(Mathf.Sin((Time.unscaledTime + p.flicker) * 14f));

            var col = p.baseColor;
            col.a = alpha;
            if (p.gfx != null) p.gfx.color = col;

            _pieces[i] = p;
        }

        if (_elapsed >= _total)
        {
            _finished = true;
            _onComplete?.Invoke();
            Destroy(gameObject);
        }
    }
}

// Marker: any UI graphic under an object carrying this component is treated as
// a live effect rather than bar art, and is skipped by the shatter.
// BossBarFrameGlow adds it to its own container automatically.
public class BossBarExcludeFromShatter : MonoBehaviour { }
