using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

//  DebugMenu.cs  
public static class DebugCheats
{
    public static bool PlayerGodMode = false;
    public static bool EnemyGodMode = false;

    // Called from CharacterStats.TakeDamage. Returns true to fully cancel a hit.
    public static bool DamageBlocked(CharacterStats cs)
    {
        if (cs == null) return false;
        if (PlayerGodMode && cs is PlayerStats) return true;
        if (EnemyGodMode && cs is EnemyStats) return true;
        return false;
    }

    // Reset between Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PlayerGodMode = false;
        EnemyGodMode = false;
    }
}


public class DebugMenu : MonoBehaviour
{
    // Master switch — set false (or delete the file) to strip from builds.
    // static readonly (not const) so "if (!ENABLED) return;" isn't flagged as
    // unreachable code (IDE0035) when the value is true.
    private static readonly bool ENABLED = true;

    // Hide the button on menu scenes (anywhere without a gameplay singleton).
    private static readonly bool HIDE_ON_MENUS = true;

    // Keyboard shortcut to open/close the panel.
    private const Key TOGGLE_KEY = Key.F10;

    // Keep the OS cursor HIDDEN during gameplay and turn it on only while this
    // panel is open. Never applies in menu scenes, and never while a real menu
    // (pause / options / augments) is up — those own the cursor themselves.
    // Set false to leave the game's cursor exactly as it was.
    private static readonly bool HIDE_CURSOR_IN_GAMEPLAY = true;

    private static DebugMenu _instance;

    // -------------------------------------------------------------------------
    //  Bootstrap — no GameObject or prefab needed anywhere in your scenes.
    // -------------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() { _instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!ENABLED) return;
        if (_instance != null) return;

        var go = new GameObject("[DebugMenu]");
        _instance = go.AddComponent<DebugMenu>();
        DontDestroyOnLoad(go);
    }

    //  State
    private bool _open = false;

    // Cursor LOCK state only. Cursor VISIBILITY is owned by UIModalStack (it writes
    // Cursor.visible on every push/pop), so we must not also write it ourselves —
    // that double ownership is why a self-managed cursor got overwritten.
    private CursorLockMode _prevCursorLock;

    // While open, the panel registers itself as a modal on the project's own
    // UIModalStack (MenuTheme.cs) — the same mechanism the pause menu uses. The
    // stack then owns the freeze, the cursor and attack suppression, and restores
    // the pre-open state when we pop. `freeze` follows this toggle.
    private bool _pauseWhenOpen = true;
    private bool _modalPushed = false;

    // Toggles
    private bool _towerGod = false;
    private bool _coreGod = false;
    private bool _infiniteEnergy = false;

    // God-mode bookkeeping so we can restore the exact prior value.
    private readonly Dictionary<int, bool> _towerOrigImmune = new Dictionary<int, bool>();
    private bool _coreImmuneStored = false;
    private bool _coreOrigImmune = false;

    // Spawn palette
    private List<GameObject> _enemyPalette;
    private int _spawnCount = 1;
    private Vector2 _spawnScroll;
    private string _lastSpawnInfo = "";

    // Manual-health inputs
    private string _playerHpInput = "";
    private float _towerHpPct = 1f;
    private float _coreHpPct = 1f;
    private string _enemyHpInput = "";
    private string _energyInput = "999999";

    // Augment UI
    private string[] _rarityNames;
    private int _selectedRarity = 0;
    private bool _onlyImplementedAugments = true;

    // Scroll positions
    private Vector2 _panelScroll;
    private Vector2 _augmentScroll;

    // GUI styling
    private bool _stylesReady;
    private GUIStyle _header, _btn, _toggle, _label, _openBtn;
    private float _scale = 1f;

    //  Per-frame enforcement of the "held" cheats.
    //  (Player/Enemy god are handled by the CharacterStats guard — nothing to do
    //   here for them.)
    private void Update()
    {
        bool available = !HIDE_ON_MENUS || InGameplay;

        // Left a gameplay scene while open → close cleanly and restore the cursor.
        if (_open && !available) SetOpen(false);

        // F10 toggles the panel (new Input System — the project already uses it).
        var kb = Keyboard.current;
        if (available && kb != null && kb[TOGGLE_KEY].wasPressedThisFrame)
            SetOpen(!_open);

        if (_towerGod) EnforceTowerGod();
        if (_coreGod) EnforceCoreGod();
        if (_infiniteEnergy) EnforceInfiniteEnergy();
    }

    // Cursor ownership, in LateUpdate so it runs AFTER every other script's Update.
    //   panel open  → force the cursor on (UIModalStack sets it on push, but a
    //                 per-frame cursor-hider elsewhere would undo that).
    //   panel closed → keep it hidden during gameplay, so the game starts (and
    //                 stays) cursor-free until F10. Skipped while any real menu is
    //                 on the modal stack, since those need a usable pointer.
    private void LateUpdate()
    {
        if (_open)
        {
            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            return;
        }

        if (HIDE_CURSOR_IN_GAMEPLAY
            && UIModalStack.GameplayActive   // in a run, not a menu scene
            && !UIModalStack.IsOpen          // no pause/options/augment screen up
            && Cursor.visible)
        {
            Cursor.visible = false;
        }
    }

    // Open/close the panel. Freeze, cursor visibility and attack suppression are
    // delegated to UIModalStack so the panel behaves like any other modal and the
    // stack restores the pre-open state for us on pop.
    private void SetOpen(bool open)
    {
        if (open == _open) return;

        if (open)
        {
            _prevCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;   // stack doesn't manage lock
            PushModal();
        }
        else
        {
            PopModal();
            Cursor.lockState = _prevCursorLock;
        }
        _open = open;
    }

    // Register as a modal. Pushing with freeze=true stops the clock and suppresses
    // attacks; either way the stack forces Cursor.visible = true. Pushing again
    // while already on the stack just updates the freeze flag (UIModalStack.Bump),
    // which is how the pause toggle below takes effect live.
    private void PushModal()
    {
        UIModalStack.Push(this, freeze: _pauseWhenOpen);
        _modalPushed = true;
    }

    private void PopModal()
    {
        if (!_modalPushed) return;
        UIModalStack.Pop(this);
        _modalPushed = false;
    }

    // Safety: never strand a frozen clock / hidden cursor if this object is torn
    // down (scene teardown, play-mode exit) while the panel is open.
    private void OnDisable()
    {
        PopModal();
        if (_open)
        {
            Cursor.lockState = _prevCursorLock;
            _open = false;
        }
    }


    private IEnumerable<PlayerStats> Players() =>
        Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);

    private IEnumerable<EnemyStats> Enemies() =>
        Object.FindObjectsByType<EnemyStats>(FindObjectsSortMode.None);

    private IEnumerable<Tower> Towers() =>
        Object.FindObjectsByType<Tower>(FindObjectsSortMode.None);

    private CentralCore Core() => Object.FindFirstObjectByType<CentralCore>();

    private bool InGameplay =>
        GameOrchestrator.Instance != null || EnergyManager.Instance != null;


    // Towers: block enemy damage AND counter passive energy decay by pinning full.
    private void EnforceTowerGod()
    {
        foreach (var t in Towers())
        {
            if (t == null) continue;
            int id = t.GetInstanceID();
            if (!_towerOrigImmune.ContainsKey(id)) _towerOrigImmune[id] = t.immuneToEnemyDamage;
            t.immuneToEnemyDamage = true;
            if (!t.IsDestroyed()) t.SetEnergy(t.GetMaxEnergy());
        }
    }

    private void RestoreTowerGod()
    {
        var byId = Towers().Where(t => t != null).ToDictionary(t => t.GetInstanceID());
        foreach (var kv in _towerOrigImmune)
            if (byId.TryGetValue(kv.Key, out var t))
                t.immuneToEnemyDamage = kv.Value;
        _towerOrigImmune.Clear();
    }

    private void EnforceCoreGod()
    {
        var core = Core();
        if (core == null) return;
        if (!_coreImmuneStored) { _coreOrigImmune = core.immuneToEnemyDamage; _coreImmuneStored = true; }
        core.immuneToEnemyDamage = true;
        core.SetEnergy(core.GetMaxEnergy());
    }

    private void RestoreCoreGod()
    {
        var core = Core();
        if (core != null && _coreImmuneStored) core.immuneToEnemyDamage = _coreOrigImmune;
        _coreImmuneStored = false;
    }

    private void EnforceInfiniteEnergy()
    {
        var em = EnergyManager.Instance;
        if (em == null) return;
        if (em.GetPlayerEnergy() < 100_000) em.SetPlayerEnergy(999_999); // re-set only on dip
    }


    private void RefreshEnemyPalette()
    {
        var seen = new HashSet<int>();
        var list = new List<GameObject>();

        void Add(GameObject go)
        {
            if (go != null && seen.Add(go.GetInstanceID())) list.Add(go);
        }
        void AddWaveConfig(WaveConfig wc)
        {
            if (wc == null || wc.waves == null) return;
            foreach (var w in wc.waves)
            {
                if (w?.enemies == null) continue;
                foreach (var g in w.enemies) Add(g?.enemyPrefab);
            }
        }

        // 1) Whatever the WaveSpawner is currently configured with.
        var ws = Object.FindAnyObjectByType<WaveSpawner>();
        if (ws != null) AddWaveConfig(ws.waveConfig);

        // 2) The orchestrator's run blueprint — includes bosses + the full pool.
        var orch = GameOrchestrator.Instance;
        if (orch != null)
        {
            var rc = orch.runConfig;
            if (rc != null)
            {
                if (rc.waveConfigPool != null)
                    foreach (var wc in rc.waveConfigPool) AddWaveConfig(wc);
                if (rc.enemyPool != null)
                    foreach (var e in rc.enemyPool) Add(e?.enemyPrefab);
                if (rc.stageBossPrefabs != null)
                    foreach (var b in rc.stageBossPrefabs) Add(b);
                Add(rc.stageBossPrefab);
                Add(rc.finalBossPrefab);
            }

            // 3) The live, generated run plan (procedural waves + resolved bosses).
            var plan = orch.RunPlan;
            if (plan != null)
                foreach (var stage in plan)
                {
                    if (stage == null) continue;
                    if (stage.waves != null)
                        foreach (var w in stage.waves)
                        {
                            if (w?.enemies == null) continue;
                            foreach (var g in w.enemies) Add(g?.enemyPrefab);
                        }
                    Add(stage.stageBossPrefab);
                }
        }

        _enemyPalette = list;
    }

    private void SpawnMany(GameObject prefab)
    {
        if (prefab == null) return;
        int n = Mathf.Clamp(_spawnCount, 1, 100);
        for (int i = 0; i < n; i++) SpawnOne(prefab);
        _lastSpawnInfo = $"Spawned {prefab.name} ×{n}";
        Debug.Log($"[DebugMenu] {_lastSpawnInfo}");
    }

    private void SpawnOne(GameObject prefab)
    {
        var ws = Object.FindAnyObjectByType<WaveSpawner>();
        SpawnDirection dir = RandomDirection();

        if (ws != null)
        {
            // Use the game's OWN tested spawn path — it handles spawn-area
            // positioning and obstacle avoidance for us. "Unindicated" so a debug
            // spawn doesn't light a misleading wave-direction arc.
            // Safe during an orchestrated run: wave-completion cross-scans the live
            // EnemyStats in the scene, so an extra enemy can never end a wave early,
            // and the orchestrator's counter self-corrects once the scene clears.
            ws.SpawnEnemyUnindicated(prefab, dir);
        }
        else
        {
            // No WaveSpawner in the scene: best-effort direct spawn near the core.
            Instantiate(prefab, GetRandomSpawnPos(null), Quaternion.identity);
        }
    }

    private static SpawnDirection RandomDirection()
    {
        var values = (SpawnDirection[])System.Enum.GetValues(typeof(SpawnDirection));
        return values[Random.Range(0, values.Length)];
    }

    private Vector3 GetRandomSpawnPos(WaveSpawner ws)
    {
        if (ws != null && ws.spawnAreas != null && ws.spawnAreas.Count > 0)
        {
            var valid = ws.spawnAreas.Where(a => a != null).ToList();
            if (valid.Count > 0)
            {
                var area = valid[Random.Range(0, valid.Count)];
                Bounds b = area.bounds;
                Vector2 p = new Vector2(Random.Range(b.min.x, b.max.x),
                                        Random.Range(b.min.y, b.max.y));
                Vector2 onCollider = area.ClosestPoint(p); // pull onto the collider
                return new Vector3(onCollider.x, onCollider.y, 0f);
            }
        }

        // Fallback: a ring around the core (or origin) so it's at least on-screen.
        var core = Core();
        Vector3 center = core != null ? core.transform.position : Vector3.zero;
        Vector2 offset = Random.insideUnitCircle.normalized * 8f;
        return center + new Vector3(offset.x, offset.y, 0f);
    }


    private void SetPlayerHealth(float value)
    {
        foreach (var p in Players())
            if (p != null) p.SetHealthAndNotify(Mathf.Clamp(value, 0f, p.maxHealth));
    }

    private void FullHealPlayers()
    {
        foreach (var p in Players())
            if (p != null) p.SetHealthAndNotify(p.maxHealth);
    }

    private void SetAllTowersPct(float pct)
    {
        pct = Mathf.Clamp01(pct);
        foreach (var t in Towers())
            if (t != null && !t.IsDestroyed())
                t.SetEnergy(t.GetMaxEnergy() * pct);
    }

    private void SetCorePct(float pct)
    {
        var core = Core();
        if (core != null) core.SetEnergy(core.GetMaxEnergy() * Mathf.Clamp01(pct));
    }

    private void SetAllEnemiesHealth(float value)
    {
        foreach (var e in Enemies())
            if (e != null) e.currentHealth = Mathf.Clamp(value, 0f, e.maxHealth);
    }

    private void FullHealEnemies()
    {
        foreach (var e in Enemies())
            if (e != null) e.currentHealth = e.maxHealth;
    }

    private void SetEnergy(int amount) => EnergyManager.Instance?.SetPlayerEnergy(amount);
    private void GiveEnergy(int amount) => EnergyManager.Instance?.GivePlayerEnergy(amount);


    private void EnsureRarityNames()
    {
        if (_rarityNames != null) return;
        var reg = AugmentRegistry.Instance;
        var cfgs = reg != null ? reg.GetRarityConfigurations() : null;
        _rarityNames = (cfgs != null && cfgs.Length > 0)
            ? cfgs.Select(c => c.rarityName).ToArray()
            : new[] { "Common", "Rare", "Epic", "Legendary" };
    }

    private string SelectedRarity()
    {
        EnsureRarityNames();
        if (_selectedRarity < 0 || _selectedRarity >= _rarityNames.Length) return "Common";
        return _rarityNames[_selectedRarity];
    }

    // Mirrors AugmentsMenu: registry.ApplyAugment(...) then handler.ApplyAugmentEffect(...).
    private void ApplyAugment(int id)
    {
        var reg = AugmentRegistry.Instance;
        if (reg == null) return;

        var chooser = Object.FindAnyObjectByType<PlayerStats>();
        bool ok = reg.ApplyAugment(id, SelectedRarity(), chooser);
        if (ok)
        {
            var handler = Object.FindAnyObjectByType<AugmentEffectHandler>();
            if (handler != null) handler.ApplyAugmentEffect(id, chooser);
        }
        else Debug.LogWarning($"[DebugMenu] Failed to apply augment {id}");
    }

    private void ApplyAllImplemented()
    {
        var reg = AugmentRegistry.Instance;
        if (reg == null) return;
        foreach (var a in reg.GetAllAugments().OrderBy(a => a.ID))
            if (reg.HasImplementation(a.ID) && !reg.IsAugmentApplied(a.ID))
                ApplyAugment(a.ID);
    }


    private void BuildStyles()
    {
        // Larger base so the panel/controls are comfortable; still scales with res.
        _scale = Mathf.Clamp(Screen.height / 800f, 1f, 2.6f);
        int fs = Mathf.RoundToInt(18 * _scale);
        int bp = Mathf.RoundToInt(9 * _scale);   // button/toggle vertical padding

        _header = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * _scale), fontStyle = FontStyle.Bold };
        _label = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = true };
        _btn = new GUIStyle(GUI.skin.button)
        {
            fontSize = fs,
            padding = new RectOffset(bp + 4, bp + 4, bp, bp) // taller/chunkier buttons
        };
        _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = fs, padding = new RectOffset(Mathf.RoundToInt(22 * _scale), 4, 2, 4) };
        _openBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(20 * _scale),
            fontStyle = FontStyle.Bold
        };

        _stylesReady = true;
    }

    private void OnGUI()
    {
        if (HIDE_ON_MENUS && !InGameplay) return;
        if (!_stylesReady) BuildStyles();

        float pad = 12f * _scale;
        float openW = 150f * _scale, openH = 54f * _scale;

        var openRect = new Rect(Screen.width - openW - pad, Screen.height - openH - pad, openW, openH);
        if (GUI.Button(openRect, _open ? "CLOSE" : "DEBUG (F10)", _openBtn)) SetOpen(!_open);

        if (!_open) return;

        float panelW = Mathf.Min(Screen.width - 2 * pad, 520f * _scale);
        float panelH = Mathf.Min(Screen.height - openH - 3 * pad, 860f * _scale);
        var panelRect = new Rect(Screen.width - panelW - pad,
                                 Screen.height - panelH - openH - 2 * pad, panelW, panelH);

        GUI.Box(panelRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(panelRect.x + pad, panelRect.y + pad,
                                     panelRect.width - 2 * pad, panelRect.height - 2 * pad));
        _panelScroll = GUILayout.BeginScrollView(_panelScroll);

        DrawTogglesSection(); Space();
        DrawSpawnSection(); Space();
        DrawHealthSection(); Space();
        DrawEnergySection(); Space();
        DrawAugmentSection();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void Space() { GUILayout.Space(8f * _scale); }
    private void Header(string t) { GUILayout.Label(t, _header); }

    private void DrawTogglesSection()
    {
        Header("Cheats");

        bool pg = GUILayout.Toggle(DebugCheats.PlayerGodMode, "  Player God Mode", _toggle);
        if (pg != DebugCheats.PlayerGodMode) DebugCheats.PlayerGodMode = pg;

        bool eg = GUILayout.Toggle(DebugCheats.EnemyGodMode, "  Enemy God Mode (all enemies)", _toggle);
        if (eg != DebugCheats.EnemyGodMode) DebugCheats.EnemyGodMode = eg;

        bool tg = GUILayout.Toggle(_towerGod, "  Tower God Mode (all towers)", _toggle);
        if (tg != _towerGod) { _towerGod = tg; if (!tg) RestoreTowerGod(); }

        bool cg = GUILayout.Toggle(_coreGod, "  Core God Mode", _toggle);
        if (cg != _coreGod) { _coreGod = cg; if (!cg) RestoreCoreGod(); }

        bool ie = GUILayout.Toggle(_infiniteEnergy, "  Infinite Player Energy", _toggle);
        if (ie != _infiniteEnergy) _infiniteEnergy = ie;

        // Lets you unpause without closing the panel — handy for watching spawned
        // enemies actually move while the menu stays up.
        bool pw = GUILayout.Toggle(_pauseWhenOpen, "  Pause game while this menu is open", _toggle);
        if (pw != _pauseWhenOpen)
        {
            _pauseWhenOpen = pw;
            PushModal();   // re-push updates the freeze flag without a second pop
        }
    }

    private void DrawSpawnSection()
    {
        Header("Spawn Enemy");
        if (_enemyPalette == null) RefreshEnemyPalette();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Palette: {_enemyPalette?.Count ?? 0}", _label);
        if (GUILayout.Button("Refresh", _btn, GUILayout.Width(90 * _scale))) RefreshEnemyPalette();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Count", _label, GUILayout.Width(50 * _scale));
        int.TryParse(GUILayout.TextField(_spawnCount.ToString(), _btn, GUILayout.Width(55 * _scale)), out _spawnCount);
        _spawnCount = Mathf.Clamp(_spawnCount, 1, 100);
        GUILayout.Label("← click a name below to spawn", _label);
        GUILayout.EndHorizontal();

        if (_enemyPalette != null && _enemyPalette.Count > 0)
        {
            // Each name IS the spawn button — one click spawns `Count` copies.
            _spawnScroll = GUILayout.BeginScrollView(_spawnScroll, GUILayout.Height(170 * _scale));
            foreach (var prefab in _enemyPalette)
            {
                if (prefab == null) continue;
                if (GUILayout.Button(prefab.name, _btn)) SpawnMany(prefab);
            }
            GUILayout.EndScrollView();
        }
        else
        {
            GUILayout.Label("No enemy prefabs found. Assign a WaveConfig / RunConfig, " +
                            "or open this while a run is active, then Refresh.", _label);
        }

        if (!string.IsNullOrEmpty(_lastSpawnInfo))
            GUILayout.Label(_lastSpawnInfo, _label);
    }

    private void DrawHealthSection()
    {
        Header("Health");

        // Player
        var p0 = Object.FindAnyObjectByType<PlayerStats>();
        GUILayout.Label(p0 != null ? $"Player: {p0.currentHealth:F0}/{p0.maxHealth:F0}" : "Player: (none)", _label);
        GUILayout.BeginHorizontal();
        _playerHpInput = GUILayout.TextField(_playerHpInput, _btn, GUILayout.Width(80 * _scale));
        if (GUILayout.Button("Set", _btn, GUILayout.Width(55 * _scale)))
            if (float.TryParse(_playerHpInput, out float v)) SetPlayerHealth(v);
        if (GUILayout.Button("Full heal", _btn)) FullHealPlayers();
        GUILayout.EndHorizontal();

        // Towers (HP == energy pool)
        GUILayout.Label($"Towers (HP = energy): {Towers().Count(t => t != null && !t.IsDestroyed())} alive", _label);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{_towerHpPct * 100f:F0}%", _label, GUILayout.Width(45 * _scale));
        _towerHpPct = GUILayout.HorizontalSlider(_towerHpPct, 0f, 1f, GUILayout.Width(110 * _scale));
        if (GUILayout.Button("Apply", _btn, GUILayout.Width(65 * _scale))) SetAllTowersPct(_towerHpPct);
        if (GUILayout.Button("Full", _btn, GUILayout.Width(55 * _scale))) SetAllTowersPct(1f);
        GUILayout.EndHorizontal();

        // Core (HP == energy pool)
        var core = Core();
        GUILayout.Label(core != null ? $"Core: {core.GetEnergy():F0}/{core.GetMaxEnergy():F0}" : "Core: (none)", _label);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{_coreHpPct * 100f:F0}%", _label, GUILayout.Width(45 * _scale));
        _coreHpPct = GUILayout.HorizontalSlider(_coreHpPct, 0f, 1f, GUILayout.Width(110 * _scale));
        if (GUILayout.Button("Apply", _btn, GUILayout.Width(65 * _scale))) SetCorePct(_coreHpPct);
        if (GUILayout.Button("Full", _btn, GUILayout.Width(55 * _scale))) SetCorePct(1f);
        GUILayout.EndHorizontal();

        // Enemies
        GUILayout.Label($"Enemies: {Enemies().Count(e => e != null)} alive", _label);
        GUILayout.BeginHorizontal();
        _enemyHpInput = GUILayout.TextField(_enemyHpInput, _btn, GUILayout.Width(80 * _scale));
        if (GUILayout.Button("Set all", _btn, GUILayout.Width(70 * _scale)))
            if (float.TryParse(_enemyHpInput, out float ev)) SetAllEnemiesHealth(ev);
        if (GUILayout.Button("Full heal", _btn)) FullHealEnemies();
        GUILayout.EndHorizontal();
    }

    private void DrawEnergySection()
    {
        Header("Player Energy");
        var em = EnergyManager.Instance;
        GUILayout.Label(em != null ? $"Current: {em.GetPlayerEnergy()}" : "EnergyManager: (none)", _label);
        GUILayout.BeginHorizontal();
        _energyInput = GUILayout.TextField(_energyInput, _btn, GUILayout.Width(100 * _scale));
        if (GUILayout.Button("Set", _btn, GUILayout.Width(55 * _scale)))
            if (int.TryParse(_energyInput, out int amt)) SetEnergy(amt);
        if (GUILayout.Button("+1000", _btn, GUILayout.Width(75 * _scale))) GiveEnergy(1000);
        GUILayout.EndHorizontal();
    }

    private void DrawAugmentSection()
    {
        Header("Augments");
        var reg = AugmentRegistry.Instance;
        if (reg == null) { GUILayout.Label("AugmentRegistry not ready.", _label); return; }

        EnsureRarityNames();
        GUILayout.Label("Apply-as rarity:", _label);
        _selectedRarity = GUILayout.SelectionGrid(
            Mathf.Clamp(_selectedRarity, 0, _rarityNames.Length - 1),
            _rarityNames, Mathf.Min(_rarityNames.Length, 4), _btn);

        GUILayout.BeginHorizontal();
        _onlyImplementedAugments = GUILayout.Toggle(_onlyImplementedAugments, " implemented only", _toggle);
        if (GUILayout.Button("Apply ALL", _btn)) ApplyAllImplemented();
        GUILayout.EndHorizontal();

        _augmentScroll = GUILayout.BeginScrollView(_augmentScroll, GUILayout.Height(220 * _scale));
        foreach (var a in reg.GetAllAugments().OrderBy(a => a.ID))
        {
            bool impl = reg.HasImplementation(a.ID);
            if (_onlyImplementedAugments && !impl) continue;
            bool applied = reg.IsAugmentApplied(a.ID);

            GUILayout.BeginHorizontal();
            string tag = (applied ? "[ON] " : "") + (impl ? "" : "(no impl) ");
            GUILayout.Label($"{a.ID}. {tag}{a.Name}", _label);
            GUI.enabled = impl;
            if (GUILayout.Button(applied ? "Re-apply" : "Apply", _btn, GUILayout.Width(85 * _scale)))
                ApplyAugment(a.ID);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }
}


