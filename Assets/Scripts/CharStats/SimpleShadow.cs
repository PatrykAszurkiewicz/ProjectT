using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

//  SIMPLE SHADOW
[DisallowMultipleComponent]
[DefaultExecutionOrder(30000)] // after scripts that animate / sort / scale the sprite
public class SimpleShadow : MonoBehaviour
{
    public enum GroundLineMode { AutoFromSprite, Manual }

    [Header("Source")]
    [Tooltip("Empty = the SpriteRenderer on this object (or first one in children).")]
    [SerializeField] private SpriteRenderer targetSprite;

    [Header("Ground line")]
    [Tooltip("AutoFromSprite = lowest point of the first frame shown. Manual = Manual Ground Y.")]
    [SerializeField] private GroundLineMode groundLine = GroundLineMode.AutoFromSprite;
    [SerializeField] private float manualGroundY = -0.5f;
    [Tooltip("Nudge the ground line up (+) / down (-), local units — for transparent padding under the feet.")]
    [SerializeField] private float groundAdjust = 0f;

    [Header("Sun")]
    [Tooltip("Direction the shadow is cast on the ground, degrees. 0 = right, 270 = down. " +
             "Avoid ~30°–150° (upward): the body covers the shadow.")]
    [Range(0f, 360f)][SerializeField] private float castDirection = 320f;
    [Tooltip("Shadow length relative to object height. 0 = flat, 1 = as long as the object is tall.")]
    [Range(0f, 1.5f)][SerializeField] private float length = 0.5f;

    [Header("Silhouette look")]
    [SerializeField] private Color color = Color.black;
    [Range(0f, 1f)][SerializeField] private float opacity = 0.4f;
    [Range(0f, 1f)][SerializeField] private float fadeTowardTip = 0.6f;
    [Tooltip("Edge softness in world units (silhouette drawn several times, offset).")]
    [Range(0f, 0.3f)][SerializeField] private float blur = 0.04f;
    [Range(1, 6)][SerializeField] private int blurSamples = 4;

    [Header("Contact ellipse")]
    [SerializeField] private bool contactShadow = true;
    [Range(0f, 1f)][SerializeField] private float contactOpacity = 0.35f;
    [Tooltip("Ellipse width as a fraction of the sprite's width.")]
    [Range(0.1f, 1.5f)][SerializeField] private float contactWidth = 0.75f;
    [Range(0.1f, 1f)][SerializeField] private float contactAspect = 0.35f;

    [Header("Sorting & visibility")]
    [Tooltip("Order relative to the sprite. -1 = just behind it.")]
    [SerializeField] private int sortingOrderOffset = -1;
    [SerializeField] private bool followSpriteVisibility = true;

    [Header("Troubleshooting")]
    [Tooltip("Log what the shadow is doing (once per second).")]
    [SerializeField] private bool debugLog = false;
    [Tooltip("Draw the shadow above everything, to check it renders at all.")]
    [SerializeField] private bool debugDrawOnTop = false;

    // ── runtime 
    private Transform _root;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private SpriteRenderer _contact;
    private MaterialPropertyBlock _mpb;
    private bool _failed;

    private readonly List<Vector3> _verts = new List<Vector3>();
    private readonly List<Vector2> _uvs = new List<Vector2>();
    private readonly List<Color32> _cols = new List<Color32>();
    private readonly List<int> _tris = new List<int>();

    private bool _groundLocked;
    private float _lockedGroundY;

    private Sprite _builtSprite;
    private Texture _builtTexture;
    private float _builtSignX, _builtSX, _builtSY, _builtAlpha = -1f;
    private bool _dirty = true;

    private float _hiddenSince = -1f;
    private bool _warnedHidden;
    private float _nextDebugLog;

    private static Material _material;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    // ── lifecycle 
    private void OnValidate() { _dirty = true; }

    private void OnDisable()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root.gameObject);
        if (_mesh != null) Destroy(_mesh);
    }

    private void LateUpdate()
    {
        if (_failed) return;

        if (targetSprite == null)
        {
            targetSprite = GetComponent<SpriteRenderer>();
            if (targetSprite == null) targetSprite = GetComponentInChildren<SpriteRenderer>();
            if (targetSprite == null) { Hidden("no SpriteRenderer found on this object or its children"); return; }
        }
        if (_root == null && !CreateObjects()) return;

        // ── visibility ──
        Sprite sp = targetSprite.sprite;
        if (sp == null) { Hidden("target SpriteRenderer has no sprite"); return; }

        float spriteAlpha = 1f;
        if (followSpriteVisibility)
        {
            if (!targetSprite.enabled) { Hidden("target SpriteRenderer is disabled (untick Follow Sprite Visibility if another renderer draws the character)"); return; }
            if (!targetSprite.gameObject.activeInHierarchy) { Hidden("target object is inactive"); return; }
            spriteAlpha = targetSprite.color.a;
            if (spriteAlpha <= 0.001f) { Hidden("target sprite colour alpha is 0"); return; }
        }
        _hiddenSince = -1f;
        if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

        // ── scale & facing (rotation deliberately ignored) ──
        Vector3 ls = targetSprite.transform.lossyScale;
        float sx = Mathf.Abs(ls.x), sy = Mathf.Abs(ls.y);
        float signX = (ls.x < 0f ? -1f : 1f) * (targetSprite.flipX ? -1f : 1f);

        if (!_groundLocked && groundLine == GroundLineMode.AutoFromSprite)
        {
            _lockedGroundY = GetData(sp).minY;
            _groundLocked = true;
            _dirty = true;
        }

        float alphaKey = Mathf.Round(spriteAlpha * 50f) / 50f;
        if (_dirty || sp != _builtSprite || signX != _builtSignX || alphaKey != _builtAlpha ||
            Mathf.Abs(sx - _builtSX) > 0.001f || Mathf.Abs(sy - _builtSY) > 0.001f)
        {
            Rebuild(sp, signX, sx, sy, alphaKey);
        }

        // ── follow & sort ──
        _root.SetPositionAndRotation(targetSprite.transform.position, Quaternion.identity);

        int layer = targetSprite.sortingLayerID;
        int order = debugDrawOnTop ? 32000 : targetSprite.sortingOrder + sortingOrderOffset;
        _meshRenderer.sortingLayerID = layer;
        _meshRenderer.sortingOrder = order;

        _contact.enabled = contactShadow;
        _contact.sortingLayerID = layer;
        _contact.sortingOrder = order;
        Color cc = color;
        cc.a = contactOpacity * spriteAlpha;
        _contact.color = cc;

        if (debugLog && Time.unscaledTime >= _nextDebugLog)
        {
            _nextDebugLog = Time.unscaledTime + 1f;
            SpriteData d = GetData(sp);
            Debug.Log($"[SimpleShadow] {name}: sprite='{sp.name}' verts={d.verts.Length} " +
                      $"spriteLocalY[{d.minY:F2}..{d.maxY:F2}] groundY={GroundY:F2} scale=({sx:F2},{sy:F2}) " +
                      $"shadowVerts={_verts.Count} meshBounds={_meshRenderer.bounds} " +
                      $"layer={SortingLayer.IDToName(layer)} order={order} (sprite order {targetSprite.sortingOrder}) " +
                      $"shader={_material.shader.name}", this);
        }
    }

    private void Hidden(string reason)
    {
        if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);

        if (_hiddenSince < 0f) _hiddenSince = Time.unscaledTime;
        bool warn = (!_warnedHidden && Time.unscaledTime - _hiddenSince > 2f) ||
                    (debugLog && Time.unscaledTime >= _nextDebugLog);
        if (warn)
        {
            _warnedHidden = true;
            _nextDebugLog = Time.unscaledTime + 1f;
            Debug.LogWarning($"[SimpleShadow] {name}: shadow hidden — {reason}.", this);
        }
    }

    // ── geometry 
    private float GroundY =>
        (groundLine == GroundLineMode.Manual ? manualGroundY : _lockedGroundY) + groundAdjust;

    private void Rebuild(Sprite sp, float signX, float sx, float sy, float spriteAlpha)
    {
        _dirty = false;
        _builtSprite = sp;
        _builtSignX = signX;
        _builtSX = sx;
        _builtSY = sy;
        _builtAlpha = spriteAlpha;

        SpriteData d = GetData(sp);
        float g = GroundY;
        float height = Mathf.Max(0.0001f, d.maxY - g);

        float rad = castDirection * Mathf.Deg2Rad;
        Vector2 cast = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * length;

        int n = Mathf.Max(1, blurSamples);
        // Per-copy alpha so n overlapping copies add up to roughly `opacity`.
        float target = Mathf.Clamp01(opacity * spriteAlpha);
        float perCopy = 1f - Mathf.Pow(1f - target, 1f / n);

        // Colour is baked into the vertices, so any sprite shader that multiplies
        // texture by vertex colour renders it correctly — no material properties needed.
        byte r = (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
        byte gCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);

        _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();

        for (int k = 0; k < n; k++)
        {
            Vector2 jitter = Vector2.zero;
            if (n > 1 && blur > 0f)
            {
                float a = (k / (float)n) * Mathf.PI * 2f;
                jitter = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * blur;
            }

            int baseIndex = _verts.Count;
            for (int i = 0; i < d.verts.Length; i++)
            {
                Vector2 v = d.verts[i];
                float h = v.y - g;                  // height above the ground line
                float up = Mathf.Max(0f, h);
                float below = Mathf.Min(0f, h);     // padding under the feet: keep it tight

                float x = v.x * signX * sx + cast.x * up * sy + jitter.x;
                float y = g * sy + cast.y * up * sy + below * sy * 0.3f + jitter.y;
                _verts.Add(new Vector3(x, y, 0f));
                _uvs.Add(d.uvs[i]);

                float fade = 1f - fadeTowardTip * Mathf.Clamp01(up / height);
                _cols.Add(new Color32(r, gCol, b, (byte)Mathf.RoundToInt(255f * perCopy * fade)));
            }
            for (int t = 0; t < d.tris.Length; t++) _tris.Add(baseIndex + d.tris[t]);
        }

        _mesh.Clear();
        _mesh.SetVertices(_verts);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetColors(_cols);
        _mesh.SetTriangles(_tris, 0);
        _mesh.RecalculateBounds();

        if (sp.texture != _builtTexture)
        {
            _builtTexture = sp.texture;
            _mpb.SetTexture(MainTexId, sp.texture);
            _meshRenderer.SetPropertyBlock(_mpb);
        }

        float w = (d.maxX - d.minX) * sx * contactWidth;
        _contact.transform.localPosition = new Vector3(d.centerX * signX * sx, g * sy, 0f);
        _contact.transform.localScale = new Vector3(w, w * contactAspect, 1f);
    }

    private bool CreateObjects()
    {
        if (_material == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null)
            {
                _failed = true;
                Debug.LogError("[SimpleShadow] No sprite shader found ('Sprites/Default' or URP " +
                               "'Sprite-Unlit-Default'). Add one to Project Settings > Graphics > " +
                               "Always Included Shaders.", this);
                return false;
            }
            _material = new Material(sh) { name = "SimpleShadow" };
        }

        var go = new GameObject(name + " Shadow");
        if (gameObject.scene.name == "DontDestroyOnLoad") DontDestroyOnLoad(go);
        else if (gameObject.scene.IsValid() && go.scene != gameObject.scene)
            SceneManager.MoveGameObjectToScene(go, gameObject.scene);

        _mesh = new Mesh { name = "SimpleShadowMesh" };
        _mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _meshRenderer = go.AddComponent<MeshRenderer>();
        _meshRenderer.sharedMaterial = _material;
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _mpb = new MaterialPropertyBlock();

        var contactGo = new GameObject("Contact");
        contactGo.transform.SetParent(go.transform, false);
        _contact = contactGo.AddComponent<SpriteRenderer>();
        _contact.sprite = GetSoftDisc();

        _root = go.transform;
        _dirty = true;
        return true;
    }

    // ── sprite mesh cache (sprite.vertices etc. allocate on every access) ────
    private class SpriteData
    {
        public Vector2[] verts;
        public Vector2[] uvs;
        public ushort[] tris;
        public float minX, maxX, minY, maxY, centerX;
    }

    private static readonly Dictionary<Sprite, SpriteData> _cache = new Dictionary<Sprite, SpriteData>();

    private static SpriteData GetData(Sprite sp)
    {
        if (_cache.TryGetValue(sp, out SpriteData d)) return d;
        if (_cache.Count > 4096) _cache.Clear();

        d = new SpriteData { verts = sp.vertices, uvs = sp.uv, tris = sp.triangles };
        d.minX = d.minY = float.MaxValue;
        d.maxX = d.maxY = float.MinValue;
        foreach (var v in d.verts)
        {
            if (v.x < d.minX) d.minX = v.x;
            if (v.x > d.maxX) d.maxX = v.x;
            if (v.y < d.minY) d.minY = v.y;
            if (v.y > d.maxY) d.maxY = v.y;
        }
        d.centerX = (d.minX + d.maxX) * 0.5f;
        _cache[sp] = d;
        return d;
    }

    // ── soft disc for the contact ellipse 
    private static Sprite _disc;

    private static Sprite GetSoftDisc()
    {
        if (_disc != null) return _disc;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SimpleShadowDisc" };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = r >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, r);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _disc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _disc;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _cache.Clear();
        _disc = null;
        _material = null;
    }

    // ── editor helpers 
    [ContextMenu("Re-detect ground line")]
    private void RedetectGround()
    {
        _groundLocked = false;
        _dirty = true;
    }

    private void OnDrawGizmosSelected()
    {
        var sr = targetSprite != null ? targetSprite : GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        Vector3 ls = sr.transform.lossyScale;
        float sx = Mathf.Abs(ls.x), sy = Mathf.Abs(ls.y);
        Bounds b = sr.sprite.bounds;

        float g = (groundLine == GroundLineMode.Manual
                      ? manualGroundY
                      : (Application.isPlaying && _groundLocked ? _lockedGroundY : b.min.y))
                  + groundAdjust;

        Vector3 p = sr.transform.position;
        Vector3 left = p + new Vector3(b.min.x * sx, g * sy, 0f);
        Vector3 right = p + new Vector3(b.max.x * sx, g * sy, 0f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(left, right);

        float rad = castDirection * Mathf.Deg2Rad;
        Vector3 mid = (left + right) * 0.5f;
        float h = (b.max.y - g) * sy * length;
        Vector3 tip = mid + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * h;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(mid, tip);
        Gizmos.DrawWireSphere(tip, 0.05f);
    }
}



