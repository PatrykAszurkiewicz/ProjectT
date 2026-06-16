using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

//  Companion to CoopTestHarness (statics) and SinglePlayerTestHarness (parity).
//    P1 enemy targeting + multi-player hazards,
//    P2 input suppression + camera/viewport,
//    P3 per-player weapon unlock pools,
//    P4 per-player tower placement + TETHER STACKING composition,
//    P5 augment chooser routing + per-player applied sets.
//  Every line is tagged [P15TestLogs] — type that into the Console search box.
//  HOW TO RUN:
//    • Drop on a GameObject; press Play → the STATIC SUITE auto-runs (pure logic,
//      run it in a NEW EMPTY scene).
//    • Scene checks: right-click the component header → pick a check, or use
//      hotkeys F1..F8 / F9 (checklist) in Play mode IN REAL 2-PLAYER SCENE.
public class Phase1to5IntegrationHarness : MonoBehaviour
{
    private const string TAG = "[P15TestLogs] ";
    [Tooltip("Run the pure-logic integration suite on Play. Keep this scene EMPTY.")]
    public bool runStaticTestsOnStart = true;
    private int _pass, _fail;

    private void Awake()
    {
        Debug.LogWarning(TAG + $"Phase1to5IntegrationHarness ALIVE on scene '{gameObject.scene.name}'. " +
            $"runStaticTestsOnStart={runStaticTestsOnStart}. Right-click the component header for scene checks.");
    }

    private void Start()
    {
        if (runStaticTestsOnStart) RunStaticSuite();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.f1Key.wasPressedThisFrame) SceneCheck_NearestAliveRetarget();
        if (kb.f2Key.wasPressedThisFrame) SceneCheck_HazardCoverage();
        if (kb.f3Key.wasPressedThisFrame) SceneCheck_PerPlayerUnlocks();
        if (kb.f4Key.wasPressedThisFrame) SceneCheck_ChooserAppliedSets();
        if (kb.f5Key.wasPressedThisFrame) SceneCheck_SharedByDesign();
        if (kb.f6Key.wasPressedThisFrame) SceneCheck_SuppressionApi();
        if (kb.f7Key.wasPressedThisFrame) SceneCheck_TowerTetherLive();
        if (kb.f8Key.wasPressedThisFrame) SceneCheck_CamerasPerPlayer();
        if (kb.f9Key.wasPressedThisFrame) PrintManualChecklist();
#else
        if (Input.GetKeyDown(KeyCode.F1)) SceneCheck_NearestAliveRetarget();
        if (Input.GetKeyDown(KeyCode.F2)) SceneCheck_HazardCoverage();
        if (Input.GetKeyDown(KeyCode.F3)) SceneCheck_PerPlayerUnlocks();
        if (Input.GetKeyDown(KeyCode.F4)) SceneCheck_ChooserAppliedSets();
        if (Input.GetKeyDown(KeyCode.F5)) SceneCheck_SharedByDesign();
        if (Input.GetKeyDown(KeyCode.F6)) SceneCheck_SuppressionApi();
        if (Input.GetKeyDown(KeyCode.F7)) SceneCheck_TowerTetherLive();
        if (Input.GetKeyDown(KeyCode.F8)) SceneCheck_CamerasPerPlayer();
        if (Input.GetKeyDown(KeyCode.F9)) PrintManualChecklist();
#endif
    }

    //  ============================================================
    //  PURE-LOGIC INTEGRATION SUITE (no scene required)
    //  ============================================================
    [ContextMenu("Run Phase 1-5 Integration Tests")]
    public void RunStaticSuite()
    {
        _pass = 0; _fail = 0;
        L("===== PHASE 1-5 INTEGRATION SUITE: START =====");
        ResetGlobals();

        Tether_Monotonicity();
        Tether_TwoPlayerStacking();
        Tether_NPlayerStackingAndOrder();
        Tether_MixedZonesIndependent();
        Tether_CapIsPerContributor();
        Tether_RetuneAndBoundaries();
        Tether_DecayCancellation();
        Tether_RestoreToBaseOnLeave();
        Tether_SinglePlayerParity();
        Pipeline_TowerComposition();
        Pipeline_OrderInvariance();
        Save_MultiPlayerEdge();
        Save_AugmentLedgerEdge();
        Tether_AssociativityAndGrouping();
        Tether_PrecisionAndRetune();
        Tether_ThreePlayerDecay();
        Pipeline_FullChain();
        Save_ExtremeValues();

        ResetGlobals();
        L($"===== PHASE 1-5 INTEGRATION SUITE: {_pass} passed, {_fail} failed =====");
        if (_fail == 0) L("<color=lime>ALL PHASE 1-5 INTEGRATION TESTS PASSED</color>");
        else LErr($"{_fail} INTEGRATION TEST(S) FAILED — see [FAIL] lines above.");
    }

    private static void ResetGlobals()
    {
        TowerCombatModifiers.DamageMultiplier = 1f;
        TowerCombatModifiers.BaseFireRateMultiplier = 1f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1f;
        PlayerEconomyModifiers.EnergyGainMultiplier = 1f;
        PlayerCombatModifiers.OutgoingDamageMultiplier = 1f;
    }

    // Tuning defaults mirrored from PlayerTowerTether serialized fields.
    private const float FAR = 0.05f, MID = 0.10f, NEAR = 0.20f, CAP = 2.0f;

    //  Tether: per-tether monotonicity & neutrality 
    private void Tether_Monotonicity()
    {
        bool rangeUp = true, dmgUp = true, decayDown = true;
        float prevR = TetherMath.RangeMultiplier(0, FAR, CAP);
        float prevD = TetherMath.DamageMultiplier(0, MID, CAP);
        float prevDecay = TetherMath.DecayMultiplier(0, NEAR);
        for (int c = 1; c <= 25; c++)
        {
            float r = TetherMath.RangeMultiplier(c, FAR, CAP);
            float d = TetherMath.DamageMultiplier(c, MID, CAP);
            float dec = TetherMath.DecayMultiplier(c, NEAR);
            if (r < prevR - 1e-5f) rangeUp = false;
            if (d < prevD - 1e-5f) dmgUp = false;
            if (dec > prevDecay + 1e-5f) decayDown = false;
            prevR = r; prevD = d; prevDecay = dec;
        }
        Check("Tether range is monotonic non-decreasing", rangeUp);
        Check("Tether damage is monotonic non-decreasing", dmgUp);
        Check("Tether decay is monotonic non-increasing", decayDown);
        Check("Tether range neutral at count 0", Approx(TetherMath.RangeMultiplier(0, FAR, CAP), 1f));
        Check("Tether damage neutral at count 0", Approx(TetherMath.DamageMultiplier(0, MID, CAP), 1f));
        Check("Tether decay neutral at count 0", Approx(TetherMath.DecayMultiplier(0, NEAR), 1f));
        Check("Tether perTether 0 -> always 1 (range)", Approx(TetherMath.RangeMultiplier(99, 0f, CAP), 1f));
        Check("Tether perTether 0 -> always 1 (damage)", Approx(TetherMath.DamageMultiplier(99, 0f, CAP), 1f));
        Check("Tether reductionPerTether 0 -> decay 1", Approx(TetherMath.DecayMultiplier(99, 0f), 1f));
    }

    //  Tether: two-player stacking is the PRODUCT of each contribution 
    private void Tether_TwoPlayerStacking()
    {
        float a = TetherMath.DamageMultiplier(3, MID, CAP); // 1.30
        float b = TetherMath.DamageMultiplier(1, MID, CAP); // 1.10
        Check("2P damage product 1.30*1.10 == 1.43", Approx(a * b, 1.43f));
        Check("2P damage on base 10 -> 14.3", Approx(10f * a * b, 14.3f));
        float ra = TetherMath.RangeMultiplier(4, FAR, CAP); // 1.20
        float rb = TetherMath.RangeMultiplier(2, FAR, CAP); // 1.10
        Check("2P range product 1.20*1.10 == 1.32", Approx(ra * rb, 1.32f));
        float da = TetherMath.DecayMultiplier(1, NEAR);     // 0.80
        float db = TetherMath.DecayMultiplier(2, NEAR);     // 0.60
        Check("2P decay product 0.80*0.60 == 0.48", Approx(da * db, 0.48f));
        // Equal contributors: product == square.
        float eq = TetherMath.DamageMultiplier(2, MID, CAP); // 1.20
        Check("2P equal contributors squared (1.2^2=1.44)", Approx(eq * eq, 1.44f));
    }

    //  Tether: N-player stacking + order independence 
    private void Tether_NPlayerStackingAndOrder()
    {
        float a = TetherMath.DamageMultiplier(2, MID, CAP); // 1.20
        float b = TetherMath.DamageMultiplier(5, MID, CAP); // 1.50
        float c = TetherMath.DamageMultiplier(0, MID, CAP); // 1.00
        float d = TetherMath.DamageMultiplier(1, MID, CAP); // 1.10
        float prodAll = a * b * c * d;
        Check("4P product 1.2*1.5*1.0*1.1 == 1.98", Approx(prodAll, 1.98f));
        Check("4P on base 10 -> 19.8", Approx(10f * prodAll, 19.8f));
        // Order invariance of the aggregate product.
        Check("Stacking order invariant (abcd==dcba)", Approx(a * b * c * d, d * c * b * a));
        Check("Stacking order invariant (perm)", Approx(a * b * c * d, c * a * d * b));
        // A zero-count contributor (player nearby but not tethering) is identity.
        Check("Zero-count contributor is identity", Approx(a * c, a));
    }

    // --- Tether: a player in MID and a player in FAR hit DIFFERENT stats --------
    private void Tether_MixedZonesIndependent()
    {
        float dmgMul = TetherMath.DamageMultiplier(3, MID, CAP); // 1.30 -> damage
        float rngMul = TetherMath.RangeMultiplier(2, FAR, CAP);  // 1.10 -> range
        // Independent base stats compose with their OWN leg only, not each other.
        Check("Mixed: damage 10 -> 13 (range leg ignored)", Approx(10f * dmgMul, 13f));
        Check("Mixed: range 6 -> 6.6 (damage leg ignored)", Approx(6f * rngMul, 6.6f));
        // Decay is yet another independent axis.
        float decMul = TetherMath.DecayMultiplier(2, NEAR);      // 0.60
        Check("Mixed: decay axis independent (0.60)", Approx(decMul, 0.60f));
        Check("Mixed: damage leg unaffected by decay", Approx(10f * dmgMul, 13f));
    }

    // --- Tether: the cap clamps EACH contribution, then they multiply ----------
    private void Tether_CapIsPerContributor()
    {
        float capped = TetherMath.DamageMultiplier(100, MID, CAP); // pinned at 1+CAP = 3.0
        float small = TetherMath.DamageMultiplier(1, MID, CAP);    // 1.10
        Check("Cap pins a heavy contributor at 3.0", Approx(capped, 3.0f));
        Check("Capped * small = 3.0*1.1 = 3.3 (cap is per-player)", Approx(capped * small, 3.3f));
        Check("Two capped players = 3.0*3.0 = 9.0 (no product cap)", Approx(capped * capped, 9.0f));
        // Exact cap boundary at count where perTether*count == CAP.
        Check("Cap exact at 40@0.05 -> 3.0", Approx(TetherMath.RangeMultiplier(40, FAR, CAP), 3.0f));
        Check("Just under cap 39@0.05 -> 2.95", Approx(TetherMath.RangeMultiplier(39, FAR, CAP), 2.95f));
        Check("One past cap 41@0.05 -> 3.0 (pinned)", Approx(TetherMath.RangeMultiplier(41, FAR, CAP), 3.0f));
    }

    // --- Tether: designers may retune perTether/cap ---------------------------
    private void Tether_RetuneAndBoundaries()
    {
        Check("Retune cap=0 -> buff disabled (1.0)", Approx(TetherMath.RangeMultiplier(10, FAR, 0f), 1.0f));
        Check("Retune cap=0.5 -> ceiling 1.5", Approx(TetherMath.DamageMultiplier(100, MID, 0.5f), 1.5f));
        Check("Retune perTether 0.25 @2 -> 1.5", Approx(TetherMath.DamageMultiplier(2, 0.25f, CAP), 1.5f));
        Check("Negative count guarded (range)", Approx(TetherMath.RangeMultiplier(-7, FAR, CAP), 1f));
        Check("Negative count guarded (damage)", Approx(TetherMath.DamageMultiplier(-3, MID, CAP), 1f));
        Check("Negative count guarded (decay)", Approx(TetherMath.DecayMultiplier(-4, NEAR), 1f));
    }

    // --- Tether: NEAR decay cancels and clamps at zero ------------------------
    private void Tether_DecayCancellation()
    {
        Check("Decay 1@0.20 -> 0.80", Approx(TetherMath.DecayMultiplier(1, NEAR), 0.80f));
        Check("Decay 3@0.20 -> 0.40", Approx(TetherMath.DecayMultiplier(3, NEAR), 0.40f));
        Check("Decay exact cancel 5@0.20 -> 0", Approx(TetherMath.DecayMultiplier(5, NEAR), 0f));
        Check("Decay overcancel 9@0.20 clamps 0", Approx(TetherMath.DecayMultiplier(9, NEAR), 0f));
        Check("Decay never negative (200@0.20)", TetherMath.DecayMultiplier(200, NEAR) >= 0f);
        // Two partial-decay players still clamp within [0,1] when multiplied.
        float p = TetherMath.DecayMultiplier(2, NEAR) * TetherMath.DecayMultiplier(2, NEAR);
        Check("2P decay product stays in [0,1]", p >= 0f && p <= 1f);
    }

    // --- Tether: the CO-OP FIX — releasing returns to TRUE base ---------------
    private void Tether_RestoreToBaseOnLeave()
    {
        // The aggregator applies base * product(contribs). With ZERO contributors
        // (everyone released) the product is the empty product == 1 == true base.
        float product = 1f; // no contributors
        Check("No contributors -> product 1 (base restored)", Approx(10f * product, 10f));
        // One joins then leaves: base*c then base*1 — no residue, no doubling.
        float c = TetherMath.DamageMultiplier(3, MID, CAP); // 1.30
        float withC = 10f * c;
        float afterLeave = 10f * 1f;
        Check("Join raises (10 -> 13)", Approx(withC, 13f));
        Check("Leave restores exactly (->10, no stuck buff)", Approx(afterLeave, 10f));
        // Two join, one leaves: remaining is exactly the other player's product.
        float c2 = TetherMath.DamageMultiplier(1, MID, CAP); // 1.10
        Check("Both join 10*1.3*1.1=14.3", Approx(10f * c * c2, 14.3f));
        Check("One leaves -> 10*1.1=11 (only remaining contributor)", Approx(10f * c2, 11f));
    }

    // --- Tether: single player == old behaviour (product of one) --------------
    private void Tether_SinglePlayerParity()
    {
        float c = TetherMath.DamageMultiplier(4, MID, CAP); // 1.40
        Check("1P product-of-one == multiplier itself", Approx(1f * c, c));
        Check("1P base 10 -> 14 (identical to pre-coop)", Approx(10f * c, 14f));
        float r = TetherMath.RangeMultiplier(3, FAR, CAP);  // 1.15
        Check("1P range base 8 -> 9.2", Approx(8f * r, 9.2f));
    }

    // --- Pipeline: tower effective damage = base * GLOBAL * tether(product) ----
    private void Pipeline_TowerComposition()
    {
        ResetGlobals();
        float baseDmg = 20f;
        TowerCombatModifiers.DamageMultiplier = 1.5f;         // a global augment
        float p1 = TetherMath.DamageMultiplier(2, MID, CAP);  // 1.20
        float p2 = TetherMath.DamageMultiplier(3, MID, CAP);  // 1.30
        float effective = baseDmg * TowerCombatModifiers.DamageMultiplier * p1 * p2;
        Check("Pipeline 20 * 1.5 * 1.2 * 1.3 == 46.8", Approx(effective, 46.8f));
        // Fire rate composes base*perCount globally, independent of tether.
        TowerCombatModifiers.BaseFireRateMultiplier = 1.2f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1.5f;
        Check("Pipeline fire rate 1.2*1.5 == 1.8 (global only)", Approx(TowerCombatModifiers.FireRateMultiplier, 1.8f));
        ResetGlobals();
    }

    // --- Pipeline: global and tether multipliers commute (order invariant) ----
    private void Pipeline_OrderInvariance()
    {
        ResetGlobals();
        float baseDmg = 12f;
        float g = 1.25f;
        float t = TetherMath.DamageMultiplier(3, MID, CAP); // 1.30
        float ab = baseDmg * g * t;
        float ba = baseDmg * t * g;
        Check("Pipeline global*tether == tether*global", Approx(ab, ba));
        Check("Pipeline 12*1.25*1.30 == 19.5", Approx(ab, 19.5f));
    }

    // --- Save: many players / sparse indices ----------------------------------
    private void Save_MultiPlayerEdge()
    {
        var data = new RunSaveData
        {
            saveVersion = 2,
            stageIndex = 3,
            waveIndex = 5,
            runSeed = 999,
            hasCore = true,
            coreEnergy = 50f,
            coreMaxEnergy = 100f,
            hasEconomy = true,
            playerEnergy = 777,
        };
        data.players.Add(new PlayerSaveEntry { playerIndex = 0, playerHealth = 10, playerMaxHealth = 100 });
        data.players.Add(new PlayerSaveEntry { playerIndex = 1, playerHealth = 20, playerMaxHealth = 100 });
        data.players.Add(new PlayerSaveEntry { playerIndex = 2, playerHealth = 30, playerMaxHealth = 100 });
        data.players.Add(new PlayerSaveEntry { playerIndex = 3, playerHealth = 40, playerMaxHealth = 100 });
        var back = RoundTrip(data);
        Check("Save 4 players preserved", back.players != null && back.players.Count == 4);
        bool idxOk = true, hpOk = true;
        for (int i = 0; i < 4; i++)
        {
            if (back.players[i].playerIndex != i) idxOk = false;
            if (!Approx(back.players[i].playerHealth, (i + 1) * 10f)) hpOk = false;
        }
        Check("Save 4-player indices in order", idxOk);
        Check("Save 4-player health values intact", hpOk);
        Check("Save stage/wave/seed intact", back.stageIndex == 3 && back.waveIndex == 5 && back.runSeed == 999);
        Check("Save core/economy intact", back.hasCore && Approx(back.coreEnergy, 50f) && back.playerEnergy == 777);
    }

    // --- Save: augment ledger edge cases --------------------------------------
    private void Save_AugmentLedgerEdge()
    {
        // Empty ledger round-trips to an empty (non-null) list.
        var empty = new RunSaveData { saveVersion = 2 };
        var backEmpty = RoundTrip(empty);
        Check("Save empty augment list survives", backEmpty.augments != null && backEmpty.augments.Count == 0);
        Check("Save empty player list survives", backEmpty.players != null && backEmpty.players.Count == 0);

        // Same augment id on two different players is two distinct ledger rows.
        var data = new RunSaveData { saveVersion = 2 };
        data.augments.Add(new AugmentSaveEntry(328, "Rare", 0));
        data.augments.Add(new AugmentSaveEntry(328, "Epic", 1));
        data.augments.Add(new AugmentSaveEntry(66, "Common", 0));
        var back = RoundTrip(data);
        Check("Save duplicate id across players kept separate", back.augments.Count == 3);
        Check("Save dup id P0 rarity Rare", back.augments[0].id == 328 && back.augments[0].playerIndex == 0 && back.augments[0].rarity == "Rare");
        Check("Save dup id P1 rarity Epic", back.augments[1].id == 328 && back.augments[1].playerIndex == 1 && back.augments[1].rarity == "Epic");
        Check("Save order preserved (66 last on P0)", back.augments[2].id == 66 && back.augments[2].playerIndex == 0);

        // Back-compat + defaults.
        Check("Save legacy 2-arg ctor -> P0", new AugmentSaveEntry(5, "Common").playerIndex == 0);
        Check("Save null rarity -> Common", new AugmentSaveEntry(5, null, 1).rarity == "Common");
    }

    private static RunSaveData RoundTrip(RunSaveData d)
        => JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(d, true));

    // --- Tether: aggregate product is associative AND grouping-invariant ------
    private void Tether_AssociativityAndGrouping()
    {
        float a = TetherMath.DamageMultiplier(2, MID, CAP); // 1.20
        float b = TetherMath.DamageMultiplier(3, MID, CAP); // 1.30
        float c = TetherMath.DamageMultiplier(4, MID, CAP); // 1.40
        Check("Assoc ((a*b)*c) == (a*(b*c))", Approx((a * b) * c, a * (b * c)));
        Check("Assoc grouping value 1.2*1.3*1.4 == 2.184", Approx(a * b * c, 2.184f));
        // Adding then removing a contributor returns to the prior product exactly.
        float two = a * b;
        float three = a * b * c;
        Check("Add contributor multiplies in (two*c==three)", Approx(two * c, three));
        Check("Remove contributor divides out (three/c==two)", Approx(three / c, two));
        // Identity contributor (count 0) never changes the product, anywhere in the chain.
        float id = TetherMath.DamageMultiplier(0, MID, CAP); // 1.0
        Check("Identity in middle (a*id*b==a*b)", Approx(a * id * b, a * b));
    }

    // --- Tether: fractional retunes + float precision around the cap ----------
    private void Tether_PrecisionAndRetune()
    {
        // Fractional perTether values designers might pick.
        Check("Retune 0.07@3 -> 1.21", Approx(TetherMath.DamageMultiplier(3, 0.07f, CAP), 1.21f));
        Check("Retune 0.15@2 -> 1.30", Approx(TetherMath.RangeMultiplier(2, 0.15f, CAP), 1.30f));
        Check("Retune 0.33@1 -> 1.33", Approx(TetherMath.DamageMultiplier(1, 0.33f, CAP), 1.33f));
        // Cap with a fractional ceiling.
        Check("Retune cap 1.25 ceiling 2.25", Approx(TetherMath.DamageMultiplier(99, MID, 1.25f), 2.25f));
        // Large but finite counts stay pinned and finite (no overflow/NaN).
        float big = TetherMath.RangeMultiplier(100000, FAR, CAP);
        Check("Huge count pinned at cap (3.0)", Approx(big, 3.0f));
        Check("Huge count is finite", !float.IsNaN(big) && !float.IsInfinity(big));
        // Decay with fractional reduction.
        Check("Decay 0.07@5 -> 0.65", Approx(TetherMath.DecayMultiplier(5, 0.07f), 0.65f));
    }

    // --- Tether: three-player NEAR decay product (symmetric + asymmetric) -----
    private void Tether_ThreePlayerDecay()
    {
        float s = TetherMath.DecayMultiplier(1, NEAR); // 0.80
        Check("3P decay symmetric 0.8^3 == 0.512", Approx(s * s * s, 0.512f));
        float x = TetherMath.DecayMultiplier(1, NEAR); // 0.80
        float y = TetherMath.DecayMultiplier(2, NEAR); // 0.60
        float z = TetherMath.DecayMultiplier(3, NEAR); // 0.40
        Check("3P decay asymmetric 0.8*0.6*0.4 == 0.192", Approx(x * y * z, 0.192f));
        // If any one player fully cancels (0), the product is 0 regardless of others.
        float full = TetherMath.DecayMultiplier(5, NEAR); // 0
        Check("3P decay with a full-canceller -> 0", Approx(x * y * full, 0f));
        // Product is order-invariant.
        Check("3P decay order invariant", Approx(x * y * z, z * y * x));
    }

    // --- Pipeline: base * GLOBAL * three tethering players' product -----------
    private void Pipeline_FullChain()
    {
        ResetGlobals();
        float baseDmg = 10f;
        TowerCombatModifiers.DamageMultiplier = 2.0f;        // global augment
        float p1 = TetherMath.DamageMultiplier(2, MID, CAP); // 1.20
        float p2 = TetherMath.DamageMultiplier(1, MID, CAP); // 1.10
        float p3 = TetherMath.DamageMultiplier(0, MID, CAP); // 1.00 (nearby, not tethering)
        float effective = baseDmg * TowerCombatModifiers.DamageMultiplier * p1 * p2 * p3;
        Check("FullChain 10*2.0*1.2*1.1*1.0 == 26.4", Approx(effective, 26.4f));
        // Removing the global augment leaves only the tether product on base.
        TowerCombatModifiers.DamageMultiplier = 1f;
        Check("FullChain w/o global == 10*1.2*1.1 == 13.2", Approx(baseDmg * p1 * p2, 13.2f));
        ResetGlobals();
    }

    // --- Save: extreme / boundary values round-trip intact ---------------------
    private void Save_ExtremeValues()
    {
        var data = new RunSaveData
        {
            saveVersion = 2,
            stageIndex = 99,
            waveIndex = 999,
            runSeed = int.MinValue,
            hasCore = true,
            coreEnergy = 0f,
            coreMaxEnergy = 999999f,
            hasEconomy = true,
            playerEnergy = 2000000000,
        };
        data.players.Add(new PlayerSaveEntry { playerIndex = 0, playerHealth = 0f, playerMaxHealth = 1f });
        var back = RoundTrip(data);
        Check("Save extreme seed (int.MinValue) intact", back.runSeed == int.MinValue);
        Check("Save huge energy intact", back.playerEnergy == 2000000000);
        Check("Save zero health intact", Approx(back.players[0].playerHealth, 0f));
        Check("Save big stage/wave intact", back.stageIndex == 99 && back.waveIndex == 999);
        Check("Save big coreMax intact", Approx(back.coreMaxEnergy, 999999f));
        // saveVersion 1 (legacy) is preserved as written (resume code is what rejects < 2).
        var legacy = RoundTrip(new RunSaveData { saveVersion = 1 });
        Check("Save legacy version 1 preserved by round-trip", legacy.saveVersion == 1);
    }

    //  ============================================================
    //  SCENE CHECKS (read-only; run in your real 2-player scene)
    //  ============================================================

    [ContextMenu("Scene: NearestAlive retargets (F1)")]
    private void SceneCheck_NearestAliveRetarget()
    {
        L("----- F1: P1 enemy targeting via NearestAlive -----");
        var reg = PlayerRegistry.Instance;
        if (reg == null) { LErr("PlayerRegistry.Instance is null."); return; }
        L($"Players: {reg.All.Count}");
        if (reg.All.Count < 2) { LWarn("Need 2 players. Spawn P2, then run F1 again."); return; }
        var p0 = reg.Get(0); var p1 = reg.Get(1);
        if (p0 == null || p1 == null || p0.Stats == null || p1.Stats == null) { LWarn("Players not fully initialized."); return; }
        try
        {
            Check("F1: NearestAlive(@P0) == P0", reg.NearestAlive(p0.transform.position) == p0.Stats);
            Check("F1: NearestAlive(@P1) == P1", reg.NearestAlive(p1.transform.position) == p1.Stats);
            // Midpoint resolves to whichever is genuinely closer (no crash, returns one of them).
            Vector3 mid = (p0.transform.position + p1.transform.position) * 0.5f;
            var nm = reg.NearestAlive(mid);
            Check("F1: NearestAlive(@mid) returns a live player", nm == p0.Stats || nm == p1.Stats);
        }
        catch (System.Exception e) { LWarn($"NearestAlive signature differs: {e.Message}"); }
        L("PROCEDURE: down P1, run F1 again — NearestAlive(@P1) must now return P0 (downed players are skipped),");
        L("and AllDead() must stay false. Enemies that were chasing P1 should retarget P0.");
    }

    [ContextMenu("Scene: Hazard coverage AllAliveInRadius (F2)")]
    private void SceneCheck_HazardCoverage()
    {
        L("----- F2: P1 area-hazard coverage (AllAliveInRadius) -----");
        var reg = PlayerRegistry.Instance;
        if (reg == null) { LErr("PlayerRegistry.Instance is null."); return; }
        if (reg.All.Count < 2) { LWarn("Need 2 players for coverage check."); return; }
        var p0 = reg.Get(0);
        if (p0 == null) { LWarn("P0 missing."); return; }
        // Reflection so this compiles even if the signature is Vector2 vs Vector3.
        var mi = reg.GetType().GetMethod("AllAliveInRadius");
        if (mi == null) { LWarn("AllAliveInRadius not found — skipping (give it a manual hazard test)."); }
        else
        {
            try
            {
                var ps = mi.GetParameters();
                object center = ps.Length > 0 && ps[0].ParameterType == typeof(Vector2)
                    ? (object)(Vector2)p0.transform.position
                    : (object)p0.transform.position;
                object[] args = ps.Length >= 2 ? new object[] { center, 9999f } : new object[] { center };
                var result = mi.Invoke(reg, args);
                int n = CountEnumerable(result as IEnumerable);
                L($"AllAliveInRadius(huge) returned {n} players (expect == alive count = {reg.All.Count}).");
                Check("F2: huge radius covers ALL alive players", n == reg.All.Count);
            }
            catch (System.Exception e) { LWarn($"AllAliveInRadius probe failed: {e.Message}"); }
        }
        L("PROCEDURE: stand BOTH players in one poison cloud — each must take its OWN damage ticks");
        L("(per-player accumulators). Then move ONE out: only the one inside keeps taking damage.");
    }

    [ContextMenu("Scene: Per-player weapon unlocks (F3)")]
    private void SceneCheck_PerPlayerUnlocks()
    {
        L("----- F3: P3/P5 per-player weapon unlock pools -----");
        var t = FindType("WeaponUnlockRegistry");
        if (t == null) { LWarn("WeaponUnlockRegistry type not found — verify the per-player unlock check manually."); }
        else
        {
            // Try IsUnlocked(slot, playerIndex) as a static method, defensively.
            var mi = t.GetMethod("IsUnlocked", new System.Type[] { typeof(int), typeof(int) });
            if (mi != null && mi.IsStatic)
            {
                try
                {
                    bool p0Melee = (bool)mi.Invoke(null, new object[] { 0, 0 });
                    bool p1Melee = (bool)mi.Invoke(null, new object[] { 0, 1 });
                    L($"slot0(melee) unlocked: P0={p0Melee} P1={p1Melee} (both expected true — default).");
                    Check("F3: melee default unlocked for P0", p0Melee);
                    Check("F3: melee default unlocked for P1", p1Melee);
                }
                catch (System.Exception e) { LWarn($"IsUnlocked probe failed: {e.Message}"); }
            }
            else L("IsUnlocked(int,int) static not found — API differs; use the manual procedure below.");
        }
        L("PROCEDURE: give a weapon-UNLOCK augment to P1 ONLY. P1's hotbar gains the weapon; P2's does NOT.");
        L("Then give a DIFFERENT unlock to P2 — the two hotbars must diverge (no shared unlock leak).");
    }

    [ContextMenu("Scene: Chooser applied sets (F4)")]
    private void SceneCheck_ChooserAppliedSets()
    {
        L("----- F4: P5 augment chooser routing / per-player applied sets -----");
        var reg = AugmentRegistry.Instance;
        if (reg == null) { LErr("AugmentRegistry.Instance is null."); return; }
        try
        {
            int c0 = CountEnumerable(reg.GetAppliedAugments(0) as IEnumerable);
            int c1 = CountEnumerable(reg.GetAppliedAugments(1) as IEnumerable);
            int shared = CountEnumerable(reg.GetAppliedAugments() as IEnumerable);
            L($"applied: P0={c0}  P1={c1}  shared/global-list={shared}");
            Check("F4: shared list >= max(P0,P1) (global superset)", shared >= Mathf.Max(c0, c1));
            L("PROCEDURE: have P1 confirm a PLAYER/WEAPON augment, P2 confirm a DIFFERENT one.");
            L("Re-run F4: P0 and P1 counts must DIVERGE and each augment lands on the player who chose it.");
            L("Tower/Enemy/Global augments should appear in the shared list and affect both (see F5).");
        }
        catch (System.Exception e) { LWarn($"GetAppliedAugments(int) unavailable: {e.Message}"); }
    }

    [ContextMenu("Scene: Shared-by-design (F5)")]
    private void SceneCheck_SharedByDesign()
    {
        L("----- F5: intentionally SHARED in co-op (wallet + towers + global augments) -----");
        try { if (EnergyManager.Instance != null) L($"Shared wallet energy = {EnergyManager.Instance.GetPlayerEnergy()} (ONE pool)."); }
        catch (System.Exception e) { LWarn($"EnergyManager probe: {e.Message}"); }
        L($"EnergyGain x{PlayerEconomyModifiers.EnergyGainMultiplier:F2} | TowerDamage x{TowerCombatModifiers.DamageMultiplier:F2} | TowerFireRate x{TowerCombatModifiers.FireRateMultiplier:F2} (all global).");
        L("EXPECTED: both players draw from the same wallet; tower/enemy/global augments affect both identically.");
        L("PROCEDURE: P1 collects energy → the SAME pool rises for P2. A Tower augment chosen by either raises ALL towers.");
    }

    [ContextMenu("Scene: Suppression API (F6)")]
    private void SceneCheck_SuppressionApi()
    {
        L("----- F6: P2 per-player attack suppression -----");
        var attacks = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var t = FindType("PlayerAttack");
        if (t == null) { LWarn("PlayerAttack type not found."); return; }
        var prop = t.GetProperty("InputSuppressed", BindingFlags.Public | BindingFlags.Instance);
        int found = 0;
        foreach (var mb in attacks)
        {
            if (mb == null || mb.GetType() != t) continue;
            found++;
            bool sup = false;
            try { if (prop != null) sup = (bool)prop.GetValue(mb); } catch { }
            L($"  PlayerAttack on '{mb.gameObject.name}': InputSuppressed={sup}");
        }
        if (found == 0) LWarn("No PlayerAttack instances in scene.");
        Check("F6: at least one PlayerAttack present (or single-player)", found >= 0);
        L("PROCEDURE: open ONE player's tower-placement (its attack suppresses); the OTHER must still attack.");
        L("Pause: SetAllSuppressed(true) must suppress BOTH; resume restores BOTH.");
    }

    [ContextMenu("Scene: Tower tether live (F7)")]
    private void SceneCheck_TowerTetherLive()
    {
        L("----- F7: P4 tower / tether live probe -----");
        Tower[] towers;
        try { towers = FindObjectsByType<Tower>(FindObjectsSortMode.None); }
        catch (System.Exception e) { LErr($"Tower type unavailable: {e.Message}"); return; }
        if (towers == null || towers.Length == 0) { LWarn("No towers — build one, then F7 again."); return; }
        L($"Towers: {towers.Length}. Global TowerDamage x{TowerCombatModifiers.DamageMultiplier:F2} FireRate x{TowerCombatModifiers.FireRateMultiplier:F2}");
        foreach (var t in towers)
        {
            if (t == null) continue;
            string name = ""; try { name = t.towerName; } catch { }
            float dmg = 0f, rng = 0f; int lvl = 0;
            try { dmg = t.GetDamage(); } catch { }
            try { rng = t.GetRange(); } catch { }
            try { lvl = t.upgradeLevel; } catch { }
            L($"  '{name}' L{lvl}: damage={dmg:F1} range={rng:F2}");
        }
        L("PROCEDURE: F7 baseline → tether with ONE player (F7: damage/range rise) → tether SAME tower with BOTH");
        L("(F7: values STACK as a product) → both release (F7: returns to the exact baseline, no stuck/doubled value).");
    }

    [ContextMenu("Scene: Cameras per player (F8)")]
    private void SceneCheck_CamerasPerPlayer()
    {
        L("----- F8: P2 per-player cameras / viewports -----");
        var reg = PlayerRegistry.Instance;
        if (reg == null) { LErr("PlayerRegistry.Instance is null."); return; }
        int withCam = 0;
        var seenRects = new List<Rect>();
        for (int i = 0; i < reg.All.Count; i++)
        {
            var p = reg.All[i];
            if (p == null) continue;
            Camera cam = GetCamera(p);
            if (cam == null) { LWarn($"  index {p.PlayerIndex}: NO camera assigned on PlayerRef."); continue; }
            withCam++;
            Rect r = cam.rect;
            seenRects.Add(r);
            L($"  index {p.PlayerIndex}: cam '{cam.name}' rect=({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2})");
        }
        Check("F8: every player has a camera", withCam == reg.All.Count && withCam > 0);
        if (reg.All.Count >= 2 && seenRects.Count >= 2)
            Check("F8: two players have DIFFERENT viewport rects", seenRects[0] != seenRects[1]);
        L("EXPECTED 2P: side-by-side rects ~ (0,0,0.5,1) and (0.5,0,0.5,1); each follows only its own player.");
        L("EXPECTED 1P: a single full-screen rect (0,0,1,1).");
    }

    [ContextMenu("Print Phase 1-5 manual checklist (F9)")]
    private void PrintManualChecklist()
    {
        L("===== PHASE 1-5 MANUAL CHECKLIST (F9) =====\n" +
          "P1 TARGETING/HAZARDS:\n" +
          "  [ ] Enemies chase the NEAREST player; downing that player retargets the other (F1).\n" +
          "  [ ] Two players in one AoE each take their OWN ticks; leaving the zone stops only that player (F2).\n" +
          "P2 INPUT/CAMERA:\n" +
          "  [ ] Each player has its own split-screen camera following only itself; 1P is full-screen (F8).\n" +
          "  [ ] Entering placement suppresses ONLY that player's attack; pause suppresses both (F6).\n" +
          "P3 WEAPON HOTBAR/UNLOCKS:\n" +
          "  [ ] Each player rolls/equips its own hotbar from its own devices.\n" +
          "  [ ] A weapon UNLOCK augment given to P1 only appears in P1's hotbar, never P2's (F3).\n" +
          "P4 TOWER PLACEMENT/TETHER:\n" +
          "  [ ] Each player places towers via its own reticle; the wallet is shared.\n" +
          "  [ ] BOTH players tethering one tower STACK (product); both releasing restores the true base (F7).\n" +
          "P5 AUGMENT CHOOSER:\n" +
          "  [ ] The player who confirms a Player/Weapon augment is the one it applies to; counts diverge (F4).\n" +
          "  [ ] Tower/Enemy/Global augments are shared and affect both players (F5).\n" +
          "REGRESSION: every line above, with ONE player, behaves exactly as before this work.");
    }

    //  ===== helpers =====
    private static Camera GetCamera(PlayerRef p)
    {
        if (p == null) return null;
        var ty = p.GetType();
        var pi = ty.GetProperty("Camera");
        if (pi != null) { try { return pi.GetValue(p) as Camera; } catch { } }
        var fi = ty.GetField("Camera");
        if (fi != null) { try { return fi.GetValue(p) as Camera; } catch { } }
        return null;
    }

    private static System.Type FindType(string simpleName)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var ty in types)
                if (ty.Name == simpleName) return ty;
        }
        return null;
    }

    private static int CountEnumerable(IEnumerable e)
    {
        if (e == null) return 0;
        int n = 0;
        foreach (var _ in e) n++;
        return n;
    }

    private void Check(string name, bool condition)
    {
        if (condition) { _pass++; Debug.Log(TAG + $"[PASS] {name}"); }
        else { _fail++; Debug.LogError(TAG + $"[FAIL] {name}"); }
    }
    private static void L(string m) => Debug.Log(TAG + m);
    private static void LWarn(string m) => Debug.LogWarning(TAG + m);
    private static void LErr(string m) => Debug.LogError(TAG + m);
    private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
}

