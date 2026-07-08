using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class TowerSelectionWheel : MonoBehaviour
{
    private GameObject[] towers;
    private TowerSlot targetSlot;
    private GameObject[] slices;
    private int hoveredIndex = -1;
    private bool isActive = false;

    //  Visual style 
    static readonly Color SliceBase = new Color(0.20f, 0.16f, 0.28f, 0.93f); // deep violet-grey
    static readonly Color SliceHover = new Color(0.60f, 0.42f, 0.92f, 0.98f); // bright purple
    static readonly Color BackingColor = new Color(0.46f, 0.28f, 0.72f, 0.55f); // purple glow tint
    static readonly Color HubColor = new Color(0.13f, 0.10f, 0.20f, 0.96f); // dark violet
    const float HoverPop = 1.08f;

    private GameObject backing;
    private GameObject hub;
    private Sprite discSprite;
    private Sprite glowSprite;

    // Co-op: this wheel belongs to one player. Hover comes from that player's
    // reticle (PlayerAim.Direction); confirm from their Build action.
    private PlayerAim _aim;
    private UnityEngine.InputSystem.InputAction _confirm;
    private PlayerRef _owner;
    private Transform _byPlayer;

    public bool IsOpen => isActive;

    public void Configure(PlayerAim aim, UnityEngine.InputSystem.InputAction confirm, PlayerRef owner)
    {
        _aim = aim;
        _confirm = confirm;
        _owner = owner;
    }

    void Update()
    {
        if (!isActive) return;
        HandleInput();
    }

    public void OpenWheel(GameObject[] towerArray, TowerSlot slot, Transform byPlayer)
    {
        if (towerArray == null || towerArray.Length <= 1 || slot == null) return;
        towers = towerArray;
        targetSlot = slot;
        _byPlayer = byPlayer;
        transform.position = slot.transform.position;
        if (slices != null)
        {
            foreach (GameObject slice in slices)
            {
                if (slice != null) DestroyImmediate(slice);
            }
        }
        transform.localScale = Vector3.one;

        CreatePieSlices();
        gameObject.SetActive(true);
        isActive = true;
        StartCoroutine(ScaleUp());
    }

    public void CloseWheel()
    {
        isActive = false;
        gameObject.SetActive(false);
        CleanUp();
    }

    void CreatePieSlices()
    {
        CleanUp();

        CreateBackingAndHub();

        int count = Mathf.Min(towers.Length, 8);
        slices = new GameObject[count];
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            slices[i] = CreatePieSlice(i, angleStep);
        }
    }

    // Faint dark disc behind the wheel (contrast halo over the busy grass) plus a
    // small hub filling the centre hole, so the ring reads as one cohesive control.
    void CreateBackingAndHub()
    {
        backing = new GameObject("Backing");
        backing.transform.parent = transform;
        backing.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        backing.transform.localScale = Vector3.one * 1.45f;
        var bsr = backing.AddComponent<SpriteRenderer>();
        bsr.sprite = GetGlowSprite();
        bsr.color = BackingColor;
        bsr.sortingLayerName = "Default";
        bsr.sortingOrder = 2998; // behind slices (3000)

        hub = new GameObject("Hub");
        hub.transform.parent = transform;
        hub.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        hub.transform.localScale = Vector3.one * 0.42f;
        var hsr = hub.AddComponent<SpriteRenderer>();
        hsr.sprite = GetDiscSprite();
        hsr.color = HubColor;
        hsr.sortingLayerName = "Default";
        hsr.sortingOrder = 3003;
    }

    GameObject CreatePieSlice(int index, float angleStep)
    {
        float startAngle = index * angleStep;
        float midAngle = startAngle + angleStep * 0.5f;

        // Create slice object
        GameObject slice = new GameObject($"Slice{index}");
        slice.transform.parent = transform;
        slice.transform.localPosition = Vector3.zero;

        // Create pie slice sprite
        SpriteRenderer sr = slice.AddComponent<SpriteRenderer>();
        sr.sprite = CreatePieSliceSprite(startAngle, angleStep);
        sr.color = SliceBase;
        sr.sortingOrder = 3000; // Above grass Y-sort range (400-1600) but below fog (5000)

        // Readable label: horizontal text just outside the slice, growing away from
        // the wheel centre, with a dark outline so it stays legible over the grass.
        CreateLabel(slice.transform, midAngle, GetTowerName(index));

        return slice;
    }

    // Builds a horizontal, outlined label whose INNER edge sits on a common circle
    // just outside the ring, growing radially OUTWARD. 8-way anchoring keeps every
    // label clear of the wheel and lines their inner edges up so they read as a tidy
    // ring instead of drifting across the slices.
    void CreateLabel(Transform parent, float midAngle, string displayText)
    {
        float rad = midAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        float labelRadius = 1.1f; // the text's inner edge/corner sits here (ring ≈ 0.92)
        Vector3 pos = new Vector3(dir.x * labelRadius, dir.y * labelRadius, -0.1f);

        // Pick the anchor so the pinned corner/edge faces the wheel centre and the
        // text grows away from it.
        const float t = 0.38f; // ~sin(22.5°): cardinal vs diagonal boundary
        bool right = dir.x > t, left = dir.x < -t;
        bool up = dir.y > t, down = dir.y < -t;

        TextAnchor anchor;
        if (up && right) anchor = TextAnchor.LowerLeft;
        else if (up && left) anchor = TextAnchor.LowerRight;
        else if (down && right) anchor = TextAnchor.UpperLeft;
        else if (down && left) anchor = TextAnchor.UpperRight;
        else if (right) anchor = TextAnchor.MiddleLeft;
        else if (left) anchor = TextAnchor.MiddleRight;
        else if (up) anchor = TextAnchor.LowerCenter;
        else anchor = TextAnchor.UpperCenter;

        TextAlignment align =
            (anchor == TextAnchor.LowerLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft)
                ? TextAlignment.Left
            : (anchor == TextAnchor.LowerRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight)
                ? TextAlignment.Right
                : TextAlignment.Center;

        GameObject labelGO = new GameObject("Label");
        labelGO.transform.parent = parent;
        labelGO.transform.localPosition = pos;
        labelGO.transform.localRotation = Quaternion.identity;

        // Black outline: four diagonal copies behind the white text.
        const float o = 0.02f;
        Vector2[] offsets =
        {
            new Vector2(o, o), new Vector2(-o, o),
            new Vector2(o, -o), new Vector2(-o, -o),
        };
        foreach (var off in offsets)
            MakeTextMesh(labelGO.transform, displayText, anchor, align,
                         new Vector3(off.x, off.y, 0.01f), Color.black, 3001);

        // White foreground on top.
        MakeTextMesh(labelGO.transform, displayText, anchor, align,
                     Vector3.zero, Color.white, 3002);
    }

    TextMesh MakeTextMesh(Transform parent, string txt, TextAnchor anchor,
                          TextAlignment align, Vector3 localPos, Color color, int order)
    {
        GameObject go = new GameObject("T");
        go.transform.parent = parent;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;

        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = txt;
        tm.fontSize = 15;
        tm.characterSize = 0.2f;
        tm.color = color;
        tm.anchor = anchor;
        tm.alignment = align;
        tm.lineSpacing = 0.8f; // tighter stacking for wrapped words

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingLayerName = "Default";
            mr.sortingOrder = order;
        }
        return tm;
    }

    string GetTowerName(int index)
    {
        string raw = null;
        if (index < towers.Length && towers[index] != null)
        {
            Tower tower = towers[index].GetComponent<Tower>();
            raw = (tower != null && !string.IsNullOrEmpty(tower.towerName))
                ? tower.towerName
                : towers[index].name;
        }
        if (string.IsNullOrEmpty(raw)) raw = $"Tower {index + 1}";
        return FormatTowerLabel(raw);
    }

    // Drops a trailing "Tower" word and stacks the remaining words on separate lines
    // so long names stay narrow instead of sweeping across neighbouring slices.
    string FormatTowerLabel(string raw)
    {
        string s = raw.Trim();

        const string suffix = " tower";
        if (s.Length > suffix.Length && s.ToLowerInvariant().EndsWith(suffix))
            s = s.Substring(0, s.Length - suffix.Length).Trim();

        var words = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            if (words[i].Length > 12) words[i] = words[i].Substring(0, 12);

        return string.Join("\n", words);
    }

    Sprite CreatePieSliceSprite(float startAngle, float angleSpan)
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];

        Vector2 center = Vector2.one * (size * 0.5f);
        float outerRadius = size * 0.46f;
        float innerRadius = size * 0.20f;
        float radFeather = 3.0f;   // soft radial edges (px)

        float gapDeg = 2.5f;       // half-gap carved off each angular side
        float angFeather = 2.0f;   // soft angular edges (deg)

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 p = new Vector2(x, y) - center;
                float dist = p.magnitude;
                float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;

                // Smooth ring band.
                float radA = Mathf.Clamp01((dist - innerRadius) / radFeather)
                           * Mathf.Clamp01((outerRadius - dist) / radFeather);

                // Smooth angular wedge with a gap on each side.
                float a = Mathf.Repeat(angle - startAngle, 360f);
                float lo = gapDeg;
                float hi = angleSpan - gapDeg;
                float angA = Mathf.Clamp01((a - lo) / angFeather)
                           * Mathf.Clamp01((hi - a) / angFeather);

                // Subtle inner→outer brightness ramp; tinted by the slice colour this
                // reads as a soft purple gradient (darker near the hub, brighter at rim).
                float radialT = Mathf.InverseLerp(innerRadius, outerRadius, dist);
                float lum = Mathf.Lerp(0.60f, 1.0f, radialT);

                pixels[y * size + x] = new Color(lum, lum, lum, radA * angA);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 128f);
    }

    // Soft-edged white disc, reused (tinted) for the backing halo and centre hub.
    Sprite GetDiscSprite()
    {
        if (discSprite != null) return discSprite;

        int size = 256;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        Vector2 center = Vector2.one * (size * 0.5f);
        float radius = size * 0.48f;
        float feather = 3.0f;

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float d = (new Vector2(x, y) - center).magnitude;
                float a = Mathf.Clamp01((radius - d) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        discSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 128f);
        return discSprite;
    }

    // Soft radial glow (brightest at centre, fading to nothing at the edge), tinted
    // purple for the backing halo.
    Sprite GetGlowSprite()
    {
        if (glowSprite != null) return glowSprite;

        int size = 256;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        Vector2 center = Vector2.one * (size * 0.5f);
        float radius = size * 0.5f;

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float d = (new Vector2(x, y) - center).magnitude / radius; // 0..1
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // ease toward the centre for a soft glow
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 128f);
        return glowSprite;
    }

    bool IsAngleInRange(float angle, float start, float end)
    {
        if (end > 360f)
        {
            return angle >= start || angle <= (end - 360f);
        }
        return angle >= start && angle <= end;
    }

    void HandleInput()
    {
        if (slices == null) return;

        // Hover direction = this player's reticle direction (mouse or stick).
        Vector2 direction = _aim != null ? _aim.Direction : Vector2.right;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

        float mouseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (mouseAngle < 0) mouseAngle += 360f;

        // Find which slice this angle corresponds to
        int newHover = -1;
        if (slices.Length > 0)
        {
            float angleStep = 360f / slices.Length;

            // Find the closest slice based on angle direction
            for (int i = 0; i < slices.Length; i++)
            {
                float sliceStartAngle = i * angleStep;
                float sliceEndAngle = sliceStartAngle + angleStep;

                // Adjust for wraparound at 0/360 degrees
                if (IsAngleInRange(mouseAngle, sliceStartAngle, sliceEndAngle))
                {
                    newHover = i;
                    break;
                }
            }
        }

        // Update hover styling
        if (newHover != hoveredIndex)
        {
            // Clear old hover
            if (hoveredIndex >= 0 && hoveredIndex < slices.Length && slices[hoveredIndex] != null)
            {
                slices[hoveredIndex].GetComponent<SpriteRenderer>().color = SliceBase;
                slices[hoveredIndex].transform.localScale = Vector3.one;
            }

            // Set new hover
            if (newHover >= 0 && newHover < slices.Length && slices[newHover] != null)
            {
                slices[newHover].GetComponent<SpriteRenderer>().color = SliceHover;
                slices[newHover].transform.localScale = Vector3.one * HoverPop;
            }

            hoveredIndex = newHover;
        }

        // Confirm with this player's Build action.
        if (_confirm != null && _confirm.WasPressedThisFrame() && hoveredIndex >= 0)
        {
            SelectSlice(hoveredIndex);
        }
    }

    void SelectSlice(int index)
    {
        if (TowerPlacementManager.Instance != null && index < towers.Length)
        {
            TowerPlacementManager.Instance.BuildAt(targetSlot, index, _byPlayer);
        }
        CloseWheel();
    }

    void CleanUp()
    {
        if (slices != null)
        {
            for (int i = 0; i < slices.Length; i++)
            {
                if (slices[i] != null)
                {
                    DestroyImmediate(slices[i]);
                }
            }
        }
        slices = null;
        hoveredIndex = -1;

        if (backing != null) { DestroyImmediate(backing); backing = null; }
        if (hub != null) { DestroyImmediate(hub); hub = null; }
    }

    IEnumerator ScaleUp()
    {
        // Quick ease-out pop-in.
        float dur = 0.12f;
        float t = 0f;
        Vector3 from = Vector3.one * 0.82f;
        Vector3 to = Vector3.one;
        transform.localScale = from;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = 1f - (1f - k) * (1f - k); // ease-out quad
            transform.localScale = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }
        transform.localScale = to;
    }

    IEnumerator ScaleDown()
    {
        isActive = false;
        gameObject.SetActive(false);
        CleanUp();
        yield break;
    }

    void OnDisable()
    {
        isActive = false;
    }
}
