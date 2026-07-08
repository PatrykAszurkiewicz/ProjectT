using System.Collections.Generic;


// JSON-serializable snapshot of a run, written to disk at every wave start so a
// crash or exit can resume at the last wave boundary.
// Design: this is a SEED + REPLAY-LOG save, not a full world dump.
//   runSeed reproduces the exact run plan via GameOrchestrator.GenerateRunPlan()
//     (biomes / weather / wave sequence are deterministic given RunConfig + RNG).
//   augments[] is replayed in order on load (ApplyAugment(id, rarity)) to rebuild
//     every augment effect, weapon unlock and stat multiplier through the existing
//     code path.
//   Only the truly run-mutated absolutes (player/core/economy/tower energy and the
//     stage/wave position) are stored directly.
//
// Everything here is plain public fields / List&lt;serializable&gt; so Unity's
// JsonUtility can round-trip it. No Unity object references.

[System.Serializable]
public class RunSaveData
{
    // v2 (Phase 7c): players[] replaces the single flat player block so co-op
    // runs save/resume every player. Saves written by v1 are rejected on load
    // (resume saves are transient crash-recovery, so this costs nothing).
    public int saveVersion = 2;
    public long timestampUnix;     // for "continue run from 12:04" UI, optional
    public string runConfigName;     // informational / sanity check on load

    // Reproduces the run plan deterministically.
    public int runSeed;

    // Difficulty this run started on (0 = Normal, 1 = Nightmare). Restored on resume so
    // a continued run keeps the same Nightmare/Normal scaling regardless of the current
    // menu toggle. Missing in older saves → 0 (Normal), so no save-version bump needed.
    public int difficulty = 0;

    // Where in the run we were when this checkpoint was written.
    public int stageIndex;
    public int waveIndex;

    // True if this checkpoint was written at the START OF THE FINAL BOSS (after all
    // stages, their bosses, and post-stage rewards were already completed). When set,
    // resume rebuilds the last stage's arena/towers but SKIPS its boss + reward and
    // goes straight to the final boss, instead of rewinding to the last wave.
    // (stageIndex/waveIndex are still stored clamped-in-range for the continue UI.)
    public bool atFinalBoss = false;

    // Players (absolutes), one entry per registered player, keyed by playerIndex.
    // Single player = a one-element list at index 0.
    public List<PlayerSaveEntry> players = new List<PlayerSaveEntry>();

    // How many players were seated when this run started. The continue gate refuses
    // to load until this many controllers are connected. Defaults to players.Count.
    public int runPlayerCount = 1;

    // Core (absolutes).
    public bool hasCore;
    public float coreEnergy, coreMaxEnergy;

    // Economy (absolute).
    public bool hasEconomy;
    public int playerEnergy;

    // Replay log — rebuilt on load via AugmentRegistry.ApplyAugment(id, rarity).
    public List<AugmentSaveEntry> augments = new List<AugmentSaveEntry>();

    // In-run weapon/tool slots unlocked by collecting a BOSS BLUEPRINT DROP (not via
    // an augment). Augment-driven unlocks are rebuilt by replaying `augments`; a
    // blueprint-drop unlock has no augment behind it, so it's stored here and
    // re-applied on resume via WeaponUnlockRegistry.ForceUnlock(slot, playerIndex).
    // The PERMANENT cross-run blueprint itself lives separately in PlayerPrefs
    // (WeaponBlueprintRegistry); this list only restores the CURRENT run's hotbar.
    public List<BlueprintUnlockSaveEntry> blueprintUnlocks = new List<BlueprintUnlockSaveEntry>();

    // Lore fragments discovered as of this checkpoint. Snapshot of LoreCodex, so a
    // resumed run agrees with the codex about which chests have already been read.
    public List<int> loreFragmentIds = new List<int>();

    // Tower layout. Recreation needs the build API — see TowerSaveEntry + the
    // integration note. Captured regardless so the data is ready when that lands.
    public List<TowerSaveEntry> towers = new List<TowerSaveEntry>();

    // Equipped weapon/tool by Resources asset name (e.g. "MeleeTest"). Usually
    // emergent from augment replay, but stored explicitly to cover manual swaps.
    public string equippedWeaponAsset;
    public string equippedToolAsset;
}

// One player's run-mutated absolutes, keyed by playerIndex (0 = P1, 1 = P2…).
[System.Serializable]
public class PlayerSaveEntry
{
    public int playerIndex;
    public float playerHealth, playerMaxHealth, playerArmor;
    public float playerMana, playerMaxMana;
    public float playerStamina, playerMaxStamina;
    public int playerDashesLeft;
}

// One in-run hotbar unlock that came from collecting a boss blueprint drop, keyed
// by the slot index (matches WeaponRollController.allWeaponSlots) and the player
// who collected it. Re-applied verbatim on resume.
[System.Serializable]
public class BlueprintUnlockSaveEntry
{
    public int slot = -1;
    public int playerIndex = 0;

    public BlueprintUnlockSaveEntry() { }
    public BlueprintUnlockSaveEntry(int slot, int playerIndex)
    {
        this.slot = slot;
        this.playerIndex = playerIndex;
    }
}

[System.Serializable]
public class AugmentSaveEntry
{
    public int id;
    public string rarity = "Common";
    // Phase 7c: which player picked this augment, so resume replays it onto the
    // right player (Player/Weapon effects + their weapon unlocks). 0 in single player.
    public int playerIndex = 0;

    public AugmentSaveEntry() { }
    public AugmentSaveEntry(int id, string rarity) : this(id, rarity, 0) { }
    public AugmentSaveEntry(int id, string rarity, int playerIndex)
    {
        this.id = id;
        this.rarity = string.IsNullOrEmpty(rarity) ? "Common" : rarity;
        this.playerIndex = playerIndex;
    }
}

[System.Serializable]
public class TowerSaveEntry
{
    // Stable slot identity across sessions: (ring, slot). The layout is
    // deterministic from the run seed, so these coordinates reproduce reliably.
    public int ringIndex = -1;
    public int slotIndex = -1;
    public int towerType;       // (int)Tower.TowerType
    public int upgradeLevel = 1;
    public float currentEnergy;
    public float maxEnergy;
}
