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
        { 314, 6 },  // BombLauncher
        { 315, 7 },  // Trap
        { 317, 8 },  // Turret
        { 316, 9 },  // Decoy
        { 318, 10 }, // Boomerang
        { 321, 11 }, // Revenant Necronomicon (Book)
        { 322, 12 }, // Battle Hammer
        { 323, 13 }, // Stealth Cloak
        { 324, 14 }, // Torch
        { 326, 15 }, // Time Clock
        { 327, 16 }, // Mortar
        { 329, 17 }, // Smoke Screen

    };

    // Phase 5/6 (co-op): unlocks are PER PLAYER. Each player only sees the
    // weapons/tools THEY unlocked; slot 0 (Melee) is the default for everyone.
    private readonly Dictionary<int, HashSet<int>> _unlockedByPlayer = new Dictionary<int, HashSet<int>>();
    public System.Action OnUnlocksChanged;

    private HashSet<int> PoolFor(int playerIndex)
    {
        if (!_unlockedByPlayer.TryGetValue(playerIndex, out var set))
        {
            set = new HashSet<int> { 0 }; // Melee always available
            _unlockedByPlayer[playerIndex] = set;
        }
        return set;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (AugmentRegistry.Instance != null)
        {
            // Seed each player's pool from THEIR applied set (single-player / replay).
            int players = PlayerRegistry.Count > 0 ? PlayerRegistry.Count : 1;
            for (int idx = 0; idx < Mathf.Max(players, 1); idx++)
                foreach (int id in AugmentRegistry.Instance.GetAppliedAugments(idx))
                    TryUnlock(id, idx, silent: true);

            AugmentRegistry.Instance.OnAugmentAppliedByPlayer += OnAugmentAppliedByPlayer;
        }

        // Notify after all initial unlocks are set
        OnUnlocksChanged?.Invoke();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (AugmentRegistry.Instance != null)
            AugmentRegistry.Instance.OnAugmentAppliedByPlayer -= OnAugmentAppliedByPlayer;
    }

    void OnAugmentAppliedByPlayer(AugmentData data, int playerIndex) => TryUnlock(data.ID, playerIndex);

    // Per-player unlock (Phase 5/6).
    public void TryUnlock(int augmentID, int playerIndex, bool silent = false)
    {
        if (!AugmentToSlot.TryGetValue(augmentID, out int slot)) return;
        if (!PoolFor(playerIndex).Add(slot)) return;

        if (!silent)
            Debug.Log($"[WeaponUnlockRegistry] P{playerIndex} unlocked slot {slot} via augment {augmentID}");

        OnUnlocksChanged?.Invoke();
    }

    // Back-compat: single-player path unlocks for player 0.
    public void TryUnlock(int augmentID, bool silent = false) => TryUnlock(augmentID, 0, silent);

    public void ForceUnlock(int slot, int playerIndex = 0)
    {
        if (PoolFor(playerIndex).Add(slot))
            OnUnlocksChanged?.Invoke();
    }

    public bool IsUnlocked(int slot, int playerIndex)
    {
        if (slot == 0) return true; // Melee default for everyone
        return _unlockedByPlayer.TryGetValue(playerIndex, out var set) && set.Contains(slot);
    }

    // Back-compat (player 0).
    public bool IsUnlocked(int slot) => IsUnlocked(slot, 0);
    public IReadOnlyCollection<int> UnlockedSlots => PoolFor(0);
}

