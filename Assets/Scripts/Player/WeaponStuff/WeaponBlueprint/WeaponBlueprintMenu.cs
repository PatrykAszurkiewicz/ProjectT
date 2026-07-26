using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// WEAPON BLUEPRINT MENU
// Shows every weapon/tool slot as a circular hotbar-style token, split into two rows:
//   Row 1 — WEAPONS  (WeaponData.IsTool == false)
//   Row 2 — TOOLS    (WeaponData.IsTool == true)
// A slot the player has permanently discovered (WeaponBlueprintRegistry) shows
// its weapon/tool icon; a not-yet-discovered slot shows a plain black circle.
// Drag  component in, pick WeaponBlueprintMenu.OpenMenu()
public class WeaponBlueprintMenu : MonoBehaviour
{
    [Header("Fonts (optional — matches OptionsMenu)")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private Font titleFontTtf;

    [Header("Slot data (indices match WeaponRollController.allWeaponSlots)")]
    [Tooltip("EASIEST: drag your Player PREFAB here to reuse the 18 WeaponData assets already " +
             "assigned on its Weapon Roll Controller — no per-slot setup, and it works even on a " +
             "main-menu screen where no player is instantiated (it reads the prefab asset).")]
    [SerializeField] private WeaponRollController slotSource;

    [Tooltip("OPTIONAL override. Leave empty to use 'Slot Source' (or a live controller in the " +
             "scene). Only fill this in if you want a custom/curated slot list instead of the " +
             "player's. Indices must match WeaponRollController.allWeaponSlots.")]
    [SerializeField] private WeaponData[] weaponSlots;

    [Header("Ring frame (matches WeaponRollUI)")]
    [Tooltip("Resources path to the ring frame drawn around each token. Same asset the hotbar " +
             "uses for a non-equipped slot. Falls back to a procedural circle if missing.")]
    [SerializeField] private string ringFramePath = "Sprites/HUD/WeaponActive/WeaponNotActive";

    [Header("Layout")]
    [SerializeField] private float slotSpacing = 16f;
    [SerializeField] private float minCell = 84f;
    [SerializeField] private float maxCell = 150f;
    [Tooltip("Icon size as a fraction of the token (0.58 matches the hotbar).")]
    [SerializeField] private float iconRatio = 0.66f;
    [Tooltip("Black-circle size (fraction of the token) drawn for a locked slot.")]
    [SerializeField] private float lockedRatio = 0.80f;

    // Inner content width available to a row = panel width - 2*side inset.
    private const float PanelWidth = 1440f;
    private const float SideInset = 72f;
    private const float RowContentWidth = PanelWidth - SideInset * 2f;

    private static readonly Color SlotBgFallback = new Color(0.10f, 0.10f, 0.10f, 0.9f);

    private static WeaponBlueprintMenu _instance;
    private GameObject _root;
    private TMP_FontAsset _font;

    private RectTransform _weaponRow, _toolRow;
    private TextMeshProUGUI _countLabel;
    private bool _subscribed;

    //  ENTRY POINTS 
    private void Awake() { if (_instance == null) _instance = this; }
    private void OnDestroy()
    {
        Unsubscribe();
        if (_instance == this) _instance = null;
    }

    public static void Open()
    {
        // Prefer a scene-placed component, even an inactive one: that is the instance
        // whose 'Slot Source' you filled in. Creating a bare GameObject instead gives an
        // UNCONFIGURED menu — slotSource null — and in a scene with no live
        // WeaponRollController (i.e. MenuScene) ResolveSlots() then returns null and the
        // screen renders zero tokens and "Discovered 0/0". That's the main-menu bug.
        if (_instance == null)
            _instance = FindFirstObjectByType<WeaponBlueprintMenu>(FindObjectsInactive.Include);

        if (_instance == null)
        {
            _instance = new GameObject("WeaponBlueprintMenu").AddComponent<WeaponBlueprintMenu>();
            Debug.LogWarning("[WeaponBlueprintMenu] No configured instance in the scene — created a " +
                             "bare one. It can only show slots if a WeaponRollController exists in the " +
                             "scene. Add a WeaponBlueprintMenu component to this scene and assign " +
                             "'Slot Source' = your Player prefab.");
        }
        _instance.OpenMenu();
    }
    public static void Close() { if (_instance != null) _instance.CloseMenu(); }

    /// <summary>Assign this to a Button's OnClick to open the menu.</summary>
    public void OpenMenu()
    {
        MenuTheme.EnsureEventSystem();
        if (_root == null) BuildUI();
        Populate();
        Subscribe();               // live-refresh if a blueprint unlocks while open
        _root.SetActive(true);

        // Was a bare `Cursor.visible = true` with no restore, so the pointer stayed on
        // after closing back into gameplay. UIModalStack owns the cursor now, and being
        // on the stack means Esc closes THIS overlay instead of toggling the pause menu
        // underneath it. freeze only in-run: this also opens from the main menu.
        UIModalStack.Push(this, freeze: UIModalStack.GameplayActive);
    }

    public void CloseMenu()
    {
        Unsubscribe();
        if (_root != null) _root.SetActive(false);
        UIModalStack.Pop(this);
    }

    private void Update()
    {
        if (_root != null && _root.activeSelf && MenuBackInput.ConsumeBack(this))
            CloseMenu();
    }

    private void OnDisable()
    {
        if (UIModalStack.Contains(this)) UIModalStack.Pop(this);
    }

    public void ToggleMenu()
    {
        if (_root != null && _root.activeSelf) CloseMenu(); else OpenMenu();
    }

    //  BLUEPRINT STATE 
    // Discovered = the permaupgrade blueprint has ever been found. Swap this one
    // line to `WeaponUnlockRegistry.Instance.IsUnlocked(slot)` if you'd rather
    // gate on THIS RUN's hotbar unlocks instead of the persistent blueprints.
    private static bool IsSlotUnlocked(int slot)
    {
        var reg = WeaponBlueprintRegistry.Instance;
        return reg != null && reg.IsBlueprinted(slot);
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        if (WeaponBlueprintRegistry.Instance != null)
        {
            WeaponBlueprintRegistry.Instance.OnBlueprintsChanged += Populate;
            _subscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (WeaponBlueprintRegistry.Instance != null)
            WeaponBlueprintRegistry.Instance.OnBlueprintsChanged -= Populate;
        _subscribed = false;
    }

    // Where the 18 WeaponData entries come from, in priority order:
    //   1. an explicit curated override array (only if it actually has entries),
    //   2. 'slotSource' — the Player prefab/controller you dragged in (read straight
    //      off the prefab asset, so it works with NO instantiated player),
    //   3. a live WeaponRollController already in the scene (in-game pause case).
    private WeaponData[] ResolveSlots()
    {
        if (HasAny(weaponSlots)) return weaponSlots;
        if (slotSource != null && HasAny(slotSource.allWeaponSlots)) return slotSource.allWeaponSlots;
        var ctrl = FindFirstObjectByType<WeaponRollController>();
        return ctrl != null ? ctrl.allWeaponSlots : null;
    }

    private static bool HasAny(WeaponData[] arr)
    {
        if (arr == null) return false;
        for (int i = 0; i < arr.Length; i++) if (arr[i] != null) return true;
        return false;
    }

    //  POPULATE THE TWO ROWS 
    private void Populate()
    {
        if (_root == null) return;

        var slots = ResolveSlots();
        if (!HasAny(slots))
        {
            // Was `slots == null`, which missed an all-null array and reported nothing.
            Debug.LogError("[WeaponBlueprintMenu] No slot data — the screen will be empty and show " +
                           "'Discovered 0/0'. Assign 'Slot Source' (your Player PREFAB) on the " +
                           "WeaponBlueprintMenu component in THIS scene. The prefab is read as an " +
                           "asset, so it works with no player instantiated (main menu).");
        }

        // Partition slot indices into weapons / tools (skip empty slots).
        var weaponIdx = new List<int>();
        var toolIdx = new List<int>();
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var wd = slots[i];
                if (wd == null) continue;
                if (wd.IsTool) toolIdx.Add(i); else weaponIdx.Add(i);
            }
        }

        // One shared cell size (driven by the longer row) so both rows align.
        int maxCount = Mathf.Max(weaponIdx.Count, toolIdx.Count, 1);
        float fit = (RowContentWidth - slotSpacing * (maxCount - 1)) / maxCount;
        float cell = Mathf.Clamp(fit, minCell, maxCell);

        FillRow(_weaponRow, weaponIdx, slots, cell);
        FillRow(_toolRow, toolIdx, slots, cell);

        if (_countLabel != null)
        {
            int total = weaponIdx.Count + toolIdx.Count;
            int found = 0;
            foreach (int i in weaponIdx) if (IsSlotUnlocked(i)) found++;
            foreach (int i in toolIdx) if (IsSlotUnlocked(i)) found++;
            _countLabel.text = $"Discovered {found} / {total}";
        }
    }

    private void FillRow(RectTransform row, List<int> indices, WeaponData[] slots, float cell)
    {
        if (row == null) return;

        // Clear old tokens.
        for (int c = row.childCount - 1; c >= 0; c--) Destroy(row.GetChild(c).gameObject);

        SetH(row.gameObject, cell);

        foreach (int slot in indices)
            MakeSlotCell(row, slots[slot], IsSlotUnlocked(slot), cell);
    }

    private void MakeSlotCell(Transform parent, WeaponData wd, bool unlocked, float cell)
    {
        var cellGo = MenuTheme.NewUI(unlocked ? $"Slot_{wd.weaponName}" : "Slot_Locked", parent);
        var le = cellGo.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = cell;
        le.minHeight = le.preferredHeight = cell;
        le.flexibleWidth = le.flexibleHeight = 0f;

        // Ring frame — full token size (dimmed when the slot is locked).
        var frame = cellGo.AddComponent<Image>();
        frame.raycastTarget = false;
        Sprite ring = RingSprite();
        if (ring != null)
        {
            frame.sprite = ring;
            frame.preserveAspect = true;
            frame.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }
        else
        {
            frame.sprite = CircleSprite();       // procedural fallback = dark disc
            frame.color = SlotBgFallback;
        }

        // Inner content: the icon (unlocked) or a black circle (locked).
        var inner = new GameObject("Inner", typeof(Image));
        inner.transform.SetParent(cellGo.transform, false);
        var img = inner.GetComponent<Image>();
        img.raycastTarget = false;
        var irt = img.rectTransform;
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = Vector2.zero;

        if (unlocked)
        {
            img.sprite = WeaponRollUI.LoadIconForData(wd);  // same resolver as the hotbar
            img.preserveAspect = true;
            img.color = Color.white;
            irt.sizeDelta = Vector2.one * cell * iconRatio;
        }
        else
        {
            img.sprite = CircleSprite();
            img.color = Color.black;
            irt.sizeDelta = Vector2.one * cell * lockedRatio;
        }
    }

    //  UI CONSTRUCTION (mirrors OptionsMenu / ContinueRunMenu) 
    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);

        _root = new GameObject("BlueprintCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4850; // standalone overlay (ContinueRun 4800 < this < Options 4900 < Rebind 5000)
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var dim = MenuTheme.NewUI("Dim", _root.transform);
        MenuTheme.Stretch(dim.GetComponent<RectTransform>());
        dim.AddComponent<Image>().sprite = MenuTheme.VerticalGradient(MenuTheme.GradTop, MenuTheme.GradBottom);

        var panel = MenuTheme.NewUI("Panel", dim.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(PanelWidth, 860);
        MenuTheme.ApplySprite(panel.AddComponent<Image>(), MenuTheme.PanelSprite, MenuTheme.PanelSolid);

        var inner = MenuTheme.NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(SideInset, 72); irt.offsetMax = new Vector2(-SideInset, -78);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 14; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperCenter;

        var title = MenuTheme.NewText("WEAPON BLUEPRINTS", inner.transform, 48, TextAlignmentOptions.Center, _font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 56);

        AddDivider(inner.transform);

        _countLabel = MenuTheme.NewText("", inner.transform, 30, TextAlignmentOptions.Center, _font);
        _countLabel.color = MenuTheme.ValueCol;                 // near-white — readable on grey
        _countLabel.fontStyle = FontStyles.Bold;
        _countLabel.characterSpacing = 3f;
        SetH(_countLabel, 40);

        AddHeader(inner.transform, "WEAPONS");
        _weaponRow = MakeRow(inner.transform);

        AddHeader(inner.transform, "TOOLS");
        _toolRow = MakeRow(inner.transform);

        var spacer = MenuTheme.NewUI("Spacer", inner.transform);
        var sle = spacer.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 0f;

        var close = MenuTheme.NewButton("Back", inner.transform, 22, _font);
        SetH(close, 52);
        close.onClick.AddListener(CloseMenu);
    }

    private RectTransform MakeRow(Transform parent)
    {
        var row = MenuTheme.NewUI("Row", parent);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = slotSpacing;
        h.childAlignment = TextAnchor.MiddleCenter;   // short rows centre; full rows fill
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        SetH(row, maxCell);                           // real height set per-populate
        return row.GetComponent<RectTransform>();
    }

    private void AddHeader(Transform parent, string text)
    {
        var t = MenuTheme.NewText(text, parent, 20, TextAlignmentOptions.Left, _font);
        t.color = MenuTheme.Magenta; t.fontStyle = FontStyles.Bold; t.characterSpacing = 4f;
        SetH(t, 24);
    }

    private void AddDivider(Transform parent)
    {
        var holder = MenuTheme.NewUI("RuleHolder", parent);
        SetH(holder, 12);
        var rule = MenuTheme.NewUI("Rule", holder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.20f, 0.5f); rr.anchorMax = new Vector2(0.80f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.8f);
    }

    private static void SetH(Component c, float h) => SetH(c.gameObject, h);
    private static void SetH(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0f;
    }

    //  RING + PROCEDURAL CIRCLE (fallback, mirrors WeaponRollUI) 
    private bool _ringResolved;
    private Sprite _ring;
    private Sprite RingSprite()
    {
        if (!_ringResolved)
        {
            _ringResolved = true;
            _ring = Resources.Load<Sprite>(ringFramePath);
            if (_ring == null)
                Debug.LogWarning($"[WeaponBlueprintMenu] Ring frame not found at 'Resources/{ringFramePath}'. " +
                                 "Using a procedural circle instead.");
        }
        return _ring;
    }

    private static Sprite _circle;
    private static Sprite CircleSprite()
    {
        if (_circle != null) return _circle;
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, mipChain: true, linear: false)
        {
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 4
        };
        var px = new Color[S * S];
        float c = S * 0.5f, r = S * 0.5f - 2f;
        const float aa = 2.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01((r - d) / aa);
                px[y * S + x] = new Color(1f, 1f, 1f, t * t * (3f - 2f * t));
            }
        tex.SetPixels(px);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        _circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _circle;
    }
}

