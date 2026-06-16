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
