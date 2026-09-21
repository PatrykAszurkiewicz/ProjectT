using UnityEngine;

// =============================================================================
//  BOSS DAMAGE ROUTING
// -----------------------------------------------------------------------------
//  A source-tagging shim for damage dealt directly (not via a Projectile).
//
//  WHY IT EXISTS
//  `CharacterStats.TakeDamage(float)` carries no damage source, and adding one
//  would change a signature that every enemy and every damage source in the
//  project overrides or calls. The Bellkeeper needs the distinction (towers take a
//  25% penalty in State A and are ignored outright in State B, while player hits
//  drive Mechanic 2), so instead of changing the base signature we tag at the few
//  call sites that matter.
//
//  Tower.cs has exactly FOUR direct-damage sites, and each one already calls
//  `TowerKillAttribution.MarkTowerHit(target)` on the line immediately above:
//
//      Tower.cs:919   hammer AoE slam
//      Tower.cs:1522  laser tick
//      Tower.cs:2842  melee swipe
//      Tower.cs:2895  base melee / secondary hit
//
//  Replace `stats.TakeDamage(x)` with `BossDamageRouting.FromTower(stats, x)` at
//  those four lines. Tower PROJECTILES need no change — Boss5.OnTriggerEnter2D
//  already recognises `Projectile` as tower damage, exactly as Boss2 does.
//
//  BEHAVIOUR FOR EVERYTHING ELSE IS IDENTICAL. For any enemy that is not a Boss5,
//  FromTower is a straight passthrough to the same TakeDamage call that was there
//  before — same value, same order, same virtual dispatch. Nothing else changes.
//
//  Skipping the edit entirely is also safe: Boss5 falls back to its `Auto`
//  classification (is a live player within melee reach?), which reads those four
//  paths correctly whenever the player is not standing on top of the boss.
// =============================================================================

public static class BossDamageRouting
{
    /// Deal tower damage to an enemy, tagging the source when the enemy cares.
    ///
    /// Takes CharacterStats rather than EnemyStats so every caller can use it —
    /// Tower and Projectile hold EnemyStats, WeaponProjectile holds CharacterStats.
    /// TakeDamage is virtual, so dispatch is identical either way.
    public static void FromTower(CharacterStats stats, float amount)
    {
        if (stats == null) return;

        if (stats is Boss5 bellkeeper)
        {
            bellkeeper.TakeDamageFrom(amount, BossDamageSource.Tower);
            return;
        }

        stats.TakeDamage(amount);
    }

    /// GameObject overload, for a call site that has the object rather than the stats.
    public static void FromTower(GameObject enemy, float amount)
    {
        if (enemy == null) return;
        FromTower(enemy.GetComponent<CharacterStats>(), amount);
    }

    /// Player-sourced direct damage (melee swings, ranged hits, player-owned AoE).
    ///
    /// WeaponProjectile routes through this. That matters more than it looks: without
    /// an explicit tag, a ranged hit lands as Unknown and Boss5's Auto policy would
    /// classify it by PROXIMITY — a shot fired from across the arena has no player
    /// nearby, so it would be read as TOWER damage, silently taking the 25% penalty
    /// and being absorbed entirely during a challenge. Mechanic 2 would then never
    /// count ranged hits at all.
    public static void FromPlayer(CharacterStats stats, float amount)
    {
        if (stats == null) return;

        if (stats is Boss5 bellkeeper)
        {
            bellkeeper.TakeDamageFrom(amount, BossDamageSource.Player);
            return;
        }

        stats.TakeDamage(amount);
    }

    public static void FromPlayer(GameObject enemy, float amount)
    {
        if (enemy == null) return;
        FromPlayer(enemy.GetComponent<CharacterStats>(), amount);
    }
}


