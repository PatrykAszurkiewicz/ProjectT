using System.Collections.Generic;
using UnityEngine;


// In-memory, single-session checkpoint system that powers the wave-rewind clock.
// A snapshot is captured at the START of every wave. Not PlayerPrefs / a disk save. 

public class WaveCheckpointService : MonoBehaviour
{
    public static WaveCheckpointService Instance { get; private set; }

    [Tooltip("Log capture/restore details to the console.")]
    public bool debugLog = false;

    /// The most recently captured wave-start snapshot (null until the first wave starts)
    public RunSnapshot CurrentSnapshot { get; private set; }
    public bool HasSnapshot => CurrentSnapshot != null;


    /// Snapshot of the START OF THE CURRENT STAGE (captured at wave 0 and held for
    /// the whole stage, even as later waves overwrite CurrentSnapshot)

    public RunSnapshot StageStartSnapshot { get; private set; }
    public bool HasStageStartSnapshot => StageStartSnapshot != null;


    // Snapshot of the START OF THE FINAL BOSS FIGHT. 
    public RunSnapshot FinalBossSnapshot { get; private set; }
    public bool HasFinalBossSnapshot => FinalBossSnapshot != null;


    // Capture the start-of-final-boss state. Called by GameOrchestrator right after
    // the final boss spawns. Uses stageIndex/waveIndex = -1 as a sentinel ("not a
    // stage/wave") since the final boss has neither.

    public void CaptureFinalBossSnapshot()
    {
        FinalBossSnapshot = BuildSnapshot(-1, -1);
        if (debugLog)
            Debug.Log($"[Checkpoint] Final-boss snapshot held (players={FinalBossSnapshot.players.Count}, " +
                      $"core={FinalBossSnapshot.coreEnergy:F0}, energy={FinalBossSnapshot.playerEnergy}).");
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    //  CAPTURE  — called by GameOrchestrator at the start of every wave.
    public void CaptureSnapshot(int stageIndex, int waveIndex)
    {
        var snap = BuildSnapshot(stageIndex, waveIndex);
        CurrentSnapshot = snap;

        // The first wave of a stage IS the stage start. Hold a copy so a boss
        // rewind (later in the stage) can return here even after CurrentSnapshot
        // has been overwritten by subsequent waves.
        if (waveIndex == 0)
        {
            StageStartSnapshot = snap;
            if (debugLog)
                Debug.Log($"[Checkpoint] Stage-start snapshot held @ stage {stageIndex}.");
        }

        if (debugLog)
            Debug.Log($"[Checkpoint] Captured wave-start snapshot @ stage {stageIndex} wave {waveIndex} " +
                      $"(towers={snap.towers.Count}, players={snap.players.Count}, core={snap.coreEnergy:F0}, " +
                      $"energy={snap.playerEnergy}).");
    }

    private RunSnapshot BuildSnapshot(int stageIndex, int waveIndex)
    {
        var snap = new RunSnapshot { stageIndex = stageIndex, waveIndex = waveIndex };

        snap.players = CapturePlayers();

        var core = FindFirstObjectByType<CentralCore>();
        if (core != null)
        {
            snap.hasCore = true;
            snap.coreEnergy = core.currentEnergy;
            snap.coreMaxEnergy = core.maxEnergy;
        }

        if (EnergyManager.Instance != null)
        {
            snap.hasEconomy = true;
            snap.playerEnergy = EnergyManager.Instance.GetPlayerEnergy();
        }

        if (LoreCodex.Instance != null)
            snap.loreFragmentIds = LoreCodex.Instance.GetDiscoveredSnapshot();

        snap.towers = new List<TowerSnapshot>();
        foreach (var t in FindObjectsByType<Tower>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            snap.towers.Add(new TowerSnapshot
            {
                tower = t,
                slot = t.GetComponentInParent<TowerSlot>(),
                towerType = t.towerType,
                upgradeLevel = t.upgradeLevel,
                currentEnergy = t.currentEnergy,
                maxEnergy = t.maxEnergy,
            });
        }

        return snap;
    }

    // Drop all stored snapshots (e.g. on run restart / game over)
    public void ClearSnapshot() { CurrentSnapshot = null; StageStartSnapshot = null; FinalBossSnapshot = null; }

    //  RESTORE  — called by GameOrchestrator.RewindToCurrentWaveStart().
    public void RestoreSnapshot(RunSnapshot snap)
    {
        if (snap == null) { Debug.LogWarning("[Checkpoint] RestoreSnapshot called with null snapshot."); return; }

        ClearLiveEnemies();
        ClearTransientObjects();   // hazard clouds + player deployables
        RestorePlayers(snap.players);
        RestoreCore(snap);
        RestoreEconomy(snap);
        RestoreLore(snap);
        RestoreTowers(snap);

        // Wipe any chests created/opened during the rewound wave so the map matches
        // the rolled-back codex (the fragments they would have given are available again).
        LoreChestSpawner.Instance?.ClearAllChests();

        // OPTIONAL cleanup

        if (debugLog)
            Debug.Log($"[Checkpoint] Restored wave-start snapshot (stage {snap.stageIndex} wave {snap.waveIndex}).");
    }


    // Destroy every living, non-ambient enemy WITHOUT routing through
    // Die()/PerformDeath() — so no energy drops spawn, no kill rewards fire, and
    // no wave counters get decremented. 

    private void ClearLiveEnemies()
    {
        int cleared = 0;
        foreach (var es in FindObjectsByType<EnemyStats>(FindObjectsSortMode.None))
        {
            if (es == null) continue;
            if (es.GetComponent<GremlinController>() != null) continue; // ambient — leave alone

            // PerformDeath normally tears down the health bar; we bypass it, so do it here.
            var hb = es.GetHealthBar();
            if (hb != null) Destroy(hb.gameObject);

            Destroy(es.gameObject);
            cleared++;
        }

        if (debugLog) Debug.Log($"[Checkpoint] Silently cleared {cleared} enemies.");
    }


    // Destroy objects spawned during the rewound wave that aren't part of the snapshot.
    //   Enemy hazard clouds — must vanish, or they keep affecting the player in a
    //      timeline that no longer exists.
    //   Player deployables — the rewind refunds the energy spent placing them, so
    //      leaving them up would duplicate them for free.
    private void ClearTransientObjects()
    {
        // 1) Enemy-dropped hazard clouds
        DestroyAllOfType<PoisonCloud>();   // Parfumer
        DestroyAllOfType<BufferFog>();     // Buffer

        // 2) Player-placed deployables
        DestroyAllOfType<TurretUnit>();
        DestroyAllOfType<TrapMine>();
        DestroyAllOfType<BombMine>();
        DestroyAllOfType<PlacedTorch>();
        DestroyAllOfType<SmokeScreenProjectile>(); // canister still arcing
        DestroyAllOfType<SmokeScreenCloud>();       // lingering smoke wall

        // 3) Uncollected energy drops. RestoreEconomy resets player energy to the
        //    wave-start absolute, so any orb still lying on the ground (or mid-arc)
        //    belongs to a timeline that no longer exists — collecting it after the
        //    rewind would credit energy the snapshot already rolled back. Covers
        //    enemy/boss drops as well as lore-chest orbs.
        DestroyAllOfType<EnergyDrop>();

        // 4) Lingering poison ON the player. The Parfumer's poison keeps ticking
        //    ~20s after exposure via a PoisonStatusEffect. The rewind restores
        //    wave-start HP, so a poison from the rewound timeline must not survive.
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
        {
            var poison = playerGO.GetComponent<PoisonStatusEffect>();
            if (poison != null) Destroy(poison);
        }

        if (debugLog) Debug.Log("[Checkpoint] Cleared transient wave objects.");
    }

    private void DestroyAllOfType<T>() where T : Component
    {
        foreach (var obj in FindObjectsByType<T>(FindObjectsSortMode.None))
            if (obj != null && obj.gameObject != null)
                Destroy(obj.gameObject);
    }


    // ---- Per-player capture / restore (Phase 7c) ----------------------------
    // Single player: PlayerRef self-registers at index 0, so this yields a
    // one-element list and reproduces the old behaviour exactly. The
    // FindFirstObjectByType fallback only triggers if nothing is registered.

    private List<PlayerSnapshot> CapturePlayers()
    {
        var list = new List<PlayerSnapshot>();
        var reg = PlayerRegistry.Instance;
        if (reg != null && PlayerRegistry.Count > 0)
        {
            foreach (var pr in reg.All)
                if (pr != null && pr.Stats != null)
                    list.Add(SnapshotOf(pr.Stats, pr.PlayerIndex));
        }
        if (list.Count == 0)
        {
            var p = FindFirstObjectByType<PlayerStats>();
            if (p != null) list.Add(SnapshotOf(p, 0));
        }
        return list;
    }

    private PlayerSnapshot SnapshotOf(PlayerStats p, int index) => new PlayerSnapshot
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

    private void RestorePlayers(List<PlayerSnapshot> players)
    {
        if (players == null) return;
        foreach (var ps in players)
        {
            var stats = ResolveStats(ps.playerIndex);
            if (stats == null) continue;

            // If this player is currently downed (e.g. mid-wave clock rewind),
            // clear the downed state first — no heal/immunity, since we set the
            // exact wave-start HP next.
            var downed = stats.GetComponent<PlayerDownedState>();
            if (downed != null && downed.IsDowned) downed.RestoreControl();

            stats.maxHealth = ps.playerMaxHealth;
            stats.currentArmor = ps.playerArmor;
            stats.SetHealthAndNotify(ps.playerHealth); // clamps + fires OnHealthChanged

            stats.maxMana = ps.playerMaxMana;
            stats.currentMana = Mathf.Min(ps.playerMana, stats.maxMana);

            stats.maxStamina = ps.playerMaxStamina;
            stats.currentStamina = Mathf.Min(ps.playerStamina, stats.maxStamina);

            stats.dashesLeft = ps.playerDashesLeft;
        }
    }

    private PlayerStats ResolveStats(int index)
    {
        var reg = PlayerRegistry.Instance;
        if (reg != null)
        {
            var pr = reg.Get(index);
            if (pr != null && pr.Stats != null) return pr.Stats;
        }
        return FindFirstObjectByType<PlayerStats>(); // legacy single-player fallback
    }

    private void RestoreCore(RunSnapshot snap)
    {
        if (!snap.hasCore) return;
        var core = FindFirstObjectByType<CentralCore>();
        if (core == null) return;

        core.SetMaxEnergy(snap.coreMaxEnergy); // no-op within a wave, but safe
        core.SetEnergy(snap.coreEnergy);
    }

    private void RestoreEconomy(RunSnapshot snap)
    {
        if (!snap.hasEconomy || EnergyManager.Instance == null) return;
        EnergyManager.Instance.SetPlayerEnergy(snap.playerEnergy);
    }

    private void RestoreLore(RunSnapshot snap)
    {
        if (snap.loreFragmentIds == null || LoreCodex.Instance == null) return;
        LoreCodex.Instance.RestoreDiscoveredExact(snap.loreFragmentIds);
    }

    private void RestoreTowers(RunSnapshot snap)
    {
        if (snap.towers == null) return;

        // Index snapshot towers by their slot so we can match against the scene.
        var bySlot = new Dictionary<TowerSlot, TowerSnapshot>();
        foreach (var ts in snap.towers)
            if (ts.slot != null) bySlot[ts.slot] = ts;

        var matchedSlots = new HashSet<TowerSlot>();

        foreach (var t in FindObjectsByType<Tower>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            var slot = t.GetComponentInParent<TowerSlot>();

            if (slot != null && bySlot.TryGetValue(slot, out var ts))
            {
                // Tower that existed at wave start (maybe now disabled by damage, or
                // upgraded mid-wave). Roll its upgrade level back to the wave-start
                // value first — SetUpgradeLevel re-derives the max-health bonus (and
                // damage is derived live), so a mid-wave upgrade is undone here.
                if (t.upgradeLevel != ts.upgradeLevel) t.SetUpgradeLevel(ts.upgradeLevel);

                // Restoring its energy past zero auto-re-enables a disabled tower.
                RestoreTowerEnergy(t, ts.currentEnergy);
                matchedSlots.Add(slot);
            }
            else
            {
                // Built mid-wave (not in the snapshot) — undo the build.
                if (slot != null) slot.RemoveTower();
                else Destroy(t.gameObject);
            }
        }

        // Any snapshot tower whose slot now has no live Tower was *fully removed*
        // mid-wave. Rebuild it from the snapshot (cost-free) so the rewind restores
        // the exact tower layout that existed at wave start.
        foreach (var kv in bySlot)
        {
            if (matchedSlots.Contains(kv.Key)) continue;
            var ts = kv.Value;
            var placement = TowerPlacementManager.Instance;
            if (placement == null)
            {
                Debug.LogWarning("[Checkpoint] No TowerPlacementManager — cannot recreate removed tower.");
                continue;
            }
            if (kv.Key.IsOccupied) continue; // something is already there
            var rebuilt = placement.RestoreTowerInto(kv.Key, ts.towerType, ts.upgradeLevel, ts.currentEnergy);
            if (debugLog && rebuilt != null)
                Debug.Log($"[Checkpoint] Rebuilt removed tower in slot '{kv.Key.name}'.");
        }
    }

    private void RestoreTowerEnergy(Tower t, float target)
    {
        float delta = target - t.currentEnergy;
        if (delta > 0.01f)
            t.SupplyEnergy(delta);          // crossing 0 upward re-enables a disabled tower
        else if (delta < -0.01f)
            t.currentEnergy = target;       // simple downward correction; no re-enable needed
    }
}

/// <summary>Plain in-memory record of everything a wave-start rewind must restore.</summary>
public class RunSnapshot
{
    public int stageIndex;
    public int waveIndex;

    // Phase 7c: one entry per player (single player = one entry at index 0).
    public List<PlayerSnapshot> players = new List<PlayerSnapshot>();

    public bool hasCore;
    public float coreEnergy, coreMaxEnergy;

    public bool hasEconomy;
    public int playerEnergy;

    // Lore fragments discovered as of this wave start. Lets a rewind roll the codex
    // back exactly: a fragment opened mid-wave becomes available again afterwards.
    public List<int> loreFragmentIds;

    public List<TowerSnapshot> towers;
}

// One player's wave-start state, keyed by PlayerIndex.
public class PlayerSnapshot
{
    public int playerIndex;
    public float playerHealth, playerMaxHealth, playerArmor;
    public float playerMana, playerMaxMana;
    public float playerStamina, playerMaxStamina;
    public int playerDashesLeft;
}

public class TowerSnapshot
{
    public Tower tower;        // live reference at capture time (may disable later)
    public TowerSlot slot;         // stable identity used to match on restore
    public Tower.TowerType towerType;
    public int upgradeLevel;
    public float currentEnergy;
    public float maxEnergy;
}
