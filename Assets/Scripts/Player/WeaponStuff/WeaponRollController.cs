using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum ScrollTarget { None, Weapon, Tool }


// Per-player weapon/tool hotbar controller. Lives on the PLAYER prefab (root).
//
// Co-op: binds to its OWN sibling Weapon (GetComponentInChildren) and sibling
// WeaponRollUI — not FindFirstObjectByType, which would grab P1's. The unlock POOL is
// shared (WeaponUnlockRegistry), but each player independently SELECTS which entry
// they hold (_weaponIndex/_toolIndex are per-instance).
//
// INPUT: every control here now goes through the "Player" action map, so all of it is
// rebindable and shows up in ControlRebindScreen. Previously this script polled raw
// device paths (pad.dpad.*, mouse.scroll, kb.digit1Key…kb.digit0Key, shift), which is
// why rebinding Previous/Next in the controls screen did nothing — those actions
// existed in the asset, bound to the very D-pad controls this script read directly,
// and nothing consumed them.
//
//   PreviousWeapon / NextWeapon   D-pad left/right, wheel DOWN (next weapon)
//   PreviousTool   / NextTool     D-pad up/down, Z / X, wheel UP (previous tool)
//   Hotbar1..Hotbar10             number row 1..0  → weapon slot
//   HotbarModifier (held)         + a cycle or Hotbar action → acts on TOOLS instead
//
// THE MOUSE WHEEL IS SPLIT ON PURPOSE: up cycles TOOLS, down cycles WEAPONS. It is not
// an up/down pair for one list. This looks like an inconsistency and is not — do not
// "normalise" it to prev/next weapon (that has been done once already and it removed
// the only way to change tools with the mouse).
//
// Actions are POLLED rather than wired to PlayerInput UnityEvents on purpose: it needs
// no inspector setup on the player prefab and cannot be broken by someone switching
// the PlayerInput notification behaviour.
public class WeaponRollController : MonoBehaviour
{
    [Header("Slots 0-17: Melee, Ranged, GrapplingHook, Shield, ObstacleDrawer, Flamethrower, BombLauncher, Trap, Turret, Decoy, Boomerang, Book, BattleHammer, StealthCloak, Torch, TimeClock, Mortar, SmokeScreen")]
    public WeaponData[] allWeaponSlots = new WeaponData[18];

    public const int HotbarSlots = 10;

    Weapon _weapon;
    WeaponRollUI _ui;
    PlayerInput _playerInput;
    int _playerIndex;   // this player's index, for the per-player unlock pool

    // ---- Bound actions (resolved once, follow rebinds automatically) ------
    InputAction _prevWeapon, _nextWeapon, _prevTool, _nextTool, _modifier;
    readonly InputAction[] _hotbar = new InputAction[HotbarSlots];
    bool _actionsResolved;

    readonly List<int> _activeWeapons = new List<int>();
    readonly List<int> _activeTools = new List<int>();

    int _weaponIndex;
    int _toolIndex;

    public int WeaponCount => _activeWeapons.Count;
    public int ToolCount => _activeTools.Count;
    public int CurrentWeaponIndex => _weaponIndex;
    public int CurrentToolIndex => _toolIndex;

    // Legacy compatibility
    public int ActiveCount => _activeWeapons.Count + _activeTools.Count;
    public int CurrentActiveIndex => _weaponIndex;

    public WeaponData WeaponDataAt(int pos)
    {
        if (pos < 0 || pos >= _activeWeapons.Count) return null;
        int raw = _activeWeapons[pos];
        return raw < allWeaponSlots.Length ? allWeaponSlots[raw] : null;
    }

    public WeaponData ToolDataAt(int pos)
    {
        if (pos < 0 || pos >= _activeTools.Count) return null;
        int raw = _activeTools[pos];
        return raw < allWeaponSlots.Length ? allWeaponSlots[raw] : null;
    }

    public WeaponData DataAt(int pos) => WeaponDataAt(pos);

    void Awake()
    {
        // Sibling weapon (child of this player) and this player's PlayerInput.
        _weapon = GetComponentInChildren<Weapon>();
        _playerInput = GetComponent<PlayerInput>() ?? GetComponentInParent<PlayerInput>();

        var pref = GetComponent<PlayerRef>() ?? GetComponentInParent<PlayerRef>();
        _playerIndex = pref != null ? pref.PlayerIndex : 0;

        if (_weapon == null)
            Debug.LogWarning("[WeaponRollController] No sibling Weapon found under this player. " +
                             "This controller should live on the Player prefab root, with the Weapon as a child.");
    }

    void Start()
    {
        ResolveActions();

        // Sibling UI (on the same player). Fallback to scene search for the
        // single-player / legacy layout.
        _ui = GetComponent<WeaponRollUI>() ?? GetComponentInChildren<WeaponRollUI>();
        if (_ui == null) _ui = FindFirstObjectByType<WeaponRollUI>();

        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged += OnUnlocksChanged;
        else
            Debug.LogError("[WeaponRollController] WeaponUnlockRegistry.Instance is null in Start!");

        Rebuild();

        if (_activeWeapons.Count > 0)
            EquipWeapon(ScrollTarget.None);
        if (_activeTools.Count > 0)
            EquipTool(ScrollTarget.None);
    }

    void OnDestroy()
    {
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged -= OnUnlocksChanged;
    }

    // ---- Action resolution -----------------------------------------------
    // Rebinding mutates the SAME InputAction objects (it only changes their overrides),
    // so these references stay valid for the lifetime of the player — no need to
    // re-resolve when ControlRebindService fires OnRebindsChanged.
    void ResolveActions()
    {
        if (_playerInput == null || _playerInput.actions == null)
        {
            Debug.LogWarning("[WeaponRollController] No PlayerInput/actions on this player — " +
                             "hotbar input is disabled. Add a PlayerInput with PlayerInputActions.");
            return;
        }

        _prevWeapon = PlayerAttack.FindAction(_playerInput, "PreviousWeapon");
        _nextWeapon = PlayerAttack.FindAction(_playerInput, "NextWeapon");
        _prevTool = PlayerAttack.FindAction(_playerInput, "PreviousTool");
        _nextTool = PlayerAttack.FindAction(_playerInput, "NextTool");
        _modifier = PlayerAttack.FindAction(_playerInput, "HotbarModifier");

        for (int i = 0; i < HotbarSlots; i++)
            _hotbar[i] = PlayerAttack.FindAction(_playerInput, $"Hotbar{i + 1}");

        _actionsResolved = _nextWeapon != null || _prevWeapon != null || _hotbar[0] != null;

        if (!_actionsResolved)
            Debug.LogWarning("[WeaponRollController] The Player map has no hotbar actions " +
                             "(NextWeapon / PreviousWeapon / Hotbar1…). Is PlayerInputActions " +
                             "up to date on this PlayerInput?");
    }

    // True while the tool modifier (Ctrl by default) is held. Reuses PlayerAttack's
    // shared held-check so the modifier follows rebinds like everything else.
    bool ToolModifierHeld => PlayerAttack.ActionPhysicallyHeld(_modifier);

    void Update()
    {
        if (!_actionsResolved) return;

        // Suspend hotbar input while a menu owns input. Was `Time.timeScale == 0f`,
        // which missed non-freezing overlays — the D-pad would still cycle weapons
        // underneath them. UIModalStack is the same authority the menu cursor uses.
        if (Time.timeScale == 0f || UIModalStack.MenuInputActive) return;

        bool tools = ToolModifierHeld;

        // Cycle. The dedicated tool actions always mean tools; the weapon actions mean
        // tools while the modifier is held — so Ctrl+wheel-down cycles tools FORWARD,
        // which is what shift+wheel-down used to do (on a control that isn't also Dash).
        // Plain wheel-up is bound to PreviousTool, so tools are reachable without any
        // modifier, exactly as before.
        if (Triggered(_nextWeapon)) { if (tools) CycleTool(+1); else CycleWeapon(+1); }
        if (Triggered(_prevWeapon)) { if (tools) CycleTool(-1); else CycleWeapon(-1); }
        if (Triggered(_nextTool)) CycleTool(+1);
        if (Triggered(_prevTool)) CycleTool(-1);

        // Direct select: 1..0 picks a weapon, modifier+1..0 picks a tool.
        for (int i = 0; i < HotbarSlots; i++)
        {
            if (!Triggered(_hotbar[i])) continue;
            if (tools) PickTool(i);
            else PickWeapon(i);
        }
    }

    static bool Triggered(InputAction a) => a != null && a.triggered;

    void OnUnlocksChanged()
    {
        var previousWeapons = new HashSet<int>(_activeWeapons);
        var previousTools = new HashSet<int>(_activeTools);
        Rebuild();

        int newWeapon = -1;
        foreach (int raw in _activeWeapons)
            if (!previousWeapons.Contains(raw)) { newWeapon = raw; break; }

        if (newWeapon >= 0)
            _weaponIndex = _activeWeapons.IndexOf(newWeapon);
        else
            _weaponIndex = Mathf.Clamp(_weaponIndex, 0, Mathf.Max(_activeWeapons.Count - 1, 0));

        int newTool = -1;
        foreach (int raw in _activeTools)
            if (!previousTools.Contains(raw)) { newTool = raw; break; }

        if (newTool >= 0)
            _toolIndex = _activeTools.IndexOf(newTool);
        else
            _toolIndex = Mathf.Clamp(_toolIndex, 0, Mathf.Max(_activeTools.Count - 1, 0));

        ScrollTarget target = newTool >= 0 ? ScrollTarget.Tool : (newWeapon >= 0 ? ScrollTarget.Weapon : ScrollTarget.None);
        _ui?.Refresh(_weaponIndex, _toolIndex, target);

        if (_activeWeapons.Count > 0) EquipWeapon(ScrollTarget.None);
        if (_activeTools.Count > 0) EquipTool(ScrollTarget.None);
    }

    void Rebuild()
    {
        _activeWeapons.Clear();
        _activeTools.Clear();
        var reg = WeaponUnlockRegistry.Instance;

        for (int i = 0; i < allWeaponSlots.Length; i++)
        {
            if (allWeaponSlots[i] == null) continue;
            if (reg != null && !reg.IsUnlocked(i, _playerIndex)) continue;

            if (allWeaponSlots[i].IsTool)
                _activeTools.Add(i);
            else
                _activeWeapons.Add(i);
        }

        _activeWeapons.Sort();
        _activeTools.Sort();

        _weaponIndex = _activeWeapons.Count > 0 ? Mathf.Clamp(_weaponIndex, 0, _activeWeapons.Count - 1) : 0;
        _toolIndex = _activeTools.Count > 0 ? Mathf.Clamp(_toolIndex, 0, _activeTools.Count - 1) : 0;
    }

    void CycleWeapon(int dir)
    {
        if (_activeWeapons.Count <= 1) return;
        _weaponIndex = (_weaponIndex + dir + _activeWeapons.Count) % _activeWeapons.Count;
        EquipWeapon(ScrollTarget.Weapon);
    }

    void PickWeapon(int pos)
    {
        if (pos < 0 || pos >= _activeWeapons.Count || pos == _weaponIndex) return;
        _weaponIndex = pos;
        EquipWeapon(ScrollTarget.Weapon);
    }

    void EquipWeapon(ScrollTarget scrollTarget)
    {
        if (_activeWeapons.Count == 0 || _weapon == null) return;
        WeaponData chosen = allWeaponSlots[_activeWeapons[_weaponIndex]];
        if (chosen == null) return;

        _weapon.HotSwapWeapon(chosen);
        _ui?.Refresh(_weaponIndex, _toolIndex, scrollTarget);
    }

    void CycleTool(int dir)
    {
        if (_activeTools.Count <= 1) return;
        _toolIndex = (_toolIndex + dir + _activeTools.Count) % _activeTools.Count;
        EquipTool(ScrollTarget.Tool);
    }

    void PickTool(int pos)
    {
        if (pos < 0 || pos >= _activeTools.Count || pos == _toolIndex) return;
        _toolIndex = pos;
        EquipTool(ScrollTarget.Tool);
    }

    void EquipTool(ScrollTarget scrollTarget)
    {
        if (_activeTools.Count == 0 || _weapon == null) return;
        WeaponData chosen = allWeaponSlots[_activeTools[_toolIndex]];
        if (chosen == null) return;

        _weapon.HotSwapTool(chosen);
        _ui?.Refresh(_weaponIndex, _toolIndex, scrollTarget);
    }
}
