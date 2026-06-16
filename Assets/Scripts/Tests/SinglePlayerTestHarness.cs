using System.Reflection;
using UnityEngine;

/// Single-player regression harness. Companion to CoopTestHarness — where that one proves
/// per-player ISOLATION, this one proves SINGLE-PLAYER PARITY: with exactly one player, every
/// back-compat overload behaves identically to the indexed player-0 path, nothing leaks into
/// other slots, and the systems match their pre-co-op behavior.
/// USAGE: drop on a GameObject in an EMPTY scene and press Play — the static suite auto-runs,
/// no clicks. Scene checks (F1, F2) are optional and meant for your real single-player scene.
/// Every line is tagged [SPTestRunLogs] for Console filtering.

public class SinglePlayerTestHarness : MonoBehaviour
{
    private const string TAG = "[SPTestRunLogs] ";

    [Tooltip("Run the pure-logic single-player parity suite on Play. Keep this scene EMPTY.")]
    public bool runStaticTestsOnStart = true;

    private int _pass, _fail;

    private void Awake()
    {
        Debug.LogWarning(TAG + $"SinglePlayerTestHarness ALIVE on scene '{gameObject.scene.name}'. " +
                         $"runStaticTestsOnStart={runStaticTestsOnStart}.");
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
        if (kb.f1Key.wasPressedThisFrame) SceneCheck_SinglePlayerRegistry();
        if (kb.f2Key.wasPressedThisFrame) SceneCheck_BarsHiddenForAbsentPlayers();
#else
        if (Input.GetKeyDown(KeyCode.F1)) SceneCheck_SinglePlayerRegistry();
        if (Input.GetKeyDown(KeyCode.F2)) SceneCheck_BarsHiddenForAbsentPlayers();
#endif
    }

    [ContextMenu("Run Single-Player Static Tests")]
    public void RunStaticSuite()
    {
        _pass = 0; _fail = 0;
        L("===== SINGLE-PLAYER PARITY SUITE: START =====");

        ResetAll();
        SP_CooldownParity();
        SP_ParryParity();
        SP_ProjectileParryParity();
        SP_TetherSingleContributor();
        SP_RuntimeModifierDefaults();
        SP_SaveRoundTripOnePlayer();
        SP_NoLeakIntoOtherSlots();
        SP_ResetClean();
        ResetAll();

        L($"===== SINGLE-PLAYER PARITY SUITE: {_pass} passed, {_fail} failed =====");
        if (_fail == 0) L("<color=lime>ALL SINGLE-PLAYER TESTS PASSED</color>");
        else LErr($"{_fail} SINGLE-PLAYER TEST(S) FAILED — see [FAIL] lines above.");
    }

    private static void ResetAll()
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

    private void SP_CooldownParity()
    {
        CooldownModifier.Reset();
        // Single-arg (back-compat) write must land on player 0 and match the indexed read.
        CooldownModifier.SetReductionPercent(25f);
        Check("SP cd 1-arg set == indexed P0", Approx(CooldownModifier.MultiplierFor(0), 0.75f));
        Check("SP cd Multiplier prop == MultiplierFor(0)", Approx(CooldownModifier.Multiplier, CooldownModifier.MultiplierFor(0)));
        Check("SP cd 1-arg Apply == indexed Apply(.,0)", Approx(CooldownModifier.Apply(8f), CooldownModifier.Apply(8f, 0)));
        Check("SP cd Apply(8) == 6", Approx(CooldownModifier.Apply(8f), 6f));

        // A single player must never touch any other slot.
        Check("SP cd P1 untouched", Approx(CooldownModifier.MultiplierFor(1), 1f));
        Check("SP cd P5 untouched", Approx(CooldownModifier.MultiplierFor(5), 1f));

        // Stack back-compat overload also targets player 0.
        CooldownModifier.Reset();
        CooldownModifier.StackReductionPercent(20f);
        CooldownModifier.StackReductionPercent(20f);
        Check("SP cd 1-arg stack -> 0.64 on P0", Approx(CooldownModifier.MultiplierFor(0), 0.64f));
        Check("SP cd stack left P1 alone", Approx(CooldownModifier.MultiplierFor(1), 1f));

        CooldownModifier.Reset();
        Check("SP cd default 1.0", Approx(CooldownModifier.Multiplier, 1f));
    }

    private void SP_ParryParity()
    {
        ParryUpgrades.ResetAll();

        // Back-compat properties must mirror the indexed player-0 accessors at all times.
        ParryUpgrades.SetLongerParryStun(0, 1.25f);
        ParryUpgrades.SetLongerParryWindow(0, 4);
        ParryUpgrades.SetPowerfulParry(0, 0.45f, 3.5f);
        ParryUpgrades.SetHealOnParry(0, 0.05f);

        Check("SP parry ExtraStunSeconds == For(0)", Approx(ParryUpgrades.ExtraStunSeconds, ParryUpgrades.ExtraStunSecondsFor(0)));
        Check("SP parry ExtraParryFrames == For(0)", ParryUpgrades.ExtraParryFrames == ParryUpgrades.ExtraParryFramesFor(0));
        Check("SP parry PowerfulParryEnabled == For(0)", ParryUpgrades.PowerfulParryEnabled == ParryUpgrades.PowerfulParryEnabledFor(0));
        Check("SP parry PowerfulDamageBonus == For(0)", Approx(ParryUpgrades.PowerfulParryDamageBonus, ParryUpgrades.PowerfulParryDamageBonusFor(0)));
        Check("SP parry PowerfulDuration == For(0)", Approx(ParryUpgrades.PowerfulParryDuration, ParryUpgrades.PowerfulParryDurationFor(0)));
        Check("SP parry HealOnParryEnabled == For(0)", ParryUpgrades.HealOnParryEnabled == ParryUpgrades.HealOnParryEnabledFor(0));
        Check("SP parry HealOnParryPercent == For(0)", Approx(ParryUpgrades.HealOnParryPercent, ParryUpgrades.HealOnParryPercentFor(0)));

        // Values are what we set.
        Check("SP parry stun value 1.25", Approx(ParryUpgrades.ExtraStunSeconds, 1.25f));
        Check("SP parry frames value 4", ParryUpgrades.ExtraParryFrames == 4);
        Check("SP parry MaxExtraParryFrames == 4", ParryUpgrades.MaxExtraParryFrames() == 4);

        // No leak to P1.
        Check("SP parry P1 clean stun", Approx(ParryUpgrades.ExtraStunSecondsFor(1), 0f));
        Check("SP parry P1 clean frames", ParryUpgrades.ExtraParryFramesFor(1) == 0);
        Check("SP parry P1 clean powerful", !ParryUpgrades.PowerfulParryEnabledFor(1));

        // Back-compat ResolveDamageDebuff (no index) must equal indexed player-0 resolve.
        ParryUpgrades.ResolveDamageDebuff(0.3f, 1f, out float b0, out float d0);
        ParryUpgrades.ResolveDamageDebuff(0, 0.3f, 1f, out float bi, out float di);
        Check("SP resolve back-compat bonus == indexed P0", Approx(b0, bi));
        Check("SP resolve back-compat duration == indexed P0", Approx(d0, di));
        Check("SP resolve uses powerful (0.45/3.5)", Approx(b0, 0.45f) && Approx(d0, 3.5f));

        // Base constants unchanged.
        Check("SP parry base stun normal 3", Approx(ParryUpgrades.BaseStunNormal, 3f));
        Check("SP parry base stun boss 2", Approx(ParryUpgrades.BaseStunBoss, 2f));
        Check("SP parry base dmg bonus 0.30", Approx(ParryUpgrades.BaseDamageBonus, 0.30f));

        ParryUpgrades.ResetAll();
    }

    private void SP_ProjectileParryParity()
    {
        ProjectileParry.Reset();
        Check("SP projparry default locked", !ProjectileParry.Unlocked && !ProjectileParry.UnlockedFor(0));

        // Global setter (back-compat) maps to player 0.
        ProjectileParry.Unlocked = true;
        Check("SP projparry global set -> P0 unlocked", ProjectileParry.UnlockedFor(0));
        Check("SP projparry global getter true", ProjectileParry.Unlocked);
        Check("SP projparry P1 still locked", !ProjectileParry.UnlockedFor(1));

        ProjectileParry.Unlocked = false;
        Check("SP projparry global clear -> P0 locked", !ProjectileParry.UnlockedFor(0));
        Check("SP projparry global getter false", !ProjectileParry.Unlocked);

        // Indexed unlock of P0 reflects in the global getter (single player == player 0).
        ProjectileParry.SetUnlocked(0);
        Check("SP projparry indexed P0 -> global true", ProjectileParry.Unlocked);

        ProjectileParry.Reset();
    }

    private void SP_TetherSingleContributor()
    {
        const float farPer = 0.05f, midPer = 0.10f, nearPer = 0.20f, cap = 2.0f;

        // With one player, the aggregator applies the PRODUCT of one contribution == that
        // contribution. So base * singleMultiplier, identical to the old snapshot/apply.
        float dmg = TetherMath.DamageMultiplier(4, midPer, cap); // 1.40
        Check("SP tether single damage 4 tethers == 1.40", Approx(dmg, 1.40f));
        Check("SP tether product-of-one == itself", Approx(1f * dmg, dmg));
        Check("SP tether base 10 -> 14", Approx(10f * dmg, 14f));

        float rng = TetherMath.RangeMultiplier(3, farPer, cap); // 1.15
        Check("SP tether single range 3 tethers == 1.15", Approx(rng, 1.15f));
        Check("SP tether base range 8 -> 9.2", Approx(8f * rng, 9.2f));

        float decay = TetherMath.DecayMultiplier(2, nearPer); // 0.6
        Check("SP tether single decay 2 tethers == 0.6", Approx(decay, 0.6f));

        // Zero tethers -> neutral (no buff), matching idle.
        Check("SP tether 0 count damage neutral", Approx(TetherMath.DamageMultiplier(0, midPer, cap), 1f));
        Check("SP tether 0 count decay neutral", Approx(TetherMath.DecayMultiplier(0, nearPer), 1f));
    }

    private void SP_RuntimeModifierDefaults()
    {
        Check("SP econ energy default 1", Approx(PlayerEconomyModifiers.EnergyGainMultiplier, 1f));
        Check("SP combat outgoing default 1", Approx(PlayerCombatModifiers.OutgoingDamageMultiplier, 1f));
        Check("SP tower damage default 1", Approx(TowerCombatModifiers.DamageMultiplier, 1f));
        Check("SP tower firerate default 1", Approx(TowerCombatModifiers.FireRateMultiplier, 1f));

        // Composition still holds in single player (one player drives the same globals).
        TowerCombatModifiers.BaseFireRateMultiplier = 1.25f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1.6f;
        Check("SP tower firerate compose 1.25*1.6 == 2.0", Approx(TowerCombatModifiers.FireRateMultiplier, 2.0f));
        TowerCombatModifiers.BaseFireRateMultiplier = 1f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1f;
    }

    private void SP_SaveRoundTripOnePlayer()
    {
        var data = new RunSaveData
        {
            saveVersion = 2,
            stageIndex = 0,
            waveIndex = 0,
            runSeed = 7,
            hasCore = true,
            coreEnergy = 100f,
            coreMaxEnergy = 100f,
            hasEconomy = true,
            playerEnergy = 120,
        };
        data.players.Add(new PlayerSaveEntry { playerIndex = 0, playerHealth = 95, playerMaxHealth = 100, playerDashesLeft = 1 });
        data.augments.Add(new AugmentSaveEntry(328, "Rare", 0));

        string json = JsonUtility.ToJson(data, true);
        var back = JsonUtility.FromJson<RunSaveData>(json);

        Check("SP save version 2", back.saveVersion == 2);
        Check("SP save exactly 1 player", back.players != null && back.players.Count == 1);
        Check("SP save player index 0", back.players[0].playerIndex == 0);
        Check("SP save player health 95", Approx(back.players[0].playerHealth, 95f));
        Check("SP save exactly 1 augment", back.augments != null && back.augments.Count == 1);
        Check("SP save augment 328 -> P0", back.augments[0].id == 328 && back.augments[0].playerIndex == 0);
        Check("SP save core/economy preserved", back.hasCore && back.playerEnergy == 120);
    }

    private void SP_NoLeakIntoOtherSlots()
    {
        ResetAll();
        // Apply a full single-player loadout to player 0 only.
        CooldownModifier.SetReductionPercent(30f);
        ParryUpgrades.SetLongerParryStun(0, 2f);
        ParryUpgrades.SetLongerParryWindow(0, 5);
        ParryUpgrades.SetPowerfulParry(0, 0.6f, 4f);
        ParryUpgrades.SetHealOnParry(0, 0.06f);
        ProjectileParry.Unlocked = true;

        // Every other slot must remain pristine.
        bool clean = true;
        for (int i = 1; i <= 3; i++)
        {
            clean &= Approx(CooldownModifier.MultiplierFor(i), 1f);
            clean &= Approx(ParryUpgrades.ExtraStunSecondsFor(i), 0f);
            clean &= ParryUpgrades.ExtraParryFramesFor(i) == 0;
            clean &= !ParryUpgrades.PowerfulParryEnabledFor(i);
            clean &= !ParryUpgrades.HealOnParryEnabledFor(i);
            clean &= !ProjectileParry.UnlockedFor(i);
        }
        Check("SP full loadout leaks into NO other slot (P1..P3)", clean);

        // And player 0 actually has everything.
        Check("SP P0 has full loadout",
            Approx(CooldownModifier.MultiplierFor(0), 0.7f) &&
            Approx(ParryUpgrades.ExtraStunSecondsFor(0), 2f) &&
            ParryUpgrades.ExtraParryFramesFor(0) == 5 &&
            ParryUpgrades.PowerfulParryEnabledFor(0) &&
            ParryUpgrades.HealOnParryEnabledFor(0) &&
            ProjectileParry.UnlockedFor(0));

        ResetAll();
    }

    private void SP_ResetClean()
    {
        CooldownModifier.SetReductionPercent(40f);
        ParryUpgrades.SetPowerfulParry(0, 0.5f, 2f);
        ProjectileParry.Unlocked = true;

        CooldownModifier.Reset();
        ParryUpgrades.ResetAll();
        ProjectileParry.Reset();

        Check("SP reset cd clean", Approx(CooldownModifier.Multiplier, 1f));
        Check("SP reset parry clean", !ParryUpgrades.PowerfulParryEnabled && ParryUpgrades.MaxExtraParryFrames() == 0);
        Check("SP reset projparry clean", !ProjectileParry.Unlocked);
    }

    //  ===== scene checks (run in your real single-player scene) =====

    [ContextMenu("Scene: single-player registry (F1)")]
    private void SceneCheck_SinglePlayerRegistry()
    {
        L("----- F1: single-player registry -----");
        L($"PlayerRegistry.Count = {PlayerRegistry.Count} (expect 0 with no registry, or 1 if the single player registers).");
        if (PlayerRegistry.Count > 0)
        {
            var p0 = PlayerRegistry.Instance.Get(0);
            var p1 = PlayerRegistry.Instance.Get(1);
            Check("F1: Get(0) valid", p0 != null && p0.Stats != null);
            Check("F1: Get(1) null in single player", p1 == null);
            L($"AllDead() = {PlayerRegistry.Instance.AllDead()} (expect false while the player is alive).");
        }
        else
        {
            L("Registry empty -> bars fall back to FindAnyObjectByType<PlayerStats>() for slot 0; higher slots hide.");
        }
    }

    [ContextMenu("Scene: P2 bars hidden (F2)")]
    private void SceneCheck_BarsHiddenForAbsentPlayers()
    {
        L("----- F2: P2 health/stamina bars should be HIDDEN in single player -----");
        ReportBars<HealthBarUI>("HealthBarUI");
        ReportBars<StaminaBarUI>("StaminaBarUI");
        L("Expect: index 0 bars active, index 1 bars INACTIVE (hidden). If a P2 bar is still visible,");
        L("check its 'Hide Root When Absent' (or that its assigned bar reference isn't the script's own object).");
    }

    private void ReportBars<T>(string label) where T : MonoBehaviour
    {
        var bars = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (bars.Length == 0) { L($"  ({label}: none found)"); return; }
        var field = typeof(T).GetField("playerIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var b in bars)
        {
            int idx = -1;
            try { if (field != null) idx = (int)field.GetValue(b); } catch { }
            bool active = b.gameObject.activeInHierarchy;
            string verdict = (idx >= 1 && active) ? "  <-- should be HIDDEN" : "";
            L($"  {label} index={idx} activeInHierarchy={active}{verdict}");
        }
    }

    private void Check(string name, bool condition)
    {
        if (condition) { _pass++; Debug.Log(TAG + $"[PASS] {name}"); }
        else { _fail++; Debug.LogError(TAG + $"[FAIL] {name}"); }
    }

    private static void L(string m) => Debug.Log(TAG + m);
    private static void LErr(string m) => Debug.LogError(TAG + m);
    private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
}
