using UnityEngine;

/// Called from a boss's death routine. Rolls a probability, then —
/// if it passes — picks an undiscovered droppable slot and spawns a
/// WeaponBlueprintDrop at the boss's death position.
///
/// Add this MonoBehaviour to a scene singleton (alongside EnergyDropManager),
/// or invoke the static API directly from Boss1.ExecuteBossDeath().
public class BossBlueprintDropper : MonoBehaviour
{
    public static BossBlueprintDropper Instance { get; private set; }

    [Header("Drop Probability")]
    [Range(0f, 1f)]
    [Tooltip("Base chance a stage boss death produces a blueprint drop. " +
             "Set to 0 to disable. The dropper also requires at least one " +
             "undiscovered droppable slot.")]
    public float baseDropChance = 0.25f;

    [Tooltip("Optional per-stage multiplier on drop chance. Index 0 = stage 1, etc. " +
             "Leave empty to use baseDropChance for every stage.")]
    public float[] perStageMultiplier = new float[0];

    [Header("Pity (anti-streak)")]
    [Tooltip("If true, after a successful drop the NEXT boss has its chance reduced to pityChanceAfterDrop. " +
             "If false, every boss rolls independently.")]
    public bool usePity = false;
    [Range(0f, 1f)] public float pityChanceAfterDrop = 0f;
    private bool _lastBossDropped = false;

    [Header("No-Undiscovered-Slots Fallback")]
    [Tooltip("What to do if every droppable slot is already blueprinted.")]
    public NothingLeftBehavior whenAllDiscovered = NothingLeftBehavior.Nothing;
    [Tooltip("Energy burst amount if fallback = DropEnergyBurst. Scales with stage via EnergyDropManager.")]
    public int fallbackEnergyValue = 60;
    [Tooltip("How many small energy drops to scatter in the fallback burst.")]
    [Min(1)] public int fallbackEnergyCount = 4;

    public enum NothingLeftBehavior
    {
        Nothing,
        DropEnergyBurst,
    }

    [Header("Spawn")]
    [Tooltip("Horizontal scatter radius from boss death position so the drop doesn't sit on the corpse.")]
    public float spawnOffsetRadius = 2.5f;

    [Header("Debug")]
    public bool debugLog = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    //  STATIC API 

    /// Convenience entry point for bosses. Creates a singleton if absent.
    public static void RollAndSpawn(Vector3 bossPosition, int stageIndex)
    {
        if (Instance == null)
        {
            var go = new GameObject("BossBlueprintDropper");
            go.AddComponent<BossBlueprintDropper>();
        }
        Instance.TryDrop(bossPosition, stageIndex);
    }

    //  CORE 

    public void TryDrop(Vector3 bossPosition, int stageIndex)
    {
        var registry = WeaponBlueprintRegistry.Instance;
        if (registry == null)
        {
            if (debugLog) Debug.LogWarning("[BlueprintDropper] No WeaponBlueprintRegistry in scene — skipping drop.");
            return;
        }

        // Roll chance
        float chance = baseDropChance;
        if (perStageMultiplier != null && stageIndex >= 0 && stageIndex < perStageMultiplier.Length)
            chance *= perStageMultiplier[stageIndex];

        if (usePity && _lastBossDropped)
            chance = pityChanceAfterDrop;

        float roll = Random.value;
        bool passed = roll <= chance;

        if (debugLog)
            Debug.Log($"[BlueprintDropper] Stage {stageIndex + 1} boss died. " +
                      $"Roll {roll:F2} vs chance {chance:F2} → {(passed ? "DROP" : "no drop")}");

        if (!passed)
        {
            _lastBossDropped = false;
            return;
        }

        // Pick an undiscovered slot
        int slot = registry.PickRandomUndiscoveredSlot();
        if (slot < 0)
        {
            if (debugLog) Debug.Log("[BlueprintDropper] All droppable slots already blueprinted — fallback path.");
            HandleNothingLeft(bossPosition, stageIndex);
            _lastBossDropped = false;
            return;
        }

        // Find WeaponData for the slot via the player's WeaponRollController
        WeaponData data = LookupWeaponDataForSlot(slot);
        if (data == null)
        {
            // Slot exists in registry but the player's WeaponRollController has no asset wired up.
            // Spawn anyway with a null icon — the registry update is what matters.
            if (debugLog) Debug.LogWarning($"[BlueprintDropper] No WeaponData for slot {slot}; spawning drop with no icon.");
        }

        Vector3 spawnPos = bossPosition + (Vector3)(Random.insideUnitCircle * spawnOffsetRadius);
        WeaponBlueprintDrop.Spawn(spawnPos, slot, data);

        _lastBossDropped = true;
        if (debugLog) Debug.Log($"[BlueprintDropper] Spawned blueprint for slot {slot} ({(data != null ? data.weaponName : "no-data")}).");
    }

    //  HELPERS 

    private WeaponData LookupWeaponDataForSlot(int slot)
    {
        var ctrl = FindFirstObjectByType<WeaponRollController>();
        if (ctrl == null || ctrl.allWeaponSlots == null) return null;
        if (slot < 0 || slot >= ctrl.allWeaponSlots.Length) return null;
        return ctrl.allWeaponSlots[slot];
    }

    private void HandleNothingLeft(Vector3 pos, int stageIndex)
    {
        switch (whenAllDiscovered)
        {
            case NothingLeftBehavior.Nothing:
                return;

            case NothingLeftBehavior.DropEnergyBurst:
                int per = Mathf.Max(1, Mathf.RoundToInt((float)fallbackEnergyValue / fallbackEnergyCount));
                for (int i = 0; i < fallbackEnergyCount; i++)
                {
                    Vector3 offset = (Vector3)(Random.insideUnitCircle * spawnOffsetRadius);
                    EnergyDrop.CreateEnergyDrop(pos + offset, per);
                }
                return;
        }
    }
}
