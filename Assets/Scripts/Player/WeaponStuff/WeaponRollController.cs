using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum ScrollTarget { None, Weapon, Tool }

public class WeaponRollController : MonoBehaviour
{
    [Header("Slots 0-10: Melee, Ranged, GrapplingHook, Shield, ObstacleDrawer, Flamethrower, BombLauncher, Trap, Turret, Decoy, Boomerang")]
    public WeaponData[] allWeaponSlots = new WeaponData[11];

    Weapon _weapon;
    WeaponRollUI _ui;

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
        _weapon = FindFirstObjectByType<Weapon>();
        if (_weapon == null)
            Debug.LogError("[WeaponRollController] No Weapon component found in scene.");
    }

    void Start()
    {
        _ui = FindFirstObjectByType<WeaponRollUI>();

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

    void Update()
    {
        bool shiftHeld = Keyboard.current != null &&
            (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        // Mouse scroll: up = cycle tool, down = cycle weapon
        if (Mouse.current != null)
        {
            float s = Mouse.current.scroll.ReadValue().y;
            if (s > 0f)
            {
                // Scroll up → tool
                if (shiftHeld)
                    CycleTool(-1);
                else
                    CycleTool(-1);
            }
            else if (s < 0f)
            {
                // Scroll down → weapon (shift overrides to tool)
                if (shiftHeld)
                    CycleTool(+1);
                else
                    CycleWeapon(+1);
            }
        }

        // Number keys: plain = weapon, shift = tool
        if (Keyboard.current != null)
        {
            if (shiftHeld)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) PickTool(0);
                if (Keyboard.current.digit2Key.wasPressedThisFrame) PickTool(1);
                if (Keyboard.current.digit3Key.wasPressedThisFrame) PickTool(2);
                if (Keyboard.current.digit4Key.wasPressedThisFrame) PickTool(3);
                if (Keyboard.current.digit5Key.wasPressedThisFrame) PickTool(4);
                if (Keyboard.current.digit6Key.wasPressedThisFrame) PickTool(5);
                if (Keyboard.current.digit7Key.wasPressedThisFrame) PickTool(6);
                if (Keyboard.current.digit8Key.wasPressedThisFrame) PickTool(7);
                if (Keyboard.current.digit9Key.wasPressedThisFrame) PickTool(8);
                if (Keyboard.current.digit0Key.wasPressedThisFrame) PickTool(9);
            }
            else
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) PickWeapon(0);
                if (Keyboard.current.digit2Key.wasPressedThisFrame) PickWeapon(1);
                if (Keyboard.current.digit3Key.wasPressedThisFrame) PickWeapon(2);
                if (Keyboard.current.digit4Key.wasPressedThisFrame) PickWeapon(3);
                if (Keyboard.current.digit5Key.wasPressedThisFrame) PickWeapon(4);
                if (Keyboard.current.digit6Key.wasPressedThisFrame) PickWeapon(5);
                if (Keyboard.current.digit7Key.wasPressedThisFrame) PickWeapon(6);
                if (Keyboard.current.digit8Key.wasPressedThisFrame) PickWeapon(7);
                if (Keyboard.current.digit9Key.wasPressedThisFrame) PickWeapon(8);
                if (Keyboard.current.digit0Key.wasPressedThisFrame) PickWeapon(9);
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

        if (_ui == null) _ui = FindFirstObjectByType<WeaponRollUI>();
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
            if (reg != null && !reg.IsUnlocked(i)) continue;

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

        if (_ui == null) _ui = FindFirstObjectByType<WeaponRollUI>();
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

        if (_ui == null) _ui = FindFirstObjectByType<WeaponRollUI>();
        _ui?.Refresh(_weaponIndex, _toolIndex, scrollTarget);
    }
}
