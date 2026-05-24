using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// PERMAUPGRADE LAYER — separate from WeaponUnlockRegistry.
/// WeaponUnlockRegistry  = "what's currently in your hotbar this run" (driven by augments picked this run)
/// WeaponBlueprintRegistry = "what blueprints you have ever discovered" (persists across runs via PlayerPrefs)
///
/// A blueprint does NOT put the weapon in the hotbar. It biases the augment-reward pool so that
/// the unlock-augment for that slot can (or can ONLY) appear as a future augment roll.
///
/// Slot indices match WeaponRollController.allWeaponSlots:
///   0 Melee, 1 Ranged, 2 GrapplingHook, 3 Shield, 4 ObstacleDrawer,
///   5 Flamethrower, 6 BombLauncher, 7 Trap, 8 Turret, 9 Decoy, 10 Boomerang,
///   11 Book, 12 BattleHammer, 13 StealthCloak
public class WeaponBlueprintRegistry : MonoBehaviour
{
    public static WeaponBlueprintRegistry Instance { get; private set; }

    private const string PrefsKey = "WeaponBlueprints_v1";

    // Slots considered "starter" — already known, never offered as a blueprint drop.
    // Melee (0) is always available; treat it as pre-blueprinted.
    [Header("Starter Slots (auto-blueprinted, never dropped)")]
    [Tooltip("Slot indices that count as already-blueprinted from the start. " +
             "Melee (0) is the default starting weapon.")]
    [SerializeField] private int[] starterSlots = new int[] { 0 };

    // Slots that CAN be blueprinted by boss drops. Anything not in this list is ignored
    // by the dropper (e.g. you may want Shield to remain quest-only, etc.).
    [Header("Droppable Slots")]
    [Tooltip("Which slots may be rolled as a boss drop. Leave empty to allow all non-starter slots.")]
    [SerializeField]
    private int[] droppableSlotsWhitelist = new int[]
    {
        1,  // Ranged
        2,  // GrapplingHook
        3,  // Shield
        4,  // ObstacleDrawer
        5,  // Flamethrower
        6,  // BombLauncher
        7,  // Trap
        8,  // Turret
        9,  // Decoy
        10, // Boomerang
        11, // Revenant Necronomicon (Book)
        12, // Battle Hammer
        13, // Stealth Cloak
    };

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool resetOnPlay = false;

    private readonly HashSet<int> _blueprintedSlots = new HashSet<int>();

    public System.Action<int> OnBlueprintUnlocked;   // (slot)
    public System.Action OnBlueprintsChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // DontDestroyOnLoad requires a root-level GameObject. If the user nested
        // this component under another object in the Hierarchy, detach first.
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        if (resetOnPlay)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            if (debugLog) Debug.Log("[BlueprintRegistry] Reset on play.");
        }

        LoadFromPrefs();

        // Always seed starter slots
        foreach (int s in starterSlots) _blueprintedSlots.Add(s);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    //  PUBLIC API 

    public bool IsBlueprinted(int slot) => _blueprintedSlots.Contains(slot);

    public IReadOnlyCollection<int> BlueprintedSlots => _blueprintedSlots;

    /// True if this slot is in the droppable whitelist (or whitelist is empty = allow-all-non-starter).
    public bool IsDroppable(int slot)
    {
        if (System.Array.IndexOf(starterSlots, slot) >= 0) return false;
        if (droppableSlotsWhitelist == null || droppableSlotsWhitelist.Length == 0) return true;
        return System.Array.IndexOf(droppableSlotsWhitelist, slot) >= 0;
    }

    /// All droppable slots that haven't been blueprinted yet.
    public List<int> GetUndiscoveredDroppableSlots()
    {
        var result = new List<int>();
        if (droppableSlotsWhitelist != null && droppableSlotsWhitelist.Length > 0)
        {
            foreach (int s in droppableSlotsWhitelist)
                if (!_blueprintedSlots.Contains(s)) result.Add(s);
        }
        else
        {
            // No whitelist — anything non-starter is fair game.
            // Slot count must match WeaponRollController.allWeaponSlots.Length.
            for (int s = 0; s < 14; s++)
                if (IsDroppable(s) && !_blueprintedSlots.Contains(s)) result.Add(s);
        }
        return result;
    }

    /// Returns a random undiscovered droppable slot, or -1 if everything is already known.
    public int PickRandomUndiscoveredSlot()
    {
        var pool = GetUndiscoveredDroppableSlots();
        if (pool.Count == 0) return -1;
        return pool[Random.Range(0, pool.Count)];
    }

    /// Add a blueprint. Returns true if newly added.
    public bool UnlockBlueprint(int slot)
    {
        if (slot < 0) return false;
        if (!_blueprintedSlots.Add(slot)) return false;

        SaveToPrefs();
        if (debugLog) Debug.Log($"[BlueprintRegistry] Blueprint unlocked: slot {slot}");

        OnBlueprintUnlocked?.Invoke(slot);
        OnBlueprintsChanged?.Invoke();
        return true;
    }

    /// Wipe all blueprints. Useful for "reset progress" buttons / debug.
    public void ClearAll()
    {
        _blueprintedSlots.Clear();
        foreach (int s in starterSlots) _blueprintedSlots.Add(s);
        SaveToPrefs();
        OnBlueprintsChanged?.Invoke();
    }

    //  PERSISTENCE 

    private void LoadFromPrefs()
    {
        _blueprintedSlots.Clear();
        if (!PlayerPrefs.HasKey(PrefsKey)) return;

        string raw = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;

        foreach (string part in raw.Split(','))
        {
            if (int.TryParse(part, out int s)) _blueprintedSlots.Add(s);
        }

        if (debugLog && _blueprintedSlots.Count > 0)
            Debug.Log($"[BlueprintRegistry] Loaded {_blueprintedSlots.Count} blueprints from prefs: " +
                      string.Join(",", _blueprintedSlots));
    }

    private void SaveToPrefs()
    {
        string raw = string.Join(",", _blueprintedSlots);
        PlayerPrefs.SetString(PrefsKey, raw);
        PlayerPrefs.Save();
    }
}
