using UnityEngine;
using System.Collections.Generic;

public class WeaponUnlockRegistry : MonoBehaviour
{
    public static WeaponUnlockRegistry Instance { get; private set; }

    private static readonly Dictionary<int, int> AugmentToSlot = new Dictionary<int, int>
    {
        { 2,  0 },   // Melee
        { 66, 1 },   // Ranged
        { 65, 2 },   // Grappling Hook
        { 81, 3 },   // Shield
        { 4,  4 },   // Obstacle Drawer
        { 93, 5 },   // Flamethrower
        { 314, 6 },   // BombLauncher
        { 315, 7 },   // Trap
        { 317, 8 },   // Turret
    };

    private readonly HashSet<int> _unlocked = new HashSet<int>();
    public System.Action OnUnlocksChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Slot 0 (Melee) is always available as default
        _unlocked.Add(0);

        if (AugmentRegistry.Instance != null)
        {
            foreach (int id in AugmentRegistry.Instance.GetAppliedAugments())
                TryUnlock(id, silent: true);

            AugmentRegistry.Instance.OnAugmentApplied += OnAugmentApplied;
        }

        // Notify after all initial unlocks are set
        OnUnlocksChanged?.Invoke();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (AugmentRegistry.Instance != null)
            AugmentRegistry.Instance.OnAugmentApplied -= OnAugmentApplied;
    }

    void OnAugmentApplied(AugmentData data) => TryUnlock(data.ID);

    public void TryUnlock(int augmentID, bool silent = false)
    {
        if (!AugmentToSlot.TryGetValue(augmentID, out int slot)) return;
        if (!_unlocked.Add(slot)) return;

        if (!silent)
            Debug.Log($"[WeaponUnlockRegistry] Unlocked slot {slot} via augment {augmentID}");

        OnUnlocksChanged?.Invoke();
    }

    public void ForceUnlock(int slot)
    {
        if (_unlocked.Add(slot))
        {
            //Debug.Log($"[WeaponUnlockRegistry] Force-unlocked slot {slot}");
            OnUnlocksChanged?.Invoke();
        }
    }

    public IReadOnlyCollection<int> UnlockedSlots => _unlocked;
    public bool IsUnlocked(int slot) => _unlocked.Contains(slot);
}
