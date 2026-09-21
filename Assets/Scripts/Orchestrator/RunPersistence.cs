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
    // The difficulty the run STARTED on. Kept for diagnostics only: every autosave
    // writes EnemyStatModifierManager.ActiveMode (the difficulty actually in force at
    // the stage being saved), which is what resume restores. The old comment claimed
    // this field was written into the save; it never was.
    private int runStartDifficulty;
    private bool seedSet;
    private readonly List<AugmentSaveEntry> augmentLedger = new List<AugmentSaveEntry>();
    private readonly List<BlueprintUnlockSaveEntry> blueprintUnlockLedger = new List<BlueprintUnlockSaveEntry>();
    private string runConfigName;

    // Last tool asset equipped this run (Resources/Weapons/<name>). Fed by
    // RecordEquippedTool from every tool-swap site; written into the save and replayed
    // by RestoreEquipment so a MANUAL swap survives a resume.
    private string equippedToolName;

    //  STATIC, INSTANCE-FREE SAVE ACCESS 
    // The save is just a file in persistentDataPath. RunPersistence is a GameScene
    // object (no DontDestroyOnLoad), so in the MAIN MENU there is NO instance — yet
    // the continue screen must still see whether a save exists. These statics read the
    // file directly so save inspection works from ANY scene, instance or not.
    public const string DefaultFileName = "run_save.json";
    private static string s_fileName = DefaultFileName;

    public static string SaveFilePath => Path.Combine(Application.persistentDataPath, s_fileName);
    public static bool SaveExists => File.Exists(SaveFilePath);

    /// <summary>Read the save from disk without needing a live RunPersistence instance.</summary>
    public static bool TryReadSave(out RunSaveData data)
    {
        data = null;
        try
        {
            if (!File.Exists(SaveFilePath)) return false;
            data = JsonUtility.FromJson<RunSaveData>(File.ReadAllText(SaveFilePath));
            return data != null;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Persistence] Failed to read save (static): {e.Message}");
            return false;
        }
    }

    /// <summary>How many players the saved run needs, or 0 if there is no usable save. Instance-free.</summary>
    public static int RequiredPlayersInSaveStatic()
    {
        if (!TryReadSave(out var d) || d == null) return 0;
        int n = d.runPlayerCount;
        if (n <= 0) n = (d.players != null && d.players.Count > 0) ? d.players.Count : 1;
        return Mathf.Max(1, n);
    }

    /// <summary>Delete the save from disk without needing a live instance.</summary>
    public static void DeleteSaveFile()
    {
        try { if (File.Exists(SaveFilePath)) File.Delete(SaveFilePath); }
        catch (System.Exception e) { Debug.LogWarning($"[Persistence] Could not delete save (static): {e.Message}"); }
    }

    private string FilePath => SaveFilePath;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        // Mirror the configured filename into the static path so menu-scene reads
        // (where there is no instance) use the same file this instance writes.
        s_fileName = string.IsNullOrEmpty(fileName) ? DefaultFileName : fileName;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    //  LEDGER  (called by GameOrchestrator / the augment menu)

    /// Begin a fresh run: remember the seed, clear the augment log, delete any old save.
    // Back-compat overload — defaults to Normal for any caller not passing a difficulty.
    public void BeginRun(int seed, string configName)
        => BeginRun(seed, configName, (int)EnemyStatModifierManager.DifficultyMode.Normal);

    public void BeginRun(int seed, string configName, int difficulty)
    {
        runSeed = seed;
        runStartDifficulty = difficulty;
        seedSet = true;
        runConfigName = configName;
        augmentLedger.Clear();
        blueprintUnlockLedger.Clear();
        equippedToolName = null;
        DeleteSave();

        // Fresh run → clear the previous run's combat telemetry. Resume goes through
        // AdoptLoadedRun (not BeginRun), so a continued run keeps its restored totals.
        CombatStats.Instance?.ResetForNewRun();

        if (debugLog) Debug.Log($"[Persistence] New run started (seed={seed}, difficulty={difficulty}).");
    }

    /// Record an augment the moment it is applied, WITH its rolled rarity and the
    /// player who picked it (Phase 7c) so resume replays it onto the right player.
    public void RecordAugment(int augmentId, string rarity, int playerIndex)
    {
        augmentLedger.Add(new AugmentSaveEntry(augmentId, rarity, playerIndex));
    }

    // Back-compat: single-player path records for player 0.
    public void RecordAugment(int augmentId, string rarity) => RecordAugment(augmentId, rarity, 0);

    /// Find a player's Weapon without assuming it is parented under the player.
    /// Order: under the player (the intended layout) -> anywhere under the player's
    /// ROOT (covers a Weapon that is a sibling rather than a child) -> the only Weapon
    /// in the scene. Returns null only if the scene genuinely has none.
    private Weapon ResolveWeapon(int playerIndex)
    {
        var stats = ResolveStats(playerIndex);

        if (stats != null)
        {
            var w = stats.GetComponentInChildren<Weapon>(true);
            if (w != null) return w;

            // Sibling layout: Weapon lives beside the player under a shared root.
            w = stats.transform.root.GetComponentInChildren<Weapon>(true);
            if (w != null)
            {
                Debug.Log($"[Persistence] Weapon for P{playerIndex} found under root " +
                          $"'{stats.transform.root.name}' rather than under the player itself.");
                return w;
            }
        }

        // Last resort — unambiguous only in single player, so say so if it is not.
        var all = FindObjectsByType<Weapon>(FindObjectsSortMode.None);
        if (all.Length == 1) return all[0];
        if (all.Length > 1)
            Debug.LogWarning($"[Persistence] {all.Length} Weapons in the scene and none resolvable " +
                             $"from P{playerIndex} — cannot decide which to restore onto. " +
                             "Parent each Weapon under its own player.");
        return null;
    }

    /// Record the tool asset the player just equipped (the Resources/Weapons asset
    /// name, e.g. "ShieldTest"). Call this from every tool-swap site.
    public void RecordEquippedTool(string assetName)
    {
        if (!string.IsNullOrEmpty(assetName)) equippedToolName = assetName;
    }

    /// Record an in-run weapon/tool unlock that came from collecting a boss BLUEPRINT
    /// DROP (not from an augment). Re-applied on resume so the picked-up weapon stays in
    /// the player's hotbar — the augment replay can't rebuild it because there was no
    /// augment. Deduped so repeated wave-start saves don't bloat the ledger.
    public void RecordBlueprintUnlock(int slot, int playerIndex)
    {
        if (slot < 0) return;
        foreach (var e in blueprintUnlockLedger)
            if (e.slot == slot && e.playerIndex == playerIndex) return; // already recorded
        blueprintUnlockLedger.Add(new BlueprintUnlockSaveEntry(slot, playerIndex));
        if (debugLog) Debug.Log($"[Persistence] Recorded blueprint-drop unlock: slot {slot} (P{playerIndex}).");
    }


    // Non-destructive peek (TryLoad does NOT consume — only OnSaveConsumed deletes).
    public bool TryPeekSave(out RunSaveData data) => TryLoad(out data);

    /// How many players the saved run needs, or 0 if there is no usable save.
    public int RequiredPlayersInSave()
    {
        if (!HasSave || !TryLoad(out var d) || d == null) return 0;
        int n = d.runPlayerCount;
        if (n <= 0) n = (d.players != null && d.players.Count > 0) ? d.players.Count : 1;
        return Mathf.Max(1, n);
    }

    //  AUTOSAVE  (called by GameOrchestrator at wave start)

    public void AutoSaveWaveStart(int stageIndex, int waveIndex, bool atFinalBoss = false)
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
            // Current active difficulty (re-locked per stage), so resume restores the
            // difficulty that was in force at the resumed stage — not the run-start one.
            difficulty = (int)EnemyStatModifierManager.ActiveMode,
            stageIndex = stageIndex,
            waveIndex = waveIndex,
            atFinalBoss = atFinalBoss,
            augments = new List<AugmentSaveEntry>(augmentLedger),
            blueprintUnlocks = new List<BlueprintUnlockSaveEntry>(blueprintUnlockLedger),
        };

        // Players (per-player; single player = one entry at index 0).
        data.players = CapturePlayers();

        // GUARD: refuse to overwrite a good save with a PLAYERLESS snapshot.
        //
        // CapturePlayers() returns an empty list when the registry is empty AND no
        // PlayerStats exists in the scene — i.e. the run is executing with no player at
        // all. That happened for real: a persisted CoopManager stopped seating players
        // after a scene reload, the orchestrator ran its waves regardless, and every
        // wave start wrote a save whose players[] was empty. Resuming such a save
        // restores no health/armour/mana/stamina and silently hands back a default-stat
        // player. Keeping the previous (valid) save is strictly better.
        if (data.players.Count == 0)
        {
            Debug.LogError("[Persistence] AutoSave SKIPPED at stage " + stageIndex + " wave " + waveIndex +
                           " — no player found in the scene (PlayerRegistry empty and no PlayerStats). " +
                           "The existing save was left untouched rather than overwritten with a " +
                           "playerless snapshot. Something failed to spawn the player for this run.");
            return;
        }

        data.runPlayerCount = Mathf.Max(1, data.players.Count);

        // Combat telemetry (damage dealt/received + DPS clock). Written after the
        // player entries exist so per-player totals land on the right entry.
        CombatStats.Instance?.CaptureInto(data);

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
        // FIX: equippedToolAsset was declared in RunSaveData but never written, and
        // equippedWeaponAsset was written but never read back. Both are now round-tripped
        // by RestoreEquipment() below, so a MANUAL mid-run swap survives a resume
        // (augment replay alone cannot reproduce one).
        var wsm = WeaponSelectionManager.Instance;
        if (wsm != null && wsm.SelectedWeapon != null)
            data.equippedWeaponAsset = wsm.SelectedWeapon.name;

        // The tool slot has no global "selected tool" manager, so we keep a small
        // ledger instead: AugmentEffectHandler.ApplyToolSwap (and any other swap site)
        // reports the asset name here via RecordEquippedTool.
        if (!string.IsNullOrEmpty(equippedToolName))
            data.equippedToolAsset = equippedToolName;

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
        runStartDifficulty = data.difficulty;
        seedSet = true;
        runConfigName = data.runConfigName;

        // Restore the run's difficulty so later autosaves keep it and live scaling
        // matches. (GameOrchestrator also sets this before stages run; doing it here
        // keeps AdoptLoadedRun self-contained.)
        EnemyStatModifierManager.SetActiveMode(data.difficulty);
        augmentLedger.Clear();
        if (data.augments != null) augmentLedger.AddRange(data.augments);
        blueprintUnlockLedger.Clear();
        if (data.blueprintUnlocks != null) blueprintUnlockLedger.AddRange(data.blueprintUnlocks);
        equippedToolName = data.equippedToolAsset;
    }

    /// FIX: equippedWeaponAsset was written to disk and never read; equippedToolAsset
    /// was neither written nor read. A manual mid-run weapon/tool swap therefore did NOT
    /// survive a resume — the augment replay put back whatever the last augment granted.
    /// Call this AFTER the augment replay so it wins over it, which is the whole point.
    public void RestoreEquipment(RunSaveData data)
    {
        if (data == null) return;

        // FIX: this used to be a bare stats.GetComponentInChildren<Weapon>(). Your own
        // WeaponRollController logs
        //     "No sibling Weapon found under this player. This controller should live on
        //      the Player prefab root, with the Weapon as a child."
        // which proves the Weapon is NOT always parented under the player. When it isn't,
        // the lookup returned null and the whole equipment restore no-opped SILENTLY —
        // the resume looked fine and just quietly reverted your weapon.
        // Search widens progressively and says so when it has to fall back.
        var weapon = ResolveWeapon(0);
        if (weapon == null)
        {
            Debug.LogWarning("[Persistence] No Weapon found anywhere in the scene — the saved " +
                             "weapon/tool could not be restored. (Ideally the Weapon is a child " +
                             "of the player root; see WeaponRollController's warning.)");
            return;
        }

        if (!string.IsNullOrEmpty(data.equippedWeaponAsset))
        {
            var wd = Resources.Load<WeaponData>("Weapons/" + data.equippedWeaponAsset);
            if (wd != null)
            {
                weapon.HotSwapWeapon(wd);
                if (WeaponSelectionManager.Instance != null)
                    WeaponSelectionManager.Instance.SelectedWeapon = wd;
            }
            else Debug.LogWarning($"[Persistence] Saved weapon '{data.equippedWeaponAsset}' not found under Resources/Weapons.");
        }

        if (!string.IsNullOrEmpty(data.equippedToolAsset))
        {
            var td = Resources.Load<WeaponData>("Weapons/" + data.equippedToolAsset);
            if (td != null) weapon.HotSwapTool(td);
            else Debug.LogWarning($"[Persistence] Saved tool '{data.equippedToolAsset}' not found under Resources/Weapons.");
        }
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

        // NOTE: the CORE is deliberately NOT restored here — see RestoreCore().
        // Applying it at this point was the bug: TowerDefenseMap.GenerateMap() (run a
        // moment later, when the stage layout is applied) DESTROYS the CentralCore and
        // builds a fresh one at coreStartingEnergy, wiping whatever we set. RestoreCore
        // seeds the map instead, so the rebuilt core comes up already holding the saved
        // value.
        RestoreCore(data);

        if (data.hasEconomy && EnergyManager.Instance != null)
            EnergyManager.Instance.SetPlayerEnergy(data.playerEnergy);

        // Lore codex — restore the saved discovered set so the resumed run matches.
        if (data.loreFragmentIds != null && LoreCodex.Instance != null)
            LoreCodex.Instance.RestoreDiscoveredExact(data.loreFragmentIds);

        // Combat telemetry — resume keeps counting from the saved totals (a fresh
        // run resets these separately via StartRun → ResetForNewRun).
        CombatStats.Instance?.RestoreFrom(data);
    }

    /// Restore the Central Core's energy in a way that SURVIVES the map rebuild.
    ///
    /// The bug this fixes: resume ran
    ///     RestoreAbsolutes()  ->  JumpToWave()  ->  RunStage()  ->  ApplyBiome()
    ///     ->  TowerDefenseMap.ApplyLayout()  ->  GenerateMap()  ->  ClearExistingMap()
    /// and ClearExistingMap destroys the core while CreateCentralCore makes a new one at
    /// coreStartingEnergy. On a freshly loaded scene ApplyLayout can never short-circuit
    /// (sourceLayoutCaptured is false), so the rebuild ALWAYS happened and the restored
    /// core energy was ALWAYS discarded — every resume handed you a full-health core.
    ///
    /// TowerDefenseMap.SeedCoreEnergy() stashes the values and applies them inside
    /// CreateCentralCore, so the rebuild produces a correctly-damaged core. If the core
    /// already exists and no rebuild is pending, SeedCoreEnergy applies them immediately.
    public void RestoreCore(RunSaveData data)
    {
        if (data == null || !data.hasCore) return;

        var map = FindFirstObjectByType<TowerDefenseMap>();
        if (map != null)
        {
            map.SeedCoreEnergy(data.coreEnergy, data.coreMaxEnergy);
            return;
        }

        // No map in the scene (shouldn't happen) — fall back to the direct write.
        var core = FindFirstObjectByType<CentralCore>();
        if (core != null)
        {
            core.SetMaxEnergy(data.coreMaxEnergy);
            core.SetEnergy(data.coreEnergy);
        }
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
                slot, (Tower.TowerType)entry.towerType, entry.upgradeLevel,
                entry.currentEnergy, entry.maxEnergy);
            if (tower != null) restored++; else skipped++;
        }

        if (debugLog)
            Debug.Log($"[Persistence] Tower restore: {restored} rebuilt, {skipped} skipped (of {data.towers.Count} saved).");
    }
}

// One-shot handoff from ContinueRunMenu → CoopManager (seating) + GameOrchestrator
// (resume vs fresh). Avoids coupling the menu to SessionConfig's API.
public static class RunResumeIntent
{
    public static bool Pending { get; private set; }
    public static bool Resume { get; private set; }
    public static int PlayerCount { get; private set; }

    public static void Set(bool resume, int count)
    { Pending = true; Resume = resume; PlayerCount = Mathf.Max(1, count); }
    public static void Clear() { Pending = false; Resume = false; PlayerCount = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
}


