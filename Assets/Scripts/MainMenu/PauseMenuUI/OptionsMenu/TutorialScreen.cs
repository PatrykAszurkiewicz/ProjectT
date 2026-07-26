using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

//  TUTORIAL SCREEN
//  Tutorial overlay, built programmatically and skinned
//  with shared MenuTheme (same MenuPanel 1 frame, Button 1 sprites, purple
//  gradient backdrop and Cinzel font used by OptionsMenu / ContinueRunMenu).
//  CONTROLS-AWARE - resolved at runtime from your real PlayerInputActions asset,
//  so it shows whatever each action is actually bound to (and follows live
//  rebinds), falling back to clean defaults if the asset can't be found.
//  Also carries procedural diagrams (tether zones, tower types, energy flow) -
//  no sprites or prefabs required.

public class TutorialScreen : MonoBehaviour
{
    [Header("Fonts (optional - matches OptionsMenu)")]
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private Font titleFontTtf;

    [Header("Input (optional - auto-resolved if left empty)")]
    [Tooltip("Drag your PlayerInputActions asset here for exact, rebind-aware " +
             "control hints. If empty, the screen tries a PlayerInput in the " +
             "scene, then Resources/PlayerInputActions, then built-in defaults.")]
    public InputActionAsset inputActions;

    [Header("Economy labels (auto-read from EnergyManager if present)")]
    public int towerBuildCost = 100;
    public int towerUpgradeCost = 50;
    public int towerDisassembleRefund = 50;
    public int playerStartingEnergy = 300;

    [Header("Behaviour")]
    [Tooltip("Freeze the game (Time.timeScale = 0) while open. Harmless on the " +
             "main menu; useful if you open it mid-run.")]
    public bool pauseGameWhileOpen = true;

    [Tooltip("Which control scheme to show first. Auto = most recently used device.")]
    public DefaultScheme defaultScheme = DefaultScheme.Auto;
    public enum DefaultScheme { Auto, KeyboardMouse, Gamepad }

    //  runtime state 
    private GameObject _root;
    private TMP_FontAsset _font;
    private bool _isOpen;
    private bool _showGamepad;

    private readonly List<ChipBinding> _chips = new List<ChipBinding>();
    private Button _kbTab, _padTab;

    private const string GROUP_KBM = "Keyboard&Mouse";
    private const string GROUP_PAD = "Gamepad";

    // theme-derived colours
    private static readonly Color RowA = new Color(0.78f, 0.30f, 0.92f, 0.07f);
    private static readonly Color RowB = new Color(1f, 1f, 1f, 0.025f);
    private static readonly Color DescCol = new Color(0.93f, 0.91f, 0.97f, 1f);
    // Tips used to be italic magenta on a pale lilac stripe - effectively invisible.
    // Near-white body, gold "TIP" badge, no italics.
    private static readonly Color TipCol = new Color(0.97f, 0.96f, 1.00f, 1f);
    private const string TIP_BADGE = "<b><color=#FFC44D>TIP</color></b>   ";

    // tether zone colours - copied from PlayerTowerTether so the diagram matches
    // the in-game beam colours exactly.
    private static readonly Color FarCol = new Color(1.00f, 0.85f, 0.30f, 1f); // gold   (range)
    private static readonly Color MidCol = new Color(1.00f, 0.55f, 0.20f, 1f); // orange (damage)
    private static readonly Color NearCol = new Color(0.30f, 0.90f, 1.00f, 1f); // cyan   (defense)


    public static void ShowTutorial()
    {
        // Include inactive: the scene-placed instance (with your font slots and
        // inputActions asset assigned) may be disabled. Creating a bare one instead
        // silently loses that configuration.
        var inst = FindFirstObjectByType<TutorialScreen>(FindObjectsInactive.Include);
        if (inst == null) inst = new GameObject("TutorialScreen").AddComponent<TutorialScreen>();
        inst.Open();
    }

    public void Open()
    {
        MenuTheme.EnsureEventSystem();
        if (_root == null) BuildUI();

        RefreshEconomyFromGame();
        ApplyRebindOverrides(ResolveAsset());   // reflect any committed rebinds
        _showGamepad = ResolveInitialScheme();
        RefreshAllChips();
        UpdateSchemeTabs();

        _isOpen = true;
        _root.SetActive(true);

        UIModalStack.Push(this, freeze: pauseGameWhileOpen && UIModalStack.GameplayActive);
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        if (_root != null) _root.SetActive(false);
        UIModalStack.Pop(this);
    }

    public void Toggle() { if (_isOpen) Close(); else Open(); }

    private void Update()
    {
        if (!_isOpen) return;

        // Esc / Start / B is a CONSUMABLE press. We only take it when we are the
        // frontmost modal, and taking it stops PauseMenuController (and anything
        // else listening on the same frame) from also reacting. Previously both
        // ran, in undefined order.
        if (MenuBackInput.ConsumeBack(this)) Close();
    }

    //  CONTENT
    private void OnDisable()
    {
        // Scene reload / destroyed while open: never leave the stack (and therefore
        // Time.timeScale) pinned by a screen that no longer exists.
        if (_isOpen) { _isOpen = false; UIModalStack.Pop(this); }
    }

    private List<Section> BuildContent()
    {
        string buildCost = towerBuildCost.ToString();
        string upCost = towerUpgradeCost.ToString();
        string refund = towerDisassembleRefund.ToString();
        string start = playerStartingEnergy.ToString();

        var s = new List<Section>();

        s.Add(new Section("YOUR MISSION", new List<Entry>
        {
            Info("Defend the Central Core",
                 "Enemies attack the Core. If its energy hits zero, the run ends.",
                 "GOAL"),
            Info("Energy Is Everything",
                 "Energy builds, upgrades and repairs. Towers and the Core burn it over time.",
                 "Lifeblood")
        }));

        s.Add(new Section("MOVEMENT", new List<Entry>
        {
            Fallback("Move", "WASD", "Left Stick", "Walk around the battlefield."),
            Bind("Sprint", "Sprint", "leftShift", "leftShoulder", "Left Shift", "LB",
                 "Hold to run faster.", "Hold to sprint, tap to dodge."),
            Bind("Dodge / Dash", "Dash", "leftShift", "buttonEast", "Left Shift", "B",
                 "A quick burst of speed to escape attacks."),
            Fallback("Aim", "Mouse", "Right Stick",
                 "Aims your weapon and the build reticle.")
        }));

        s.Add(new Section("COMBAT", new List<Entry>
        {
            Bind("Attack", "AttackWeapon", "leftButton", "rightTrigger", "Left Click", "RT",
                 "Attack with your weapon.", "Hold it down for the hammer and flamethrower."),
            Bind("Use Tool", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 "Use your tool: shield, bombs, traps, turrets."),
            new Entry {
                title = "Switch Weapon", desc = "Cycle to the next weapon.",
                fallbackOnly = true, kbFallback = "Scroll Down", gpFallback = "D-Pad",
                tip = "Scroll DOWN for weapons, UP for tools. Keys 1 and 2 also work."
            },
            new Entry {
                title = "Switch Tool", desc = "Cycle to the next tool.",
                fallbackOnly = true, kbFallback = "Scroll Up", gpFallback = "D-Pad"
            },
            Bind("Block", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 "Hold your shield up to absorb hits."),
            Bind("Parry", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 "Raise the shield the moment a \"!\" appears above an enemy. Negates the hit and stuns them.",
                 "Ranged enemies show no \"!\". Parry their shots in flight.")
        }));

        s.Add(new Section("BUILDING & TOWERS", new List<Entry>
        {
            Bind("Enter Build Mode", "Placement", "space", "buttonNorth", "Space", "Y",
                 "Toggle build mode. Free tower slots light up.",
                 "Press again to exit, or to back out of a menu."),
            Bind("Build Tower", "Build", "leftButton", "rightTrigger", "Left Click", "RT",
                 $"Aim at an empty slot and build. Costs {buildCost} energy."),
            Fallback("Pick From the Wheel", "Aim + Left Click", "Right Stick + RT",
                 "With several tower types, a wheel opens. Aim to highlight, press Build to confirm."),
            Bind("Open Tower Menu", "AttackTool", "rightButton", "leftTrigger", "Right Click", "LT",
                 "Aim at your tower to open Upgrade / Disassemble."),
            Fallback("Navigate the Menu", "Aim / Move", "Left Stick",
                 "Highlight an option in the menu."),
            Bind("Confirm the Choice", "Build", "leftButton", "rightTrigger", "Left Click", "RT",
                 "Confirms the highlighted option.",
                 "Tool always cancels safely. It never triggers an action by accident."),
            Bind("Supply / Repair", "Build", "leftButton", "rightTrigger", "Hold L-Click", "Hold RT",
                 "Hold on a damaged tower or the Core to refill its energy."),
            Info("Tower Synergy",
                 "Towers built close together boost each other's damage.",
                 "Cluster")
        })
        { visual = BuildPlacementSteps });

        // UPGRADES - ladder diagram beneath the rows
        s.Add(new Section("UPGRADE & DISASSEMBLY", new List<Entry>
        {
            Info("Upgrade a Tower",
                 $"Costs {upCost} energy. Grants +20% output and +20% health.",
                 $"-{upCost}"),
            Info("Two Upgrades Maximum",
                 "A tower can be upgraded twice, up to Level 3.",
                 "Max Lv3"),
            Info("Greyed Out?",
                 "Upgrade locks at max level, or when you cannot afford it.",
                 "Locked"),
            Info("Disassemble a Tower",
                 $"Removes the tower, frees the slot, and returns +{refund} energy.",
                 $"+{refund}")
        })
        { visual = BuildUpgradeLadder });

        // TOWER TYPES - grid diagram beneath the rows
        s.Add(new Section("TOWER TYPES", new List<Entry>
        {
            Info("Pick the Right Tower",
                 "Each type answers a different threat. Mix them.",
                 "5 Types")
        })
        { visual = BuildTowerTypeGrid });

        // TETHER - ring diagram beneath the rows
        s.Add(new Section("TETHER BUFFS", new List<Entry>
        {
            Info("Stand Near Your Towers",
                 "Tethers form automatically. Your distance decides which buff they get.",
                 "Proximity")
        })
        { visual = BuildTetherDiagram });

        // ENERGY - flow strip beneath the rows
        s.Add(new Section("ENERGY MANAGEMENT", new List<Entry>
        {
            Info("Collect Energy",
                 $"You start with {start}. Kill enemies and walk over their drops.",
                 "Walk over"),
            Info("Decay Is the Real Enemy",
                 "Towers and the Core lose energy over time. At zero, a tower dies and the Core falls.",
                 "Warning"),
            Info("Spend Wisely",
                 $"Build costs {buildCost}, upgrade costs {upCost}. Refunds are partial.",
                 "Economy")
        })
        { visual = BuildEnergyStrip });

        s.Add(new Section("AUGMENTS & BLUEPRINTS", new List<Entry>
        {
            Info("Augments (This Run)",
                 "Pick an Augment between challenges. They reset next run.",
                 "Per Run"),
            Info("Blueprints (Permanent)",
                 "Bosses drop weapon blueprints. Walk over one to keep it forever.",
                 "Permanent"),
            Info("What a Blueprint Does",
                 "It does not give you the weapon. It lets that weapon appear in your Augment choices.",
                 "Unlocks")
        }));

        // GREMLINS - trail diagram beneath the rows
        s.Add(new Section("GREMLINS", new List<Entry>
        {
            Info("Catch It for Free Energy",
                 "A harmless gremlin roams the map. It flees when you get close.",
                 "Bonus"),
            Fallback("Chase and Hit It", "Any Attack", "Any Attack",
                 "It runs at 70% of your speed, so you can catch it. One hit kills it and it drops energy."),
            Info("Follow the Footprints",
                 "A footprint trail leads to the nearest gremlin.",
                 "Trail")
        })
        { visual = BuildGremlinTrail });

        // VORTEX - red trail diagram beneath the rows
        s.Add(new Section("VORTEX", new List<Entry>
        {
            Info("A Rift That Spawns Enemies",
                 "A vortex tears open on the map and spits out waves of enemies until you close it.",
                 "Threat"),
            Fallback("Destroy It", "Attack It", "Attack It",
                 "The vortex is killable. Fight through its spawns and break it to stop the flow."),
            Info("Follow the Red Trail",
                 "Red footprints lead to the nearest vortex. Deal with it before it overwhelms you.",
                 "Red Trail")
        })
        { visual = BuildVortexTrail });

        // LORE - chest trail diagram beneath the rows
        s.Add(new Section("LORE CHESTS & THE ARCHIVE", new List<Entry>
        {
            Info("Chests in the Field",
                 "Chests spawn out in the open. A footprint trail leads to the nearest one.",
                 "Explore"),
            Fallback("Open the Chest", "Walk Into It", "Walk Into It",
                 "A scroll unfurls with a piece of the story. The game pauses while you read."),
            Fallback("Dismiss the Scroll", "Click / Space / Esc", "Any Button",
                 "The fragment is saved permanently."),
            Fallback("Lore Archive", "J", "Options Menu",
                 "Read every fragment you have collected. It carries across runs.")
        })
        { visual = BuildChestTrail });

        s.Add(new Section("SYSTEM", new List<Entry>
        {
            Bind("Pause", "Pause", "escape", "start", "Esc", "Start", "Pause the game and open the menu.")
        }));

        return s;
    }

    // entry factories 
    private Entry Info(string title, string desc, string chip)
        => new Entry { title = title, desc = desc, infoChip = chip };

    private Entry Fallback(string title, string kb, string gp, string desc)
        => new Entry { title = title, desc = desc, fallbackOnly = true, kbFallback = kb, gpFallback = gp };

    private Entry Bind(string title, string action, string kbPrefer, string gpPrefer,
                       string kbFallback, string gpFallback, string desc, string tip = null)
        => new Entry
        {
            title = title,
            desc = desc,
            tip = tip,
            action = action,
            kbPrefer = kbPrefer,
            gpPrefer = gpPrefer,
            kbFallback = kbFallback,
            gpFallback = gpFallback
        };

    //  PROCEDURAL DIAGRAMS

    // Shared card background for every diagram: rounded, subtly lit panel.
    private GameObject DiagramCard(Transform parent, string name, float height)
    {
        var holder = MenuTheme.NewUI(name, parent);
        SetH(holder, height);
        var bg = holder.AddComponent<Image>();
        bg.sprite = RoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0f, 0f, 0f, 0.34f);
        return holder;
    }

    //  TETHER: player at the centre, towers sitting in each zone 
    // Matches PlayerTowerTether: NEAR = 0-30% of range, MID = 30-60%, FAR = 60-100%.
    // The PLAYER is the centre because the tether measures the distance FROM YOU
    // to each tower - walking changes which buff every tower receives.
    private void BuildTetherDiagram(Transform parent)
    {
        var holder = DiagramCard(parent, "TetherDiagram", 470);

        // The circular field. Anchored left, its own square area.
        var field = MenuTheme.NewUI("Field", holder.transform);
        var frt = field.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0.40f, 1f);
        frt.offsetMin = new Vector2(14, 14); frt.offsetMax = new Vector2(-6, -14);

        const float D = 360f;                       // outer diameter
        float[] frac = { 1.00f, 0.60f, 0.30f };     // FAR, MID, NEAR
        Color[] cols = { FarCol, MidCol, NearCol };

        // rings, largest first so smaller ones stack on top
        for (int i = 0; i < 3; i++)
        {
            var ring = MenuTheme.NewUI("Zone" + i, field.transform);
            var img = ring.AddComponent<Image>();
            img.sprite = CircleSprite();
            var c = cols[i]; c.a = 0.26f + i * 0.13f;
            img.color = c;
            CenterSquare(ring, D * frac[i]);
        }

        // a tower parked in each zone band, on the tether line
        //   NEAR band centre ~15% of D/2, MID ~45%, FAR ~80%
        PlaceTetherTower(field.transform, NearCol, 0.150f, D, 90f);
        PlaceTetherTower(field.transform, MidCol, 0.450f, D, 205f);
        PlaceTetherTower(field.transform, FarCol, 0.800f, D, 330f);

        // the player, drawn last so it sits on top of the tether lines
        var player = MenuTheme.NewUI("Player", field.transform);
        var pImg = player.AddComponent<Image>();
        pImg.sprite = PlayerSprite();
        pImg.color = Color.white;
        CenterSquare(player, 54f);

        var youLbl = MenuTheme.NewText("PLAYER", field.transform, 20, TextAlignmentOptions.Center, _font);
        youLbl.fontStyle = FontStyles.Bold;
        youLbl.color = Color.white;
        var yrt = youLbl.rectTransform;
        yrt.anchorMin = yrt.anchorMax = new Vector2(0.5f, 0.5f);
        yrt.pivot = new Vector2(0.5f, 0.5f);
        yrt.sizeDelta = new Vector2(120, 26);
        yrt.anchoredPosition = new Vector2(0, -40);

        // legend on the right - now with room to breathe
        var legend = MenuTheme.NewUI("Legend", holder.transform);
        var lrt = legend.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0.41f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(6, 16); lrt.offsetMax = new Vector2(-18, -16);
        var lv = legend.AddComponent<VerticalLayoutGroup>();
        lv.spacing = 12; lv.childControlWidth = true; lv.childControlHeight = true;
        lv.childForceExpandWidth = true; lv.childForceExpandHeight = false;
        lv.childAlignment = TextAnchor.MiddleLeft;

        LegendRow(legend.transform, NearCol, "NEAR", "0 - 30%",
                  "DEFENSE. Slows the energy decay of tethered towers and of the Core. " +
                  "Every extra tether cuts decay further.");
        LegendRow(legend.transform, MidCol, "MID", "30 - 60%",
                  "DAMAGE. About +10% tower damage for each tethered tower.");
        LegendRow(legend.transform, FarCol, "FAR", "60 - 100%",
                  "RANGE. About +5% tower range for each tethered tower. Good for sniping.");

        var note = MenuTheme.NewText(
            //"Walk in to defend  |  hang back to boost damage  |  stand off to extend range"
            "",
            legend.transform, 19, TextAlignmentOptions.Left, _font);
        note.color = TipCol; note.fontStyle = FontStyles.Normal;
        note.textWrappingMode = TextWrappingModes.Normal;
        SetH(note, 50);
    }

    // Drop a tower icon at `distFrac` of the field radius, at `angleDeg`, and draw
    // the coloured tether beam from the player to it.
    private void PlaceTetherTower(Transform field, Color col, float distFrac, float D, float angleDeg)
    {
        float radius = (D * 0.5f) * distFrac;
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 pos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;

        // beam: a thin rotated bar from centre to the tower
        var beam = MenuTheme.NewUI("Beam", field);
        var bImg = beam.AddComponent<Image>();
        bImg.color = new Color(col.r, col.g, col.b, 0.95f);
        var brt = beam.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);              // rotate about the player
        brt.sizeDelta = new Vector2(radius, 5f);
        brt.anchoredPosition = Vector2.zero;
        brt.localRotation = Quaternion.Euler(0, 0, angleDeg);

        // tower icon
        var tower = MenuTheme.NewUI("Tower", field);
        var tImg = tower.AddComponent<Image>();
        tImg.sprite = TowerSprite();
        tImg.color = col;
        var trt = tower.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(46, 46);
        trt.anchoredPosition = pos;
    }

    private static void CenterSquare(GameObject go, float size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;
    }

    private void LegendRow(Transform parent, Color col, string zone, string range, string desc)
    {
        var row = MenuTheme.NewUI("Legend_" + zone, parent);
        SetH(row, 92);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16; h.padding = new RectOffset(6, 6, 4, 4);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;   // <- keeps the swatch a CIRCLE, not an oval
        h.childAlignment = TextAnchor.MiddleLeft;

        // swatch wrapper keeps a fixed square regardless of row height
        var wrap = MenuTheme.NewUI("SwatchWrap", row.transform);
        var wle = wrap.GetComponent<LayoutElement>() ?? wrap.AddComponent<LayoutElement>();
        wle.minWidth = 34; wle.preferredWidth = 34; wle.flexibleWidth = 0;
        wle.minHeight = 34; wle.preferredHeight = 34; wle.flexibleHeight = 0;

        var swatch = MenuTheme.NewUI("Swatch", wrap.transform);
        var si = swatch.AddComponent<Image>();
        si.sprite = CircleSprite(); si.color = col;
        var srt = swatch.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;

        var block = MenuTheme.NewUI("Txt", row.transform);
        var bv = block.AddComponent<VerticalLayoutGroup>();
        bv.spacing = 2; bv.childControlWidth = true; bv.childControlHeight = true;
        bv.childForceExpandWidth = true; bv.childForceExpandHeight = false;
        bv.childAlignment = TextAnchor.MiddleLeft;
        var ble = block.GetComponent<LayoutElement>() ?? block.AddComponent<LayoutElement>();
        ble.flexibleWidth = 1; ble.minWidth = 340;

        var t = MenuTheme.NewText($"{zone}   <size=80%>({range})</size>", block.transform,
                                  23, TextAlignmentOptions.Left, _font);
        t.fontStyle = FontStyles.Bold; t.color = col;
        SetH(t, 28);

        var d = MenuTheme.NewText(desc, block.transform, 19, TextAlignmentOptions.TopLeft, _font);
        d.color = new Color(0.93f, 0.90f, 0.98f, 1f);
        d.textWrappingMode = TextWrappingModes.Normal;
    }

    //  TOWER TYPES: 2-column grid of coloured tokens 
    private void BuildTowerTypeGrid(Transform parent)
    {
        string[,] types =
        {
            { "Basic",     "Reliable single-target damage. Cheap and steady." },
            { "Laser",     "Fast, precise, continuous beam damage." },
            { "Generator", "Produces energy for nearby towers instead of shooting." },
            { "Hammer",    "Heavy close-range smash for leakers near the Core." },
            { "Heal",      "Feeds energy back into neighbouring towers." },
        };
        Color[] cols =
        {
            new Color(0.85f, 0.86f, 0.92f), // Basic
            new Color(0.35f, 0.90f, 1.00f), // Laser
            new Color(0.40f, 0.60f, 1.00f), // Generator
            new Color(0.90f, 0.65f, 0.35f), // Hammer
            new Color(1.00f, 0.55f, 0.75f), // Heal
        };

        // 5 cells in a 2-column grid = 3 rows.
        const float CELL_H = 104f;
        int rows = Mathf.CeilToInt(types.GetLength(0) / 2f);
        var holder = DiagramCard(parent, "TowerGrid", rows * CELL_H + (rows - 1) * 12 + 24);

        var grid = MenuTheme.NewUI("Grid", holder.transform);
        var grt = grid.GetComponent<RectTransform>();
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(14, 12); grt.offsetMax = new Vector2(-14, -12);
        var g = grid.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(604, CELL_H);
        g.spacing = new Vector2(12, 12);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 2;

        for (int i = 0; i < types.GetLength(0); i++)
        {
            var cell = MenuTheme.NewUI("Type_" + types[i, 0], grid.transform);
            var ci = cell.AddComponent<Image>();
            ci.sprite = RoundedSprite(); ci.type = Image.Type.Sliced;
            ci.color = new Color(cols[i].r, cols[i].g, cols[i].b, 0.10f);

            var ch = cell.AddComponent<HorizontalLayoutGroup>();
            ch.spacing = 14; ch.padding = new RectOffset(14, 14, 10, 10);
            ch.childControlWidth = true; ch.childControlHeight = true;
            ch.childForceExpandWidth = false; ch.childForceExpandHeight = false;
            ch.childAlignment = TextAnchor.MiddleLeft;

            // little tower glyph, tinted per type
            var iconWrap = MenuTheme.NewUI("IconWrap", cell.transform);
            var ile = iconWrap.GetComponent<LayoutElement>() ?? iconWrap.AddComponent<LayoutElement>();
            ile.minWidth = 44; ile.preferredWidth = 44; ile.flexibleWidth = 0;
            ile.minHeight = 44; ile.preferredHeight = 44; ile.flexibleHeight = 0;
            var icon = MenuTheme.NewUI("Icon", iconWrap.transform);
            var di = icon.AddComponent<Image>();
            di.sprite = TowerSprite(); di.color = cols[i];
            MenuTheme.Stretch(icon.GetComponent<RectTransform>());

            var block = MenuTheme.NewUI("Txt", cell.transform);
            var bv = block.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 2; bv.childControlWidth = true; bv.childControlHeight = true;
            bv.childForceExpandWidth = true; bv.childForceExpandHeight = false;
            bv.childAlignment = TextAnchor.MiddleLeft;
            var ble = block.GetComponent<LayoutElement>() ?? block.AddComponent<LayoutElement>();
            ble.flexibleWidth = 1; ble.minWidth = 370;

            var t = MenuTheme.NewText(types[i, 0], block.transform, 23, TextAlignmentOptions.Left, _font);
            t.fontStyle = FontStyles.Bold; t.color = cols[i];
            SetH(t, 28);

            var d = MenuTheme.NewText(types[i, 1], block.transform, 18, TextAlignmentOptions.TopLeft, _font);
            d.color = new Color(0.93f, 0.90f, 0.98f, 1f);
            d.textWrappingMode = TextWrappingModes.Normal;
        }
    }

    //  PLACEMENT: numbered steps, one per row 
    // A horizontal 5-node strip crushed the text into unreadable slivers. Full-width
    // numbered rows give each step room for a real key chip and a real sentence.
    private void BuildPlacementSteps(Transform parent)
    {
        var holder = DiagramCard(parent, "PlacementSteps", 5 * 76 + 4 * 8 + 24);

        var col = MenuTheme.NewUI("Col", holder.transform);
        var crt = col.GetComponent<RectTransform>();
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(16, 12); crt.offsetMax = new Vector2(-16, -12);
        var cv = col.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 8; cv.childControlWidth = true; cv.childControlHeight = true;
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;

        StepRow(col.transform, 1, "Space / Y", "Enter build mode", NearCol);
        StepRow(col.transform, 2, "Aim", "Point at an empty slot, or at one of your towers", NearCol);
        StepRow(col.transform, 3, "L-Click / RT", "On an EMPTY SLOT: build a tower", MidCol);
        StepRow(col.transform, 4, "R-Click / LT", "On YOUR TOWER: open the Upgrade / Disassemble menu", MidCol);
        StepRow(col.transform, 5, "L-Click / RT", "Confirm the highlighted option  (R-Click / LT cancels)",
                MenuTheme.Magenta);
    }

    private void StepRow(Transform parent, int number, string key, string text, Color col)
    {
        var row = MenuTheme.NewUI("Step" + number, parent);
        SetH(row, 76);
        var bg = row.AddComponent<Image>();
        bg.sprite = RoundedSprite(); bg.type = Image.Type.Sliced;
        bg.color = new Color(col.r, col.g, col.b, 0.13f);

        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16; h.padding = new RectOffset(14, 14, 8, 8);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;

        // number badge
        var badge = MenuTheme.NewUI("Num", row.transform);
        var bi = badge.AddComponent<Image>();
        bi.sprite = CircleSprite(); bi.color = col;
        var ble0 = badge.GetComponent<LayoutElement>() ?? badge.AddComponent<LayoutElement>();
        ble0.minWidth = 44; ble0.preferredWidth = 44; ble0.flexibleWidth = 0;
        ble0.minHeight = 44; ble0.preferredHeight = 44; ble0.flexibleHeight = 0;
        var nTxt = MenuTheme.NewText(number.ToString(), badge.transform, 24, TextAlignmentOptions.Center, _font);
        nTxt.fontStyle = FontStyles.Bold; nTxt.color = new Color(0.08f, 0.06f, 0.12f);
        MenuTheme.Stretch(nTxt.rectTransform);

        // key chip
        var chip = MenuTheme.NewUI("Key", row.transform);
        var ci = chip.AddComponent<Image>();
        ci.sprite = RoundedSprite(); ci.type = Image.Type.Sliced;
        ci.color = new Color(0f, 0f, 0f, 0.45f);
        var cle = chip.GetComponent<LayoutElement>() ?? chip.AddComponent<LayoutElement>();
        cle.minWidth = 210; cle.preferredWidth = 210; cle.flexibleWidth = 0;
        cle.minHeight = 52; cle.preferredHeight = 52; cle.flexibleHeight = 0;
        var kTxt = MenuTheme.NewText(key, chip.transform, 22, TextAlignmentOptions.Center, _font);
        kTxt.fontStyle = FontStyles.Bold; kTxt.color = col;
        kTxt.textWrappingMode = TextWrappingModes.NoWrap;
        MenuTheme.Stretch(kTxt.rectTransform);

        // instruction
        var t = MenuTheme.NewText(text, row.transform, 22, TextAlignmentOptions.Left, _font);
        t.color = new Color(0.95f, 0.93f, 0.99f, 1f);
        t.textWrappingMode = TextWrappingModes.Normal;
        var tle = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
        tle.flexibleWidth = 1; tle.minWidth = 420;
    }

    //  UPGRADE LADDER: Lv1 -> Lv2 -> Lv3, then disassembly 
    private void BuildUpgradeLadder(Transform parent)
    {
        var holder = DiagramCard(parent, "UpgradeLadder", 380);

        // top: three towers, growing, with the cost on each arrow
        var lane = MenuTheme.NewUI("Lane", holder.transform);
        var lrt = lane.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0.42f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(18, 0); lrt.offsetMax = new Vector2(-18, -14);
        var h = lane.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10; h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        LevelNode(lane.transform, "LEVEL 1", "as built", 52f, new Color(0.75f, 0.78f, 0.88f));
        CostArrow(lane.transform, "-" + towerUpgradeCost);
        LevelNode(lane.transform, "LEVEL 2", "+20% output\n+20% health", 66f, new Color(0.65f, 0.85f, 1.00f));
        CostArrow(lane.transform, "-" + towerUpgradeCost);
        LevelNode(lane.transform, "LEVEL 3", "MAX\n+20% again", 82f, MenuTheme.Magenta);

        // bottom: the disassemble payback
        var foot = MenuTheme.NewUI("Foot", holder.transform);
        var frt = foot.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0.40f);
        frt.offsetMin = new Vector2(18, 16); frt.offsetMax = new Vector2(-18, 0);
        var fh = foot.AddComponent<HorizontalLayoutGroup>();
        fh.spacing = 14; fh.childControlWidth = true; fh.childControlHeight = true;
        fh.childForceExpandWidth = false; fh.childForceExpandHeight = true;
        fh.childAlignment = TextAnchor.MiddleLeft;

        var chip = MenuTheme.NewUI("Refund", foot.transform);
        var ci = chip.AddComponent<Image>();
        ci.sprite = RoundedSprite(); ci.type = Image.Type.Sliced;
        ci.color = new Color(0.45f, 0.90f, 0.55f, 0.22f);
        var cle = chip.GetComponent<LayoutElement>() ?? chip.AddComponent<LayoutElement>();
        cle.minWidth = 300; cle.preferredWidth = 300; cle.flexibleWidth = 0;
        var cTxt = MenuTheme.NewText("DISASSEMBLE\n+" + towerDisassembleRefund + " energy back",
                                     chip.transform, 24, TextAlignmentOptions.Center, _font);
        cTxt.fontStyle = FontStyles.Bold; cTxt.color = new Color(0.60f, 0.95f, 0.65f);
        MenuTheme.Stretch(cTxt.rectTransform);

        var note = MenuTheme.NewText(
            "Towers upgrade twice, to a maximum of LEVEL 3. Each step costs " + towerUpgradeCost +
            " energy and greys out if you cannot afford it. Disassembling returns " +
            towerDisassembleRefund + " energy and frees the slot.",
            foot.transform, 23, TextAlignmentOptions.Left, _font);
        note.color = new Color(0.93f, 0.90f, 0.98f, 1f);
        note.textWrappingMode = TextWrappingModes.Normal;
        var nle = note.GetComponent<LayoutElement>() ?? note.gameObject.AddComponent<LayoutElement>();
        nle.flexibleWidth = 1; nle.minWidth = 500;
    }

    private void LevelNode(Transform parent, string title, string sub, float iconSize, Color col)
    {
        var node = MenuTheme.NewUI("Lv_" + title, parent);
        var img = node.AddComponent<Image>();
        img.sprite = RoundedSprite(); img.type = Image.Type.Sliced;
        img.color = new Color(col.r, col.g, col.b, 0.14f);
        var le = node.GetComponent<LayoutElement>() ?? node.AddComponent<LayoutElement>();
        le.flexibleWidth = 1; le.minWidth = 180;

        var v = MenuTheme.NewUI("V", node.transform);
        var vrt = v.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(8, 8); vrt.offsetMax = new Vector2(-8, -8);
        var vg = v.AddComponent<VerticalLayoutGroup>();
        vg.spacing = 4; vg.childControlWidth = true; vg.childControlHeight = true;
        vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
        vg.childAlignment = TextAnchor.MiddleCenter;

        // tower icon, growing with the level
        var iconWrap = MenuTheme.NewUI("IconWrap", v.transform);
        var ile = iconWrap.GetComponent<LayoutElement>() ?? iconWrap.AddComponent<LayoutElement>();
        ile.minHeight = 86; ile.preferredHeight = 86; ile.flexibleHeight = 0;
        var icon = MenuTheme.NewUI("Icon", iconWrap.transform);
        var ii = icon.AddComponent<Image>();
        ii.sprite = TowerSprite(); ii.color = col;
        var irt2 = icon.GetComponent<RectTransform>();
        irt2.anchorMin = irt2.anchorMax = new Vector2(0.5f, 0.5f);
        irt2.pivot = new Vector2(0.5f, 0.5f);
        irt2.sizeDelta = new Vector2(iconSize, iconSize);
        irt2.anchoredPosition = Vector2.zero;

        var t = MenuTheme.NewText(title, v.transform, 26, TextAlignmentOptions.Center, _font);
        t.fontStyle = FontStyles.Bold; t.color = col;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        SetH(t, 32);

        var d = MenuTheme.NewText(sub, v.transform, 21, TextAlignmentOptions.Top, _font);
        d.color = new Color(0.92f, 0.89f, 0.97f, 1f);
        d.textWrappingMode = TextWrappingModes.Normal;
    }

    private void CostArrow(Transform parent, string cost)
    {
        var wrap = MenuTheme.NewUI("CostArrow", parent);
        var le = wrap.GetComponent<LayoutElement>() ?? wrap.AddComponent<LayoutElement>();
        le.minWidth = 104; le.preferredWidth = 104; le.flexibleWidth = 0;
        var v = wrap.AddComponent<VerticalLayoutGroup>();
        v.spacing = 0; v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.MiddleCenter;

        var a = MenuTheme.NewText(">", wrap.transform, 32, TextAlignmentOptions.Center, _font);
        a.color = MenuTheme.Magenta; a.fontStyle = FontStyles.Bold;
        SetH(a, 38);

        var c = MenuTheme.NewText(cost, wrap.transform, 24, TextAlignmentOptions.Center, _font);
        c.color = new Color(1.00f, 0.55f, 0.55f); c.fontStyle = FontStyles.Bold;
        c.textWrappingMode = TextWrappingModes.NoWrap;
        SetH(c, 26);
    }

    //  GREMLIN / LORE trails 
    private void BuildGremlinTrail(Transform parent)
    {
        BuildTrailDiagram(parent, "GremlinTrail", GremlinSprite(),
            new Color(0.55f, 0.90f, 0.45f, 1f), new Color(0.05f, 0.05f, 0.08f, 1f), "GREMLIN",
            "Runs at 70% of your speed.\nOne hit kills it. Drops a pile of energy.",
            "Footprints lead to the nearest gremlin.");
    }

    private void BuildChestTrail(Transform parent)
    {
        BuildTrailDiagram(parent, "ChestTrail", ChestSprite(),
            new Color(0.98f, 0.80f, 0.32f, 1f), new Color(0.74f, 0.74f, 0.80f, 1f), "LORE CHEST",
            "Walk into it to open it.\nRead the scroll, then press Esc or click.",
            "Footprints lead to the nearest chest.");
    }

    private void BuildVortexTrail(Transform parent)
    {
        // RED footprints trail the vortex - matches VortexPathIndicator.footprintTint.
        BuildTrailDiagram(parent, "VortexTrail", VortexSprite(),
            new Color(1f, 0.34f, 0.30f, 1f), new Color(1f, 0.30f, 0.28f, 1f), "VORTEX",
            "A rift that spits out waves of enemies.\nDestroy it to stop the flow.",
            "RED footprints lead to the nearest vortex.");
    }

    // Player on the left, footprints across the middle, the target on the right.
    // Text lives in a clear band UNDER the lane at a readable size.
    private void BuildTrailDiagram(Transform parent, string name, Sprite targetSprite,
                                   Color targetCol, Color footCol, string targetName, string targetSub, string caption)
    {
        var holder = DiagramCard(parent, name, 260);

        // ---- lane (top 55%) ----
        var lane = MenuTheme.NewUI("Lane", holder.transform);
        var lrt = lane.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0.52f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(28, 0); lrt.offsetMax = new Vector2(-28, -12);

        var player = MenuTheme.NewUI("Player", lane.transform);
        var pImg = player.AddComponent<Image>();
        pImg.sprite = PlayerSprite(); pImg.color = Color.white;
        var prt = player.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(74, 74);
        prt.anchoredPosition = new Vector2(40, 0);

        // Each footprint = coloured dot on a thin CONTRAST rim, so a BLACK gremlin
        // print stays black (a light rim) instead of washing out to grey.
        const int STEPS = 8;
        float lum = footCol.r * 0.299f + footCol.g * 0.587f + footCol.b * 0.114f;
        Color rimCol = lum < 0.45f ? new Color(0.97f, 0.97f, 1f, 1f)
                                   : new Color(0.05f, 0.04f, 0.08f, 1f);
        for (int i = 0; i < STEPS; i++)
        {
            float t = 0.16f + 0.66f * ((i + 1f) / (STEPS + 1f));
            float a = 0.45f + 0.5f * ((i + 1f) / STEPS);

            var slot = MenuTheme.NewUI("Step" + i, lane.transform);
            var srt = slot.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(t, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(24, 24);
            srt.anchoredPosition = new Vector2(0, (i % 2 == 0) ? 13f : -13f);

            var rim = slot.AddComponent<Image>();
            rim.sprite = CircleSprite();
            rim.color = new Color(rimCol.r, rimCol.g, rimCol.b, a * 0.85f);

            var dot = MenuTheme.NewUI("Dot", slot.transform);
            var di = dot.AddComponent<Image>();
            di.sprite = CircleSprite();
            di.color = new Color(footCol.r, footCol.g, footCol.b, a);
            var drt = dot.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
            drt.offsetMin = new Vector2(4, 4); drt.offsetMax = new Vector2(-4, -4);
        }

        var target = MenuTheme.NewUI("Target", lane.transform);
        var tImg = target.AddComponent<Image>();
        tImg.sprite = targetSprite; tImg.color = targetCol;
        var trt = target.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(84, 84);
        trt.anchoredPosition = new Vector2(-46, 0);

        // ---- label band (bottom 52%) ----
        var band = MenuTheme.NewUI("Band", holder.transform);
        var brt = band.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0.50f);
        brt.offsetMin = new Vector2(24, 14); brt.offsetMax = new Vector2(-24, 0);
        var bh = band.AddComponent<HorizontalLayoutGroup>();
        bh.spacing = 24; bh.childControlWidth = true; bh.childControlHeight = true;
        bh.childForceExpandWidth = false; bh.childForceExpandHeight = true;
        bh.childAlignment = TextAnchor.UpperLeft;

        // left: YOU + caption
        var left = MenuTheme.NewUI("Left", band.transform);
        var lv = left.AddComponent<VerticalLayoutGroup>();
        lv.spacing = 4; lv.childControlWidth = true; lv.childControlHeight = true;
        lv.childForceExpandWidth = true; lv.childForceExpandHeight = false;
        var lle = left.GetComponent<LayoutElement>() ?? left.AddComponent<LayoutElement>();
        lle.flexibleWidth = 1; lle.minWidth = 380;

        var youLbl = MenuTheme.NewText("PLAYER", left.transform, 24, TextAlignmentOptions.Left, _font);
        youLbl.fontStyle = FontStyles.Bold; youLbl.color = Color.white;
        SetH(youLbl, 30);
        var cap = MenuTheme.NewText(caption, left.transform, 20, TextAlignmentOptions.TopLeft, _font);
        cap.color = TipCol; cap.fontStyle = FontStyles.Normal;
        cap.textWrappingMode = TextWrappingModes.Normal;

        // right: target name + facts
        var right = MenuTheme.NewUI("Right", band.transform);
        var rv = right.AddComponent<VerticalLayoutGroup>();
        rv.spacing = 4; rv.childControlWidth = true; rv.childControlHeight = true;
        rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;
        var rle = right.GetComponent<LayoutElement>() ?? right.AddComponent<LayoutElement>();
        rle.flexibleWidth = 1; rle.minWidth = 400;

        var tLbl = MenuTheme.NewText(targetName, right.transform, 24, TextAlignmentOptions.Right, _font);
        tLbl.fontStyle = FontStyles.Bold; tLbl.color = targetCol;
        SetH(tLbl, 30);
        var sub = MenuTheme.NewText(targetSub, right.transform, 20, TextAlignmentOptions.TopRight, _font);
        sub.color = new Color(0.94f, 0.91f, 0.98f, 1f);
        sub.textWrappingMode = TextWrappingModes.Normal;
    }

    //  ENERGY: a left-to-right flow strip 
    private void BuildEnergyStrip(Transform parent)
    {
        var holder = DiagramCard(parent, "EnergyStrip", 190);

        var row = MenuTheme.NewUI("Row", holder.transform);
        var rrt = row.GetComponent<RectTransform>();
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
        rrt.offsetMin = new Vector2(16, 14); rrt.offsetMax = new Vector2(-16, -14);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4; h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;

        FlowNode(row.transform, "KILL", "enemies drop energy", NearCol);
        FlowArrow(row.transform);
        FlowNode(row.transform, "COLLECT", "walk over the drops", NearCol);
        FlowArrow(row.transform);
        FlowNode(row.transform, "BUILD", $"-{towerBuildCost} build\n-{towerUpgradeCost} upgrade", MidCol);
        FlowArrow(row.transform);
        FlowNode(row.transform, "SUPPLY", "hold to fight decay", FarCol);
        FlowArrow(row.transform);
        FlowNode(row.transform, "SURVIVE", "the Core holds", MenuTheme.Magenta);
    }

    private void FlowNode(Transform parent, string title, string sub, Color col)
    {
        var node = MenuTheme.NewUI("Node_" + title, parent);
        var img = node.AddComponent<Image>();
        img.sprite = RoundedSprite(); img.type = Image.Type.Sliced;
        img.color = new Color(col.r, col.g, col.b, 0.16f);
        var le = node.GetComponent<LayoutElement>() ?? node.AddComponent<LayoutElement>();
        le.flexibleWidth = 1; le.minWidth = 150;

        var v = MenuTheme.NewUI("V", node.transform);
        var vrt = v.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(8, 10); vrt.offsetMax = new Vector2(-8, -10);
        var vg = v.AddComponent<VerticalLayoutGroup>();
        vg.spacing = 6;
        vg.childControlWidth = true; vg.childControlHeight = true;
        vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
        vg.childAlignment = TextAnchor.MiddleCenter;

        // No auto-sizing: fixed, legible sizes.
        var t = MenuTheme.NewText(title, v.transform, 22, TextAlignmentOptions.Center, _font);
        t.fontStyle = FontStyles.Bold; t.color = col;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        SetH(t, 28);

        var d = MenuTheme.NewText(sub, v.transform, 17, TextAlignmentOptions.Top, _font);
        d.color = new Color(0.95f, 0.93f, 0.99f, 1f);
        d.textWrappingMode = TextWrappingModes.Normal;
    }

    private void FlowArrow(Transform parent)
    {
        var a = MenuTheme.NewText(">", parent, 30, TextAlignmentOptions.Center, _font);
        a.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.9f);
        a.fontStyle = FontStyles.Bold;
        var le = a.GetComponent<LayoutElement>() ?? a.gameObject.AddComponent<LayoutElement>();
        le.minWidth = 24; le.preferredWidth = 24; le.flexibleWidth = 0;
    }

    //  PROCEDURAL SPRITES (cached, anti-aliased)
    // Icons render small (<=100px). 192px + 3x3 supersampling is plenty and cuts
    // first-open cost ~4x vs the old 256/4. Mipmaps (below) smooth the downscale.
    private const int TEX = 192;   // texture resolution
    private const int SS = 3;      // supersampling: SS x SS samples per pixel

    private static Sprite _circle, _tower, _player, _gremlin, _chest, _vortex;
    private static readonly Dictionary<int, Sprite> _rounded = new Dictionary<int, Sprite>();

    /// Rasterise `inside(uv)` (uv in 0..1, y up) into a white sprite with soft edges.
    private static Sprite Rasterize(Func<Vector2, bool> inside, int size = TEX)
    {
        var tex = NewTex(size);
        var px = new Color32[size * size];
        float inv = 1f / size;
        float step = inv / SS;
        float half = step * 0.5f;
        int total = SS * SS;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < SS; sy++)
                {
                    float v = y * inv + sy * step + half;
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float u = x * inv + sx * step + half;
                        if (inside(new Vector2(u, v))) hits++;
                    }
                }
                byte a = (byte)(255 * hits / total);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    //  shape predicates (normalized space) 
    private static bool Disc(Vector2 p, float cx, float cy, float r)
        => (p - new Vector2(cx, cy)).sqrMagnitude <= r * r;

    private static bool Box(Vector2 p, float x0, float y0, float x1, float y1)
        => p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1;

    /// Rounded box (capsule-ish); r is the corner radius.
    private static bool RBox(Vector2 p, float x0, float y0, float x1, float y1, float r)
    {
        float cx = Mathf.Clamp(p.x, x0 + r, x1 - r);
        float cy = Mathf.Clamp(p.y, y0 + r, y1 - r);
        return (p - new Vector2(cx, cy)).sqrMagnitude <= r * r;
    }

    /// Symmetric trapezoid: half-width lerps from `hw0` at y0 to `hw1` at y1.
    private static bool Trap(Vector2 p, float cx, float y0, float y1, float hw0, float hw1)
    {
        if (p.y < y0 || p.y > y1) return false;
        float t = (p.y - y0) / Mathf.Max(1e-5f, y1 - y0);
        return Mathf.Abs(p.x - cx) <= Mathf.Lerp(hw0, hw1, t);
    }

    private static Sprite CircleSprite()
    {
        if (_circle == null) _circle = Rasterize(p => Disc(p, 0.5f, 0.5f, 0.49f), 128);
        return _circle;
    }

    // Turret: wide plinth, tapered body, crenellated crown, barrel.
    private static Sprite TowerSprite()
    {
        if (_tower != null) return _tower;
        _tower = Rasterize(p =>
        {
            // plinth
            if (RBox(p, 0.13f, 0.05f, 0.87f, 0.18f, 0.035f)) return true;
            // tapered body
            if (Trap(p, 0.5f, 0.18f, 0.66f, 0.30f, 0.24f)) return true;
            // crown ring
            if (RBox(p, 0.20f, 0.66f, 0.80f, 0.76f, 0.02f)) return true;
            // merlons (battlements)
            if (p.y > 0.76f && p.y <= 0.87f)
            {
                if (Box(p, 0.21f, 0.76f, 0.31f, 0.87f)) return true;
                if (Box(p, 0.37f, 0.76f, 0.47f, 0.87f)) return true;
                if (Box(p, 0.53f, 0.76f, 0.63f, 0.87f)) return true;
                if (Box(p, 0.69f, 0.76f, 0.79f, 0.87f)) return true;
            }
            // barrel / spire
            if (RBox(p, 0.455f, 0.80f, 0.545f, 0.97f, 0.04f)) return true;
            return false;
        });
        return _tower;
    }

    // Player: helmeted head, rounded shoulders, tapered torso.
    private static Sprite PlayerSprite()
    {
        if (_player != null) return _player;
        _player = Rasterize(p =>
        {
            // head
            if (Disc(p, 0.5f, 0.79f, 0.145f)) return true;
            // neck
            if (Box(p, 0.455f, 0.66f, 0.545f, 0.70f)) return true;
            // shoulders + torso (rounded, tapering down)
            if (RBox(p, 0.235f, 0.24f, 0.765f, 0.685f, 0.16f))
            {
                // taper the waist so it reads as a body, not a pill
                float t = Mathf.InverseLerp(0.685f, 0.24f, p.y); // 0 top -> 1 bottom
                float halfW = Mathf.Lerp(0.265f, 0.175f, t);
                if (Mathf.Abs(p.x - 0.5f) <= halfW) return true;
            }
            return false;
        });
        return _player;
    }

    // Gremlin: squat body, two big pointed ears, hollow eyes.
    private static Sprite GremlinSprite()
    {
        if (_gremlin != null) return _gremlin;
        _gremlin = Rasterize(p =>
        {
            // hollow eyes punch through everything
            if (Disc(p, 0.405f, 0.50f, 0.058f)) return false;
            if (Disc(p, 0.595f, 0.50f, 0.058f)) return false;

            // body
            if (Disc(p, 0.5f, 0.42f, 0.325f)) return true;

            // ears: tapering triangles leaning outward
            if (Trap(p, 0.255f, 0.60f, 0.95f, 0.115f, 0.008f)) return true;
            if (Trap(p, 0.745f, 0.60f, 0.95f, 0.115f, 0.008f)) return true;
            return false;
        });
        return _gremlin;
    }

    // Chest: domed lid, banded body, latch.
    private static Sprite ChestSprite()
    {
        if (_chest != null) return _chest;
        _chest = Rasterize(p =>
        {
            // keyhole punches through the latch
            if (Disc(p, 0.5f, 0.40f, 0.035f)) return false;

            // body
            bool body = RBox(p, 0.13f, 0.12f, 0.87f, 0.53f, 0.04f);
            // domed lid: half-disc sitting on the body line
            bool lid = p.y >= 0.53f && Disc(p, 0.5f, 0.53f, 0.37f);
            // latch plate straddling the seam
            bool latch = RBox(p, 0.44f, 0.33f, 0.56f, 0.60f, 0.02f);

            if (!(body || lid || latch)) return false;

            // dark seam between lid and body (a thin gap), except across the latch
            if (!latch && p.y > 0.515f && p.y < 0.555f) return false;
            return true;
        });
        return _chest;
    }

    // A swirling vortex: a bright core with two spiral arms.
    private static Sprite VortexSprite()
    {
        if (_vortex != null) return _vortex;
        _vortex = Rasterize(p =>
        {
            Vector2 d = p - new Vector2(0.5f, 0.5f);
            float r = d.magnitude;
            if (r > 0.48f) return false;
            if (r < 0.12f) return true;                       // bright core
            float spiral = Mathf.Atan2(d.y, d.x) + r * 11f;   // twist with radius
            float arms = Mathf.Max(Mathf.Cos(spiral), Mathf.Cos(spiral + Mathf.PI));
            float thickness = Mathf.Lerp(0.55f, 0.05f, r / 0.48f);
            return arms > (1f - thickness);
        });
        return _vortex;
    }

    // 9-sliced rounded rectangle used for diagram cards / cells / flow nodes.
    // Rasterised at high res so the corners stay smooth at any size.
    private static Sprite RoundedSprite(int radius = 16)
    {
        int r = Mathf.Max(2, radius);
        if (_rounded.TryGetValue(r, out var cached) && cached != null) return cached;

        int size = r * 8;                       // plenty of texels per corner
        float rn = 1f / 8f;                     // corner radius in normalized space
        int border = size / 8;

        var tex = NewTex(size);
        var px = new Color32[size * size];
        float inv = 1f / size, step = inv / SS, half = step * 0.5f;
        int total = SS * SS;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < SS; sy++)
                {
                    float v = y * inv + sy * step + half;
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float u = x * inv + sx * step + half;
                        if (RBox(new Vector2(u, v), 0f, 0f, 1f, 1f, rn)) hits++;
                    }
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / total));
            }
        tex.SetPixels32(px); tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                   100f, 0, SpriteMeshType.FullRect,
                                   new Vector4(border, border, border, border));
        _rounded[r] = sprite;
        return sprite;
    }

    // mipChain:true builds MIPMAPS; with Trilinear filtering these are what stop the
    // shimmery/pixelated look when a sprite is drawn much smaller than its texture.
    private static Texture2D NewTex(int s) =>
        new Texture2D(s, s, TextureFormat.RGBA32, true)
        { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };

    //  UI CONSTRUCTION  (mirrors OptionsMenu structure)
    private Image _dimBlocker, _viewportBlocker;

    // 100+ generated graphics are all raycast targets by default; the raycaster walks
    // every one on pointer events, and it bloats canvas work. Turn them ALL off, then
    // re-enable only what must receive input: the real buttons, the full-screen dim
    // (blocks clicks to the paused game), and the transparent viewport (catches scroll).
    private void OptimizeRaycasts()
    {
        var graphics = _root.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++) graphics[i].raycastTarget = false;

        var selectables = _root.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
            if (selectables[i].targetGraphic != null) selectables[i].targetGraphic.raycastTarget = true;

        if (_dimBlocker != null) _dimBlocker.raycastTarget = true;
        if (_viewportBlocker != null) _viewportBlocker.raycastTarget = true;
    }

    private void BuildUI()
    {
        _font = MenuTheme.ResolveFont(titleFont, titleFontTtf);

        _root = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var dim = MenuTheme.NewUI("Dim", _root.transform);
        MenuTheme.Stretch(dim.GetComponent<RectTransform>());
        _dimBlocker = dim.AddComponent<Image>();
        _dimBlocker.sprite = MenuTheme.VerticalGradient(MenuTheme.GradTop, MenuTheme.GradBottom);

        var panel = MenuTheme.NewUI("Panel", dim.transform);
        var pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(1480, 1030);
        MenuTheme.ApplySprite(panel.AddComponent<Image>(), MenuTheme.PanelSprite, MenuTheme.PanelSolid);

        // inner column, inset clear of the decorative frame. Smaller TOP inset
        // pulls the title up and hands the freed space to the scroll body.
        var inner = MenuTheme.NewUI("Inner", panel.transform);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(82, 86); irt.offsetMax = new Vector2(-82, -58);
        var v = inner.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperCenter;

        var title = MenuTheme.NewText("HOW TO PLAY", inner.transform, 52, TextAlignmentOptions.Center, _font);
        title.fontStyle = FontStyles.Bold; title.characterSpacing = 8f;
        title.enableVertexGradient = true;
        var top = new Color(0.97f, 0.88f, 1f, 1f);
        title.colorGradient = new VertexGradient(top, top, MenuTheme.Magenta, MenuTheme.Magenta);
        SetH(title, 50);

        var sub = MenuTheme.NewText("Defend the Central Core", inner.transform, 25, TextAlignmentOptions.Center, _font);
        sub.color = MenuTheme.ValueCol;
        SetH(sub, 28);

        var tabs = MenuTheme.NewUI("Tabs", inner.transform);
        SetH(tabs, 62);
        var th = tabs.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 16; th.childControlWidth = true; th.childControlHeight = true;
        th.childForceExpandWidth = true; th.childForceExpandHeight = true;
        _kbTab = MenuTheme.NewButton("Keyboard & Mouse", tabs.transform, 24, _font);
        _padTab = MenuTheme.NewButton("Gamepad", tabs.transform, 24, _font);
        _kbTab.onClick.AddListener(() => { _showGamepad = false; RefreshAllChips(); UpdateSchemeTabs(); });
        _padTab.onClick.AddListener(() => { _showGamepad = true; RefreshAllChips(); UpdateSchemeTabs(); });

        AddDivider(inner.transform);

        var scrollHolder = MenuTheme.NewUI("Scroll", inner.transform);
        var she = scrollHolder.AddComponent<LayoutElement>(); she.flexibleHeight = 1f; she.minHeight = 300f;
        BuildScroll((RectTransform)scrollHolder.transform);

        var back = MenuTheme.NewButton("Back", inner.transform, 24, _font);
        SetH(back, 56);
        back.onClick.AddListener(Close);

        OptimizeRaycasts();
        _root.SetActive(false);
    }

    private void BuildScroll(RectTransform holder)
    {
        var sr = holder.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;

        var viewport = MenuTheme.NewUI("Viewport", holder);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(6, 0); vrt.offsetMax = new Vector2(-18, 0);
        _viewportBlocker = viewport.AddComponent<Image>();   // transparent wheel/drag catcher
        _viewportBlocker.color = new Color(0f, 0f, 0f, 0f);
        viewport.AddComponent<RectMask2D>();
        sr.viewport = vrt;

        // zero sizeDelta + anchoredPosition. A RectTransform made via
        // `new GameObject` can carry a non-zero default sizeDelta; with stretch
        // anchors that makes the content WIDER than the viewport and centred,
        // clipping both sides.
        var content = MenuTheme.NewUI("Content", viewport.transform);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = Vector2.zero;
        crt.anchoredPosition = Vector2.zero;
        // Isolate the scrolling content in its own Canvas. Without this, every scroll
        // frame re-batches EVERY graphic on the screen; with it, only the content's
        // cached mesh is re-transformed. This is the main cause of the scroll jank.
        var contentCanvas = content.AddComponent<Canvas>();   // isolates scroll rebatch
        // REQUIRED for TextMeshPro on a sub-canvas: without these channels TMP's SDF
        // text loses anti-aliasing and looks pixelated/jagged.
        contentCanvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;
        var cv = content.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 10; cv.padding = new RectOffset(16, 16, 4, 10);
        cv.childControlWidth = true; cv.childControlHeight = true;
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = crt;

        var sbGO = MenuTheme.NewUI("Scrollbar", holder);
        var sbrt = sbGO.GetComponent<RectTransform>();
        sbrt.anchorMin = new Vector2(1f, 0f); sbrt.anchorMax = new Vector2(1f, 1f);
        sbrt.pivot = new Vector2(1f, 0.5f); sbrt.sizeDelta = new Vector2(8f, 0f);
        sbrt.anchoredPosition = Vector2.zero;
        var sbImg = sbGO.AddComponent<Image>(); sbImg.color = new Color(0f, 0f, 0f, 0.35f);
        var scrollbar = sbGO.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var handle = MenuTheme.NewUI("Handle", sbGO.transform);
        MenuTheme.Stretch(handle.GetComponent<RectTransform>());
        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.55f);
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = hImg;
        sr.verticalScrollbar = scrollbar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        foreach (var section in BuildContent())
        {
            var head = MenuTheme.NewText(section.title, content.transform, 27,
                                         TextAlignmentOptions.Left, _font);
            head.color = MenuTheme.Magenta; head.fontStyle = FontStyles.Bold; head.characterSpacing = 5f;
            head.margin = new Vector4(2, 10, 0, 2);
            SetH(head, 40);

            for (int i = 0; i < section.entries.Count; i++)
                BuildRow((RectTransform)content.transform, section.entries[i], i % 2 == 1);

            // optional procedural diagram under the rows
            section.visual?.Invoke(content.transform);
        }
    }

    private void BuildRow(RectTransform parent, Entry e, bool striped)
    {
        var row = MenuTheme.NewUI("Row", parent);
        var rImg = row.AddComponent<Image>();
        rImg.color = striped ? RowA : RowB;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 20; h.padding = new RectOffset(16, 16, 14, 14);
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.UpperLeft;
        var rcsf = row.AddComponent<ContentSizeFitter>();
        rcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        bool isInfo = !string.IsNullOrEmpty(e.infoChip);
        var chip = MenuTheme.NewUI("Chip", row.transform);
        var chipImg = chip.AddComponent<Image>();
        MenuTheme.ApplySprite(chipImg, MenuTheme.ButtonSprite, MenuTheme.BtnSolid);
        if (isInfo) chipImg.color = MenuTheme.ButtonSprite != null
            ? new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 1f)
            : MenuTheme.Violet;
        var cle = chip.GetComponent<LayoutElement>() ?? chip.AddComponent<LayoutElement>();
        cle.minWidth = 250; cle.preferredWidth = 250; cle.flexibleWidth = 0;
        cle.minHeight = 66; cle.preferredHeight = 66; cle.flexibleHeight = 0;

        var chipText = MenuTheme.NewText(isInfo ? e.infoChip : "", chip.transform, 26,
                                         TextAlignmentOptions.Center, _font);
        chipText.fontStyle = FontStyles.Bold;
        chipText.color = isInfo ? Color.white : MenuTheme.ValueCol;
        chipText.enableAutoSizing = true; chipText.fontSizeMin = 16; chipText.fontSizeMax = 26;
        chipText.margin = new Vector4(10, 4, 10, 4);
        chipText.textWrappingMode = TextWrappingModes.NoWrap;
        MenuTheme.Stretch(chipText.rectTransform);
        if (!isInfo) _chips.Add(new ChipBinding { entry = e, label = chipText });

        var block = MenuTheme.NewUI("Text", row.transform);
        var bv = block.AddComponent<VerticalLayoutGroup>();
        bv.spacing = 3; bv.childControlWidth = true; bv.childControlHeight = true;
        bv.childForceExpandWidth = true; bv.childForceExpandHeight = false;
        var bcsf = block.AddComponent<ContentSizeFitter>();
        bcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var ble = block.GetComponent<LayoutElement>() ?? block.AddComponent<LayoutElement>();
        ble.flexibleWidth = 1; ble.minWidth = 540;

        var title = MenuTheme.NewText(e.title, block.transform, 30, TextAlignmentOptions.TopLeft, _font);
        title.fontStyle = FontStyles.Bold; title.color = Color.white;
        title.textWrappingMode = TextWrappingModes.Normal;

        var desc = MenuTheme.NewText(e.desc, block.transform, 25, TextAlignmentOptions.TopLeft, _font);
        desc.color = DescCol; desc.textWrappingMode = TextWrappingModes.Normal;

        if (!string.IsNullOrEmpty(e.tip))
        {
            var tip = MenuTheme.NewText(TIP_BADGE + e.tip, block.transform, 23, TextAlignmentOptions.TopLeft, _font);
            tip.color = TipCol; tip.fontStyle = FontStyles.Normal;
            tip.textWrappingMode = TextWrappingModes.Normal;
            tip.margin = new Vector4(0, 6, 0, 2);
        }
    }

    private void AddDivider(Transform parent)
    {
        var holder = MenuTheme.NewUI("RuleHolder", parent);
        SetH(holder, 12);
        var rule = MenuTheme.NewUI("Rule", holder.transform);
        var rr = rule.GetComponent<RectTransform>();
        rr.anchorMin = new Vector2(0.12f, 0.5f); rr.anchorMax = new Vector2(0.88f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.sprite = MenuTheme.HorizontalFade();
        img.color = new Color(MenuTheme.Magenta.r, MenuTheme.Magenta.g, MenuTheme.Magenta.b, 0.8f);
    }

    private static void SetH(Component c, float hgt) => SetH(c.gameObject, hgt);
    private static void SetH(GameObject go, float hgt)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = hgt; le.preferredHeight = hgt; le.flexibleHeight = 0f;
    }

    //  DEVICE TABS + CHIP REFRESH
    private void UpdateSchemeTabs()
    {
        StyleTab(_kbTab, !_showGamepad);
        StyleTab(_padTab, _showGamepad);
    }

    private void StyleTab(Button btn, bool active)
    {
        if (btn == null) return;
        if (btn.targetGraphic is Image img)
            img.color = active ? MenuTheme.BtnActive
                               : (MenuTheme.ButtonSprite != null ? Color.white : MenuTheme.BtnSolid);
        var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.color = active ? Color.white : MenuTheme.ValueCol;
    }

    private void RefreshAllChips()
    {
        foreach (var c in _chips)
            if (c.label != null) c.label.text = ChipTextFor(c.entry, _showGamepad);
    }

    private string ChipTextFor(Entry e, bool gamepad)
    {
        if (e.fallbackOnly) return gamepad ? e.gpFallback : e.kbFallback;

        if (e.comboAction != null)
        {
            if (gamepad) return e.gpFallback;
            string a = ResolveBinding(e.action, false, null, null, FirstToken(e.kbFallback));
            string b = ResolveBinding(e.comboAction, false, null, null, e.comboKbFallback);
            return a + " / " + b;
        }

        string fallback = gamepad ? e.gpFallback : e.kbFallback;
        return ResolveBinding(e.action, gamepad, e.kbPrefer, e.gpPrefer, fallback);
    }

    private static string FirstToken(string s) =>
        string.IsNullOrEmpty(s) ? s : s.Split('/')[0].Trim();
    //  BINDING RESOLUTION 
    private InputActionAsset _resolvedAsset;
    private bool _assetResolved;

    private InputActionAsset ResolveAsset()
    {
        if (_assetResolved) return _resolvedAsset;
        _assetResolved = true;
        if (inputActions != null) { _resolvedAsset = inputActions; return _resolvedAsset; }
        var pi = FindFirstObjectByType<PlayerInput>();
        if (pi != null && pi.actions != null) { _resolvedAsset = pi.actions; return _resolvedAsset; }
        _resolvedAsset = Resources.Load<InputActionAsset>("PlayerInputActions");
        return _resolvedAsset;
    }

    // Push any committed rebinds onto our asset so the chips match what the player
    // sees in-game. Called via reflection so this file still compiles if dropped
    // into a project without ControlRebindService. Equivalent to:
    //   ControlRebindService.ApplyTo(asset);
    private static System.Reflection.MethodInfo _applyTo;
    private static bool _applyToResolved;
    private static void ApplyRebindOverrides(InputActionAsset asset)
    {
        if (asset == null) return;
        try
        {
            if (!_applyToResolved)
            {
                _applyToResolved = true;
                Type svc = Type.GetType("ControlRebindService");
                if (svc == null)
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    { svc = asm.GetType("ControlRebindService"); if (svc != null) break; }
                _applyTo = svc?.GetMethod("ApplyTo",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(InputActionAsset) }, null);
            }
            _applyTo?.Invoke(null, new object[] { asset });
        }
        catch { /* no rebind service present - chips fall back to defaults */ }
    }

    private string ResolveBinding(string actionName, bool gamepad,
                                  string kbPrefer, string gpPrefer, string fallback)
    {
        var asset = ResolveAsset();
        if (asset == null || string.IsNullOrEmpty(actionName)) return fallback;
        var action = asset.FindAction(actionName, false);
        if (action == null) return fallback;

        string group = gamepad ? GROUP_PAD : GROUP_KBM;
        string prefer = gamepad ? gpPrefer : kbPrefer;

        string firstMatch = null;
        try
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                if (!(b.groups ?? string.Empty).Contains(group)) continue;
                string path = b.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;
                if (firstMatch == null) firstMatch = path;
                if (!string.IsNullOrEmpty(prefer) &&
                    path.IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0)
                    return Friendly(path, gamepad);
            }
        }
        catch { return fallback; }

        return firstMatch != null ? Friendly(firstMatch, gamepad) : fallback;
    }

    private static string Friendly(string path, bool gamepad)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string p = path.ToLowerInvariant();

        if (gamepad)
        {
            if (p.Contains("buttonnorth")) return "Y";
            if (p.Contains("buttonsouth")) return "A";
            if (p.Contains("buttoneast")) return "B";
            if (p.Contains("buttonwest")) return "X";
            if (p.Contains("righttrigger")) return "RT";
            if (p.Contains("lefttrigger")) return "LT";
            if (p.Contains("rightshoulder")) return "RB";
            if (p.Contains("leftshoulder")) return "LB";
            if (p.Contains("dpad")) return "D-Pad";
            if (p.Contains("leftstickpress")) return "L3";
            if (p.Contains("rightstickpress")) return "R3";
            if (p.Contains("leftstick")) return "Left Stick";
            if (p.Contains("rightstick")) return "Right Stick";
            if (p.Contains("start")) return "Start";
            if (p.Contains("select")) return "Back";
        }
        else
        {
            if (p.Contains("leftbutton")) return "Left Click";
            if (p.Contains("rightbutton")) return "Right Click";
            if (p.Contains("middlebutton")) return "Middle Click";
        }

        try
        {
            string s = InputControlPath.ToHumanReadableString(
                path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(s) ? path : s;
        }
        catch { return path; }
    }

    //  SCHEME / ECONOMY
    private bool ResolveInitialScheme()
    {
        if (defaultScheme == DefaultScheme.Gamepad) return true;
        if (defaultScheme == DefaultScheme.KeyboardMouse) return false;
        var pad = Gamepad.current;
        if (pad == null) return false;
        double padT = pad.lastUpdateTime;
        double kbT = Keyboard.current != null ? Keyboard.current.lastUpdateTime : 0;
        double mT = Mouse.current != null ? Mouse.current.lastUpdateTime : 0;
        return padT >= Math.Max(kbT, mT);
    }

    private void RefreshEconomyFromGame()
    {
        var em = EnergyManager.Instance;
        if (em == null) return;
        try { int build = em.GetTowerBuildCost(); if (build > 0) towerBuildCost = build; }
        catch { /* keep inspector defaults */ }
    }

    //  DATA TYPES
    private class Section
    {
        public string title; public List<Entry> entries;
        /// Optional procedural diagram rendered under this section's rows.
        public Action<Transform> visual;
        public Section(string t, List<Entry> e) { title = t; entries = e; }
    }

    private class Entry
    {
        public string title, desc, tip;
        public string action, comboAction;
        public string kbPrefer, gpPrefer;
        public string kbFallback, gpFallback, comboKbFallback;
        public string infoChip;
        public bool fallbackOnly;
    }

    private class ChipBinding { public Entry entry; public TextMeshProUGUI label; }
}

