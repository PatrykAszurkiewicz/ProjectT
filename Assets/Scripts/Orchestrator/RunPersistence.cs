using System.Collections.Generic;
using System.IO;
using UnityEngine;


// Cross-session persistence for a run. Sits alongside the in-memory
// WaveCheckpointService (which powers the rewind clock); this one writes a small
// JSON save to disk at every wave start so a crash or exit can resume the run.
// It keeps a running LEDGER for the things that must be replayed rather than dumped:
//   - the run seed (set once at StartRun)
//   - the ordered (augmentId, rarity) list (recorded as the player picks them)
// At each wave start it combines that ledger with the live player/core/economy/tower
// state and writes RunSaveData to disk.
// It is a RESUME save, not a save-anywhere system. By default the file
// is deleted the moment it is consumed on load 

public class RunPersistence : MonoBehaviour
{
    public static RunPersistence Instance { get; private set; }

    [Tooltip("Delete the save as soon as it is loaded (and on death/victory). " +
             "true = crash/exit recovery only (no save-scumming). false = persistent reload point.")]
    public bool deleteOnConsume = true;

    [Tooltip("Save file name under Application.persistentDataPath.")]
    public string fileName = "run_save.json";

    public bool debugLog = false;

    //  Running ledger (the replay inputs) 
    private int runSeed;
    private bool seedSet;
    private readonly List<AugmentSaveEntry> augmentLedger = new List<AugmentSaveEntry>();
    private string runConfigName;

    private string FilePath => Path.Combine(Application.persistentDataPath, fileName);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    //  LEDGER  (called by GameOrchestrator / the augment menu)

    /// Begin a fresh run: remember the seed, clear the augment log, delete any old save.
    public void BeginRun(int seed, string configName)
    {
        runSeed = seed;
        seedSet = true;
        runConfigName = configName;
        augmentLedger.Clear();
        DeleteSave();
        if (debugLog) Debug.Log($"[Persistence] New run started (seed={seed}).");
    }

    /// Record an augment the moment it is applied, WITH its rolled rarity and the
    /// player who picked it (Phase 7c) so resume replays it onto the right player.
    public void RecordAugment(int augmentId, string rarity, int playerIndex)
    {
        augmentLedger.Add(new AugmentSaveEntry(augmentId, rarity, playerIndex));
    }

    /// Back-compat: single-player path records for player 0.
    public void RecordAugment(int augmentId, string rarity) => RecordAugment(augmentId, rarity, 0);

    //  AUTOSAVE  (called by GameOrchestrator at wave start)

    public void AutoSaveWaveStart(int stageIndex, int waveIndex)
    {
        if (!seedSet)
        {
            if (debugLog) Debug.LogWarning("[Persistence] AutoSave skipped — no run seed set. Call BeginRun() in StartRun().");
            return;
        }

        var data = new RunSaveData
        {
            timestampUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            runConfigName = runConfigName,
            runSeed = runSeed,
            stageIndex = stageIndex,
            waveIndex = waveIndex,
            augments = new List<AugmentSaveEntry>(augmentLedger),
        };

        // Players (per-player; single player = one entry at index 0).
        data.players = CapturePlayers();

        var core = FindFirstObjectByType<CentralCore>();
        if (core != null)
        {
            data.hasCore = true;
            data.coreEnergy = core.currentEnergy;
            data.coreMaxEnergy = core.maxEnergy;
        }

        if (EnergyManager.Instance != null)
        {
            data.hasEconomy = true;
            data.playerEnergy = EnergyManager.Instance.GetPlayerEnergy();
        }

        // Lore: snapshot which fragments have been discovered so a resume agrees
        // with the codex about which chests are already read.
        if (LoreCodex.Instance != null)
            data.loreFragmentIds = LoreCodex.Instance.GetDiscoveredSnapshot();

        // Equipped weapon/tool (best effort; usually also emergent from augment replay).
        if (WeaponSelectionManager.Instance != null && WeaponSelectionManager.Instance.SelectedWeapon != null)
            data.equippedWeaponAsset = WeaponSelectionManager.Instance.SelectedWeapon.name;

        // Towers: capture what we can. Slot identity + recreation needs TowerDefenseMap
        // / TowerSlot / the build script — see CaptureTowers().
        CaptureTowers(data);

        WriteSave(data);
    }

    //  DISK I/O

    public bool HasSave => File.Exists(FilePath);

    public bool TryLoad(out RunSaveData data)
    {
        data = null;
        try
        {
            if (!File.Exists(FilePath)) return false;
            string json = File.ReadAllText(FilePath);
            data = JsonUtility.FromJson<RunSaveData>(json);
            return data != null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Persistence] Failed to load save: {e.Message}");
            return false;
        }
    }

    private void WriteSave(RunSaveData data)
    {
        try
        {
            // Write to a temp file then move, so a crash mid-write can't corrupt the save.
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
            if (debugLog)
                Debug.Log($"[Persistence] Saved @ stage {data.stageIndex} wave {data.waveIndex} " +
                          $"(players={data.players.Count}, augments={data.augments.Count}, towers={data.towers.Count}).");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Persistence] Failed to write save: {e.Message}");
        }
    }

    public void DeleteSave()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (System.Exception e) { Debug.LogWarning($"[Persistence] Could not delete save: {e.Message}"); }
    }

    /// Call after a save has been successfully consumed on resume, and on death/victory.
    public void OnSaveConsumed()
    {
        if (deleteOnConsume) DeleteSave();
    }

    //  RESTORE HELPERS  (applied by GameOrchestrator.ResumeFromSave)

    /// Re-seed the ledger from a loaded save so subsequent autosaves stay correct.
    public void AdoptLoadedRun(RunSaveData data)
    {
        runSeed = data.runSeed;
        seedSet = true;
        runConfigName = data.runConfigName;
        augmentLedger.Clear();
        augmentLedger.AddRange(data.augments);
    }

    /// Apply the absolute player/core/economy values. Call this LAST (after augment
    /// replay) so one-time augment-on-pick grants don't overwrite the saved totals.
    public void RestoreAbsolutes(RunSaveData data)
    {
        if (data.players != null)
        {
            foreach (var ps in data.players)
            {
                var player = ResolveStats(ps.playerIndex);
                if (player == null) continue;

                player.maxHealth = ps.playerMaxHealth;
                player.currentArmor = ps.playerArmor;
                player.SetHealthAndNotify(ps.playerHealth);
                player.maxMana = ps.playerMaxMana;
                player.currentMana = Mathf.Min(ps.playerMana, player.maxMana);
                player.maxStamina = ps.playerMaxStamina;
                player.currentStamina = Mathf.Min(ps.playerStamina, player.maxStamina);
                player.dashesLeft = ps.playerDashesLeft;
            }
        }

        if (data.hasCore)
        {
            var core = FindFirstObjectByType<CentralCore>();
            if (core != null)
            {
                core.SetMaxEnergy(data.coreMaxEnergy);
                core.SetEnergy(data.coreEnergy);
            }
        }

        if (data.hasEconomy && EnergyManager.Instance != null)
            EnergyManager.Instance.SetPlayerEnergy(data.playerEnergy);

        // Lore codex — restore the saved discovered set so the resumed run matches.
        if (data.loreFragmentIds != null && LoreCodex.Instance != null)
            LoreCodex.Instance.RestoreDiscoveredExact(data.loreFragmentIds);
    }

    //  PLAYERS (Phase 7c) — per-player capture / resolve, mirroring the in-memory
    //  checkpoint service. Single player yields one entry at index 0.

    private List<PlayerSaveEntry> CapturePlayers()
    {
        var list = new List<PlayerSaveEntry>();
        var reg = PlayerRegistry.Instance;
        if (reg != null && PlayerRegistry.Count > 0)
        {
            foreach (var pr in reg.All)
                if (pr != null && pr.Stats != null)
                    list.Add(EntryOf(pr.Stats, pr.PlayerIndex));
        }
        if (list.Count == 0)
        {
            var p = FindFirstObjectByType<PlayerStats>();
            if (p != null) list.Add(EntryOf(p, 0));
        }
        return list;
    }

    private PlayerSaveEntry EntryOf(PlayerStats p, int index) => new PlayerSaveEntry
    {
        playerIndex = index,
        playerHealth = p.currentHealth,
        playerMaxHealth = p.maxHealth,
        playerArmor = p.currentArmor,
        playerMana = p.currentMana,
        playerMaxMana = p.maxMana,
        playerStamina = p.currentStamina,
        playerMaxStamina = p.maxStamina,
        playerDashesLeft = p.dashesLeft,
    };

    private PlayerStats ResolveStats(int index)
    {
        var reg = PlayerRegistry.Instance;
        if (reg != null)
        {
            var pr = reg.Get(index);
            if (pr != null && pr.Stats != null) return pr.Stats;
        }
        return FindFirstObjectByType<PlayerStats>();
    }

    //  TOWERS  — the one piece that needs files I don't have yet.

    // Capture per-tower data. Slot identity (slotGlobalIndex) needs a public
    // index/lookup on TowerDefenseMap; left at -1 until that's wired.
    private void CaptureTowers(RunSaveData data)
    {
        foreach (var t in FindObjectsByType<Tower>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            var slot = t.GetComponentInParent<TowerSlot>();
            data.towers.Add(new TowerSaveEntry
            {
                ringIndex = slot != null ? slot.ringIndex : -1,
                slotIndex = slot != null ? slot.slotIndex : -1,
                towerType = (int)t.towerType,
                upgradeLevel = t.upgradeLevel,
                currentEnergy = t.currentEnergy,
                maxEnergy = t.maxEnergy,
            });
        }
    }

    /// Rebuild saved towers into their slots. Called by GameOrchestrator on resume,
    /// AFTER the map/layout has been applied (so slots exist) and augments replayed.
    public void RestoreTowers(RunSaveData data)
    {
        if (data.towers == null || data.towers.Count == 0) return;

        var placement = TowerPlacementManager.Instance;
        if (placement == null)
        {
            Debug.LogWarning("[Persistence] No TowerPlacementManager — cannot restore towers.");
            return;
        }

        int restored = 0, skipped = 0;
        foreach (var entry in data.towers)
        {
            if (entry.ringIndex < 0 || entry.slotIndex < 0) { skipped++; continue; }

            var slot = placement.FindSlot(entry.ringIndex, entry.slotIndex);
            if (slot == null) { skipped++; continue; }
            if (slot.IsOccupied) { skipped++; continue; } // already there (shouldn't happen on fresh resume)

            var tower = placement.RestoreTowerInto(
                slot, (Tower.TowerType)entry.towerType, entry.upgradeLevel, entry.currentEnergy);
            if (tower != null) restored++; else skipped++;
        }

        if (debugLog)
            Debug.Log($"[Persistence] Tower restore: {restored} rebuilt, {skipped} skipped (of {data.towers.Count} saved).");
    }
}

