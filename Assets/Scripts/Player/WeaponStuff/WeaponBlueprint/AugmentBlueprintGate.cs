using System.Collections.Generic;
using UnityEngine;

/// Helper that translates the "weapon/tool unlock augment IDs" into a blueprint gate.
/// Call from AugmentsMenu (or wherever the augment-reward pool is built) to filter or
/// reweight unlock augments based on what blueprints the player has discovered.
///
/// USAGE — filtering example, in your AugmentsMenu where you build the candidate pool:
///
///     var pool = registry.GetAllAugments();
///     pool = AugmentBlueprintGate.FilterByBlueprints(pool, gateMode: GateMode.HardFilter);
///
/// USAGE — weighting example, if you pick by weighted random:
///
///     float weight = AugmentBlueprintGate.GetSelectionWeightMultiplier(augment.ID);
///     // ... multiply your base weight by this.
public static class AugmentBlueprintGate
{
    public enum GateMode
    {
        /// Non-blueprinted unlock augments are removed from the pool entirely.
        /// Blueprinted unlock augments appear with normal weight.
        HardFilter,

        /// Non-blueprinted unlock augments stay in the pool but at a tiny weight (rare encounter).
        /// Blueprinted unlock augments get a strong boost — the intended path.
        SoftBias,

        /// Filtering disabled — every augment passes through unchanged.
        /// Use only if you want to bypass the blueprint mechanic temporarily.
        Off,
    }

    /// Augment IDs that correspond to unlock-a-weapon-or-tool. Must mirror
    /// WeaponUnlockRegistry.AugmentToSlot. Kept in sync manually — if you add a
    /// new weapon slot/augment, add it here AND in WeaponUnlockRegistry.
    private static readonly Dictionary<int, int> UnlockAugmentToSlot = new Dictionary<int, int>
    {
        { 2,  0 },   // Melee
        { 66, 1 },   // Ranged
        { 65, 2 },   // GrapplingHook
        { 81, 3 },   // Shield
        { 4,  4 },   // ObstacleDrawer
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

    // Default weights — tweak in your AugmentsMenu's serialized fields if you want them inspector-driven.
    public const float WeightBlueprintedBoost = 5f;
    public const float WeightNonBlueprintedRare = 0.2f;
    public const float WeightNormal = 1f;

    /// True if this augment ID is one of the weapon/tool unlock augments.
    public static bool IsUnlockAugment(int augmentId) => UnlockAugmentToSlot.ContainsKey(augmentId);

    /// Get the slot index for an unlock augment, or -1 if it's not an unlock augment.
    public static int SlotForUnlockAugment(int augmentId)
        => UnlockAugmentToSlot.TryGetValue(augmentId, out int slot) ? slot : -1;

    /// Inverse of SlotForUnlockAugment. Returns the augment ID that unlocks the given
    /// weapon/tool slot, or -1 if no augment maps to that slot.
    public static int AugmentForSlot(int slot)
    {
        foreach (var kv in UnlockAugmentToSlot)
            if (kv.Value == slot) return kv.Key;
        return -1;
    }

    /// Combined eligibility check used by AugmentsMenu's random pool filter.
    ///
    /// Rules:
    ///   1. Non-unlock augments → eligible iff Priority == 0 (original behaviour).
    ///   2. Unlock augments for a STARTER slot (Melee) → never eligible.
    ///      Starter weapons are always equipped; offering "Unlock Melee" is noise.
    ///   3. Unlock augments for a non-starter slot → eligible iff blueprinted.
    ///      Priority is IGNORED for blueprinted unlock-augments — without this,
    ///      Priority 1 unlocks (Shield, Flamethrower, Bomb, Trap, Decoy, Turret,
    ///      Boomerang) could never roll regardless of blueprint state.
    public static bool IsEligibleForRandomPool(AugmentData a)
    {
        if (a == null) return false;

        // Non-unlock augments use the original Priority 0 gate.
        if (!UnlockAugmentToSlot.TryGetValue(a.ID, out int slot))
            return a.Priority == 0;

        // Starter slot → never offered.
        if (IsStarterSlot(slot)) return false;

        // Non-starter unlock augment → must be blueprinted.
        var reg = WeaponBlueprintRegistry.Instance;
        if (reg == null) return false;
        return reg.IsBlueprinted(slot);
    }

    /// True if the slot is treated as already-known from the start of every run
    /// (i.e. configured in WeaponBlueprintRegistry.starterSlots).
    /// The starter slot's unlock-augment is never offered in the random pool.
    private static bool IsStarterSlot(int slot)
    {
        // Slot 0 (Melee) is the only starter today; mirror WeaponBlueprintRegistry
        // by also asking the registry whether it considers the slot droppable.
        // A non-droppable, blueprinted slot = starter.
        var reg = WeaponBlueprintRegistry.Instance;
        if (reg == null) return slot == 0; // safe default
        return reg.IsBlueprinted(slot) && !reg.IsDroppable(slot);
    }

    /// Returns true if this augment is allowed in the current pool given the gate mode.
    /// Non-unlock augments always pass. Unlock augments are checked against the blueprint registry.
    public static bool IsAllowed(int augmentId, GateMode mode = GateMode.HardFilter)
    {
        if (mode == GateMode.Off) return true;
        if (!UnlockAugmentToSlot.TryGetValue(augmentId, out int slot)) return true; // not an unlock augment

        var reg = WeaponBlueprintRegistry.Instance;
        if (reg == null) return true; // fail open — no registry means feature is off

        bool blueprinted = reg.IsBlueprinted(slot);

        return mode switch
        {
            GateMode.HardFilter => blueprinted,
            GateMode.SoftBias => true,            // SoftBias never filters; use weight instead
            _ => true,
        };
    }

    /// Returns a weight multiplier you can apply to weighted-random augment selection.
    /// 1.0 = unchanged. Higher = more likely. Use alongside or instead of IsAllowed depending on mode.
    public static float GetSelectionWeightMultiplier(int augmentId, GateMode mode = GateMode.HardFilter)
    {
        if (mode == GateMode.Off) return WeightNormal;
        if (!UnlockAugmentToSlot.TryGetValue(augmentId, out int slot)) return WeightNormal;

        var reg = WeaponBlueprintRegistry.Instance;
        if (reg == null) return WeightNormal;

        bool blueprinted = reg.IsBlueprinted(slot);

        return mode switch
        {
            GateMode.HardFilter => blueprinted ? WeightBlueprintedBoost : 0f,
            GateMode.SoftBias => blueprinted ? WeightBlueprintedBoost : WeightNonBlueprintedRare,
            _ => WeightNormal,
        };
    }

    // Debug: dump the gate's view of the world ONCE per game session so we can
    // see exactly what registry/blueprints the gate is seeing when the popup builds the pool.
    // Set this to true to dump on EVERY filter call (very spammy).
    public static bool VerboseDebugDump = false;
    private static bool _dumpedOnce = false;

    /// Convenience: filter a list of augment-IDs (or AugmentData via a selector) in one pass.
    public static List<AugmentData> FilterByBlueprints(IEnumerable<AugmentData> pool, GateMode mode = GateMode.HardFilter)
    {
        if (VerboseDebugDump || !_dumpedOnce)
        {
            _dumpedOnce = true;
            var reg = WeaponBlueprintRegistry.Instance;
            if (reg == null)
            {
                Debug.LogError("[AugmentBlueprintGate] WeaponBlueprintRegistry.Instance is NULL when filtering augments. " +
                               "Gate is failing OPEN — every unlock-augment will appear in rolls. " +
                               "Make sure a WeaponBlueprintRegistry component exists in your scene and its GameObject is active.");
            }
            else
            {
                var bp = string.Join(",", reg.BlueprintedSlots);
                //Debug.Log($"[AugmentBlueprintGate] First filter call. Gate mode={mode}. " +
                //          $"Registry sees blueprinted slots: [{bp}]");
            }
        }

        var result = new List<AugmentData>();
        foreach (var a in pool)
        {
            bool allowed = IsAllowed(a?.ID ?? -1, mode);
            if (a != null && allowed) result.Add(a);
            if (VerboseDebugDump && a != null && IsUnlockAugment(a.ID))
            {
                int slot = SlotForUnlockAugment(a.ID);
                bool blueprinted = WeaponBlueprintRegistry.Instance?.IsBlueprinted(slot) ?? false;
                //Debug.Log($"  [Gate] augment {a.ID} (slot {slot}) blueprinted={blueprinted} → {(allowed ? "ALLOWED" : "BLOCKED")}");
            }
        }
        return result;
    }
}

