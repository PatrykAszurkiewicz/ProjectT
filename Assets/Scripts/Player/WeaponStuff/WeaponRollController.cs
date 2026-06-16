using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum ScrollTarget { None, Weapon, Tool }


// Per-player weapon/tool hotbar controller. Lives on the PLAYER prefab (root).
// Co-op changes vs. the old scene singleton:
//   Binds to its OWN sibling Weapon (GetComponentInChildren) and sibling
//    WeaponRollUI — not FindFirstObjectByType, which would grab P1's.
//   Reads input from THIS player's PlayerInput devices (the paired gamepad,
//    or keyboard+mouse) instead of the global Gamepad/Keyboard/Mouse.current,
//    so each player cycles their own hotbar.
//   The unlock POOL is still shared (WeaponUnlockRegistry) — both players see
//    the same unlocked weapons — but each player independently SELECTS which
//    one they hold (_weaponIndex/_toolIndex are per-instance).
// Single player: with one player the bound devices are just the only ones, so
// behaviour matches the old global polling exactly.

public class WeaponRollController : MonoBehaviour
{
    [Header("Slots 0-17: Melee, Ranged, GrapplingHook, Shield, ObstacleDrawer, Flamethrower, BombLauncher, Trap, Turret, Decoy, Boomerang, Book, BattleHammer, StealthCloak, Torch, TimeClock, Mortar, SmokeScreen")]
    public WeaponData[] allWeaponSlots = new WeaponData[18];

    Weapon _weapon;
    WeaponRollUI _ui;
    PlayerInput _playerInput;
    int _playerIndex;   // this player's index, for the per-player unlock pool

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

    // ---- This player's input devices (not the global *.current) -----------

    Gamepad PlayerPad()
    {
        if (_playerInput == null) return null;
        foreach (var d in _playerInput.devices) if (d is Gamepad g) return g;
        return null;
    }

    Keyboard PlayerKeyboard()
    {
        if (_playerInput == null) return null;
        foreach (var d in _playerInput.devices) if (d is Keyboard k) return k;
        return null;
    }

    Mouse PlayerMouse()
    {
        if (_playerInput == null) return null;
        foreach (var d in _playerInput.devices) if (d is Mouse m) return m;
        return null;
    }

    void Update()
    {
        // Suspend ALL input handling when the game is paused (pause menu open).
        if (Time.timeScale == 0f) return;

        // Gamepad: D-pad cycles weapons (left/right) and tools (up/down).
        var pad = PlayerPad();
        if (pad != null)
        {
            if (pad.dpad.right.wasPressedThisFrame) CycleWeapon(+1);
            if (pad.dpad.left.wasPressedThisFrame) CycleWeapon(-1);
            if (pad.dpad.down.wasPressedThisFrame) CycleTool(+1);
            if (pad.dpad.up.wasPressedThisFrame) CycleTool(-1);
        }

        var kb = PlayerKeyboard();
        bool shiftHeld = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        // Mouse scroll: up = cycle tool, down = cycle weapon (shift = tool).
        var mouse = PlayerMouse();
        if (mouse != null)
        {
            float s = mouse.scroll.ReadValue().y;
            if (s > 0f)
            {
                CycleTool(-1);
            }
            else if (s < 0f)
            {
                if (shiftHeld) CycleTool(+1);
                else CycleWeapon(+1);
            }
        }

        // Number keys: plain = weapon, shift = tool.
        if (kb != null)
        {
            if (shiftHeld)
            {
                if (kb.digit1Key.wasPressedThisFrame) PickTool(0);
                if (kb.digit2Key.wasPressedThisFrame) PickTool(1);
                if (kb.digit3Key.wasPressedThisFrame) PickTool(2);
                if (kb.digit4Key.wasPressedThisFrame) PickTool(3);
                if (kb.digit5Key.wasPressedThisFrame) PickTool(4);
                if (kb.digit6Key.wasPressedThisFrame) PickTool(5);
                if (kb.digit7Key.wasPressedThisFrame) PickTool(6);
                if (kb.digit8Key.wasPressedThisFrame) PickTool(7);
                if (kb.digit9Key.wasPressedThisFrame) PickTool(8);
                if (kb.digit0Key.wasPressedThisFrame) PickTool(9);
            }
            else
            {
                if (kb.digit1Key.wasPressedThisFrame) PickWeapon(0);
                if (kb.digit2Key.wasPressedThisFrame) PickWeapon(1);
                if (kb.digit3Key.wasPressedThisFrame) PickWeapon(2);
                if (kb.digit4Key.wasPressedThisFrame) PickWeapon(3);
                if (kb.digit5Key.wasPressedThisFrame) PickWeapon(4);
                if (kb.digit6Key.wasPressedThisFrame) PickWeapon(5);
                if (kb.digit7Key.wasPressedThisFrame) PickWeapon(6);
                if (kb.digit8Key.wasPressedThisFrame) PickWeapon(7);
                if (kb.digit9Key.wasPressedThisFrame) PickWeapon(8);
                if (kb.digit0Key.wasPressedThisFrame) PickWeapon(9);
            }
        }
    }

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

