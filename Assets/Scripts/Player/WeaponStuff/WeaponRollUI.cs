using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WeaponRollUI : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 bottomLeftPadding = new Vector2(70f, 70f);
    public float circleSize = 153f;
    public float stepUp = 85f;
    public float shrinkPerStep = 0.80f;

    [Header("Colours")]
    public Color activeBg = new Color(0.18f, 0.54f, 0.44f, 1f);
    public Color inactiveBg = new Color(0.10f, 0.10f, 0.10f, 0.88f);
    public Color activeIcon = Color.white;
    public Color inactiveIcon = new Color(1f, 1f, 1f, 0.50f);

    [Header("Animation")]
    public float scrollSpeed = 0.16f;
    public float popSpeed = 0.30f;

    WeaponRollController _ctrl;
    Canvas _canvas;
    RectTransform _root;

    readonly List<RectTransform> _rects = new List<RectTransform>();
    readonly List<Image> _bgs = new List<Image>();
    readonly List<Image> _icons = new List<Image>();

    Coroutine _scrollCo;

    void Start()
    {
        StartCoroutine(Init());
    }

    IEnumerator Init()
    {
        yield return null;

        _ctrl = FindFirstObjectByType<WeaponRollController>();
        if (_ctrl == null) { Debug.LogWarning("[WeaponRollUI] WeaponRollController not found."); yield break; }

        BuildCanvas();

        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged += OnUnlocks;

        RebuildSlots();
        SnapLayout();
    }

    void OnDestroy()
    {
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged -= OnUnlocks;
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }

    public void Refresh(int newIndex, bool animate)
    {
        if (_canvas == null) return;
        if (_ctrl != null && _rects.Count != _ctrl.ActiveCount)
            RebuildSlots();
        UpdateVisuals();
        if (animate) StartScrollAnim();
        else SnapLayout();
    }

    void BuildCanvas()
    {
        var go = new GameObject("WeaponRoll_Canvas");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();

        var rootGo = new GameObject("SlotRoot", typeof(RectTransform));
        rootGo.transform.SetParent(_canvas.transform, false);
        _root = rootGo.GetComponent<RectTransform>();
        _root.anchorMin = _root.anchorMax = _root.pivot = Vector2.zero;
        _root.anchoredPosition = bottomLeftPadding;
    }

    void OnUnlocks()
    {
        if (_canvas == null) return;
        int before = _rects.Count;
        RebuildSlots();
        SnapLayout();
        for (int i = before; i < _rects.Count; i++)
            StartCoroutine(PopSlot(i));
    }

    void RebuildSlots()
    {
        foreach (var rt in _rects) if (rt) Destroy(rt.gameObject);
        _rects.Clear(); _bgs.Clear(); _icons.Clear();
        if (_ctrl == null) return;
        for (int i = 0; i < _ctrl.ActiveCount; i++)
            MakeSlot(i);
    }

    void MakeSlot(int i)
    {
        var go = new GameObject($"Slot{i}", typeof(RectTransform));
        go.transform.SetParent(_root, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = Vector2.one * circleSize;
        _rects.Add(rt);

        var bg = MakeImg(go, "BG", CircleSprite());
        bg.color = inactiveBg;
        bg.rectTransform.sizeDelta = Vector2.one * circleSize;
        _bgs.Add(bg);

        var ic = MakeImg(go, "Icon", LoadIcon(i));
        ic.preserveAspect = true;
        ic.rectTransform.sizeDelta = Vector2.one * (circleSize * 0.58f);
        _icons.Add(ic);
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

    // Layout
    // Circular delta
    static int CircularDelta(int slotIndex, int selected, int n)
    {
        int d = slotIndex - selected;
        // Wrap to the shortest arc
        while (d > n / 2) d -= n;
        while (d < -n / 2) d += n;
        // For even counts, force the halfway slot to go upward
        if (n > 1 && Mathf.Abs(d) == n / 2 && n % 2 == 0) d = n / 2;
        return d;
    }

    // How many neighbours to show each side — beyond this they're invisible
    const int VisibleRadius = 2;

    void UpdateVisuals()
    {
        int n = _rects.Count, sel = _ctrl.CurrentActiveIndex;
        for (int i = 0; i < n; i++)
        {
            int d = CircularDelta(i, sel, n);
            int absd = Mathf.Abs(d);
            bool on = d == 0;

            // Fade and shrink with distance
            float visibility = absd <= VisibleRadius ? 1f : 0f;
            float sz = circleSize * Mathf.Pow(shrinkPerStep, absd) * visibility;
            float alpha = visibility * (on ? 1f : Mathf.Pow(shrinkPerStep, absd));

            Color bg = on ? activeBg : inactiveBg;
            Color ico = on ? activeIcon : inactiveIcon;
            bg.a = alpha;
            ico.a = alpha;

            _bgs[i].color = bg;
            _icons[i].color = ico;
            _bgs[i].rectTransform.sizeDelta = Vector2.one * sz;
            _icons[i].rectTransform.sizeDelta = Vector2.one * (sz * 0.58f);

            // Selected on top, farther slots deeper
            _rects[i].SetSiblingIndex(Mathf.Max(0, n - 1 - absd));
            _rects[i].gameObject.SetActive(visibility > 0f);
        }
    }

    void SnapLayout()
    {
        if (_ctrl == null || _rects.Count == 0) return;
        UpdateVisuals();
        int n = _rects.Count, sel = _ctrl.CurrentActiveIndex;
        for (int i = 0; i < n; i++)
            _rects[i].anchoredPosition = new Vector2(0f, CircularDelta(i, sel, n) * stepUp);
    }

    void StartScrollAnim()
    {
        if (_scrollCo != null) StopCoroutine(_scrollCo);
        _scrollCo = StartCoroutine(ScrollCo());
    }

    IEnumerator ScrollCo()
    {
        int n = _rects.Count, sel = _ctrl.CurrentActiveIndex;
        float[] from = new float[n], to = new float[n];
        for (int i = 0; i < n; i++)
        {
            from[i] = _rects[i].anchoredPosition.y;
            to[i] = CircularDelta(i, sel, n) * stepUp;
        }
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / scrollSpeed;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            for (int i = 0; i < n; i++)
                _rects[i].anchoredPosition = new Vector2(0f, Mathf.Lerp(from[i], to[i], e));
            yield return null;
        }
        SnapLayout();
    }

    IEnumerator PopSlot(int idx)
    {
        if (idx >= _rects.Count) yield break;
        _rects[idx].localScale = Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / popSpeed;
            _rects[idx].localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t));
            yield return null;
        }
        _rects[idx].localScale = Vector3.one;
    }

    Sprite LoadIcon(int pos)
    {
        WeaponData wd = _ctrl?.DataAt(pos);
        if (wd == null) return null;
        string path;
        if (wd.isGrapplingHook) path = "Icons/WeaponIconGrapplingHook";
        else if (wd.isObstacleDrawer) path = "Icons/WeaponIconObstacleDrawer";
        else if (wd.isFlamethrower) path = "Icons/WeaponIconFlamethrower";
        else if (wd.isBombLauncher) path = "Icons/WeaponIconBomb";
        else if (wd.isTrap) path = "Icons/WeaponIconTrap";
        else if (wd.isTurret) path = "Icons/WeaponIconTurret";
        //TODO verify whether we can change to isShield
        else if (wd.armorBonus > 0f) path = "Icons/WeaponIconShield";
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

    static Sprite CircleSprite()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        var c = new Vector2(S * .5f, S * .5f);
        float r = S * .5f - 1f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                px[y * S + x] = new Color(1, 1, 1,
                    1f - Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), c) - (r - 1f)));
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), Vector2.one * .5f, S);
    }
}
