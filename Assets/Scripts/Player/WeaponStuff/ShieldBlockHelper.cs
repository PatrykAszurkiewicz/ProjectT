using UnityEngine;

// SHIELD BLOCK HELPER
// Static utility that any damage-dealing code can call to check if the player's
// shield blocks the incoming attack. This avoids needing to modify every single damage source individually.
// Usage:
//   if (ShieldBlockHelper.TryBlock(attackerGO, targetGO))
//       return; // damage was blocked or parried
// Also provides a helper to apply parry damage bonus:
//   float finalDmg = ShieldBlockHelper.ApplyParryBonus(enemyGO, baseDamage);

public static class ShieldBlockHelper
{

    // Checks if the target (assumed to be the player) has an active shield that blocks the attack from the given attacker.


    public static bool TryBlock(GameObject attacker, GameObject target)
    {
        if (attacker == null || target == null) return false;

        // Only works on the player
        var playerStats = target.GetComponent<PlayerStats>();
        if (playerStats == null) return false;

        // Find the Weapon component (may be on a child)
        var weapon = target.GetComponentInChildren<Weapon>();
        if (weapon == null) return false;

        var shield = weapon.GetShieldSystem();
        if (shield == null || !shield.IsRaised) return false;

        return shield.TryBlockOrParry(attacker);
    }


    // Returns the damage amount adjusted for parry bonus.

    public static float ApplyParryBonus(GameObject enemy, float baseDamage)
    {
        if (enemy == null) return baseDamage;

        var parryEffect = enemy.GetComponent<ParryStunEffect>();
        if (parryEffect != null)
            return baseDamage * parryEffect.DamageMultiplier;

        return baseDamage;
    }
}
