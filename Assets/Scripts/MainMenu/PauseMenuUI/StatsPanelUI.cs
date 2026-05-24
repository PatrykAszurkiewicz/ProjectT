using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


// Drives the pause-menu stats display — fully automatic, ZERO inspector wiring.
public class StatsPanelUI : MonoBehaviour
{
    [Header("Optional overrides (leave empty for auto-find)")]
    [Tooltip("Root to search from. Empty -> this.transform.")]
    public Transform pauseMenuRoot;
    [Tooltip("Where detailed stats render. Empty -> auto-find the StatsUI " +
             "Scroll View content ('Content' under Viewport).")]
    public Transform detailContent;

    [Header("Behaviour")]
    public float refreshInterval = 0.5f;

    //  Section model
    private enum Cat { Player, Weapon, Base, Tower, Enemy, Global }

    private class Section
    {
        public Cat category;
        public string objectName;          // section GameObject name
        public string containerName;       // child container holding the rows
        public string[] rowNames;          // expected row object names, in order
        public Button button;
        public readonly Dictionary<string, TextMeshProUGUI> rows =
            new Dictionary<string, TextMeshProUGUI>();
    }

    private readonly Section[] _sections =
    {
        new Section { category = Cat.Player, objectName = "Player", containerName = "PlayerStats",
            rowNames = new[] { "Hp", "Regen", "Stamina", "MoveSpeed", "Armor", "DashCooldown" } },
        new Section { category = Cat.Weapon, objectName = "Weapon", containerName = "WeaponStats",
            rowNames = new[] { "Name", "Damage", "DPS" } },
        new Section { category = Cat.Base,   objectName = "Base",   containerName = "BaseStats",
            rowNames = new[] { "Status", "Energy" } },
        new Section { category = Cat.Tower,  objectName = "Tower",  containerName = "TowerStats",
            rowNames = new[] { "Amount", "Damage", "FireRate", "Range" } },
        new Section { category = Cat.Enemy,  objectName = "Enemy",  containerName = "EnemyStats",
            rowNames = new[] { "HpMult", "DmgMult", "SpeedMult" } },
        new Section { category = Cat.Global, objectName = "Global", containerName = "GlobalStats",
            rowNames = new[] { "ResMult", "BonusDrop" } },
    };

    //  Runtime state
    private PlayerStats _player;
    private Transform _detailContainer;
    private int _detailCursor;
    private Cat _currentDetail = Cat.Player;
    private bool _bound;
    private float _timer;

    //  Lifecycle
    private bool _everUpdated;

    private void Start()
    {
        if (!_bound) Bind();
        _player = FindAnyObjectByType<PlayerStats>();
        RefreshAll();
        ShowDetail(_currentDetail);
    }

    private void OnEnable()
    {
        // Start may not have run yet on the very first enable — guard with _bound.
        if (_bound)
        {
            _player = FindAnyObjectByType<PlayerStats>();
            RefreshAll();
            ShowDetail(_currentDetail);
        }
        _timer = 0f;
    }

    private void Update()
    {
        if (!_everUpdated)
        {
            _everUpdated = true;
            if (verboseDiagnostics)
                Debug.Log("[StatsPanelUI] Update() is running — component is alive and active.");
            // Late safety net: if Start somehow didn't bind, do it now.
            if (!_bound) { Bind(); ShowDetail(_currentDetail); }
        }

        _timer += Time.unscaledDeltaTime;   // game is paused (timeScale 0)
        if (_timer < refreshInterval) return;
        _timer = 0f;
        if (_player == null) _player = FindAnyObjectByType<PlayerStats>();
        RefreshAll();
    }

    //  One-time binding: locate sections, rows, buttons, detail container
    [Header("Diagnostics")]
    [Tooltip("Logs a one-time report of what was found. Turn off once it works.")]
    public bool verboseDiagnostics = true;

    private void Bind()
    {
        Transform root = pauseMenuRoot != null ? pauseMenuRoot : transform;

        if (verboseDiagnostics)
            Debug.Log($"[StatsPanelUI] Bind() running. Search root = '{root.name}'.");

        int totalRowsFound = 0;
        int sectionsFound = 0;

        foreach (var s in _sections)
        {
            Transform sectionT = FindDeep(root, s.objectName);
            if (sectionT == null)
            {
                Debug.LogWarning($"[StatsPanelUI] Section '{s.objectName}' NOT FOUND under " +
                    $"'{root.name}'. The script must sit on a parent of LeftPanel.");
                continue;
            }
            sectionsFound++;

            // Button — wire its click to show this category's detail.
            s.button = sectionT.GetComponent<Button>();
            if (s.button != null)
            {
                Cat captured = s.category;          // capture for the closure
                s.button.onClick.AddListener(() => ShowDetail(captured));
            }
            else if (verboseDiagnostics)
            {
                Debug.LogWarning($"[StatsPanelUI] Section '{s.objectName}' has no Button " +
                    "component — clicking it won't open the detail view.");
            }

            // Rows — grab each named TMP inside the section's container.
            Transform container = FindDeep(sectionT, s.containerName) ?? sectionT;
            foreach (string rowName in s.rowNames)
            {
                Transform rowT = FindDeep(container, rowName);
                if (rowT == null)
                {
                    if (verboseDiagnostics)
                        Debug.LogWarning($"[StatsPanelUI] Row '{rowName}' not found in " +
                            $"'{s.objectName}/{s.containerName}'.");
                    continue;
                }
                var tmp = rowT.GetComponent<TextMeshProUGUI>()
                          ?? rowT.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) { s.rows[rowName] = tmp; totalRowsFound++; }
                else if (verboseDiagnostics)
                    Debug.LogWarning($"[StatsPanelUI] Row '{rowName}' has no TextMeshProUGUI.");
            }
        }

        // Detail container resolution.
        // Priority: explicit override -> StatsUI scroll-view 'Content' -> RightPanel.
        if (detailContent != null)
        {
            _detailContainer = detailContent;
        }
        else
        {
            // The StatsUI object owns a ScrollRect: StatsUI/Scroll View/Viewport/Content.
            // 'Content' already has a layout group, so rows stack + scroll for free.
            Transform statsUI = FindDeep(root, "StatsUI");
            Transform content = statsUI != null ? FindDeep(statsUI, "Content") : null;

            if (content == null && statsUI == null)
            {
                // StatsUI might be a sibling, not a descendant of our root — search the scene.
                var anyStats = GameObject.Find("StatsUI");
                if (anyStats != null) content = FindDeep(anyStats.transform, "Content");
            }

            _detailContainer = content;
        }

        if (_detailContainer == null)
            Debug.LogWarning("[StatsPanelUI] Detail container not found. Assign 'Detail Content' " +
                "in the inspector (e.g. StatsUI/Scroll View/Viewport/Content).");
        else
        {

            for (int i = _detailContainer.childCount - 1; i >= 0; i--)
            {
                if (verboseDiagnostics)
                {
                    Transform c = _detailContainer.GetChild(i);
                    Debug.Log($"[StatsPanelUI] Wiping pre-existing child of Content: '{c.name}'");
                }
                Destroy(_detailContainer.GetChild(i).gameObject);
            }
            // Reset the row pool so we don't hold dangling references to the
            // objects we just destroyed.
            _detailRowObjs.Clear();

            var contentRT = _detailContainer as RectTransform;
            var viewportRT = _detailContainer.parent as RectTransform;          // Content -> Viewport
            var scrollRT = viewportRT != null ? viewportRT.parent as RectTransform : null; // -> Scroll View

            // 1. Kill layout components on Scroll View, Viewport and Content.
            foreach (var rt in new[] { scrollRT, viewportRT, contentRT })
            {
                if (rt == null) continue;
                var lg = rt.GetComponent<UnityEngine.UI.LayoutGroup>();
                if (lg != null) lg.enabled = false;
                var fit = rt.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                if (fit != null) fit.enabled = false;
            }

            // 2. Stretch the Viewport to fully fill the Scroll View.
            if (viewportRT != null)
            {
                viewportRT.anchorMin = Vector2.zero;
                viewportRT.anchorMax = Vector2.one;
                viewportRT.pivot = new Vector2(0.5f, 0.5f);
                viewportRT.offsetMin = Vector2.zero;   // left/bottom inset 0
                viewportRT.offsetMax = Vector2.zero;   // right/top  inset 0
            }

            // 3. Anchor Content to the top, full width, pivot at top edge.
            if (contentRT != null)
            {
                contentRT.anchorMin = new Vector2(0f, 1f);
                contentRT.anchorMax = new Vector2(1f, 1f);
                contentRT.pivot = new Vector2(0.5f, 1f);
                contentRT.offsetMin = new Vector2(0f, contentRT.offsetMin.y);
                contentRT.offsetMax = new Vector2(0f, contentRT.offsetMax.y);
                contentRT.anchoredPosition = Vector2.zero;
            }

            // 4. Make sure the ScrollRect points at the right transforms.
            //    Keep the VERTICAL scrollbar enabled so users can see they can
            //    scroll a long Tower section. Disable only the HORIZONTAL one
            //    (it was showing up as a grey blob at the bottom).
            if (scrollRT != null)
            {
                var scrollRect = scrollRT.GetComponent<UnityEngine.UI.ScrollRect>();
                if (scrollRect != null)
                {
                    scrollRect.viewport = viewportRT;
                    scrollRect.content = contentRT;
                    scrollRect.horizontal = false;
                    scrollRect.vertical = true;
                    scrollRect.horizontalScrollbar = null; // unhook horizontal entirely
                    // Leave verticalScrollbar referenced so the thumb stays usable.
                }

                // Disable just the horizontal scrollbar (the blob source).
                Transform hsb = scrollRT.Find("Scrollbar Horizontal");
                if (hsb != null) hsb.gameObject.SetActive(false);
            }

            // Remove the leftover loose rows that sit directly under 'Scroll View'.
            if (scrollRT != null)
            {
                string[] leftovers = { "Regen", "Armor", "MsSpeed" };
                foreach (string leftover in leftovers)
                {
                    Transform t = scrollRT.Find(leftover);  // direct child only — safe
                    if (t != null) Destroy(t.gameObject);
                }
            }
        }

        if (verboseDiagnostics)
        {
            Debug.Log($"[StatsPanelUI] Bind complete: {sectionsFound}/6 sections, " +
                $"{totalRowsFound} rows found. " +
                $"Detail container = {(_detailContainer != null ? _detailContainer.name : "NONE")}.");

            var p = FindAnyObjectByType<PlayerStats>();
            Debug.Log($"[StatsPanelUI] Scene check: PlayerStats={(p != null ? "OK" : "NULL")}, " +
                $"EnergyManager={(EnergyManager.Instance != null ? "OK" : "NULL")}, " +
                $"EnemyStatModifierManager={(EnemyStatModifierManager.Instance != null ? "OK" : "NULL")}, " +
                $"AugmentRegistry={(AugmentRegistry.Instance != null ? "OK" : "NULL")}.");

            DumpAppliedAugments();
        }

        _bound = true;
    }


    // Lists every applied augment and what stat / target / operation it declares. 
    private void DumpAppliedAugments()
    {
        if (AugmentRegistry.Instance == null)
        {
            Debug.Log("[StatsPanelUI] No AugmentRegistry — no augments to list.");
            return;
        }

        var applied = AugmentRegistry.Instance.GetAppliedAugments();
        if (applied == null || applied.Count == 0)
        {
            Debug.Log("[StatsPanelUI] No augments applied yet.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[StatsPanelUI] === Applied augments ({applied.Count}) ===");

        foreach (int id in applied)
        {
            var data = AugmentRegistry.Instance.GetAugmentData(id);
            if (data == null) { sb.AppendLine($"  id={id} (no data)"); continue; }

            sb.AppendLine($"  '{data.Name}' (id={id})");
            if (data.ParsedModifications == null || data.ParsedModifications.Count == 0)
            {
                sb.AppendLine($"    -> no stat modifications (probably a special-effect augment)");
                continue;
            }
            foreach (var mod in data.ParsedModifications)
            {
                sb.AppendLine($"    -> {mod.OperationType} {mod.StatName} on {mod.TargetType} = {mod.Value}");
            }
        }
        Debug.Log(sb.ToString());
    }


    private void DumpDetailHierarchy()
    {
        if (_detailContainer == null) { Debug.LogWarning("[StatsPanelUI] DumpDetailHierarchy: no container."); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[StatsPanelUI] === Detail hierarchy dump (Content -> up) ===");

        Transform t = _detailContainer;
        int guard = 0;
        while (t != null && guard++ < 12)
        {
            var rt = t as RectTransform;
            string size = rt != null ? $"rect={rt.rect.width:0}x{rt.rect.height:0} " +
                                       $"sizeDelta={rt.sizeDelta.x:0}x{rt.sizeDelta.y:0} " +
                                       $"anchoredPos={rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0} " +
                                       $"scale={rt.lossyScale.x:0.##}"
                                     : "(no RectTransform)";

            var cg = t.GetComponent<CanvasGroup>();
            var img = t.GetComponent<UnityEngine.UI.Image>();
            var mask = t.GetComponent<UnityEngine.UI.Mask>();
            var rectMask = t.GetComponent<UnityEngine.UI.RectMask2D>();
            var canvas = t.GetComponent<Canvas>();

            string extras = "";
            if (cg != null) extras += $" CanvasGroup(alpha={cg.alpha},interactable={cg.interactable})";
            if (img != null) extras += $" Image(alpha={img.color.a:0.##},enabled={img.enabled})";
            if (mask != null) extras += $" Mask(enabled={mask.enabled})";
            if (rectMask != null) extras += $" RectMask2D(enabled={rectMask.enabled})";
            if (canvas != null) extras += $" Canvas(enabled={canvas.enabled})";

            sb.AppendLine($"  '{t.name}' active={t.gameObject.activeInHierarchy} {size}{extras}");
            t = t.parent;
        }

        // Also report the first detail row, if any.
        if (_detailContainer.childCount > 0)
        {
            var firstRow = _detailContainer.GetChild(0) as RectTransform;
            if (firstRow != null)
            {
                var firstTmp = firstRow.GetComponent<TextMeshProUGUI>();
                sb.AppendLine($"  first row '{firstRow.name}': active={firstRow.gameObject.activeInHierarchy} " +
                    $"rect={firstRow.rect.width:0}x{firstRow.rect.height:0} " +
                    $"anchoredPos={firstRow.anchoredPosition.x:0},{firstRow.anchoredPosition.y:0} " +
                    $"text='{(firstTmp != null ? firstTmp.text : "(no TMP)")}' " +
                    $"fontSize={(firstTmp != null ? firstTmp.fontSize : 0)} " +
                    $"color-alpha={(firstTmp != null ? firstTmp.color.a : 0):0.##}");
            }
        }

        Debug.Log(sb.ToString());
    }

    //  Public API (also usable from inspector OnClick if you ever want to)
    public void ShowPlayer() => ShowDetail(Cat.Player);
    public void ShowWeapon() => ShowDetail(Cat.Weapon);
    public void ShowBase() => ShowDetail(Cat.Base);
    public void ShowTower() => ShowDetail(Cat.Tower);
    public void ShowEnemy() => ShowDetail(Cat.Enemy);
    public void ShowGlobal() => ShowDetail(Cat.Global);

    private void ShowDetail(Cat category)
    {
        if (verboseDiagnostics)
            Debug.Log($"[StatsPanelUI] ShowDetail({category}) — button click received.");
        _currentDetail = category;
        RefreshDetail();
    }

    public void RefreshAll()
    {
        foreach (var s in _sections) FillSection(s);
        RefreshDetail();
    }

    //  BASIC stats — write into the existing named rows
    private void FillSection(Section s)
    {
        switch (s.category)
        {
            case Cat.Player: FillPlayer(s); break;
            case Cat.Weapon: FillWeapon(s); break;
            case Cat.Base: FillBase(s); break;
            case Cat.Tower: FillTower(s); break;
            case Cat.Enemy: FillEnemy(s); break;
            case Cat.Global: FillGlobal(s); break;
        }
    }

    private void FillPlayer(Section s)
    {
        if (_player == null) return;

        Set(s, "Hp", "Health", $"{_player.maxHealth:0}",
            AugmentMath.Multiplier("maxHealth", "Player"));
        Set(s, "Regen", "Regen", $"{_player.healthRegenRate:0.#}/s",
            AugmentMath.Multiplier("healthRegenRate", "Player"));
        Set(s, "Stamina", "Stamina", $"{_player.maxStamina:0.#}",
            AugmentMath.Multiplier("maxStamina", "Player"));
        Set(s, "MoveSpeed", "Move Speed", $"{_player.moveSpeed:0.#}",
            AugmentMath.Multiplier("moveSpeed", "Player"));
        Set(s, "Armor", "Armor", $"{_player.currentArmor:0.#}",
            AugmentMath.Multiplier("currentArmor", "Player"));
        Set(s, "DashCooldown", "Dash Cooldown", $"{_player.dashCooldown:0.#}s",
            AugmentMath.Multiplier("dashCooldown", "Player"), lowerIsBetter: true);
    }

    private void FillWeapon(Section s)
    {
        var w = ResolveWeaponData();
        if (w == null)
        {
            SetNote(s, "Name", "No weapon");
            SetNote(s, "Damage", "");
            SetNote(s, "DPS", "");
            return;
        }

        float dmgMul = AugmentMath.Multiplier("damage", "Weapon");
        string dpsText = ComputeDpsText(w);

        SetNote(s, "Name", w.weaponName, StatRowBuilder.AccentColor);
        Set(s, "Damage", "Damage", $"{w.damage:0.#}", dmgMul);
        Set(s, "DPS", "DPS", dpsText, dmgMul);
    }

    // DPS depends on weapon type:
    //   Flamethrower → damage / flameDamageInterval (continuous ticks).
    //   Grappling hook → "—" (not a damage weapon).
    //   Anything else  → damage / attackCooldown   (one hit per cooldown).

    private static string ComputeDpsText(WeaponData w)
    {
        if (w == null) return "—";

        if (w.isGrapplingHook || w.isObstacleDrawer)
            return "—";

        if (w.isFlamethrower && w.flameDamageInterval > 0f)
            return $"{w.damage / w.flameDamageInterval:0.#}";

        if (w.attackCooldown > 0f)
            return $"{w.damage / w.attackCooldown:0.#}";

        return $"{w.damage:0.#}";
    }

    private void FillBase(Section s)
    {
        var core = FindFirstObjectByType<CentralCore>();
        if (core == null)
        {
            SetNote(s, "Status", "Offline", StatRowBuilder.BadColor);
            SetNote(s, "Energy", "");
            return;
        }

        string status = core.IsEnergyDepleted() ? "DEPLETED"
                       : core.IsEnergyLow() ? "CRITICAL"
                       : "OPERATIONAL";
        Color sc = core.IsEnergyDepleted() ? StatRowBuilder.BadColor
                 : core.IsEnergyLow() ? StatRowBuilder.WarningColor
                 : StatRowBuilder.GoodColor;

        SetColored(s, "Status", "Status", status, sc);
        Set(s, "Energy", "Energy", $"{core.GetEnergy():0} / {core.GetMaxEnergy():0}", 1f);
    }

    private void FillTower(Section s)
    {
        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        if (towers.Length == 0)
        {
            SetNote(s, "Amount", "No towers", StatRowBuilder.WarningColor);
            SetNote(s, "Damage", ""); SetNote(s, "FireRate", ""); SetNote(s, "Range", "");
            return;
        }

        var attackers = towers.Where(t => !t.IsGenerator()).ToArray();
        Tower sample = attackers.Length > 0 ? attackers[0] : towers[0];

        Set(s, "Amount", "Tower Amount", $"{towers.Length}", 1f);
        Set(s, "Damage", "Damage", $"{sample.GetDamage():0.#}",
            AugmentMath.Multiplier("damage", "Tower"));
        Set(s, "FireRate", "Fire Rate", $"{sample.GetFireRate():0.00}/s",
            AugmentMath.Multiplier("fireRate", "Tower"));
        Set(s, "Range", "Range", $"{sample.GetRange():0.#}",
            AugmentMath.Multiplier("range", "Tower"));
    }

    private void FillEnemy(Section s)
    {
        var mgr = EnemyStatModifierManager.Instance;
        if (mgr == null)
        {
            SetNote(s, "HpMult", "No data", StatRowBuilder.NeutralColor);
            SetNote(s, "DmgMult", ""); SetNote(s, "SpeedMult", "");
            return;
        }

        float hp = mgr.GetHealthMultiplier();
        float dmg = mgr.GetDamageMultiplier();
        float spd = mgr.GetMoveSpeedMultiplier();

        Set(s, "HpMult", "Health", $"x{hp:0.00}", hp);
        Set(s, "DmgMult", "Attack", $"x{dmg:0.00}", dmg);
        Set(s, "SpeedMult", "Move Speed", $"x{spd:0.00}", spd);
    }

    private void FillGlobal(Section s)
    {
        var em = EnergyManager.Instance;
        if (em == null)
        {
            SetNote(s, "ResMult", "No data", StatRowBuilder.NeutralColor);
            SetNote(s, "BonusDrop", "");
            return;
        }

        Set(s, "ResMult", "Resource Mult", $"x{em.globalResourceMultiplier:0.00}",
            em.globalResourceMultiplier);
        Set(s, "BonusDrop", "Bonus Drop", $"{em.bonusResourceDropChance * 100f:0}%", 1f);
    }

    private static void Set(Section s, string rowName, string label, string value,
                            float mult, bool lowerIsBetter = false)
    {
        if (!s.rows.TryGetValue(rowName, out var tmp) || tmp == null) return;
        tmp.richText = true;
        tmp.text = StatRowBuilder.Compose(label, value, mult, lowerIsBetter, hideWhenNeutral: true);
    }

    private static void SetColored(Section s, string rowName, string label, string value, Color valueColor)
    {
        if (!s.rows.TryGetValue(rowName, out var tmp) || tmp == null) return;
        tmp.richText = true;
        tmp.text = $"<color=#{StatRowBuilder.Hex(StatRowBuilder.LabelColor)}>{label}:</color> " +
                   $"<color=#{StatRowBuilder.Hex(valueColor)}>{value}</color>";
    }

    private static void SetNote(Section s, string rowName, string text, Color? color = null)
    {
        if (!s.rows.TryGetValue(rowName, out var tmp) || tmp == null) return;
        tmp.richText = true;
        tmp.text = string.IsNullOrEmpty(text)
            ? ""
            : StatRowBuilder.ComposeNote(text, color ?? StatRowBuilder.LabelColor);
    }

    //  DETAILED stats — StatsUI scroll view content
    private void RefreshDetail()
    {
        if (_detailContainer == null)
        {
            if (verboseDiagnostics)
                Debug.LogWarning("[StatsPanelUI] RefreshDetail: detail container is NULL.");
            return;
        }
        _detailCursor = 0;

        switch (_currentDetail)
        {
            case Cat.Player: DetailPlayer(); break;
            case Cat.Weapon: DetailWeapon(); break;
            case Cat.Base: DetailBase(); break;
            case Cat.Tower: DetailTower(); break;
            case Cat.Enemy: DetailEnemy(); break;
            case Cat.Global: DetailGlobal(); break;
        }

        // Hide pooled rows we didn't use this pass.
        for (int i = _detailCursor; i < _detailRowObjs.Count; i++)
            if (_detailRowObjs[i]?.root != null)
                _detailRowObjs[i].root.gameObject.SetActive(false);
        // Re-activate rows we DID use (a pooled row hidden last pass).
        for (int i = 0; i < _detailCursor && i < _detailRowObjs.Count; i++)
            if (_detailRowObjs[i]?.root != null)
                _detailRowObjs[i].root.gameObject.SetActive(true);

        // Every refresh, sweep Content for any child we don't own and destroy it.
        // Something (likely an augment-spawning system) drops objects into our
        // scroll content; this kills the resulting visual junk on contact.
        var ownedRoots = new HashSet<GameObject>();
        foreach (var r in _detailRowObjs)
            if (r?.root != null) ownedRoots.Add(r.root.gameObject);

        for (int i = _detailContainer.childCount - 1; i >= 0; i--)
        {
            var child = _detailContainer.GetChild(i).gameObject;
            if (!ownedRoots.Contains(child))
            {
                if (verboseDiagnostics)
                    Debug.Log($"[StatsPanelUI] Destroying stray child in Content: '{child.name}'");
                Destroy(child);
            }
        }

        // Manually size the scroll content to fit the rows we wrote. This does
        // NOT rely on the prefab's VerticalLayoutGroup / ContentSizeFitter,
        // which were collapsing 'Content' to zero height.
        var contentRT = _detailContainer as RectTransform;
        if (contentRT != null)
        {
            float totalHeight = _detailRowPadding * 2f + _detailCursor * _detailRowHeightPlusGap;
            contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, totalHeight);
        }

        if (verboseDiagnostics && _currentDetail != _lastLoggedDetail)
        {
            _lastLoggedDetail = _currentDetail;
            bool containerActive = _detailContainer.gameObject.activeInHierarchy;
            Debug.Log($"[StatsPanelUI] RefreshDetail({_currentDetail}): wrote {_detailCursor} rows " +
                $"into '{_detailContainer.name}', content height set to " +
                $"{(contentRT != null ? contentRT.sizeDelta.y : 0f):0}px " +
                $"(activeInHierarchy={containerActive}).");

            if (!_dumpedHierarchy)
            {
                _dumpedHierarchy = true;
                DumpDetailHierarchy();
            }
        }
    }

    private bool _dumpedHierarchy;

    private Cat _lastLoggedDetail = (Cat)(-1);
    private float _detailRowHeightPlusGap => detailRowHeight + detailRowGap;
    private const float _detailRowPadding = 8f;

    private void DetailPlayer()
    {
        DHeader("PLAYER");
        if (_player == null) { DNote("Player not found", StatRowBuilder.BadColor); return; }

        float sprint = _player.moveSpeed * _player.sprintMultiplier;
        DRow("Sprint Speed", $"{sprint:0.#}", _player.sprintMultiplier);
        DRow("Dash Charges", $"{_player.maxDashes} max", 1f);
        DRow("Dash Speed", $"{_player.dashSpeed:0.#}", 1f);
        DRow("Dash Time", $"{_player.dashTime:0.##}s", 1f);
        DRowColored("Dash Regen", $"+{_player.dashRegenRate:0.#}/s", StatRowBuilder.GoodColor);

        DSpacer();
        DNote("Stamina Costs", StatRowBuilder.AccentColor);
        DRowColored("Stamina Regen", $"+{_player.staminaRegenRate:0.#}/s", StatRowBuilder.GoodColor);
        DRowColored("Melee Swing", $"-{_player.meleeAttackStaminaCost:0.#}", StatRowBuilder.WarningColor);
        DRowColored("Ranged Shot", $"-{_player.rangedAttackStaminaCost:0.#}", StatRowBuilder.WarningColor);
        DRowColored("Flamethrower", $"-{_player.flamethrowerStaminaDrainPerSec:0.#}/s", StatRowBuilder.WarningColor);
        DRowColored("Obstacle Draw", $"-{_player.obstacleDrawerStaminaCost:0.#}", StatRowBuilder.WarningColor);
        DRowColored("Grappling Hook", $"-{_player.grapplingHookStaminaCost:0.#}", StatRowBuilder.WarningColor);
        DRowColored("Shield/Parry", $"-{_player.shieldBlockStaminaCost:0.#}", StatRowBuilder.WarningColor);
    }

    private void DetailWeapon()
    {
        DHeader("WEAPON");
        var w = ResolveWeaponData();
        if (w == null) { DNote("No weapon equipped", StatRowBuilder.WarningColor); return; }

        float dmgMul = AugmentMath.Multiplier("damage", "Weapon");

        DNote(w.weaponName, StatRowBuilder.AccentColor);
        DRow("Damage", $"{w.damage:0.#}", dmgMul);
        if (!w.isGrapplingHook && !w.isObstacleDrawer)
            DRow("DPS", ComputeDpsText(w), dmgMul);
        DRow("Cooldown", $"{w.attackCooldown:0.##}s", 1f, lowerIsBetter: true);
        if (w.armorBonus > 0) DRow("Armor Bonus", $"+{w.armorBonus:0.#}", 1f);
        if (w.knockBack) DRow("Knockback", $"{w.knockBackForce:0.#}", 1f);

        if (w.isRanged)
        {
            DRow("Projectile Speed", $"{w.projectileSpeed:0.#}", 1f);
        }
        else if (w.isGrapplingHook)
        {
            DRow("Hook Range", $"{w.hookRange:0.#}", 1f);
            DRow("Hook Speed", $"{w.hookSpeed:0.#}", 1f);
            DRow("Pull Force", $"{w.pullForce:0.#}", 1f);
        }
        else if (w.isObstacleDrawer)
        {
            DRow("Max Obstacles", $"{w.maxObstacles}", 1f);
        }
        else if (w.isHammer)
        {
            DRow("Slam Radius (AoE)", $"{w.hammerSlamRadius:0.#} units", 1f);
            DRow("Knockback Force", $"{w.hammerSlamKnockback:0.#}", 1f);
            DRow("Reach Distance", $"{w.hammerReachDistance:0.#} units", 1f);
            if (w.hammerChargeEnabled)
            {
                DSpacer();
                DNote("Charged Slam", StatRowBuilder.AccentColor);
                DRow("Charge Time", $"{w.hammerChargeTime:0.##}s", 1f);
                DRow("Max Charge Damage", $"+{w.hammerChargeBonus * 100f:0}%", 1f);
                DRow("Max Charge Radius", $"x{w.hammerChargeRadiusBonus:0.00}", 1f);
                DRow("Max Charge Reach", $"x{w.hammerChargeReachBonus:0.00}", 1f);
            }
        }
        else if (w.isFlamethrower)
        {
            DRow("Range", $"{w.flameRange:0.#} units", 1f);
            DRow("Cone Angle", $"{w.flameConeAngle:0}°", 1f);
            DRow("Tick Interval", $"{w.flameDamageInterval:0.00}s", 1f, lowerIsBetter: true);
            DRow("Fuel", $"{w.flameFuelMax:0}", 1f);
        }
        else if (w.isBoomerang)
        {
            DRow("Range", $"{w.boomerangRange:0.#} units", 1f);
            DRow("Curve", $"{w.boomerangCurve:0.#}", 1f);
        }
    }

    private void DetailBase()
    {
        DHeader("BASE");
        var core = FindFirstObjectByType<CentralCore>();
        if (core == null) { DNote("Core offline", StatRowBuilder.BadColor); return; }

        // Status sub-header colored by energy state.
        string status = core.IsEnergyDepleted() ? "DEPLETED"
                       : core.IsEnergyLow() ? "CRITICAL"
                       : "OPERATIONAL";
        Color statusColor = core.IsEnergyDepleted() ? StatRowBuilder.BadColor
                          : core.IsEnergyLow() ? StatRowBuilder.WarningColor
                          : StatRowBuilder.GoodColor;
        DNote(status, statusColor);

        float pct = core.GetEnergyPercentage();
        Color energyColor = pct < 0.3f ? StatRowBuilder.BadColor
                          : pct < 0.5f ? StatRowBuilder.WarningColor
                          : StatRowBuilder.GoodColor;
        DRowColored("Energy", $"{core.GetEnergy():0} / {core.GetMaxEnergy():0}", energyColor);
        DRowColored("Energy %", $"{pct * 100f:0}%", energyColor);

        if (core.GetArmor() > 0)
            DRowColored("Damage Mitigation", $"{core.GetArmor() * 100f:0}%", StatRowBuilder.GoodColor);
    }

    private void DetailTower()
    {
        DHeader("TOWER");
        var towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);

        if (EnergyManager.Instance != null)
        {
            DRow("Build Cost", $"{EnergyManager.Instance.GetTowerBuildCost()}", 1f);
            DRow("Sell Value", $"{EnergyManager.Instance.GetTowerSellValue()}", 1f);
            DSpacer();
        }

        if (towers.Length == 0) { DNote("No towers built", StatRowBuilder.WarningColor); return; }

        foreach (var group in towers.GroupBy(t => t.towerType))
        {
            var t = group.First();
            DNote($"{t.towerType}  x{group.Count()}", StatRowBuilder.AccentColor);
            if (t.IsGenerator())
            {
                DRow("Gen Rate", $"{t.GetGenerationRate():0.#}/s",
                     AugmentMath.Multiplier("energyGenerationRate", "Tower"));
                DRow("Gen Range", $"{t.generationRange:0.#}", 1f);
                DRow("Self Cost", $"{t.generatorSelfConsumption * 100f:0}%", 1f,
                     lowerIsBetter: true);
            }
            else
            {
                DRow("Damage", $"{t.GetDamage():0.#}", AugmentMath.Multiplier("damage", "Tower"));
                DRow("Range", $"{t.GetRange():0.#}", AugmentMath.Multiplier("range", "Tower"));
                DRow("Fire Rate", $"{t.GetFireRate():0.00}/s", AugmentMath.Multiplier("fireRate", "Tower"));
                if (t.freezeChance > 0)
                    DRowColored("Freeze Chance", $"{t.freezeChance * 100f:0}%", StatRowBuilder.AccentColor);
                if (!Mathf.Approximately(t.energyCostMultiplier, 1f))
                    DRow("Energy Cost", $"x{t.energyCostMultiplier:0.00}",
                         t.energyCostMultiplier, lowerIsBetter: true);
            }

            // Shared between attacker and generator towers.
            DRow("Max Energy", $"{t.GetMaxEnergy():0}",
                 AugmentMath.Multiplier("maxEnergy", "Tower"));
            if (t.GetArmor() > 0)
                DRowColored("Armor", $"{t.GetArmor() * 100f:0}%", StatRowBuilder.GoodColor);
            if (t.healthRegenRate > 0)
                DRowColored("Regen", $"+{t.healthRegenRate:0.#}/s", StatRowBuilder.GoodColor);

            DSpacer();
        }
    }

    private void DetailEnemy()
    {
        DHeader("ENEMY");
        var mgr = EnemyStatModifierManager.Instance;
        if (mgr == null) { DNote("No enemy data", StatRowBuilder.NeutralColor); return; }

        DRow("Attack Power", $"x{mgr.GetDamageMultiplier():0.00}", mgr.GetDamageMultiplier());
        DRow("Health", $"x{mgr.GetHealthMultiplier():0.00}", mgr.GetHealthMultiplier());
        DRow("Move Speed", $"x{mgr.GetMoveSpeedMultiplier():0.00}", mgr.GetMoveSpeedMultiplier());

        DSpacer();

        var alive = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None)
            .Where(e => e != null && !e.IsDead() && e.enemyData != null)
            .ToList();

        if (alive.Count == 0)
        {
            DNote("No enemies on field", StatRowBuilder.NeutralColor);
            return;
        }

        DNote("Current Wave", StatRowBuilder.AccentColor);
        DRow("Count Alive", $"{alive.Count}", 1f);

        // Total HP pool — sum of remaining health across all live enemies.
        float totalHp = alive.Sum(e => e.currentHealth);
        float totalMaxHp = alive.Sum(e => e.maxHealth);
        DRow("Total HP Pool", $"{totalHp:0} / {totalMaxHp:0}", 1f);

        DSpacer();
        DNote("By Type", StatRowBuilder.AccentColor);

        // Group by enemy type, one row each with count, base damage, base health.
        var groups = alive
            .GroupBy(e => e.enemyData.enemyName)
            .OrderByDescending(g => g.Count());

        foreach (var g in groups)
        {
            var sample = g.First();
            int count = g.Count();
            float dmg = sample.enemyData.damage * mgr.GetDamageMultiplier();
            float hp = sample.enemyData.maxHealth * mgr.GetHealthMultiplier();

            // One row per type, "Name xN  →  X dmg / Y HP". The label sits in
            // the left column at full visibility, stats in the right column.
            DRow($"{sample.enemyData.enemyName} x{count}",
                 $"{dmg:0}/{hp:0}", 1f);
        }
    }

    private void DetailGlobal()
    {
        DHeader("GLOBAL");
        var em = EnergyManager.Instance;
        if (em == null) { DNote("No energy data", StatRowBuilder.NeutralColor); return; }

        DRow("Player Energy", $"{em.GetPlayerEnergy()}", 1f);
        DRow("Resource Drop", $"x{em.globalResourceMultiplier:0.00}", em.globalResourceMultiplier);
        if (em.bonusResourceDropChance > 0)
            DRowColored("Bonus Drop Chance", $"{em.bonusResourceDropChance * 100f:0}%",
                        StatRowBuilder.GoodColor);
        DRow("Tower Cost", $"{em.GetTowerBuildCost()}", 1f);
        DRow("Tower Sell Value", $"{em.GetTowerSellValue()}", 1f);
        if (!Mathf.Approximately(em.globalEnergyDecayRate, 1f))
            DRow("Energy Decay", $"x{em.globalEnergyDecayRate:0.00}",
                 em.globalEnergyDecayRate, lowerIsBetter: true);
    }

    private void DHeader(string t) => WriteDetailFull(StatRowBuilder.ComposeHeader(t), TextAlignmentOptions.Top);
    private void DNote(string t, Color c) => WriteDetailFull(StatRowBuilder.ComposeNote(t, c), TextAlignmentOptions.TopLeft);
    private void DSpacer() => WriteDetailFull(" ", TextAlignmentOptions.TopLeft);
    private void DRow(string label, string value, float mult, bool lowerIsBetter = false)
    {
        var (left, right) = StatRowBuilder.ComposeParts(label, value, mult, lowerIsBetter, hideWhenNeutral: false);
        WriteDetailRow(left, right);
    }

    /// <summary>Row where the value text is tinted a specific color (semantic).</summary>
    private void DRowColored(string label, string value, Color valueColor)
    {
        string left = $"<color=#{StatRowBuilder.Hex(StatRowBuilder.LabelColor)}>{label}</color>";
        string right = $"<color=#{StatRowBuilder.Hex(valueColor)}>{value}</color>";
        WriteDetailRow(left, right);
    }

    [Header("Detail Row Sizing")]
    [Tooltip("Font size for detail rows. The only knob you usually need to change. " +
             "Row height is computed automatically as fontSize * rowHeightMultiplier.")]
    public float detailRowFontSize = 14f;
    [Tooltip("If a label is too long to fit, it auto-shrinks down to this size " +
             "before clipping. Keeps long labels on one line instead of wrapping.")]
    public float detailMinFontSize = 10f;
    [Tooltip("Row height = fontSize * this. 1.55 gives a tight readable line. " +
             "Raise to 1.7 for airier rows.")]
    [Range(1.2f, 2.5f)]
    public float detailRowHeightMultiplier = 1.55f;
    [Tooltip("Vertical gap (px) between detail rows.")]
    public float detailRowGap = 4f;
    [Tooltip("Left inset (px) for detail row text.")]
    public float detailRowLeftInset = 14f;
    [Tooltip("Right inset (px) for detail row text.")]
    public float detailRowRightInset = 14f;
    [Tooltip("Empty space (px) above the first row.")]
    public float detailTopInset = 14f;

    /// <summary>Computed row height — always sized to fit the current font.</summary>
    private float detailRowHeight => detailRowFontSize * detailRowHeightMultiplier;

    /// <summary>A pooled detail row: a parent rect with a left + right TMP.</summary>
    private class DetailRowObj
    {
        public RectTransform root;
        public TextMeshProUGUI left;
        public TextMeshProUGUI right;
    }
    private readonly List<DetailRowObj> _detailRowObjs = new List<DetailRowObj>();

    /// <summary>Writes a two-column row: label flush-left, value flush-right.</summary>
    private void WriteDetailRow(string leftText, string rightText)
    {
        var row = GetDetailRow(_detailCursor);
        if (row == null) return;

        row.left.gameObject.SetActive(true);
        row.right.gameObject.SetActive(true);
        row.left.text = leftText;
        row.right.text = rightText;

        PlaceRow(row.root);
        _detailCursor++;
    }

    /// <summary>Writes a single full-width line (header / note / spacer).</summary>
    private void WriteDetailFull(string text, TextAlignmentOptions align)
    {
        var row = GetDetailRow(_detailCursor);
        if (row == null) return;

        row.left.gameObject.SetActive(true);
        row.right.gameObject.SetActive(false);   // hide the right column
        row.left.text = text;
        row.left.alignment = align;

        // Header/note spans the FULL row width (label column normally stops at
        // the split point). Stretch the left object across the whole row.
        var lrt = row.left.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        PlaceRow(row.root);
        _detailCursor++;
    }

    private void PlaceRow(RectTransform rt)
    {
        float y = -detailTopInset - _detailCursor * _detailRowHeightPlusGap;
        rt.anchoredPosition = new Vector2(0f, y);
    }

    /// <summary>Fetches a pooled row, creating it if needed.</summary>
    private DetailRowObj GetDetailRow(int index)
    {
        if (_detailContainer == null) return null;

        while (_detailRowObjs.Count <= index)
        {
            var created = SpawnDetailRow();
            if (created == null) return null;
            _detailRowObjs.Add(created);
        }
        // Reset the row each fetch: a previous use as a header/note stretched
        // the label column to full width — restore it to the left column.
        var row = _detailRowObjs[index];
        float split = Mathf.Clamp(detailLabelWidthFraction, 0.4f, 0.85f);
        var lrt = row.left.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(split, 1f);
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        row.left.alignment = TextAlignmentOptions.Left;
        return row;
    }

    [Tooltip("Fraction of the row width given to the label column (0-1). " +
             "The value column gets the rest.")]
    [Range(0.4f, 0.85f)]
    public float detailLabelWidthFraction = 0.68f;

    // Creates one detail row
    private DetailRowObj SpawnDetailRow()
    {
        // Parent row.
        var go = new GameObject("DetailRow", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_detailContainer, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, detailRowHeight);
        rt.offsetMin = new Vector2(detailRowLeftInset, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-detailRowRightInset, rt.offsetMax.y);

        var fontSource = _sections.SelectMany(s => s.rows.Values)
                                  .FirstOrDefault(t => t != null);

        float split = Mathf.Clamp(detailLabelWidthFraction, 0.4f, 0.85f);

        // anchorMinX/anchorMaxX define which horizontal slice of the row this
        // text occupies. Label = [0 .. split], Value = [split .. 1].
        TextMeshProUGUI MakeText(string name, TextAlignmentOptions align,
                                 float anchorMinX, float anchorMaxX)
        {
            var t = new GameObject(name, typeof(RectTransform));
            var trt = (RectTransform)t.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = new Vector2(anchorMinX, 0f);
            trt.anchorMax = new Vector2(anchorMaxX, 1f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var tmp = t.AddComponent<TextMeshProUGUI>();
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            if (fontSource != null) tmp.font = fontSource.font;

            // Auto-size: text shrinks down to detailMinFontSize if it would
            // otherwise wrap or overflow. This keeps every row to a single
            // line so they can't overlap with the next row.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = detailMinFontSize;
            tmp.fontSizeMax = detailRowFontSize;
            tmp.fontSize = detailRowFontSize;
            return tmp;
        }

        return new DetailRowObj
        {
            root = rt,
            left = MakeText("Label", TextAlignmentOptions.Left, 0f, split),
            right = MakeText("Value", TextAlignmentOptions.Right, split, 1f),
        };
    }

    //  Helpers
    private WeaponData ResolveWeaponData()
    {
        Weapon weapon = null;
        if (_player != null) weapon = _player.GetComponentInChildren<Weapon>();
        if (weapon == null) weapon = FindAnyObjectByType<Weapon>();
        return weapon != null ? weapon.GetWeaponData() : null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}

