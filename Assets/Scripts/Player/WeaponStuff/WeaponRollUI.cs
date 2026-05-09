using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WeaponRollUI : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 bottomLeftPadding = new Vector2(70f, 150f);
    public float circleSize = 153f;
    public float gapBetweenRolls = 16f;
    public float rollStep = 85f;
    public float shrinkPerStep = 0.80f;

    [Header("Colours")]
    public Color weaponActiveBg = new Color(0.18f, 0.54f, 0.44f, 1f);
    public Color toolActiveBg = new Color(0.44f, 0.28f, 0.58f, 1f);
    public Color inactiveBg = new Color(0.10f, 0.10f, 0.10f, 0.88f);
    public Color activeIcon = Color.white;
    public Color inactiveIcon = new Color(1f, 1f, 1f, 0.50f);
    public Color emptyToolBg = new Color(0.15f, 0.15f, 0.15f, 0.55f);

    [Header("Animation")]
    public float scrollSpeed = 0.16f;
    public float popSpeed = 0.30f;

    WeaponRollController _ctrl;
    Canvas _canvas;

    RectTransform _weaponRoot;
    readonly List<RectTransform> _weaponRects = new List<RectTransform>();
    readonly List<Image> _weaponBgs = new List<Image>();
    readonly List<Image> _weaponIcons = new List<Image>();

    RectTransform _toolRoot;
    readonly List<RectTransform> _toolRects = new List<RectTransform>();
    readonly List<Image> _toolBgs = new List<Image>();
    readonly List<Image> _toolIcons = new List<Image>();

    RectTransform _emptyToolRect;
    Image _emptyToolBg;

    Coroutine _weaponScrollCo;
    Coroutine _toolScrollCo;

    const int VisibleRadius = 2;

    void Start() => StartCoroutine(Init());

    IEnumerator Init()
    {
        yield return null;
        _ctrl = FindFirstObjectByType<WeaponRollController>();
        if (_ctrl == null) yield break;
        BuildCanvas();
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged += OnUnlocks;
        RebuildAll();
        SnapAll();
    }

    void OnDestroy()
    {
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged -= OnUnlocks;
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    void BuildCanvas()
    {
        var go = new GameObject("WeaponRoll_Canvas");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();

        // Weapon root: active icon at Y=0 (top of weapon roll), inactive go DOWN
        var wGo = new GameObject("WeaponRoot", typeof(RectTransform));
        wGo.transform.SetParent(_canvas.transform, false);
        _weaponRoot = wGo.GetComponent<RectTransform>();
        _weaponRoot.anchorMin = _weaponRoot.anchorMax = _weaponRoot.pivot = Vector2.zero;
        _weaponRoot.anchoredPosition = bottomLeftPadding;

        // Tool root: active icon at Y=0 (bottom of tool roll), inactive go UP
        var tGo = new GameObject("ToolRoot", typeof(RectTransform));
        tGo.transform.SetParent(_canvas.transform, false);
        _toolRoot = tGo.GetComponent<RectTransform>();
        _toolRoot.anchorMin = _toolRoot.anchorMax = _toolRoot.pivot = Vector2.zero;
        _toolRoot.anchoredPosition = new Vector2(
            bottomLeftPadding.x,
            bottomLeftPadding.y + circleSize + gapBetweenRolls
        );
    }

    //  REFRESH
    public void Refresh(int weaponIndex, int toolIndex, ScrollTarget scrollTarget)
    {
        if (_canvas == null || _ctrl == null) return;

        if (_weaponRects.Count != _ctrl.WeaponCount || _toolRects.Count != _ctrl.ToolCount)
            RebuildAll();

        UpdateWeaponVisuals();
        UpdateToolVisuals();

        if (scrollTarget == ScrollTarget.Weapon)
        {
            StartWeaponScrollAnim();
            SnapToolPositions();
        }
        else if (scrollTarget == ScrollTarget.Tool)
        {
            SnapWeaponPositions();
            StartToolScrollAnim();
        }
        else
        {
            SnapAll();
        }
    }

    public void Refresh(int newIndex, bool animate)
    {
        int toolIdx = _ctrl != null ? _ctrl.CurrentToolIndex : 0;
        Refresh(newIndex, toolIdx, animate ? ScrollTarget.Weapon : ScrollTarget.None);
    }

    void OnUnlocks()
    {
        if (_canvas == null) return;
        int bW = _weaponRects.Count, bT = _toolRects.Count;
        RebuildAll();
        SnapAll();
        for (int i = bW; i < _weaponRects.Count; i++) StartCoroutine(PopRect(_weaponRects[i]));
        for (int i = bT; i < _toolRects.Count; i++) StartCoroutine(PopRect(_toolRects[i]));
    }

    //  BUILD SLOTS
    void RebuildAll()
    {
        foreach (var rt in _weaponRects) if (rt) Destroy(rt.gameObject);
        _weaponRects.Clear(); _weaponBgs.Clear(); _weaponIcons.Clear();
        foreach (var rt in _toolRects) if (rt) Destroy(rt.gameObject);
        _toolRects.Clear(); _toolBgs.Clear(); _toolIcons.Clear();
        if (_emptyToolRect != null) { Destroy(_emptyToolRect.gameObject); _emptyToolRect = null; }
        if (_ctrl == null) return;

        for (int i = 0; i < _ctrl.WeaponCount; i++)
            MakeSlot(_weaponRoot, $"W{i}", LoadIconForData(_ctrl.WeaponDataAt(i)),
                _weaponRects, _weaponBgs, _weaponIcons);

        if (_ctrl.ToolCount > 0)
        {
            for (int i = 0; i < _ctrl.ToolCount; i++)
                MakeSlot(_toolRoot, $"T{i}", LoadIconForData(_ctrl.ToolDataAt(i)),
                    _toolRects, _toolBgs, _toolIcons);
        }
        else
            BuildEmptyToolPlaceholder();
    }

    void MakeSlot(RectTransform parent, string name, Sprite iconSprite,
        List<RectTransform> rects, List<Image> bgs, List<Image> icons)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = Vector2.one * circleSize;
        rects.Add(rt);

        var bg = MakeImg(go, "BG", CircleSprite());
        bg.color = inactiveBg;
        bg.rectTransform.sizeDelta = Vector2.one * circleSize;
        bgs.Add(bg);

        var ic = MakeImg(go, "Icon", iconSprite);
        ic.preserveAspect = true;
        ic.rectTransform.sizeDelta = Vector2.one * (circleSize * 0.58f);
        icons.Add(ic);
    }

    void BuildEmptyToolPlaceholder()
    {
        float sz = circleSize * 0.7f;
        var go = new GameObject("EmptyTool", typeof(RectTransform));
        go.transform.SetParent(_toolRoot, false);
        _emptyToolRect = go.GetComponent<RectTransform>();
        _emptyToolRect.anchorMin = _emptyToolRect.anchorMax = _emptyToolRect.pivot = new Vector2(0.5f, 0f);
        _emptyToolRect.sizeDelta = Vector2.one * sz;
        _emptyToolRect.anchoredPosition = Vector2.zero;
        _emptyToolBg = MakeImg(go, "BG", CircleSprite());
        _emptyToolBg.color = emptyToolBg;
        _emptyToolBg.rectTransform.sizeDelta = Vector2.one * sz;
    }

    Image MakeImg(GameObject parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent.transform, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        return img;
    }

    //  RANK COMPUTATION
    int[] WeaponRanks(int sel, int n)
    {
        int[] rank = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = i - sel;
            if (d < 0) d += n;
            rank[i] = d;
        }
        return rank;
    }

    int[] ToolRanks(int sel, int n)
    {
        int[] rank = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = sel - i;
            if (d < 0) d += n;
            rank[i] = d;
        }
        return rank;
    }

    float WeaponYForRank(int rank) => -rank * rollStep;
    float ToolYForRank(int rank) => rank * rollStep;

    //  WEAPON VISUALS
    void UpdateWeaponVisuals()
    {
        int n = _weaponRects.Count, sel = _ctrl.CurrentWeaponIndex;
        int[] ranks = WeaponRanks(sel, n);

        for (int i = 0; i < n; i++)
        {
            int r = ranks[i];
            bool on = r == 0;
            bool visible = r <= VisibleRadius;
            float sz = visible ? circleSize * Mathf.Pow(shrinkPerStep, r) : 0f;
            float alpha = visible ? (on ? 1f : Mathf.Pow(shrinkPerStep, r)) : 0f;

            Color bg = on ? weaponActiveBg : inactiveBg;
            Color ico = on ? activeIcon : inactiveIcon;
            bg.a = alpha; ico.a = alpha;

            _weaponBgs[i].color = bg;
            _weaponIcons[i].color = ico;
            _weaponBgs[i].rectTransform.sizeDelta = Vector2.one * sz;
            _weaponIcons[i].rectTransform.sizeDelta = Vector2.one * (sz * 0.58f);
            _weaponRects[i].SetSiblingIndex(Mathf.Max(0, n - 1 - r));
            _weaponRects[i].gameObject.SetActive(visible);
        }
    }

    //  TOOL VISUALS
    void UpdateToolVisuals()
    {
        int n = _toolRects.Count, sel = _ctrl.CurrentToolIndex;
        int[] ranks = ToolRanks(sel, n);

        for (int i = 0; i < n; i++)
        {
            int r = ranks[i];
            bool on = r == 0;
            bool visible = r <= VisibleRadius;
            float sz = visible ? circleSize * Mathf.Pow(shrinkPerStep, r) : 0f;
            float alpha = visible ? (on ? 1f : Mathf.Pow(shrinkPerStep, r)) : 0f;

            Color bg = on ? toolActiveBg : inactiveBg;
            Color ico = on ? activeIcon : inactiveIcon;
            bg.a = alpha; ico.a = alpha;

            _toolBgs[i].color = bg;
            _toolIcons[i].color = ico;
            _toolBgs[i].rectTransform.sizeDelta = Vector2.one * sz;
            _toolIcons[i].rectTransform.sizeDelta = Vector2.one * (sz * 0.58f);
            _toolRects[i].SetSiblingIndex(Mathf.Max(0, n - 1 - r));
            _toolRects[i].gameObject.SetActive(visible);
        }
    }

    //  SNAP POSITIONS
    void SnapWeaponPositions()
    {
        int n = _weaponRects.Count, sel = _ctrl.CurrentWeaponIndex;
        int[] ranks = WeaponRanks(sel, n);
        for (int i = 0; i < n; i++)
            _weaponRects[i].anchoredPosition = new Vector2(0f, WeaponYForRank(ranks[i]));
    }

    void SnapToolPositions()
    {
        int n = _toolRects.Count, sel = _ctrl.CurrentToolIndex;
        int[] ranks = ToolRanks(sel, n);
        for (int i = 0; i < n; i++)
            _toolRects[i].anchoredPosition = new Vector2(0f, ToolYForRank(ranks[i]));
    }

    void SnapAll()
    {
        UpdateWeaponVisuals();
        SnapWeaponPositions();
        UpdateToolVisuals();
        SnapToolPositions();
    }

    //  SCROLL ANIMATIONS
    void StartWeaponScrollAnim()
    {
        if (_weaponScrollCo != null) StopCoroutine(_weaponScrollCo);
        _weaponScrollCo = StartCoroutine(AnimateWeaponScroll());
    }

    void StartToolScrollAnim()
    {
        if (_toolScrollCo != null) StopCoroutine(_toolScrollCo);
        _toolScrollCo = StartCoroutine(AnimateToolScroll());
    }

    IEnumerator AnimateWeaponScroll()
    {
        int n = _weaponRects.Count, sel = _ctrl.CurrentWeaponIndex;
        if (n == 0) yield break;

        int[] ranks = WeaponRanks(sel, n);
        float[] from = new float[n], to = new float[n];
        for (int i = 0; i < n; i++)
        {
            from[i] = _weaponRects[i].anchoredPosition.y;
            to[i] = WeaponYForRank(ranks[i]);
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / scrollSpeed;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            for (int i = 0; i < n; i++)
                _weaponRects[i].anchoredPosition = new Vector2(0f, Mathf.Lerp(from[i], to[i], e));
            yield return null;
        }
        SnapWeaponPositions();
    }

    IEnumerator AnimateToolScroll()
    {
        int n = _toolRects.Count, sel = _ctrl.CurrentToolIndex;
        if (n == 0) yield break;

        int[] ranks = ToolRanks(sel, n);
        float[] from = new float[n], to = new float[n];
        for (int i = 0; i < n; i++)
        {
            from[i] = _toolRects[i].anchoredPosition.y;
            to[i] = ToolYForRank(ranks[i]);
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / scrollSpeed;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            for (int i = 0; i < n; i++)
                _toolRects[i].anchoredPosition = new Vector2(0f, Mathf.Lerp(from[i], to[i], e));
            yield return null;
        }
        SnapToolPositions();
    }

    //  POP
    IEnumerator PopRect(RectTransform rt)
    {
        if (rt == null) yield break;
        rt.localScale = Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / popSpeed;
            rt.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t));
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    //  HELPERS
    Sprite LoadIconForData(WeaponData wd)
    {
        if (wd == null) return null;
        string path;
        if (wd.isGrapplingHook) path = "Icons/WeaponIconGrapplingHook";
        else if (wd.isObstacleDrawer) path = "Icons/WeaponIconObstacleDrawer";
        else if (wd.isFlamethrower) path = "Icons/WeaponIconFlamethrower";
        else if (wd.isBombLauncher) path = "Icons/WeaponIconBomb";
        else if (wd.isTrap) path = "Icons/WeaponIconTrap";
        else if (wd.isTurret) path = "Icons/WeaponIconTurret";
        else if (wd.isDecoy) path = "Icons/WeaponIconDecoy";
        else if (wd.armorBonus > 0f) path = "Icons/WeaponIconShield";
        else if (wd.isBoomerang) path = "Icons/WeaponIconBoomerang";
        else if (wd.isRanged) path = "Icons/WeaponIconRanged";
        else path = "Icons/WeaponIconMelee";
        return Resources.Load<Sprite>(path)
            ?? Resources.Load<Sprite>($"Icons/WeaponIcon{wd.weaponName.Replace(" ", "")}");
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    static Sprite _cachedCircle;
    static Sprite CircleSprite()
    {
        if (_cachedCircle != null) return _cachedCircle;
        const int S = 512;
        // Third arg `mipChain: true` enables mipmaps.
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, mipChain: true, linear: false)
        {
            filterMode = FilterMode.Trilinear,   // Trilinear blends between mip levels — smoothest
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 4                        // Aniso filtering now active because mipmaps exist
        };
        var px = new Color[S * S];
        float cx = S * 0.5f;
        float cy = S * 0.5f;
        float r = S * 0.5f - 2f;
        const float aa = 1.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01((r - d) / aa);
                float alpha = t * t * (3f - 2f * t);
                px[y * S + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(px);
        // First arg `updateMipmaps: true` regenerates the mip chain from the new pixel data.
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        _cachedCircle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _cachedCircle;
    }

}
