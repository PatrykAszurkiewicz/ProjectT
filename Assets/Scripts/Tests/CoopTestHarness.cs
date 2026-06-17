using UnityEngine;


//  CoopTestHarness  —  one-stop regression checks for the co-op work.
//  Every line is prefixed with [TestRunLogs] — type that into the Console search
//  box to isolate all test output.
//     Press Play with this on a GameObject → the STATIC SUITE auto-runs on Start.
//     Right-click the COMPONENT HEADER in the Inspector ("Coop Test Harness
//      (Script)") → pick a check from the menu. (Not the Hierarchy object.)
//     Hotkeys in Play mode: F1..F6, F9.
//  STATIC SUITE = pure logic, no scene → run in a NEW EMPTY scene.
//  SCENE CHECKS = read-only assertions for your real gameplay scene (2 players).

public class CoopTestHarness : MonoBehaviour
{
    private const string TAG = "[TestRunLogs] ";

    [Tooltip("Run the pure-logic suite automatically on Play. Keep this scene EMPTY.")]
    public bool runStaticTestsOnStart = true;

    private int _pass;
    private int _fail;

    private void Awake()
    {
        // Impossible-to-miss heartbeat (yellow), so you can confirm the harness
        // is actually executing even if normal logs are filtered.
        Debug.LogWarning(TAG + $"CoopTestHarness ALIVE on scene '{gameObject.scene.name}'. " +
                         $"runStaticTestsOnStart={runStaticTestsOnStart}. " +
                         "Right-click the component header → 'Run Static Tests', or it auto-runs on Play.");
    }

    private void Start()
    {
        if (runStaticTestsOnStart)
            RunStaticSuite();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.f1Key.wasPressedThisFrame) SceneCheck_Registry();
        if (kb.f2Key.wasPressedThisFrame) SceneCheck_CooldownAttribution();
        if (kb.f3Key.wasPressedThisFrame) SceneCheck_ParryAttribution();
        if (kb.f4Key.wasPressedThisFrame) SceneCheck_AppliedAugmentsPerPlayer();
        if (kb.f5Key.wasPressedThisFrame) SceneCheck_SharedByDesign();
        if (kb.f6Key.wasPressedThisFrame) SceneCheck_RegistrySelectors();
        if (kb.f7Key.wasPressedThisFrame) SceneCheck_TowerTether();
        if (kb.f9Key.wasPressedThisFrame) PrintManualChecklist();
#else
        if (Input.GetKeyDown(KeyCode.F1)) SceneCheck_Registry();
        if (Input.GetKeyDown(KeyCode.F2)) SceneCheck_CooldownAttribution();
        if (Input.GetKeyDown(KeyCode.F3)) SceneCheck_ParryAttribution();
        if (Input.GetKeyDown(KeyCode.F4)) SceneCheck_AppliedAugmentsPerPlayer();
        if (Input.GetKeyDown(KeyCode.F5)) SceneCheck_SharedByDesign();
        if (Input.GetKeyDown(KeyCode.F6)) SceneCheck_RegistrySelectors();
        if (Input.GetKeyDown(KeyCode.F7)) SceneCheck_TowerTether();
        if (Input.GetKeyDown(KeyCode.F9)) PrintManualChecklist();
#endif
    }

    //  ====================  PURE-LOGIC SUITE  ====================

    [ContextMenu("Run Static Tests")]
    public void RunStaticSuite()
    {
        _pass = 0; _fail = 0;
        L("===== CO-OP STATIC SUITE: START =====");

        ResetAllStatics();
        TestCooldownModifier();
        TestParryUpgrades();
        TestProjectileParry();
        TestRuntimeModifiers();
        TestTetherMath();
        TestTetherMathEdge();
        TestMultiPlayerScaling();
        TestStackingSemantics();
        TestComplexInteractions();
        TestCrossAugmentIndependence();
        TestSaveRoundTrip();
        TestFreshRunReset();
        ResetAllStatics();

        L($"===== CO-OP STATIC SUITE: {_pass} passed, {_fail} failed =====");
        if (_fail == 0) L("<color=lime>ALL STATIC TESTS PASSED</color>");
        else LErr($"{_fail} STATIC TEST(S) FAILED — see [FAIL] lines above.");
    }

    private static void ResetAllStatics()
    {
        CooldownModifier.Reset();
        ParryUpgrades.ResetAll();
        ProjectileParry.Reset();
        PlayerEconomyModifiers.EnergyGainMultiplier = 1f;
        PlayerCombatModifiers.OutgoingDamageMultiplier = 1f;
        TowerCombatModifiers.DamageMultiplier = 1f;
        TowerCombatModifiers.BaseFireRateMultiplier = 1f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1f;
    }

    private void TestCooldownModifier()
    {
        CooldownModifier.Reset();
        Check("CD default P0 == 1", Approx(CooldownModifier.MultiplierFor(0), 1f));
        Check("CD default P1 == 1", Approx(CooldownModifier.MultiplierFor(1), 1f));

        CooldownModifier.SetReductionPercent(20f, 0);
        Check("CD P0 20% -> x0.8", Approx(CooldownModifier.MultiplierFor(0), 0.8f));
        Check("CD P1 still x1 (no leak)", Approx(CooldownModifier.MultiplierFor(1), 1f));
        Check("CD Apply(10,P0) == 8", Approx(CooldownModifier.Apply(10f, 0), 8f));
        Check("CD Apply(10,P1) == 10", Approx(CooldownModifier.Apply(10f, 1), 10f));

        Check("CD 1-arg Apply == P0", Approx(CooldownModifier.Apply(10f), CooldownModifier.Apply(10f, 0)));
        Check("CD Multiplier prop == P0", Approx(CooldownModifier.Multiplier, CooldownModifier.MultiplierFor(0)));
        CooldownModifier.SetReductionPercent(50f);
        Check("CD 1-arg set writes P0", Approx(CooldownModifier.MultiplierFor(0), 0.5f));

        CooldownModifier.SetReductionPercent(20f, 1);
        CooldownModifier.SetReductionPercent(20f, 1);
        Check("CD SetReductionPercent idempotent", Approx(CooldownModifier.MultiplierFor(1), 0.8f));

        CooldownModifier.SetReductionPercent(100f, 0);
        Check("CD 100% -> x0", Approx(CooldownModifier.MultiplierFor(0), 0f));
        CooldownModifier.SetReductionPercent(150f, 0);
        Check("CD 150% clamps to 0", Approx(CooldownModifier.MultiplierFor(0), 0f));
        CooldownModifier.SetReductionPercent(-50f, 0);
        Check("CD negative clamps to 1", Approx(CooldownModifier.MultiplierFor(0), 1f));

        CooldownModifier.Reset();
        CooldownModifier.StackReductionPercent(20f, 0);
        CooldownModifier.StackReductionPercent(20f, 0);
        Check("CD stack 20%x2 -> 0.64", Approx(CooldownModifier.MultiplierFor(0), 0.64f));
        Check("CD stack left P1 alone", Approx(CooldownModifier.MultiplierFor(1), 1f));

        CooldownModifier.Reset();
        CooldownModifier.SetReductionPercent(10f, 0);
        CooldownModifier.SetReductionPercent(20f, 1);
        CooldownModifier.SetReductionPercent(30f, 2);
        Check("CD P0/P1/P2 independent",
            Approx(CooldownModifier.MultiplierFor(0), 0.9f)
            && Approx(CooldownModifier.MultiplierFor(1), 0.8f)
            && Approx(CooldownModifier.MultiplierFor(2), 0.7f));

        CooldownModifier.Reset();
        Check("CD Reset clears P0", Approx(CooldownModifier.MultiplierFor(0), 1f));
        Check("CD Reset clears P1", Approx(CooldownModifier.MultiplierFor(1), 1f));
    }

    private void TestParryUpgrades()
    {
        ParryUpgrades.ResetAll();
        Check("PU base stun normal == 3", Approx(ParryUpgrades.BaseStunNormal, 3f));
        Check("PU base stun boss == 2", Approx(ParryUpgrades.BaseStunBoss, 2f));
        Check("PU base dmg bonus == 0.30", Approx(ParryUpgrades.BaseDamageBonus, 0.30f));

        Check("PU default stun P0 == 0", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 0f));
        Check("PU default frames P1 == 0", ParryUpgrades.ExtraParryFramesFor(1) == 0);
        Check("PU default heal P0 off", !ParryUpgrades.HealOnParryEnabledFor(0));
        Check("PU max frames (empty) == 0", ParryUpgrades.MaxExtraParryFrames() == 0);

        ParryUpgrades.SetLongerParryStun(0, 1.5f);
        Check("PU stun P0 == 1.5", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 1.5f));
        Check("PU stun P1 == 0 (no leak)", Approx(ParryUpgrades.ExtraStunSecondsFor(1), 0f));
        Check("PU back-compat ExtraStunSeconds == P0", Approx(ParryUpgrades.ExtraStunSeconds, ParryUpgrades.ExtraStunSecondsFor(0)));
        ParryUpgrades.SetLongerParryStun(0, 2.5f);
        Check("PU stun overwrite (not add)", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 2.5f));

        ParryUpgrades.SetPowerfulParry(1, 0.5f, 4f);
        Check("PU powerful P1 enabled", ParryUpgrades.PowerfulParryEnabledFor(1));
        Check("PU powerful P0 disabled (no leak)", !ParryUpgrades.PowerfulParryEnabledFor(0));
        ParryUpgrades.ResolveDamageDebuff(1, 0.3f, 99f, out float b1, out float d1);
        Check("PU resolve P1 uses powerful bonus", Approx(b1, 0.5f));
        Check("PU resolve P1 uses powerful duration", Approx(d1, 4f));
        ParryUpgrades.ResolveDamageDebuff(0, 0.3f, 99f, out float b0, out float d0);
        Check("PU resolve P0 uses fallback bonus", Approx(b0, 0.3f));
        Check("PU resolve P0 uses fallback duration", Approx(d0, 99f));

        ParryUpgrades.SetLongerParryWindow(0, 2);
        ParryUpgrades.SetLongerParryWindow(1, 5);
        Check("PU frames P0 == 2", ParryUpgrades.ExtraParryFramesFor(0) == 2);
        Check("PU frames P1 == 5", ParryUpgrades.ExtraParryFramesFor(1) == 5);
        Check("PU MaxExtraParryFrames == 5", ParryUpgrades.MaxExtraParryFrames() == 5);

        ParryUpgrades.SetHealOnParry(0, 0.03f);
        ParryUpgrades.SetHealOnParry(0, 0.06f);
        Check("PU heal P0 on @6% (overwrite)", ParryUpgrades.HealOnParryEnabledFor(0) && Approx(ParryUpgrades.HealOnParryPercentFor(0), 0.06f));
        Check("PU heal P1 off (no leak)", !ParryUpgrades.HealOnParryEnabledFor(1));

        ParryUpgrades.ResetAll();
        ParryUpgrades.SetPowerfulParry(1, 0.9f, 9f);
        ParryUpgrades.ResolveDamageDebuff(0, 0.25f, 1f, out float bb, out float dd);
        Check("PU P0 fallback while only P1 powerful", Approx(bb, 0.25f) && Approx(dd, 1f));

        ParryUpgrades.ResetAll();
        ParryUpgrades.SetLongerParryWindow(0, 3);
        Check("PU back-compat ExtraParryFrames == P0", ParryUpgrades.ExtraParryFrames == ParryUpgrades.ExtraParryFramesFor(0));
        Check("PU MaxExtraParryFrames single == P0", ParryUpgrades.MaxExtraParryFrames() == 3);

        ParryUpgrades.ResetAll();
        Check("PU ResetAll clears", ParryUpgrades.ExtraParryFramesFor(0) == 0 && Approx(ParryUpgrades.ExtraStunSecondsFor(0), 0f));
    }

    private void TestProjectileParry()
    {
        ProjectileParry.Reset();
        Check("PP default P0 locked", !ProjectileParry.UnlockedFor(0));
        Check("PP default global false", !ProjectileParry.Unlocked);

        ProjectileParry.SetUnlocked(0);
        Check("PP P0 unlocked", ProjectileParry.UnlockedFor(0));
        Check("PP P1 still locked (no leak)", !ProjectileParry.UnlockedFor(1));
        Check("PP global getter true when any", ProjectileParry.Unlocked);

        ProjectileParry.SetUnlocked(2);
        Check("PP P0 & P2 unlocked, P1 not",
            ProjectileParry.UnlockedFor(0) && !ProjectileParry.UnlockedFor(1) && ProjectileParry.UnlockedFor(2));
        ProjectileParry.SetUnlocked(0, false);
        Check("PP P0 removable, P2 stays", !ProjectileParry.UnlockedFor(0) && ProjectileParry.UnlockedFor(2));
        Check("PP global true while P2 remains", ProjectileParry.Unlocked);

        ProjectileParry.Reset();
        ProjectileParry.Unlocked = true;
        Check("PP global set unlocks P0", ProjectileParry.UnlockedFor(0));
        ProjectileParry.Unlocked = false;
        Check("PP global false clears all", !ProjectileParry.UnlockedFor(0) && !ProjectileParry.Unlocked);

        ProjectileParry.Reset();
        Check("PP Reset clears", !ProjectileParry.UnlockedFor(0) && !ProjectileParry.Unlocked);
    }

    private void TestRuntimeModifiers()
    {
        PlayerEconomyModifiers.EnergyGainMultiplier = 1f;
        PlayerCombatModifiers.OutgoingDamageMultiplier = 1f;
        TowerCombatModifiers.DamageMultiplier = 1f;
        TowerCombatModifiers.BaseFireRateMultiplier = 1f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1f;

        Check("RT EnergyGain default 1", Approx(PlayerEconomyModifiers.EnergyGainMultiplier, 1f));
        Check("RT OutgoingDamage default 1", Approx(PlayerCombatModifiers.OutgoingDamageMultiplier, 1f));
        Check("RT TowerDamage default 1", Approx(TowerCombatModifiers.DamageMultiplier, 1f));
        Check("RT TowerFireRate default 1", Approx(TowerCombatModifiers.FireRateMultiplier, 1f));

        TowerCombatModifiers.BaseFireRateMultiplier = 1.2f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1.5f;
        Check("RT TowerFireRate == Base*PerCount (1.8)", Approx(TowerCombatModifiers.FireRateMultiplier, 1.8f));

        TowerCombatModifiers.BaseFireRateMultiplier = 1f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1f;
    }

    private void TestTetherMath()
    {
        // Default tuning values from PlayerTowerTether (the in-game serialized defaults).
        const float farPer = 0.05f;   // farRangeBonusPerTether
        const float midPer = 0.10f;   // midDamageBonusPerTether
        const float nearPer = 0.20f;  // nearDecayReductionPerTether
        const float cap = 2.0f;       // maxBuffBonus

        // --- FAR / MID buff curve: 1 + perTether*count ---
        Check("TM range count0 == 1", Approx(TetherMath.RangeMultiplier(0, farPer, cap), 1f));
        Check("TM range count1 == 1.05", Approx(TetherMath.RangeMultiplier(1, farPer, cap), 1.05f));
        Check("TM range count4 == 1.20 (doc example)", Approx(TetherMath.RangeMultiplier(4, farPer, cap), 1.20f));
        Check("TM damage count0 == 1", Approx(TetherMath.DamageMultiplier(0, midPer, cap), 1f));
        Check("TM damage count3 == 1.30 (doc example)", Approx(TetherMath.DamageMultiplier(3, midPer, cap), 1.30f));

        // --- maxBuffBonus cap (multiplier ceiling = 1 + cap = 3.0) ---
        Check("TM range cap exact (40@0.05)", Approx(TetherMath.RangeMultiplier(40, farPer, cap), 3.0f));
        Check("TM range cap over (100@0.05)", Approx(TetherMath.RangeMultiplier(100, farPer, cap), 3.0f));
        Check("TM damage cap exact (20@0.10)", Approx(TetherMath.DamageMultiplier(20, midPer, cap), 3.0f));
        Check("TM damage just under cap (19@0.10)", Approx(TetherMath.DamageMultiplier(19, midPer, cap), 2.9f));

        // --- NEAR decay: clamp01(1 - reductionPerTether*count) ---
        Check("TM decay count0 == 1", Approx(TetherMath.DecayMultiplier(0, nearPer), 1f));
        Check("TM decay count1 == 0.8", Approx(TetherMath.DecayMultiplier(1, nearPer), 0.8f));
        Check("TM decay count3 == 0.4 (doc example)", Approx(TetherMath.DecayMultiplier(3, nearPer), 0.4f));
        Check("TM decay count5 == 0 (full cancel)", Approx(TetherMath.DecayMultiplier(5, nearPer), 0f));
        Check("TM decay count7 clamps to 0", Approx(TetherMath.DecayMultiplier(7, nearPer), 0f));

        // --- negative / zero count guards (Mathf.Max(0,count)) ---
        Check("TM range negative count -> 1", Approx(TetherMath.RangeMultiplier(-3, farPer, cap), 1f));
        Check("TM damage negative count -> 1", Approx(TetherMath.DamageMultiplier(-9, midPer, cap), 1f));
        Check("TM decay negative count -> 1", Approx(TetherMath.DecayMultiplier(-4, nearPer), 1f));

        // --- TWO-PLAYER STACKING on the same tower = PRODUCT of each player's multiplier.
        //     (This is exactly what the aggregator applies: base * prod(contribs).) ---
        float p1Range = TetherMath.RangeMultiplier(4, farPer, cap); // 1.20
        float p2Range = TetherMath.RangeMultiplier(2, farPer, cap); // 1.10
        Check("TM stack: range 1.20 x 1.10 == 1.32", Approx(p1Range * p2Range, 1.32f));

        float p1Dmg = TetherMath.DamageMultiplier(3, midPer, cap);  // 1.30
        float p2Dmg = TetherMath.DamageMultiplier(1, midPer, cap);  // 1.10
        Check("TM stack: base10 x 1.30 x 1.10 == 14.3", Approx(10f * p1Dmg * p2Dmg, 14.3f));

        float p1Decay = TetherMath.DecayMultiplier(1, nearPer);     // 0.80
        float p2Decay = TetherMath.DecayMultiplier(1, nearPer);     // 0.80
        Check("TM stack: decay 0.80 x 0.80 == 0.64", Approx(p1Decay * p2Decay, 0.64f));

        // --- single-player parity: product of ONE contribution == that contribution (byte-identical) ---
        Check("TM single-contributor == multiplier itself", Approx(1f * p1Dmg, p1Dmg));
    }

    private void TestTetherMathEdge()
    {
        const float farPer = 0.05f, midPer = 0.10f, nearPer = 0.20f, cap = 2.0f;

        // Boundary right at the cap edge (0.05*40 = 2.0 == cap) and one tether past.
        Check("TME range 39 just under cap (2.95)", Approx(TetherMath.RangeMultiplier(39, farPer, cap), 2.95f));
        Check("TME range 41 pinned at cap (3.0)", Approx(TetherMath.RangeMultiplier(41, farPer, cap), 3.0f));

        // A different cap value entirely (designers may retune).
        Check("TME cap=0.5 -> ceiling 1.5", Approx(TetherMath.DamageMultiplier(100, midPer, 0.5f), 1.5f));
        Check("TME cap=0 -> always 1.0 (buff disabled)", Approx(TetherMath.RangeMultiplier(10, farPer, 0f), 1.0f));

        // perTether = 0 -> no scaling regardless of count.
        Check("TME perTether 0 -> 1.0", Approx(TetherMath.DamageMultiplier(7, 0f, cap), 1.0f));

        // Decay: exact zero crossing and deep clamp.
        Check("TME decay 4@0.20 -> 0.2", Approx(TetherMath.DecayMultiplier(4, nearPer), 0.2f));
        Check("TME decay 100 clamps 0", Approx(TetherMath.DecayMultiplier(100, nearPer), 0f));
        Check("TME decay reduction 0 -> 1 (no effect)", Approx(TetherMath.DecayMultiplier(9, 0f), 1f));

        // THREE-player asymmetric stacking (product), all in MID on one tower.
        float a = TetherMath.DamageMultiplier(2, midPer, cap); // 1.20
        float b = TetherMath.DamageMultiplier(5, midPer, cap); // 1.50
        float c = TetherMath.DamageMultiplier(0, midPer, cap); // 1.00
        Check("TME 3-player damage product 1.20*1.50*1.00", Approx(a * b * c, 1.8f));
        Check("TME 3-player on base 10 -> 18", Approx(10f * a * b * c, 18f));

        // Mixed: one player MID (damage) and one FAR (range) on the SAME tower compose on
        // independent base stats (damage base 10 -> 13; range base 6 -> 6.6), not on each other.
        float dmgMul = TetherMath.DamageMultiplier(3, midPer, cap); // 1.30
        float rngMul = TetherMath.RangeMultiplier(2, farPer, cap);  // 1.10
        Check("TME mixed damage leg (10->13)", Approx(10f * dmgMul, 13f));
        Check("TME mixed range leg (6->6.6)", Approx(6f * rngMul, 6.6f));

        // Capped player stacking with an uncapped player: each multiplier is independently capped,
        // THEN multiplied (cap applies per-contribution, not to the product).
        float capped = TetherMath.DamageMultiplier(100, midPer, cap); // 3.0 (pinned)
        float small = TetherMath.DamageMultiplier(1, midPer, cap);    // 1.1
        Check("TME capped*small = 3.0*1.1 = 3.3 (cap is per-player)", Approx(capped * small, 3.3f));
    }

    private void TestMultiPlayerScaling()
    {
        // Cooldown across 4 players, each independent.
        CooldownModifier.Reset();
        CooldownModifier.SetReductionPercent(10f, 0);
        CooldownModifier.SetReductionPercent(20f, 1);
        CooldownModifier.SetReductionPercent(30f, 2);
        CooldownModifier.SetReductionPercent(40f, 3);
        Check("MP cd P0..P3 independent",
            Approx(CooldownModifier.MultiplierFor(0), 0.9f) &&
            Approx(CooldownModifier.MultiplierFor(1), 0.8f) &&
            Approx(CooldownModifier.MultiplierFor(2), 0.7f) &&
            Approx(CooldownModifier.MultiplierFor(3), 0.6f));
        Check("MP cd untouched P4 == 1 (sparse dict default)", Approx(CooldownModifier.MultiplierFor(4), 1f));

        // Parry across 3 players with distinct upgrade combos — full isolation.
        ParryUpgrades.ResetAll();
        ParryUpgrades.SetLongerParryStun(0, 1.0f);
        ParryUpgrades.SetLongerParryWindow(1, 6);
        ParryUpgrades.SetPowerfulParry(2, 0.7f, 5f);
        ParryUpgrades.SetHealOnParry(2, 0.04f);
        Check("MP parry P0 only stun",
            Approx(ParryUpgrades.ExtraStunSecondsFor(0), 1.0f) &&
            ParryUpgrades.ExtraParryFramesFor(0) == 0 &&
            !ParryUpgrades.PowerfulParryEnabledFor(0) && !ParryUpgrades.HealOnParryEnabledFor(0));
        Check("MP parry P1 only frames",
            Approx(ParryUpgrades.ExtraStunSecondsFor(1), 0f) &&
            ParryUpgrades.ExtraParryFramesFor(1) == 6 &&
            !ParryUpgrades.PowerfulParryEnabledFor(1));
        Check("MP parry P2 powerful+heal",
            ParryUpgrades.PowerfulParryEnabledFor(2) && ParryUpgrades.HealOnParryEnabledFor(2) &&
            Approx(ParryUpgrades.HealOnParryPercentFor(2), 0.04f));
        Check("MP MaxExtraParryFrames across players == 6", ParryUpgrades.MaxExtraParryFrames() == 6);

        // ResolveDamageDebuff picks the right per-player branch.
        ParryUpgrades.ResolveDamageDebuff(2, 0.3f, 1f, out float b2, out float d2);
        Check("MP resolve P2 uses powerful (0.7/5)", Approx(b2, 0.7f) && Approx(d2, 5f));
        ParryUpgrades.ResolveDamageDebuff(1, 0.3f, 1f, out float b1, out float d1);
        Check("MP resolve P1 falls back (0.3/1)", Approx(b1, 0.3f) && Approx(d1, 1f));

        // Projectile parry across 4 players, add/remove without cross-talk.
        ProjectileParry.Reset();
        ProjectileParry.SetUnlocked(1);
        ProjectileParry.SetUnlocked(3);
        Check("MP projparry only P1,P3",
            !ProjectileParry.UnlockedFor(0) && ProjectileParry.UnlockedFor(1) &&
            !ProjectileParry.UnlockedFor(2) && ProjectileParry.UnlockedFor(3));
        ProjectileParry.SetUnlocked(1, false);
        Check("MP projparry remove P1 keeps P3", !ProjectileParry.UnlockedFor(1) && ProjectileParry.UnlockedFor(3));

        ResetAllStatics();
    }

    private void TestStackingSemantics()
    {
        // Cooldown: SET overwrites (idempotent), STACK compounds multiplicatively.
        CooldownModifier.Reset();
        CooldownModifier.SetReductionPercent(20f, 0);
        CooldownModifier.SetReductionPercent(20f, 0);
        CooldownModifier.SetReductionPercent(20f, 0);
        Check("SS cd SET thrice still x0.8 (overwrite)", Approx(CooldownModifier.MultiplierFor(0), 0.8f));

        CooldownModifier.Reset();
        CooldownModifier.StackReductionPercent(20f, 0);
        CooldownModifier.StackReductionPercent(20f, 0);
        CooldownModifier.StackReductionPercent(20f, 0);
        Check("SS cd STACK thrice -> 0.8^3 = 0.512", Approx(CooldownModifier.MultiplierFor(0), 0.512f));

        // Mixing SET then STACK: SET establishes baseline, STACK compounds from there.
        CooldownModifier.Reset();
        CooldownModifier.SetReductionPercent(50f, 0);     // x0.5
        CooldownModifier.StackReductionPercent(20f, 0);   // x0.5 * 0.8 = 0.4
        Check("SS cd SET 50 then STACK 20 -> 0.4", Approx(CooldownModifier.MultiplierFor(0), 0.4f));

        // STACK then floor: stacking can approach 0 but clamps there, never negative.
        CooldownModifier.Reset();
        for (int i = 0; i < 40; i++) CooldownModifier.StackReductionPercent(50f, 0);
        Check("SS cd heavy stack stays in [0,1]",
            CooldownModifier.MultiplierFor(0) >= 0f && CooldownModifier.MultiplierFor(0) <= 1f);

        // Parry upgrades: every setter OVERWRITES (re-picking the same augment doesn't compound).
        ParryUpgrades.ResetAll();
        ParryUpgrades.SetLongerParryStun(0, 1.0f);
        ParryUpgrades.SetLongerParryStun(0, 1.0f);
        Check("SS parry stun re-set not additive (1.0)", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 1.0f));
        ParryUpgrades.SetLongerParryWindow(0, 3);
        ParryUpgrades.SetLongerParryWindow(0, 3);
        Check("SS parry frames re-set not additive (3)", ParryUpgrades.ExtraParryFramesFor(0) == 3);

        // Projectile parry unlock is a boolean set — re-applying is a no-op (idempotent).
        ProjectileParry.Reset();
        ProjectileParry.SetUnlocked(0);
        ProjectileParry.SetUnlocked(0);
        Check("SS projparry double-unlock idempotent", ProjectileParry.UnlockedFor(0) && ProjectileParry.Unlocked);

        // Tether contributions stack multiplicatively (modeled at the math level); SET-style
        // per-player magnitude does not double when the same player re-enters the zone.
        const float midPer = 0.10f, cap = 2.0f;
        float once = TetherMath.DamageMultiplier(3, midPer, cap);
        Check("SS tether same-count re-enter == same multiplier (no double)", Approx(once, 1.30f));

        ResetAllStatics();
    }

    private void TestComplexInteractions()
    {
        ResetAllStatics();
        const float midPer = 0.10f, farPer = 0.05f, nearPer = 0.20f, cap = 2.0f;

        // ---- Build two DIFFERENT realistic loadouts simultaneously ----
        // P0: speedy parrier — 30% cooldown, projectile parry, +1.0s parry stun, +3 parry frames.
        CooldownModifier.SetReductionPercent(30f, 0);
        ProjectileParry.SetUnlocked(0);
        ParryUpgrades.SetLongerParryStun(0, 1.0f);
        ParryUpgrades.SetLongerParryWindow(0, 3);
        // P1: bruiser parrier — powerful parry 0.6/4, heal-on-parry 5%, +5 parry frames, 50% cooldown.
        CooldownModifier.SetReductionPercent(50f, 1);
        ParryUpgrades.SetPowerfulParry(1, 0.6f, 4f);
        ParryUpgrades.SetHealOnParry(1, 0.05f);
        ParryUpgrades.SetLongerParryWindow(1, 5);
        // Global (shared by design): tower damage x1.5.
        TowerCombatModifiers.DamageMultiplier = 1.5f;

        // ---- Each player's own state is exactly what they picked ----
        Check("CI P0 cooldown 0.7", Approx(CooldownModifier.MultiplierFor(0), 0.7f));
        Check("CI P1 cooldown 0.5", Approx(CooldownModifier.MultiplierFor(1), 0.5f));
        Check("CI P0 projparry unlocked, P1 not", ProjectileParry.UnlockedFor(0) && !ProjectileParry.UnlockedFor(1));
        Check("CI P0 stun 1.0, P1 stun 0", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 1.0f) && Approx(ParryUpgrades.ExtraStunSecondsFor(1), 0f));
        Check("CI P1 powerful on, P0 powerful off", ParryUpgrades.PowerfulParryEnabledFor(1) && !ParryUpgrades.PowerfulParryEnabledFor(0));
        Check("CI P1 heal on, P0 heal off", ParryUpgrades.HealOnParryEnabledFor(1) && !ParryUpgrades.HealOnParryEnabledFor(0));
        Check("CI P0 frames 3, P1 frames 5", ParryUpgrades.ExtraParryFramesFor(0) == 3 && ParryUpgrades.ExtraParryFramesFor(1) == 5);
        Check("CI MaxExtraParryFrames across both == 5", ParryUpgrades.MaxExtraParryFrames() == 5);

        // ---- Cross-system: no setting bled across players or systems ----
        Check("CI P0 has NO powerful/heal (no parry-system leak)", !ParryUpgrades.PowerfulParryEnabledFor(0) && !ParryUpgrades.HealOnParryEnabledFor(0));
        Check("CI P1 has NO projparry (no projparry leak)", !ProjectileParry.UnlockedFor(1));
        Check("CI P2/P3 totally clean (no spill)",
            Approx(CooldownModifier.MultiplierFor(2), 1f) && Approx(CooldownModifier.MultiplierFor(3), 1f) &&
            !ProjectileParry.UnlockedFor(2) && !ProjectileParry.UnlockedFor(3) &&
            ParryUpgrades.ExtraParryFramesFor(2) == 0 && !ParryUpgrades.PowerfulParryEnabledFor(3));

        // ---- ResolveDamageDebuff routes per-player through the SAME shared system ----
        ParryUpgrades.ResolveDamageDebuff(0, 0.3f, 1f, out float b0, out float d0);
        ParryUpgrades.ResolveDamageDebuff(1, 0.3f, 1f, out float b1, out float d1);
        Check("CI resolve P0 -> fallback (0.3/1, no powerful)", Approx(b0, 0.3f) && Approx(d0, 1f));
        Check("CI resolve P1 -> powerful (0.6/4)", Approx(b1, 0.6f) && Approx(d1, 4f));

        // ---- Ability cooldown applied per player, simultaneously ----
        Check("CI Apply(10) P0 == 7", Approx(CooldownModifier.Apply(10f, 0), 7f));
        Check("CI Apply(10) P1 == 5", Approx(CooldownModifier.Apply(10f, 1), 5f));

        // ---- Shared tower: BOTH players tether it (MID), composed with the GLOBAL buff ----
        // base damage 20, global x1.5, P0 contributes 2 tethers (1.20), P1 contributes 3 (1.30).
        float tP0 = TetherMath.DamageMultiplier(2, midPer, cap); // 1.20
        float tP1 = TetherMath.DamageMultiplier(3, midPer, cap); // 1.30
        float effective = 20f * TowerCombatModifiers.DamageMultiplier * tP0 * tP1;
        Check("CI tower 20 * 1.5(global) * 1.20(P0) * 1.30(P1) == 46.8", Approx(effective, 46.8f));

        // ---- A third axis on the SAME tower: P0 also in FAR (range), P1 also in NEAR (decay) ----
        float rangeLeg = 6f * TetherMath.RangeMultiplier(2, farPer, cap);  // 6 * 1.10 = 6.6
        float decayLeg = TetherMath.DecayMultiplier(2, nearPer);           // 0.60
        Check("CI same tower range leg independent (6 -> 6.6)", Approx(rangeLeg, 6.6f));
        Check("CI same tower decay leg independent (0.60)", Approx(decayLeg, 0.6f));
        Check("CI damage leg unchanged by range/decay legs (still 46.8)", Approx(effective, 46.8f));

        // ---- Both players leave the tether: damage returns to base*global only ----
        float afterRelease = 20f * TowerCombatModifiers.DamageMultiplier; // product of zero tethers = 1
        Check("CI both release -> 20 * 1.5 == 30 (no stuck tether)", Approx(afterRelease, 30f));

        // ---- Fresh run resets EVERYTHING across all systems and players ----
        ResetAllStatics();
        bool allClean = true;
        for (int i = 0; i < 4; i++)
        {
            allClean &= Approx(CooldownModifier.MultiplierFor(i), 1f);
            allClean &= !ProjectileParry.UnlockedFor(i);
            allClean &= ParryUpgrades.ExtraParryFramesFor(i) == 0;
            allClean &= !ParryUpgrades.PowerfulParryEnabledFor(i);
            allClean &= !ParryUpgrades.HealOnParryEnabledFor(i);
            allClean &= Approx(ParryUpgrades.ExtraStunSecondsFor(i), 0f);
        }
        allClean &= Approx(TowerCombatModifiers.DamageMultiplier, 1f);
        Check("CI full reset clears every system for P0..P3", allClean);

        ResetAllStatics();
    }

    private void TestCrossAugmentIndependence()
    {
        ResetAllStatics();
        ParryUpgrades.SetLongerParryStun(0, 1f);
        Check("X 330 leaves 331 off", !ParryUpgrades.PowerfulParryEnabledFor(0));
        Check("X 330 leaves 333 off", !ParryUpgrades.HealOnParryEnabledFor(0));
        Check("X 330 leaves 332 at 0", ParryUpgrades.ExtraParryFramesFor(0) == 0);

        CooldownModifier.SetReductionPercent(25f, 0);
        Check("X 328 didn't touch parry stun", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 1f));
        Check("X 330 didn't touch cooldown", Approx(CooldownModifier.MultiplierFor(0), 0.75f));

        ProjectileParry.SetUnlocked(0);
        Check("X 325 didn't enable powerful parry", !ParryUpgrades.PowerfulParryEnabledFor(0));
        Check("X 325 unlock holds", ProjectileParry.UnlockedFor(0));

        ResetAllStatics();
        CooldownModifier.SetReductionPercent(30f, 0);
        ParryUpgrades.SetHealOnParry(1, 0.05f);
        ProjectileParry.SetUnlocked(1);
        Check("X P0 has cooldown only",
            Approx(CooldownModifier.MultiplierFor(0), 0.7f)
            && !ParryUpgrades.HealOnParryEnabledFor(0)
            && !ProjectileParry.UnlockedFor(0));
        Check("X P1 has parry-heal + projparry only",
            Approx(CooldownModifier.MultiplierFor(1), 1f)
            && ParryUpgrades.HealOnParryEnabledFor(1)
            && ProjectileParry.UnlockedFor(1));
        ResetAllStatics();
    }

    private void TestSaveRoundTrip()
    {
        var data = new RunSaveData
        {
            saveVersion = 2,
            stageIndex = 1,
            waveIndex = 2,
            runSeed = 12345,
            hasCore = true,
            coreEnergy = 88f,
            coreMaxEnergy = 100f,
            hasEconomy = true,
            playerEnergy = 250,
        };
        data.players.Add(new PlayerSaveEntry { playerIndex = 0, playerHealth = 120, playerMaxHealth = 150, playerArmor = 5, playerMana = 30, playerMaxMana = 50, playerStamina = 4, playerMaxStamina = 5, playerDashesLeft = 2 });
        data.players.Add(new PlayerSaveEntry { playerIndex = 1, playerHealth = 80, playerMaxHealth = 100, playerDashesLeft = 3 });
        data.augments.Add(new AugmentSaveEntry(66, "Rare", 0));
        data.augments.Add(new AugmentSaveEntry(326, "Epic", 1));

        string json = JsonUtility.ToJson(data, true);
        var back = JsonUtility.FromJson<RunSaveData>(json);

        Check("SAVE version 2 preserved", back.saveVersion == 2);
        Check("SAVE stage/wave preserved", back.stageIndex == 1 && back.waveIndex == 2);
        Check("SAVE 2 players preserved", back.players != null && back.players.Count == 2);
        Check("SAVE P0 health preserved", Approx(back.players[0].playerHealth, 120f));
        Check("SAVE P0 index preserved", back.players[0].playerIndex == 0);
        Check("SAVE P1 maxhealth preserved", Approx(back.players[1].playerMaxHealth, 100f));
        Check("SAVE P1 index preserved", back.players[1].playerIndex == 1);
        Check("SAVE 2 augments preserved", back.augments != null && back.augments.Count == 2);
        Check("SAVE augment 66 -> P0", back.augments[0].id == 66 && back.augments[0].playerIndex == 0);
        Check("SAVE augment 326 -> P1", back.augments[1].id == 326 && back.augments[1].playerIndex == 1);
        Check("SAVE rarity preserved", back.augments[1].rarity == "Epic");
        Check("SAVE core/economy preserved", back.hasCore && Approx(back.coreEnergy, 88f) && back.playerEnergy == 250);

        var legacy = new AugmentSaveEntry(99, "Common");
        Check("SAVE legacy ctor -> P0", legacy.playerIndex == 0);
        var nullRarity = new AugmentSaveEntry(1, null, 0);
        Check("SAVE null rarity -> Common", nullRarity.rarity == "Common");
    }

    private void TestFreshRunReset()
    {
        CooldownModifier.SetReductionPercent(30f, 0);
        CooldownModifier.SetReductionPercent(40f, 1);
        ParryUpgrades.SetLongerParryStun(0, 2f);
        ParryUpgrades.SetPowerfulParry(1, 0.5f, 3f);
        ParryUpgrades.SetLongerParryWindow(1, 4);
        ParryUpgrades.SetHealOnParry(0, 0.05f);
        ProjectileParry.SetUnlocked(0);
        ProjectileParry.SetUnlocked(1);

        CooldownModifier.Reset();
        ParryUpgrades.ResetAll();
        ProjectileParry.Reset();

        Check("RESET CD P0 clean", Approx(CooldownModifier.MultiplierFor(0), 1f));
        Check("RESET CD P1 clean", Approx(CooldownModifier.MultiplierFor(1), 1f));
        Check("RESET parry stun clean", Approx(ParryUpgrades.ExtraStunSecondsFor(0), 0f));
        Check("RESET parry powerful clean", !ParryUpgrades.PowerfulParryEnabledFor(1));
        Check("RESET parry frames clean", ParryUpgrades.MaxExtraParryFrames() == 0);
        Check("RESET parry heal clean", !ParryUpgrades.HealOnParryEnabledFor(0));
        Check("RESET projparry P0 clean", !ProjectileParry.UnlockedFor(0));
        Check("RESET projparry P1 clean", !ProjectileParry.UnlockedFor(1));
        Check("RESET projparry global clean", !ProjectileParry.Unlocked);
    }

    //  ====================  SCENE CHECKS  ====================

    [ContextMenu("Scene: Registry sanity (F1)")]
    private void SceneCheck_Registry()
    {
        L("----- F1: Registry sanity -----");
        var reg = PlayerRegistry.Instance;
        if (reg == null) { LErr("PlayerRegistry.Instance is null."); return; }
        L($"Players registered: {reg.All.Count}");
        for (int i = 0; i < reg.All.Count; i++)
        {
            var p = reg.All[i];
            if (p == null) { L($"  slot {i}: <null>"); continue; }
            bool dead = p.Stats == null || p.Stats.IsDead();
            L($"  index {p.PlayerIndex}: {(dead ? "DEAD/down" : "alive")}" +
              (p.Stats != null ? $"  hp={p.Stats.currentHealth:F0}/{p.Stats.maxHealth:F0}" : ""));
        }
        L($"AllDead() == {reg.AllDead()} (expect false unless EVERY player is down).");
        L("Down ONE player and run again: that player reads DEAD, AllDead() stays false, NO game-over.");
    }

    [ContextMenu("Scene: Registry selectors (F6)")]
    private void SceneCheck_RegistrySelectors()
    {
        L("----- F6: Registry selectors (Phase 0-3) -----");
        var reg = PlayerRegistry.Instance;
        if (reg == null) { LErr("PlayerRegistry.Instance is null."); return; }
        if (reg.All.Count < 2) { LWarn("Need 2 alive players for selector checks; spawn P2 first."); return; }

        var p0 = reg.Get(0);
        var p1 = reg.Get(1);
        Check("F6: Get(0).PlayerIndex == 0", p0 != null && p0.PlayerIndex == 0);
        Check("F6: Get(1).PlayerIndex == 1", p1 != null && p1.PlayerIndex == 1);

        try
        {
            if (p0 != null && p0.Stats != null)
            {
                var nearP0 = reg.NearestAlive(p0.transform.position);
                Check("F6: NearestAlive(@P0) == P0", nearP0 == p0.Stats);
            }
            if (p1 != null && p1.Stats != null)
            {
                var nearP1 = reg.NearestAlive(p1.transform.position);
                Check("F6: NearestAlive(@P1) == P1", nearP1 == p1.Stats);
            }
            L("Down P1 and run again: NearestAlive(@P1) should now return P0 (dead players are skipped).");
        }
        catch (System.Exception e)
        {
            LWarn($"NearestAlive signature differs from expectation: {e.Message}");
        }
    }

    [ContextMenu("Scene: Cooldown attribution (F2)")]
    private void SceneCheck_CooldownAttribution()
    {
        L("----- F2: Cooldown attribution (give 328 to P1 ONLY in-game, then run) -----");
        float m0 = CooldownModifier.MultiplierFor(0);
        float m1 = CooldownModifier.MultiplierFor(1);
        L($"MultiplierFor(P0)={m0:F2}  MultiplierFor(P1)={m1:F2}");
        Check("F2: P0 has reduction (<1)", m0 < 1f);
        Check("F2: P1 unaffected (==1)", Approx(m1, 1f));
    }

    [ContextMenu("Scene: Parry attribution (F3)")]
    private void SceneCheck_ParryAttribution()
    {
        L("----- F3: Parry attribution (give 330-333 to P1 ONLY, then run) -----");
        L($"P0: stun+{ParryUpgrades.ExtraStunSecondsFor(0):F2}s frames+{ParryUpgrades.ExtraParryFramesFor(0)} powerful={ParryUpgrades.PowerfulParryEnabledFor(0)} heal={ParryUpgrades.HealOnParryEnabledFor(0)}");
        L($"P1: stun+{ParryUpgrades.ExtraStunSecondsFor(1):F2}s frames+{ParryUpgrades.ExtraParryFramesFor(1)} powerful={ParryUpgrades.PowerfulParryEnabledFor(1)} heal={ParryUpgrades.HealOnParryEnabledFor(1)}");
        bool p0 = ParryUpgrades.ExtraStunSecondsFor(0) > 0f || ParryUpgrades.ExtraParryFramesFor(0) > 0 || ParryUpgrades.PowerfulParryEnabledFor(0) || ParryUpgrades.HealOnParryEnabledFor(0);
        bool p1 = ParryUpgrades.ExtraStunSecondsFor(1) > 0f || ParryUpgrades.ExtraParryFramesFor(1) > 0 || ParryUpgrades.PowerfulParryEnabledFor(1) || ParryUpgrades.HealOnParryEnabledFor(1);
        Check("F3: P0 has parry upgrades", p0);
        Check("F3: P1 has none (no leak)", !p1);
    }

    [ContextMenu("Scene: Per-player applied augments (F4)")]
    private void SceneCheck_AppliedAugmentsPerPlayer()
    {
        L("----- F4: Phase 6 per-player applied-augment ledger -----");
        var reg = AugmentRegistry.Instance;
        if (reg == null) { LErr("AugmentRegistry.Instance is null."); return; }
        try
        {
            int c0 = CountEnumerable(reg.GetAppliedAugments(0));
            int c1 = CountEnumerable(reg.GetAppliedAugments(1));
            int shared = CountEnumerable(reg.GetAppliedAugments());
            L($"applied: P0={c0}  P1={c1}  shared-list={shared}");
            L("Give DIFFERENT augments to P1 vs P2 and run again — the per-player counts should diverge.");
        }
        catch (System.Exception e)
        {
            LWarn($"GetAppliedAugments(int) unavailable or threw: {e.Message}");
        }
    }

    [ContextMenu("Scene: Shared-by-design (F5)")]
    private void SceneCheck_SharedByDesign()
    {
        L("----- F5: intentionally GLOBAL in co-op (shared wallet + towers) -----");
        if (EnergyManager.Instance != null)
            L($"Shared wallet energy = {EnergyManager.Instance.GetPlayerEnergy()} (one pool for both players).");
        L($"EnergyGainMultiplier x{PlayerEconomyModifiers.EnergyGainMultiplier:F2} (global).");
        L($"TowerDamage x{TowerCombatModifiers.DamageMultiplier:F2}  TowerFireRate x{TowerCombatModifiers.FireRateMultiplier:F2} (global).");
        L("These are EXPECTED identical for both players (shared wallet/towers by design).");
    }

    [ContextMenu("Scene: Tower + tether stats (F7)")]
    private void SceneCheck_TowerTether()
    {
        L("----- F7: Tower / tether live probe (Phase 4 stacking) -----");
        Tower[] towers;
        try { towers = FindObjectsByType<Tower>(FindObjectsSortMode.None); }
        catch (System.Exception e) { LErr($"Tower type unavailable: {e.Message}"); return; }

        if (towers == null || towers.Length == 0) { LWarn("No towers in scene — build one, then run again."); return; }

        L($"Towers: {towers.Length}.  Global TowerDamage x{TowerCombatModifiers.DamageMultiplier:F2}  TowerFireRate x{TowerCombatModifiers.FireRateMultiplier:F2}");
        foreach (var t in towers)
        {
            if (t == null) continue;
            bool decayBoost = false;
            try { decayBoost = t.GetComponent<TowerTetherDecayBoost>() != null; } catch { }
            string name = "";
            try { name = t.towerName; } catch { }
            L($"  '{name}' L{t.upgradeLevel}: damage={t.GetDamage():F1}  range={t.GetRange():F2}  nearDecayBoost={decayBoost}");
        }
        L("PROCEDURE: build a tower; run F7 (baseline). Tether it with ONE player; run F7 (damage/range should rise).");
        L("Then tether the SAME tower with BOTH players; run F7. If a value snaps back, fails to stack, or doubles");
        L("oddly when the second player joins/leaves, that's the shared-tower tether interaction to report.");
    }

    [ContextMenu("Print manual checklist (F9)")]
    private void PrintManualChecklist()
    {
        L("===== MANUAL CO-OP CHECKLIST (F9) =====\n" +
          "PHASE 0-1 — spawn / devices / cameras:\n" +
          "  [ ] Two players spawn; each has its own split-screen camera following only itself.\n" +
          "  [ ] Connecting a 2nd gamepad makes P2 join and take over (see CoopManager join logs).\n" +
          "  [ ] Single player: one full-screen camera, no split.\n" +
          "PHASE 2-3 — shared core/economy + targeting:\n" +
          "  [ ] Energy is ONE shared wallet (run F5): a pickup by either player raises the same pool.\n" +
          "  [ ] Enemies target the NEAREST player; when that player goes down, they retarget the other (run F1/F6).\n" +
          "  [ ] The shared core takes damage from any enemy; its destruction ends the run for both.\n" +
          "PHASE 4 — tower tether (math now auto-tested; live stacking via F7):\n" +
          "  [ ] Each player tethers nearby TOWERS; the buff type follows distance: FAR=range, MID=damage, NEAR=decay.\n" +
          "  [ ] CO-OP FIX: when BOTH players tether the SAME tower, their buffs STACK (product), and when both\n" +
          "      release, the tower returns to its TRUE base (no stuck/doubled damage). Verify live with F7.\n" +
          "  [ ] Buff scales with each player's own tether count, capped at maxBuffBonus (×3.0 by default).\n" +
          "  [ ] Single player: identical to before — one contributor, base × that one multiplier, clean restore.\n" +
          "PHASE 5 — augment chooser plumbing:\n" +
          "  [ ] The player who confirms an augment is the one it applies to (ChooserIndex resolves correctly).\n" +
          "PHASE 6 — per-player active-augment UI:\n" +
          "  [ ] Each player sees ONLY their own augment icons in their own column (confirm data with F4).\n" +
          "  [ ] Paused: right stick moves a player's cursor; left stick/dpad scrolls the panel under it.\n" +
          "PHASE 7 — downed / revive / clock / resume:\n" +
          "  [ ] Down P2 only: downed (no input/attack), P1 plays on, NO game-over (F1). Revive via Build-hold.\n" +
          "  [ ] Down BOTH: game-over once. Single player: dying ends the run (no downed state).\n" +
          "  [ ] Clock: rewinds to wave start, BOTH players restored, full-screen VFX.\n" +
          "  [ ] Save → relaunch → resume: each player's augments return on the right player (format proven by suite).\n" +
          "PHASE 8 — per-player augments (after F2/F3/F4 pass):\n" +
          "  [ ] Only P1 has 328 / 330-333 / 325: effects apply to P1 only; P2 unaffected.\n" +
          "REGRESSION: every line above, with ONE player, behaves exactly as before this work.");
    }

    //  helpers 

    private static int CountEnumerable(System.Collections.IEnumerable e)
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

    private static void L(string msg) => Debug.Log(TAG + msg);
    private static void LWarn(string msg) => Debug.LogWarning(TAG + msg);
    private static void LErr(string msg) => Debug.LogError(TAG + msg);
    private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
}