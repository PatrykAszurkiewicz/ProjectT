using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class WeaponRollController : MonoBehaviour
{
    [Header("Slots 0-4: Melee, Ranged, GrapplingHook, Shield, ObstacleDrawer")]
    public WeaponData[] allWeaponSlots = new WeaponData[5];

    Weapon _weapon;
    WeaponRollUI _ui;

    readonly List<int> _active = new List<int>();
    int _index;

    public int ActiveCount => _active.Count;
    public int CurrentActiveIndex => _index;

    public WeaponData DataAt(int pos)
    {
        if (pos < 0 || pos >= _active.Count) return null;
        int raw = _active[pos];
        return raw < allWeaponSlots.Length ? allWeaponSlots[raw] : null;
    }

    void Awake()
    {
        _weapon = FindFirstObjectByType<Weapon>();
        if (_weapon == null)
            Debug.LogError("[WeaponRollController] No Weapon component found in scene.");
    }

    void Start()
    {
        _ui = FindFirstObjectByType<WeaponRollUI>();

        // Subscribe immediately, no coroutine delay needed.
        // AugmentEffectHandler calls TryUnlock directly so timing is never an issue.
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged += OnUnlocksChanged;
        else
            Debug.LogError("[WeaponRollController] WeaponUnlockRegistry.Instance is null in Start!");

        Rebuild();
        //Debug.Log($"[WeaponRollController] Ready. Active weapon count: {_active.Count}");

        if (_active.Count > 0)
            Equip(animate: false);
    }

    void OnDestroy()
    {
        if (WeaponUnlockRegistry.Instance != null)
            WeaponUnlockRegistry.Instance.OnUnlocksChanged -= OnUnlocksChanged;
    }

    void Update()
    {
        if (_active.Count <= 1) return;

        if (Mouse.current != null)
        {
            float s = Mouse.current.scroll.ReadValue().y;
            if (s > 0f) Cycle(-1);
            else if (s < 0f) Cycle(+1);
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Pick(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) Pick(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) Pick(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) Pick(3);
            if (Keyboard.current.digit5Key.wasPressedThisFrame) Pick(4);
        }
    }

    void OnUnlocksChanged()
    {
        // Remember which slots were active before, to find the newly added one
        var previousSlots = new System.Collections.Generic.HashSet<int>(_active);
        Rebuild();

        // Find the newly unlocked slot and select it
        int newSlot = -1;
        foreach (int raw in _active)
            if (!previousSlots.Contains(raw)) { newSlot = raw; break; }

        if (newSlot >= 0)
            _index = _active.IndexOf(newSlot);      // jump to the new weapon
        else
            _index = Mathf.Clamp(_index, 0, Mathf.Max(_active.Count - 1, 0));

        if (_ui == null) _ui = FindFirstObjectByType<WeaponRollUI>();
        _ui?.Refresh(_index, animate: true);
        Equip(animate: false);

        //Debug.Log($"[WeaponRollController] Unlocks changed. Active: {_active.Count}, index: {_index}");
    }

    void Rebuild()
    {
        _active.Clear();
        var reg = WeaponUnlockRegistry.Instance;

        for (int i = 0; i < allWeaponSlots.Length; i++)
        {
            if (allWeaponSlots[i] == null) continue;
            if (reg != null && !reg.IsUnlocked(i)) continue;
            _active.Add(i);
        }

        _active.Sort();
        _index = _active.Count > 0 ? Mathf.Clamp(_index, 0, _active.Count - 1) : 0;
    }

    void Cycle(int dir)
    {
        if (_active.Count == 0) return;
        _index = (_index + dir + _active.Count) % _active.Count;
        Equip(animate: true);
    }

    void Pick(int pos)
    {
        if (pos < 0 || pos >= _active.Count || pos == _index) return;
        _index = pos;
        Equip(animate: true);
    }

    void Equip(bool animate)
    {
        if (_active.Count == 0 || _weapon == null) return;
        WeaponData chosen = allWeaponSlots[_active[_index]];
        if (chosen == null) return;

        _weapon.HotSwapWeapon(chosen);

        if (_ui == null) _ui = FindFirstObjectByType<WeaponRollUI>();
        _ui?.Refresh(_index, animate);
    }
}