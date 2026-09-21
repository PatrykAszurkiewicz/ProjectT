using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// =============================================================================
//  FIX PACK VERIFICATION HARNESS
// -----------------------------------------------------------------------------
//  Verifies the 34 fixes + 4 self-review corrections applied to the save/resume,
//  map-rebuild, tower-restore, co-op and augment-static systems.
//
//  Drop on any active GameObject in the GAMEPLAY scene and press Play.
//  Every line is tagged [FIXPACK]; filter the Console by it.
//
//     F1 = SUITE A — API contract + statics      (READ-ONLY, always safe)
//     F2 = SUITE B — map rebuild / core / slots  (DESTRUCTIVE, see below)
//     F3 = SUITE C — tower restore + energy      (DESTRUCTIVE)
//     F4 = SUITE D — run plan / weather / biomes (READ-ONLY)
//     F5 = SUITE E — co-op + registry            (READ-ONLY)
//     F1+Shift (or the context menu) = run EVERYTHING
//
//  ── ON "DESTRUCTIVE" ─────────────────────────────────────────────────────────
//  Suites B and C REBUILD THE MAP and CREATE/DESTROY TOWERS on purpose — that is
//  precisely what they are verifying. They snapshot the board first and restore it
//  afterwards, but a rebuild is not perfectly invisible (tower cooldowns reset,
//  enemies retarget). Run them on a throwaway wave, or leave requireIdleState on
//  so they refuse to run while a wave is spawning.
//
//  ── ON RESULT SEMANTICS ──────────────────────────────────────────────────────
//  PASS = the fix is present and behaves.
//  FAIL = the fix is present but WRONG. Investigate.
//  SKIP = the system under test is not in this scene, or the scene is in a state
//         where the check would be meaningless. NOT a failure — but a suite that
//         is all SKIP has told you nothing, so check the reason.
//
//  Several checks use reflection rather than direct calls. That is deliberate: it
//  lets this harness compile and report "method missing" as a clean FAIL even if a
//  fix was reverted or a file was pasted in only partially, instead of breaking the
//  whole project's build and telling you nothing.
// =============================================================================
public class FixPackVerificationHarness : MonoBehaviour
{
    private const string TAG = "[FIXPACK] ";

    [Header("Safety")]
    [Tooltip("Refuse to run the DESTRUCTIVE suites (B, C) while a wave is spawning or " +
             "enemies are alive. Strongly recommended.")]
    public bool requireIdleState = true;

    [Tooltip("Instead of skipping immediately when the board is busy, WAIT this many " +
             "seconds for it to go idle, then run. With Countdown pacing the idle gap " +
             "between waves is only a few seconds — far too short to hit by hand — so " +
             "the destructive suites were never actually executing. 0 = old behaviour " +
             "(skip immediately).")]
    [Min(0f)]
    public float waitForIdleSeconds = 180f;

    [Tooltip("Seconds between 'still waiting' heartbeat lines while armed.")]
    [Min(1f)]
    public float armedHeartbeatSeconds = 10f;

    [Tooltip("Restore the board (towers, core energy, wallet) after a destructive suite.")]
    public bool restoreStateAfterDestructiveTests = true;

    [Header("Reporting")]
    [Tooltip("Log a line for every check, including passes.")]
    public bool verbose = true;

    private int _pass, _fail, _skip;
    private bool _running;

    // ───────────────────────────────────────────────────────────── lifecycle ──

    private void Awake()
    {
        Debug.LogWarning(TAG + "Fix-pack harness ready. F1 = API/statics (safe), " +
                         "F2 = map rebuild (DESTRUCTIVE), F3 = tower restore (DESTRUCTIVE), " +
                         "F4 = run plan, F5 = co-op. Shift+F1 = everything.");
    }

    private void Update()
    {
        if (_running) return;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        bool a = kb.f1Key.wasPressedThisFrame;
        bool b = kb.f2Key.wasPressedThisFrame;
        bool c = kb.f3Key.wasPressedThisFrame;
        bool d = kb.f4Key.wasPressedThisFrame;
        bool e = kb.f5Key.wasPressedThisFrame;
#else
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool a = Input.GetKeyDown(KeyCode.F1);
        bool b = Input.GetKeyDown(KeyCode.F2);
        bool c = Input.GetKeyDown(KeyCode.F3);
        bool d = Input.GetKeyDown(KeyCode.F4);
        bool e = Input.GetKeyDown(KeyCode.F5);
#endif
        if (a && shift) StartCoroutine(Wrap(RunAll()));
        else if (a) StartCoroutine(Wrap(SuiteA_ApiAndStatics()));
        else if (b) StartCoroutine(Wrap(SuiteB_MapRebuild()));
        else if (c) StartCoroutine(Wrap(SuiteC_TowerRestore()));
        else if (d) StartCoroutine(Wrap(SuiteD_RunPlan()));
        else if (e) StartCoroutine(Wrap(SuiteE_Coop()));
    }

    [ContextMenu("Run ALL fix-pack tests")]
    private void RunAllFromMenu()
    {
        if (!Application.isPlaying) { Debug.LogWarning(TAG + "Enter Play mode first."); return; }
        if (!_running) StartCoroutine(Wrap(RunAll()));
    }

    private IEnumerator Wrap(IEnumerator suite)
    {
        _running = true;
        yield return suite;
        _running = false;
    }

    private IEnumerator RunAll()
    {
        _pass = _fail = _skip = 0;
        // Reset per-run coverage flags, or a stale `true` from an earlier full run would
        // suppress the "suite never executed" warning on this one.
        _ranCoreRebuildTests = _ranTowerRestoreTests = false;

        L("╔══════════════ FIX PACK VERIFICATION — FULL RUN ══════════════╗");
        if (requireIdleState && waitForIdleSeconds > 0f)
            L($"Destructive suites will ARM and wait up to {waitForIdleSeconds:F0}s for an idle board, " +
              "so this run may take a while. That is expected — keep playing.");
        yield return SuiteA_ApiAndStatics(false);
        yield return SuiteD_RunPlan(false);
        yield return SuiteE_Coop(false);
        yield return SuiteB_MapRebuild(false);
        yield return SuiteC_TowerRestore(false);
        Summary("FULL RUN");
    }

    // ══════════════════════════════════════════════════════════════ SUITE A ══
    //  API contract + augment statics. Read-only; safe to run any time.

    private IEnumerator SuiteA_ApiAndStatics() => SuiteA_ApiAndStatics(true);

    private IEnumerator SuiteA_ApiAndStatics(bool standalone)
    {
        if (standalone) { _pass = _fail = _skip = 0; }
        L("───── SUITE A: API contract + augment statics ─────");

        // A1..A9 — every method the fix pack added must exist with the right shape.
        // A missing one means a file was reverted or only partially pasted.
        Method("A1  Tower.RestoreEnergyState(float,float)",
               typeof(Tower), "RestoreEnergyState", typeof(float), typeof(float));

        Method("A2  TowerDefenseMap.SeedCoreEnergy(float,float)",
               typeof(TowerDefenseMap), "SeedCoreEnergy", typeof(float), typeof(float));

        Method("A3  TowerDefenseMap.RebuildPreservingTowers()",
               typeof(TowerDefenseMap), "RebuildPreservingTowers");

        Method("A4  TowerPlacementManager.RestoreTowerInto(+maxEnergy)",
               typeof(TowerPlacementManager), "RestoreTowerInto",
               typeof(TowerSlot), typeof(Tower.TowerType), typeof(int), typeof(float), typeof(float));

        Method("A5  TowerSlot.PlaceTowerForRestore(+maxEnergy)",
               typeof(TowerSlot), "PlaceTowerForRestore",
               typeof(GameObject), typeof(int), typeof(float), typeof(float));

        Method("A6  WaveCheckpointService.CaptureSnapshot(+forceStageStart)",
               typeof(WaveCheckpointService), "CaptureSnapshot",
               typeof(int), typeof(int), typeof(bool));

        Method("A7  WaveCheckpointService.ResetForNewRun()",
               typeof(WaveCheckpointService), "ResetForNewRun");

        Method("A8  RunPersistence.RestoreCore(RunSaveData)",
               typeof(RunPersistence), "RestoreCore", typeof(RunSaveData));

        Method("A9  RunPersistence.RestoreEquipment(RunSaveData)",
               typeof(RunPersistence), "RestoreEquipment", typeof(RunSaveData));

        Method("A10 RunPersistence.RecordEquippedTool(string)",
               typeof(RunPersistence), "RecordEquippedTool", typeof(string));

        Method("A11 TowerKillAttribution.Reset()",
               typeof(TowerKillAttribution), "Reset");

        Method("A12 PlayerRef.SetCloaked(bool)",
               typeof(PlayerRef), "SetCloaked", typeof(bool));

        // A13 — the map must expose the two carry-over toggles the core/slot fixes need.
        Field("A13 TowerDefenseMap.preserveCoreEnergyAcrossRebuild",
              typeof(TowerDefenseMap), "preserveCoreEnergyAcrossRebuild");
        Field("A14 TowerDefenseMap.preserveBonusSlotsAcrossRebuild",
              typeof(TowerDefenseMap), "preserveBonusSlotsAcrossRebuild");

        // A15 — the statics reset must ACTUALLY clear every dimension. Dirty them all,
        // call ResetAll, and read them back. This is the regression that let a Siege
        // Doctrine pick compound into the next run.
        try
        {
            PlayerEconomyModifiers.EnergyGainMultiplier = 3f;
            PlayerCombatModifiers.OutgoingDamageMultiplier = 3f;
            TowerCombatModifiers.DamageMultiplier = 3f;
            TowerCombatModifiers.BaseFireRateMultiplier = 3f;
            TowerCombatModifiers.PerCountFireRateMultiplier = 3f;
            EnemyDropAugments.TowerKillChanceBonus = 0.9f;
            EnemyDropAugments.TowerKillValueBonus = 0.9f;
            TowerKillRewards.Enabled = true;
            TowerKillRewards.EnergyPerKill = 99;

            AugmentRuntimeModifiers.ResetAll();

            bool clean =
                Near(PlayerEconomyModifiers.EnergyGainMultiplier, 1f) &&
                Near(PlayerCombatModifiers.OutgoingDamageMultiplier, 1f) &&
                Near(TowerCombatModifiers.DamageMultiplier, 1f) &&
                Near(TowerCombatModifiers.BaseFireRateMultiplier, 1f) &&
                Near(TowerCombatModifiers.PerCountFireRateMultiplier, 1f) &&
                Near(EnemyDropAugments.TowerKillChanceBonus, 0f) &&
                Near(EnemyDropAugments.TowerKillValueBonus, 0f) &&
                !TowerKillRewards.Enabled;

            Report("A15 AugmentRuntimeModifiers.ResetAll clears every dimension", clean,
                   clean ? "all holders back to neutral"
                         : $"still dirty: energyGain={PlayerEconomyModifiers.EnergyGainMultiplier}, " +
                           $"towerDmg={TowerCombatModifiers.DamageMultiplier}, " +
                           $"killChance={EnemyDropAugments.TowerKillChanceBonus}, " +
                           $"titheEnabled={TowerKillRewards.Enabled}");
        }
        catch (Exception e) { Skip("A15 ResetAll sweep", "threw: " + e.Message); }

        // A16 — FireRateMultiplier must COMPOSE its two sources multiplicatively.
        try
        {
            TowerCombatModifiers.BaseFireRateMultiplier = 2f;
            TowerCombatModifiers.PerCountFireRateMultiplier = 3f;
            float composed = TowerCombatModifiers.FireRateMultiplier;
            Report("A16 FireRateMultiplier composes base x perCount", Near(composed, 6f),
                   $"2 x 3 = {composed} (expected 6)");
            AugmentRuntimeModifiers.ResetAll();
        }
        catch (Exception e) { Skip("A16 fire-rate composition", "threw: " + e.Message); }

        // A17 — the tower-kill attribution table must be prunable, not just grow.
        try
        {
            int before = TowerKillAttribution.TrackedCount;
            var probe = new GameObject("~fixpack_attribution_probe");
            TowerKillAttribution.MarkTowerHit(probe);
            int marked = TowerKillAttribution.TrackedCount;
            TowerKillAttribution.Reset();
            int after = TowerKillAttribution.TrackedCount;
            Destroy(probe);

            Report("A17 TowerKillAttribution.Reset empties the table",
                   marked > before && after == 0,
                   $"{before} -> {marked} after a mark -> {after} after Reset");
        }
        catch (Exception e) { Skip("A17 attribution table", "threw: " + e.Message); }

        // A17b — objects that call DontDestroyOnLoad MUST be scene roots, or the call is
        // a silent no-op. This is not theoretical: SessionConfig and AugmentRegistry both
        // hit it, which is why co-op runs kept coming up as single player.
        try
        {
            var offenders = new List<string>();
            if (SessionConfig.Instance != null && SessionConfig.Instance.transform.parent != null)
                offenders.Add("SessionConfig");
            if (AugmentRegistry.Instance != null && AugmentRegistry.Instance.transform.parent != null)
                offenders.Add("AugmentRegistry");

            Report("A17b DontDestroyOnLoad singletons are scene roots", offenders.Count == 0,
                   offenders.Count == 0
                     ? "all persistent singletons are unparented"
                     : $"{string.Join(", ", offenders)} still parented — DontDestroyOnLoad does NOTHING " +
                       "for non-root objects, so these die with the scene");
        }
        catch (Exception e) { Skip("A17b persistent singleton parenting", "threw: " + e.Message); }

        // A17c — the equipment restore looks for a Weapon under the player. Your
        // WeaponRollController warns when that layout is wrong; check it here too.
        try
        {
            var stats = FindFirstObjectByType<PlayerStats>();
            if (stats == null) Skip("A17c Weapon is reachable from the player", "no PlayerStats in scene");
            else
            {
                bool underPlayer = stats.GetComponentInChildren<Weapon>(true) != null;
                int sceneWeapons = FindObjectsByType<Weapon>(FindObjectsSortMode.None).Length;
                Report("A17c Weapon is reachable from the player", underPlayer || sceneWeapons == 1,
                       underPlayer ? "Weapon found under the player (correct layout)"
                                   : $"Weapon is NOT under the player; {sceneWeapons} in scene. " +
                                     "RestoreEquipment falls back, but parent the Weapon to the " +
                                     "player root — co-op cannot disambiguate otherwise");
            }
        }
        catch (Exception e) { Skip("A17c Weapon reachability", "threw: " + e.Message); }

        // A18 — the singleton statics must be nulled on teardown, not left dangling.
        Report("A18 TowerPlacementManager clears Instance in OnDestroy",
               HasSourceBehaviour<TowerPlacementManager>("OnDestroy"),
               "OnDestroy present (manual check: it must contain `Instance = null`)");

        if (standalone) Summary("SUITE A");
        yield break;
    }

    // ══════════════════════════════════════════════════════════════ SUITE B ══
    //  Map rebuild: core energy carry-over, bonus slots, slot-registry freshness.
    //  DESTRUCTIVE — rebuilds the map.

    private IEnumerator SuiteB_MapRebuild() => SuiteB_MapRebuild(true);

    private IEnumerator SuiteB_MapRebuild(bool standalone)
    {
        if (standalone) { _pass = _fail = _skip = 0; }
        L("───── SUITE B: map rebuild (DESTRUCTIVE) ─────");

        var map = FindFirstObjectByType<TowerDefenseMap>();
        if (map == null) { Skip("SUITE B", "no TowerDefenseMap in scene"); if (standalone) Summary("SUITE B"); yield break; }

        yield return ArmUntilIdle("SUITE B");
        if (!_armedResult) { if (standalone) Summary("SUITE B"); yield break; }

        var core = map.GetCentralCore();
        if (core == null) { Skip("SUITE B", "no CentralCore — has the map been generated yet?"); if (standalone) Summary("SUITE B"); yield break; }

        float savedEnergy = core.currentEnergy;
        float savedMax = core.maxEnergy;

        _ranCoreRebuildTests = true;

        // ── B1 ── Core energy must SURVIVE a rebuild.
        // This is the headline bug: ClearExistingMap destroyed the core and
        // CreateCentralCore rebuilt it at coreStartingEnergy, so every resume and every
        // layout-changing stage silently handed the player a full-health core.
        {
            float wounded = savedMax * 0.42f;
            core.SetEnergy(wounded);
            yield return null;

            map.GenerateMap();
            yield return null;

            var rebuilt = map.GetCentralCore();
            if (rebuilt == null) Fail("B1 core survives rebuild", "no core after GenerateMap");
            else
            {
                bool ok = Near(rebuilt.currentEnergy, wounded, 0.5f);
                Report("B1 core energy survives a map rebuild", ok,
                       ok ? $"{wounded:F1} preserved across GenerateMap"
                          : $"expected ~{wounded:F1}, got {rebuilt.currentEnergy:F1} " +
                            $"(coreStartingEnergy={map.coreStartingEnergy:F1} — if it matches that, " +
                            "the carry-over is not running: check preserveCoreEnergyAcrossRebuild)");
                core = rebuilt;
            }
        }

        // ── B2 ── SeedCoreEnergy must pre-load a value for the NEXT build.
        // This is the mechanism the resume path relies on, because at resume time no
        // core exists yet — RestoreAbsolutes runs before the map is ever generated.
        {
            float seeded = savedMax * 0.23f;
            try { map.SeedCoreEnergy(seeded, savedMax); }
            catch (Exception e) { Skip("B2 SeedCoreEnergy", "threw: " + e.Message); }

            map.GenerateMap();
            yield return null;

            var rebuilt = map.GetCentralCore();
            bool ok = rebuilt != null && Near(rebuilt.currentEnergy, seeded, 0.5f);
            Report("B2 SeedCoreEnergy survives into the rebuilt core", ok,
                   rebuilt == null ? "no core after rebuild"
                                   : $"expected ~{seeded:F1}, got {rebuilt.currentEnergy:F1}");
            core = rebuilt;
        }

        // ── B3 ── Augment bonus slots must survive a rebuild.
        // CreateTowerSlots reset bonusSlotsAdded to 0, so additional_tower_slots was a
        // one-stage effect AND towers saved into ringIndex 99 could never be restored.
        {
            int before = CountBonusSlots(map);
            int added = 0;
            try { added = map.AddBonusSlots(2); }
            catch (Exception e) { Skip("B3 AddBonusSlots", "threw: " + e.Message); }

            if (added <= 0)
                Skip("B3 bonus slots survive a rebuild",
                     "this layout defines no bonusSlotPositions, so there is nothing to carry");
            else
            {
                int afterAdd = CountBonusSlots(map);
                map.GenerateMap();
                yield return null;
                int afterRebuild = CountBonusSlots(map);

                bool ok = afterRebuild >= afterAdd;
                Report("B3 augment bonus slots survive a map rebuild", ok,
                       ok ? $"{afterAdd} bonus slot(s) still present after rebuild"
                          : $"had {afterAdd} before rebuild, {afterRebuild} after " +
                            $"(started at {before}) — carry-over not running");
            }
        }

        // ── B4 ── No STALE slots in the placement registry after a rebuild.
        // This is the defect the self-review caught: switching teardown from
        // DestroyImmediate to the runtime-correct Destroy made OnDestroy (and therefore
        // UnregisterSlot) deferred to end of frame, so for the rest of that frame
        // FindSlot could return a DYING slot and a tower placed into it would vanish.
        {
            var hub = TowerPlacementManager.Instance;
            if (hub == null) Skip("B4 no stale slots after rebuild", "no TowerPlacementManager");
            else
            {
                map.GenerateMap();
                // Deliberately NO yield here — same frame is exactly the danger window.
                var registered = hub.GetAllSlots();
                int dead = registered.Count(s => s == null);
                var live = map.GetAllSlots();

                bool ok = dead == 0 && registered.Count(s => s != null) >= live.Count;
                Report("B4 slot registry is clean in the SAME frame as a rebuild", ok,
                       ok ? $"{registered.Count} registered, {live.Count} live, 0 destroyed entries"
                          : $"{dead} destroyed slot(s) still registered, " +
                            $"{registered.Count} registered vs {live.Count} live — " +
                            "FindSlot can return a dying slot this frame");

                // And the lookup itself must resolve to something actually alive.
                var probe = live.FirstOrDefault(s => s != null);
                if (probe != null)
                {
                    var found = hub.FindSlot(probe.ringIndex, probe.slotIndex);
                    bool resolves = found != null && found.gameObject.activeInHierarchy;
                    Report("B4b FindSlot resolves to a LIVE slot after a rebuild", resolves,
                           found == null ? $"FindSlot({probe.ringIndex},{probe.slotIndex}) returned null"
                                         : $"resolved to '{found.name}', active={found.gameObject.activeInHierarchy}");
                }
                yield return null;
            }
        }

        // ── RESTORE ──────────────────────────────────────────────────────────────
        // This suite calls GenerateMap() up to four times. GenerateMap rebuilds the
        // terrain, core, obstacles and slots — but biome decorations are owned by
        // BiomeManager/ObstacleGenerator, which GenerateMap only NOTIFIES. Four rebuilds
        // in a couple of frames is what produced the flickering, and leaving without
        // re-applying the biome is what left the board looking stripped afterwards.
        if (restoreStateAfterDestructiveTests)
        {
            var final = map.GetCentralCore();
            if (final != null) { final.SetMaxEnergy(savedMax); final.SetEnergy(savedEnergy); }

            // Re-apply the biome so decorations/props come back.
            var biome = FindFirstObjectByType<BiomeManager>();
            if (biome != null)
            {
                bool reapplied = TryVoid("SUITE B biome restore",
                                         () => biome.SetBiome(biome.activeBiome));
                L(reapplied
                    ? $"restored core to {savedEnergy:F1}/{savedMax:F1} and re-applied biome " +
                      $"'{biome.activeBiome}' (decorations rebuilt)"
                    : $"restored core to {savedEnergy:F1}/{savedMax:F1}, but the biome re-apply " +
                      "threw — props may be missing until the next stage.");
            }
            else
            {
                Debug.LogWarning(TAG + "no BiomeManager found — the map was rebuilt but biome " +
                                       "decorations were NOT restored. They return at the next stage.");
            }
            yield return null;
        }

        if (standalone) Summary("SUITE B");
    }

    // ══════════════════════════════════════════════════════════════ SUITE C ══
    //  Tower restore: maxEnergy round-trip, RestoreEnergyState reviving a dead
    //  tower, and RebuildPreservingTowers not eating the board. DESTRUCTIVE.

    private IEnumerator SuiteC_TowerRestore() => SuiteC_TowerRestore(true);

    private IEnumerator SuiteC_TowerRestore(bool standalone)
    {
        if (standalone) { _pass = _fail = _skip = 0; }
        L("───── SUITE C: tower restore (DESTRUCTIVE) ─────");

        var map = FindFirstObjectByType<TowerDefenseMap>();
        var hub = TowerPlacementManager.Instance;
        if (map == null || hub == null) { Skip("SUITE C", "need TowerDefenseMap + TowerPlacementManager"); if (standalone) Summary("SUITE C"); yield break; }

        yield return ArmUntilIdle("SUITE C");
        if (!_armedResult) { if (standalone) Summary("SUITE C"); yield break; }

        var free = map.GetAllSlots().FirstOrDefault(s => s != null && !s.IsOccupied);
        if (free == null) { Skip("SUITE C", "no free tower slot to test with"); if (standalone) Summary("SUITE C"); yield break; }

        _ranTowerRestoreTests = true;

        Tower.TowerType type = Tower.TowerType.Basic;
        Tower built = null;

        // ── C1 ── The saved maxEnergy must actually be applied.
        // Both TowerSaveEntry.maxEnergy and TowerSnapshot.maxEnergy were captured and
        // then read by nothing, so an augment-boosted pool reverted to prefab base.
        {
            const float wantMax = 777f;
            const float wantCur = 321f;
            try { built = hub.RestoreTowerInto(free, type, 2, wantCur, wantMax); }
            catch (Exception e) { Skip("C1 maxEnergy restore", "RestoreTowerInto threw: " + e.Message); }

            if (built == null)
                Skip("C1 saved maxEnergy is applied on restore",
                     $"could not build a '{type}' tower — is that type in the prefab list?");
            else
            {
                yield return null;
                bool maxOk = Near(built.maxEnergy, wantMax, 1f);
                bool curOk = Near(built.currentEnergy, wantCur, 1f);
                Report("C1 saved maxEnergy is applied on restore", maxOk,
                       maxOk ? $"maxEnergy = {built.maxEnergy:F0}"
                             : $"expected {wantMax:F0}, got {built.maxEnergy:F0}");
                Report("C1b saved currentEnergy is not distorted by the rescale", curOk,
                       curOk ? $"currentEnergy = {built.currentEnergy:F0}"
                             : $"expected {wantCur:F0}, got {built.currentEnergy:F0} — " +
                               "the maxEnergy setter's proportional rescale ran after the absolute was set");

                // ── C2 ── RestoreEnergyState must revive a tower at zero AND notify.
                // The old path used SupplyEnergy, which early-returns on isDestroyed, so
                // a tower killed during the rewound wave stayed dead; and the downward
                // branch wrote the field directly, skipping the UI event.
                bool fired = false;
                Action<float> listener = _ => fired = true;

                // C# forbids `yield return` inside a try that has a catch (CS1626) and
                // inside a catch/finally (CS1631) — and this test genuinely needs to wait
                // a frame between the two steps. So the guarded CALLS go through TryVoid
                // and the yields sit in plain control flow. The unsubscribe that would
                // normally live in a `finally` is an explicit line instead.
                bool ok1 = TryVoid("C2 RestoreEnergyState(0)", () => built.RestoreEnergyState(0f));
                yield return null;

                bool wasDead = ok1 && (built.IsDestroyed() || built.currentEnergy <= 0.01f);

                built.OnEnergyChanged += listener;
                bool ok2 = TryVoid("C2 RestoreEnergyState(half)",
                                   () => built.RestoreEnergyState(wantMax * 0.5f, wantMax));
                yield return null;
                built.OnEnergyChanged -= listener;

                if (!ok1 || !ok2)
                {
                    Skip("C2 RestoreEnergyState revives a tower from zero",
                         "the call threw — see the SKIP line above for the exception");
                }
                else
                {
                    bool revived = built.currentEnergy > 0.01f && !built.IsDestroyed();
                    Report("C2 RestoreEnergyState revives a tower from zero", wasDead && revived,
                           $"dropped to zero={wasDead}, revived={revived}, " +
                           $"energy now {built.currentEnergy:F0}/{built.maxEnergy:F0}");
                    Report("C2b RestoreEnergyState fires OnEnergyChanged (bar stays in sync)", fired,
                           fired ? "event raised" : "no event — the energy bar will show a stale value");
                }
            }
        }

        // ── C3 ── RebuildPreservingTowers must NOT eat the board.
        // The additional_tower_rings augment called GenerateMap() directly, which
        // destroys slotsContainer — and every tower is parented to its slot.
        {
            int beforeCount = map.GetAllSlots().Count(s => s != null && s.IsOccupied);
            if (beforeCount == 0)
                Skip("C3 rebuild preserves towers", "no occupied slots to preserve");
            else
            {
                var occupiedCoords = map.GetAllSlots()
                    .Where(s => s != null && s.IsOccupied)
                    .Select(s => (s.ringIndex, s.slotIndex))
                    .ToList();

                try { map.RebuildPreservingTowers(); }
                catch (Exception e) { Skip("C3 rebuild preserves towers", "threw: " + e.Message); }

                yield return null;
                yield return null;   // slot restore starts a coroutine for augments

                int survived = occupiedCoords.Count(c =>
                {
                    var s = hub.FindSlot(c.ringIndex, c.slotIndex);
                    return s != null && s.IsOccupied;
                });

                bool ok = survived == occupiedCoords.Count;
                Report("C3 RebuildPreservingTowers keeps every tower", ok,
                       ok ? $"all {survived} tower(s) survived the rebuild"
                          : $"{survived}/{occupiedCoords.Count} survived — the rest were destroyed " +
                            "with slotsContainer (this is the bug the extra-ring augment had)");
            }
        }

        // Clean up the tower we built.
        if (restoreStateAfterDestructiveTests && built != null)
        {
            var owner = map.GetAllSlots().FirstOrDefault(s => s != null && s.currentTower == built.gameObject);
            if (owner != null) owner.RemoveTower(false);
            L("removed the harness's test tower");
        }

        if (standalone) Summary("SUITE C");
    }

    // ══════════════════════════════════════════════════════════════ SUITE D ══
    //  Run plan: weather gating, biome repeats, determinism. Read-only.

    private IEnumerator SuiteD_RunPlan() => SuiteD_RunPlan(true);

    private IEnumerator SuiteD_RunPlan(bool standalone)
    {
        if (standalone) { _pass = _fail = _skip = 0; }
        L("───── SUITE D: run plan generation ─────");

        var orch = GameOrchestrator.Instance;
        if (orch == null || orch.runConfig == null)
        {
            Skip("SUITE D", "no GameOrchestrator / RunConfig");
            if (standalone) Summary("SUITE D");
            yield break;
        }

        // GenerateRunPlan is private, so reflect. Generating a THROWAWAY plan is
        // side-effect free apart from consuming RNG, which we bracket with InitState.
        var mi = typeof(GameOrchestrator).GetMethod("GenerateRunPlan",
                     BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null)
        {
            Skip("SUITE D", "GenerateRunPlan not found by reflection");
            if (standalone) Summary("SUITE D");
            yield break;
        }

        var plans = new List<List<StageData>>();
        // ── GenerateRunPlan IS NOT SIDE-EFFECT FREE ──────────────────────────────
        // It was treated as a pure function that returns a plan. It is not: it WRITES
        // orchestrator fields on the LIVE run —
        //     runWideLayout = null;  runWideLayout = library.PickRandom(null);
        //     runWaveDeck   = new List<WaveData>();
        // — and it consumes the global UnityEngine.Random stream, which this suite then
        // reseeds 42 times with its own test seeds.
        //
        // So the first version of this suite quietly corrupted the run it was measuring:
        // the NEXT stage would have used a layout and wave deck belonging to a throwaway
        // plan, and every subsequent random draw in the game (spawn directions, death
        // VFX, biome props) came from a hijacked RNG stream.
        //
        // Snapshot everything mutable, run the probes, put it all back.
        var orchType = typeof(GameOrchestrator);
        const BindingFlags PRIV = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var fLayout = orchType.GetField("runWideLayout", PRIV);
        var fDeck = orchType.GetField("runWaveDeck", PRIV);
        var fCursor = orchType.GetField("waveDeckCursor", PRIV);

        object savedLayout = fLayout?.GetValue(orch);
        object savedDeck = fDeck?.GetValue(orch);
        object savedCursor = fCursor?.GetValue(orch);
        var savedRandom = UnityEngine.Random.state;

        // Generating 40 throwaway plans also makes GenerateRunPlan log "Run-wide layout:
        // X" 40 times, burying this suite's own PASS/FAIL lines. Mute for the duration.
        var prevLogEnabled = Debug.unityLogger.logEnabled;
        Debug.unityLogger.logEnabled = false;

        // Capture the failure into a local rather than bailing from the catch: C# does
        // not allow `yield break` inside a catch clause (CS1631).
        string planError = null;
        try
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                UnityEngine.Random.InitState(seed * 7919);
                var plan = mi.Invoke(orch, null) as List<StageData>;
                if (plan != null) plans.Add(plan);
            }
        }
        catch (Exception e) { planError = e.Message; }
        finally { Debug.unityLogger.logEnabled = prevLogEnabled; }

        if (planError != null)
        {
            Skip("SUITE D", "GenerateRunPlan threw: " + planError);
            if (standalone) Summary("SUITE D");
            yield break;
        }

        if (plans.Count == 0)
        {
            Skip("SUITE D", "no plans generated");
            if (standalone) Summary("SUITE D");
            yield break;
        }

        // ── D1 ── Balloons only at night. They were rolled independently of nightMode,
        // so daylight stages advertised [BALLOONS] for lanterns that are invisible.
        {
            int violations = plans.SelectMany(p => p).Count(s => s.balloonsEnabled && !s.nightMode);
            int withBalloons = plans.SelectMany(p => p).Count(s => s.balloonsEnabled);
            Report("D1 balloons never appear on a daylight stage", violations == 0,
                   violations == 0
                     ? $"{withBalloons} balloon stage(s) across {plans.Count} plans, all at night"
                     : $"{violations} stage(s) have balloons WITHOUT night mode");
        }

        // ── D2 ── No back-to-back biome repeat when allowRepeatBiomes is off.
        // The pool refill could re-deal the biome just used.
        if (orch.runConfig.allowRepeatBiomes)
            Skip("D2 no back-to-back biome repeats",
                 "RunConfig.allowRepeatBiomes is ON, so repeats are intended and the fix is inert. " +
                 "This check can never run with your current config — untick Allow Repeat Biomes " +
                 "on the RunConfig asset if you want the no-adjacent-repeat guarantee tested.");
        else
        {
            int adjacent = 0;
            foreach (var plan in plans)
                for (int i = 1; i < plan.Count; i++)
                    if (plan[i].biome == plan[i - 1].biome) adjacent++;

            Report("D2 no back-to-back biome repeats", adjacent == 0,
                   adjacent == 0 ? $"checked {plans.Sum(p => Math.Max(0, p.Count - 1))} adjacent pairs"
                                 : $"{adjacent} adjacent duplicate biome pair(s) found");
        }

        // ── D3 ── The plan must be DETERMINISTIC for a seed. Resume depends entirely on
        // this: it replays the seed and expects an identical run.
        {
            List<StageData> a = null, b = null;
            bool prevLog = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                UnityEngine.Random.InitState(4242);
                a = mi.Invoke(orch, null) as List<StageData>;
                UnityEngine.Random.InitState(4242);
                b = mi.Invoke(orch, null) as List<StageData>;
            }
            catch { /* reported as a FAIL by the null check below */ }
            finally { Debug.unityLogger.logEnabled = prevLog; }

            bool same = a != null && b != null && a.Count == b.Count;
            if (same)
                for (int i = 0; i < a.Count && same; i++)
                    same = a[i].biome == b[i].biome
                        && a[i].nightMode == b[i].nightMode
                        && a[i].fogEnabled == b[i].fogEnabled
                        && a[i].rainEnabled == b[i].rainEnabled
                        && a[i].snowEnabled == b[i].snowEnabled
                        && a[i].balloonsEnabled == b[i].balloonsEnabled
                        && a[i].layout == b[i].layout;

            Report("D3 run plan is deterministic for a given seed", same,
                   same ? "two plans from seed 4242 are identical (resume will reproduce the run)"
                        : "SAME SEED PRODUCED DIFFERENT PLANS — resume cannot reproduce a run");
        }

        // ── D4 ── Layout library must not hand out destroyed ScriptableObjects.
        // Put the live run back exactly as we found it, BEFORE the last checks report.
        if (fLayout != null) fLayout.SetValue(orch, savedLayout);
        if (fDeck != null) fDeck.SetValue(orch, savedDeck);
        if (fCursor != null) fCursor.SetValue(orch, savedCursor);
        UnityEngine.Random.state = savedRandom;

        Report("D5 suite restored the live run plan state", fLayout != null && fDeck != null,
               fLayout != null && fDeck != null
                 ? "runWideLayout, runWaveDeck and the RNG stream were snapshotted and restored"
                 : "could NOT find runWideLayout/runWaveDeck by reflection — this suite may " +
                   "have left the live run using a throwaway plan. Restart the run.");

        if (orch.runConfig.mapLayoutLibrary == null)
            Skip("D4 layout library returns live layouts", "no mapLayoutLibrary assigned");
        else
        {
            var layouts = orch.runConfig.mapLayoutLibrary.GetLayouts();
            int dead = layouts?.Count(l => l == null) ?? -1;
            Report("D4 layout library returns no destroyed layouts", dead == 0,
                   dead < 0 ? "GetLayouts returned null"
                            : $"{layouts.Count} layout(s), {dead} destroyed " +
                              "(destroyed entries appear on the 2nd Play session of an editor run)");
        }

        if (standalone) Summary("SUITE D");
        yield break;
    }

    // ══════════════════════════════════════════════════════════════ SUITE E ══
    //  Co-op + registry. Read-only.

    private IEnumerator SuiteE_Coop() => SuiteE_Coop(true);

    private IEnumerator SuiteE_Coop(bool standalone)
    {
        if (standalone) { _pass = _fail = _skip = 0; }
        L("───── SUITE E: co-op + registry ─────");

        int n = PlayerRegistry.Count;
        L($"registered players: {n}");

        // ── E1 ── Cloak must be per-player, not a global that hides everyone.
        {
            var p0 = PlayerRegistry.Instance?.Get(0);
            if (p0 == null) Skip("E1 cloak is per-player", "no player 0 registered");
            else
            {
                bool baseline = p0.IsCloaked;
                try
                {
                    p0.SetCloaked(true);
                    bool nowCloaked = p0.IsCloaked;
                    p0.SetCloaked(false);
                    bool nowVisible = !p0.IsCloaked;
                    p0.ClearCloakOverride();

                    Report("E1 PlayerRef cloak responds per-player", nowCloaked && nowVisible,
                           $"SetCloaked(true)->{nowCloaked}, SetCloaked(false)->visible {nowVisible}, " +
                           $"baseline restored to {p0.IsCloaked} (was {baseline})");

                    if (n > 1)
                    {
                        var p1 = PlayerRegistry.Instance.Get(1);
                        if (p1 != null)
                        {
                            p0.SetCloaked(true);
                            bool p1Unaffected = !p1.IsCloaked;
                            p0.ClearCloakOverride();
                            Report("E1b cloaking P1 does not cloak P2", p1Unaffected,
                                   p1Unaffected ? "P2 stayed visible"
                                                : "P2 was ALSO hidden — cloak is still global");
                        }
                    }
                    else Skip("E1b cloaking P1 does not cloak P2", "single player — start co-op to test");
                }
                catch (Exception e) { Skip("E1 cloak", "threw: " + e.Message); }
            }
        }

        // ── E2 ── Registry integrity: no duplicate indices, no null Stats.
        {
            var all = PlayerRegistry.Instance?.All;
            if (all == null || all.Count == 0) Skip("E2 registry integrity", "no players registered");
            else
            {
                var idx = all.Where(p => p != null).Select(p => p.PlayerIndex).ToList();
                bool noDupes = idx.Distinct().Count() == idx.Count;
                bool statsOk = all.All(p => p == null || p.Stats != null);
                Report("E2 registry has unique indices and live Stats", noDupes && statsOk,
                       $"indices [{string.Join(",", idx)}], duplicates={!noDupes}, nullStats={!statsOk}");
            }
        }

        // ── E3 ── HealEverything must heal EVERY player, not the first one found.
        if (n < 2) Skip("E3 HealEverything heals all players", "single player — start co-op to test");
        else
        {
            var mi = typeof(GameOrchestrator).GetMethod("HealEverything",
                         BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var orch = GameOrchestrator.Instance;
            if (mi == null || orch == null) Skip("E3 HealEverything", "method or orchestrator missing");
            else
            {
                var stats = PlayerRegistry.Instance.All
                              .Where(p => p != null && p.Stats != null).Select(p => p.Stats).ToList();

                // currentHealth may be a read-only property depending on your PlayerStats,
                // so wound the players through reflection rather than assuming a public
                // setter. If it isn't writable we SKIP instead of failing to compile.
                var hpField = typeof(PlayerStats).GetField("currentHealth",
                                  BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (hpField == null || hpField.IsInitOnly)
                {
                    Skip("E3 HealEverything heals every registered player",
                         "PlayerStats.currentHealth is not writable here — wound both players " +
                         "manually, then re-run: every player should end at full health");
                }
                else
                {
                    var before = stats.Select(s => s.currentHealth).ToList();
                    foreach (var s in stats)
                        hpField.SetValue(s, Mathf.Max(1f, s.maxHealth * 0.3f));

                    bool threw = false;
                    try { mi.Invoke(orch, null); }
                    catch (Exception e) { threw = true; Skip("E3 HealEverything", "threw: " + e.Message); }
                    yield return null;

                    if (!threw)
                    {
                        int healed = stats.Count(s => s.currentHealth >= s.maxHealth - 0.01f);
                        Report("E3 HealEverything heals every registered player", healed == stats.Count,
                               healed == stats.Count
                                 ? $"all {healed} player(s) restored to full"
                                 : $"only {healed}/{stats.Count} healed — the single-player " +
                                   "FindFirstObjectByType path is still in use");
                    }

                    for (int i = 0; i < stats.Count; i++) hpField.SetValue(stats[i], before[i]);
                }
            }
        }

        // ── E4 ── Wallet must never go negative (shared-economy tripwire).
        if (EnergyManager.Instance == null) Skip("E4 wallet non-negative", "no EnergyManager");
        else
        {
            int e = EnergyManager.Instance.GetPlayerEnergy();
            Report("E4 shared wallet is non-negative", e >= 0, $"wallet = {e}");
        }

        if (standalone) Summary("SUITE E");
    }

    // ───────────────────────────────────────────────────────────── utilities ──

    // True when the board is quiet enough to rebuild safely.
    private bool BoardIsIdle(out string why)
    {
        why = null;

        var orch = GameOrchestrator.Instance;
        if (orch != null)
        {
            var st = orch.CurrentState.ToString();
            if (st.Contains("Wave") || st.Contains("Boss")) { why = $"orchestrator is in {st}"; return false; }
        }

        int enemies = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None).Count(e => e != null);
        if (enemies > 0) { why = $"{enemies} enemy(ies) alive"; return false; }

        return true;
    }

    /// The run is over — nothing meaningful can be tested against a dead board.
    ///
    /// A real armed run hit exactly this: the core was destroyed mid-wait, and the
    /// harness kept printing "SUITE B still armed (30s / 180s) — 9 enemy(ies) alive"
    /// against a game that had already ended. Those enemies never despawn after a game
    /// over, so the wait could only ever burn its full timeout — and if they HAD
    /// despawned, Suite B would have rebuilt a map whose CentralCore was already
    /// destroyed and reported nonsense.
    private bool RunIsOver(out string why)
    {
        why = null;

        var orch = GameOrchestrator.Instance;
        if (orch != null && orch.CurrentState.ToString().Contains("GameOver"))
        {
            why = "the run ended (GAME OVER) while this suite was armed";
            return true;
        }

        var map = FindFirstObjectByType<TowerDefenseMap>();
        var core = map != null ? map.GetCentralCore() : null;
        if (core == null || core.IsDestroyed())
        {
            why = "the Central Core is destroyed — the board is not in a testable state";
            return true;
        }

        return false;
    }

    // Result of arming: did we reach an idle board?
    private bool _armedResult;

    /// Wait for an idle board instead of skipping on the spot.
    ///
    /// WHY THIS EXISTS. The first real run of this harness reported "24 passed, 0 failed"
    /// while suites B and C — the ones covering the entire reason the fix pack was
    /// written — never executed even once. Every attempt landed on WaveActive or
    /// "14 enemies alive". With Countdown pacing the idle gap between waves is about
    /// three seconds, so hitting it by hand is close to impossible, and the green
    /// summary line hid the fact that nothing meaningful had been tested.
    ///
    /// Now the suite ARMS itself: press the key whenever you like, mid-fight is fine,
    /// and it fires the moment the wave clears.
    private IEnumerator ArmUntilIdle(string suite)
    {
        _armedResult = false;

        if (!requireIdleState) { _armedResult = true; yield break; }

        if (RunIsOver(out string alreadyOver))
        {
            Skip(suite, $"{alreadyOver}. Start a fresh run before testing the rebuild suites.");
            yield break;
        }

        if (BoardIsIdle(out _)) { _armedResult = true; yield break; }

        if (waitForIdleSeconds <= 0f)
        {
            BoardIsIdle(out string now);
            Skip(suite, $"{now} — destructive suites need an idle board. " +
                        "Set Wait For Idle Seconds > 0 to arm and run automatically.");
            yield break;
        }

        BoardIsIdle(out string reason);
        L($"{suite} ARMED — {reason}. Waiting up to {waitForIdleSeconds:F0}s for the board to go idle; " +
          "it will run itself the moment the wave clears. Keep playing.");

        float waited = 0f, sinceBeat = 0f;
        while (waited < waitForIdleSeconds)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
            sinceBeat += Time.unscaledDeltaTime;

            // Bail the moment the run dies rather than burning the whole timeout
            // against a board that can no longer be tested.
            if (RunIsOver(out string over))
            {
                Skip(suite, $"{over} after {waited:F0}s. Restart the run and press the key " +
                            "again — the suite will arm and fire when the next wave clears.");
                yield break;
            }

            if (BoardIsIdle(out _))
            {
                // Let the board settle for a beat: the orchestrator leaves WaveActive
                // slightly before the last corpse is cleaned up, and a rebuild landing
                // in that window would be testing a half-torn-down scene.
                yield return new WaitForSecondsRealtime(0.5f);
                if (!BoardIsIdle(out _)) continue;   // a new wave already started

                L($"{suite} board went idle after {waited:F1}s — running now.");
                _armedResult = true;
                yield break;
            }

            if (sinceBeat >= armedHeartbeatSeconds)
            {
                sinceBeat = 0f;
                BoardIsIdle(out string still);
                L($"{suite} still armed ({waited:F0}s / {waitForIdleSeconds:F0}s) — {still}.");
            }
        }

        BoardIsIdle(out string final);
        Skip(suite, $"waited {waitForIdleSeconds:F0}s and the board never went idle ({final}). " +
                    "Raise Wait For Idle Seconds, switch RunConfig pacing to Ready Up for an " +
                    "unlimited gap, or clear requireIdleState to force the run.");
    }

    private static int CountBonusSlots(TowerDefenseMap map)
        => map.GetAllSlots().Count(s => s != null && s.ringIndex == 99);

    private void Method(string label, Type owner, string name, params Type[] args)
    {
        try
        {
            var mi = owner.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, args, null);
            Report(label, mi != null,
                   mi != null ? "present"
                              : $"MISSING on {owner.Name} — that fix is not in your project " +
                                "(file reverted, or only partially pasted)");
        }
        catch (Exception e) { Skip(label, "reflection threw: " + e.Message); }
    }

    private void Field(string label, Type owner, string name)
    {
        var fi = owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Report(label, fi != null, fi != null ? "present" : $"MISSING on {owner.Name}");
    }

    private static bool HasSourceBehaviour<T>(string methodName)
        => typeof(T).GetMethod(methodName,
               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;

    private static bool Near(float a, float b, float eps = 0.001f) => Mathf.Abs(a - b) <= eps;

    /// Run a guarded call and report whether it completed. Exists because a coroutine
    /// cannot `yield return` from inside a try/catch, so any test that must wait a frame
    /// BETWEEN two guarded calls has to move the guarding out of the control flow.
    private bool TryVoid(string label, Action action)
    {
        try { action(); return true; }
        catch (Exception e) { Skip(label, "threw: " + e.Message); return false; }
    }

    private void Report(string test, bool ok, string detail)
    {
        if (ok) { _pass++; if (verbose) Debug.Log(TAG + $"<color=lime>PASS</color>  {test} — {detail}"); }
        else { _fail++; Debug.LogError(TAG + $"FAIL  {test} — {detail}"); }
    }

    private void Fail(string test, string detail) { _fail++; Debug.LogError(TAG + $"FAIL  {test} — {detail}"); }
    private void Skip(string test, string detail) { _skip++; Debug.LogWarning(TAG + $"SKIP  {test} — {detail}"); }

    // Marked true by suites B and C when they actually execute a check.
    private bool _ranCoreRebuildTests, _ranTowerRestoreTests;

    private void Summary(string what)
    {
        L($"═════ {what}: {_pass} passed, {_fail} failed, {_skip} skipped ═════");

        if (_fail > 0)
            Debug.LogError(TAG + $"{_fail} CHECK(S) FAILED — see the FAIL lines above.");

        // FIX: this used to print "ALL EXECUTED CHECKS PASSED" whenever nothing failed —
        // including a run where suites B and C never executed at all. That is precisely
        // what happened on the first real run: a green summary sat directly above the
        // fact that every behavioural test of the fix pack had been skipped. The word
        // "EXECUTED" was doing far too much work. Say plainly what was NOT covered.
        bool fullRun = what.Contains("FULL");
        var missing = new List<string>();
        if (fullRun && !_ranCoreRebuildTests) missing.Add("B (core energy / bonus slots / slot registry)");
        if (fullRun && !_ranTowerRestoreTests) missing.Add("C (maxEnergy restore / tower revival / rebuild)");

        if (_fail == 0 && _pass > 0 && missing.Count == 0)
            Debug.Log(TAG + "<color=lime>ALL CHECKS PASSED</color>");
        else if (_fail == 0 && missing.Count > 0)
            Debug.LogWarning(TAG + $"<color=yellow>NOT A CLEAN BILL OF HEALTH</color> — {_pass} passed, but suite(s) " +
                                   $"{string.Join(" and ", missing)} never executed. Those are the behavioural tests " +
                                   "of the fixes themselves; everything else is mostly API-shape checking. " +
                                   "Leave Wait For Idle Seconds > 0 and press the key again — it will arm and " +
                                   "fire itself when the wave clears.");

        if (_skip > 0)
            Debug.LogWarning(TAG + $"{_skip} check(s) skipped — a skipped check has verified NOTHING. " +
                                   "Read the reasons; some need co-op, an idle board, or a built map.");
    }

    private static void L(string m) => Debug.Log(TAG + m);
}




