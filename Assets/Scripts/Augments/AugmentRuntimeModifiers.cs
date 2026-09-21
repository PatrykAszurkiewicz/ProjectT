using UnityEngine;

// =============================================================================
//  Shared runtime modifiers for augments 334–348
// -----------------------------------------------------------------------------
//  Small global multiplier holders, in the same spirit as CooldownModifier /
//  ParryUpgrades. They all default to 1.0 (no effect) so they are harmless until
//  an augment changes them. Each is read by exactly one hook site (noted below).
// =============================================================================

// 334 (Last Stand) + 348 (Energy Attunement)
public static class PlayerEconomyModifiers
{
    // Multiplies energy the player GAINS (drops, generators, kills, refunds…).
    // HOOK: EnergyManager.GivePlayerEnergy multiplies `amount` by this.
    public static float EnergyGainMultiplier = 1f;
}

// 334 (Last Stand) attack half
public static class PlayerCombatModifiers
{
    // Multiplies the player's OUTGOING attack damage.
    // HOOK: Weapon reads this where it computes the damage it deals
    //       (needs the Weapon/WeaponData damage path — see integration notes).
    public static float OutgoingDamageMultiplier = 1f;
}

// 338 (Siege Doctrine), 340 (Phalanx), 346 (Conscript's Sacrifice)
public static class TowerCombatModifiers
{
    // Global tower damage multiplier.
    // HOOK: Tower.GetEffectiveDamage() and the laser tick multiply by this.
    public static float DamageMultiplier = 1f;

    // Fire-rate has two independent, multiplicatively-composed sources:
    //   • flat buffs (338)            -> BaseFireRateMultiplier
    //   • per-active-tower buff (340) -> PerCountFireRateMultiplier
    public static float BaseFireRateMultiplier = 1f;
    public static float PerCountFireRateMultiplier = 1f;

    // HOOK: Tower.CanFire uses this combined value as the effective fire rate.
    public static float FireRateMultiplier => BaseFireRateMultiplier * PerCountFireRateMultiplier;
}

// =============================================================================
//  RUN-LIFECYCLE RESET
// -----------------------------------------------------------------------------
//  The holders above are STATIC, which means their values survive a scene load
//  and — with Domain Reload disabled, the default for fast Enter Play Mode —
//  survive leaving and re-entering Play Mode as well. Nothing reset them, so a
//  Siege Doctrine or Energy Attunement pick compounded into the NEXT run: start
//  three runs in one editor session and towers came out of the third with the
//  stacked damage bonus of all three.
//
//  Two safety nets:
//    * the SubsystemRegistration hook below clears them at the start of every
//      Play session, covering the domain-reload-off case;
//    * GameOrchestrator calls ResetAll() when a run starts, covering run-to-run
//      within a single session.
//
//  The "NOT EXHAUSTIVE" note that used to live here has been ACTIONED: ResetAll()
//  below now also clears the augment statics that live in other files
//  (EnemyDropAugments, TowerKillRewards, GeneratorAoeDamage, TowerCountScalingManager).
//  Those were the remaining dimensions that compounded run-to-run: pick Marksman's
//  Bounty in three runs of one editor session and the third run started with +90%
//  tower-kill drop chance.
// =============================================================================
public static class AugmentRuntimeModifiers
{
    /// Return every runtime augment multiplier to its neutral value.
    /// Safe to call at any time; it only writes defaults.
    public static void ResetAll()
    {
        PlayerEconomyModifiers.EnergyGainMultiplier = 1f;

        PlayerCombatModifiers.OutgoingDamageMultiplier = 1f;

        TowerCombatModifiers.DamageMultiplier = 1f;
        TowerCombatModifiers.BaseFireRateMultiplier = 1f;
        TowerCombatModifiers.PerCountFireRateMultiplier = 1f;

        ResetExternalAugmentStatics();
    }

    // Augment statics that live in OTHER files. Every member below is verified against
    // its declaring file, and each is reset to that field's OWN declared default (not
    // simply to zero) so a reset run behaves exactly like a fresh editor session.
    private static void ResetExternalAugmentStatics()
    {
        // 341 / 342 — Marksman's Bounty, Plunderer's Payload (TowerKillEconomy.cs).
        // NOTE: an earlier draft also reset EnemyDropAugments.GlobalDropChanceBonus.
        // That member does not exist — it was named only in a stale code comment.
        EnemyDropAugments.TowerKillChanceBonus = 0f;
        EnemyDropAugments.TowerKillValueBonus = 0f;

        // 335 — Energy Tithe (TowerKillEconomy.cs). EnergyPerKill's declared default is
        // 2, not 0; Enabled is the real gate.
        TowerKillRewards.Enabled = false;
        TowerKillRewards.EnergyPerKill = 2;

        // Tower-kill attribution table. Static Dictionary keyed by GetInstanceID that is
        // only pruned by Forget() on a normal death — so enemies removed any other way
        // (WaveCheckpointService.ClearLiveEnemies on a rewind, scene teardown, a boss
        // despawn) leak entries forever. Worse, Unity RECYCLES instance IDs, so a stale
        // entry can make a brand-new enemy look tower-killed and pay out a bonus drop it
        // never earned.
        TowerKillAttribution.Reset();

        // 345 — Overload Aura.
        GeneratorAoeDamage.Enabled = false;
        GeneratorAoeDamage.DamageMultiplier = 1f;

        // 340 — Phalanx (per-active-tower scaling).
        TowerCountScalingManager.Configure(0f, 0f);

        // 344 — Phoenix Protocol (tower revives).
        // Configure() CANNOT be used as a reset: it clamps its argument to a minimum
        // of 1 and ARMS the augment, so the old Configure(0) call here was granting
        // every run a free tower revive per stage. TowerRevivalManager now has an
        // explicit disable with no clamp and no "armed" log.
        TowerRevivalManager.ResetForNewRun();

        // NOT RESET, deliberately: EnergyDropManager.globalDropChance (337, Lucky
        // Strikes). It is a serialized field on a scene object, so it resets with the
        // scene — UNLESS EnergyDropManager is DontDestroyOnLoad, in which case it does
        // leak across runs. Check that; if it persists, capture its baseline at run
        // start and restore it here.
    }

    // Clears the statics at the start of every Play session. Required because
    // Unity does NOT reinitialise static fields when Domain Reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetAll();
}



