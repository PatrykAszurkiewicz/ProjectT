using UnityEngine;

//  334 — "Last Stand"
//  While the player is below a health threshold:
//    multiply energy gained  (via PlayerEconomyModifiers.EnergyGainMultiplier)
//    multiply attack power   (by scaling the equipped weapon's WeaponData.damage
//                               directly — same approach ScarecrowBuffTag uses on
//                               enemyData.damage, so it needs no Weapon.cs hook)
//  Cleanly reverses both the instant the player heals back above the threshold.
//  All three values are pushed in from the CSV by AugmentEffectHandler:
//    aug_health_threshold : fraction of max HP (default 0.30)
//    aug_energy_bonus     : extra energy gain  (default 0.30 = +30%)
//    aug_attack_bonus     : extra attack power (default 0.30 = +30%)

public class LowHealthEmpowerEffect : MonoBehaviour
{
    public float HealthThreshold = 0.30f;
    public float EnergyBonus = 0.30f;
    public float DamageBonus = 0.30f;

    private CharacterStats stats;
    private Weapon weapon;
    private bool active = false;

    // Remember exactly what we buffed so a weapon hot-swap can't corrupt the undo.
    private WeaponData buffedWeaponData;
    private float appliedDamageFactor = 1f;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
        weapon = FindAnyObjectByType<Weapon>();
    }

    private void Update()
    {
        if (stats == null || stats.maxHealth <= 0f) return;

        bool shouldBeActive = (stats.currentHealth / stats.maxHealth) < HealthThreshold
                              && !stats.IsDead();

        if (shouldBeActive == active) return;
        active = shouldBeActive;

        if (active) ApplyBonuses();
        else RemoveBonuses();
    }

    private void ApplyBonuses()
    {
        // Energy: symmetric multiply/divide so we never clobber augment 348.
        PlayerEconomyModifiers.EnergyGainMultiplier *= (1f + EnergyBonus);

        // Attack: scale the currently-equipped weapon's damage.
        if (weapon == null) weapon = FindAnyObjectByType<Weapon>();
        var wd = weapon != null ? weapon.GetWeaponData() : null;
        if (wd != null)
        {
            appliedDamageFactor = (1f + DamageBonus);
            buffedWeaponData = wd;
            wd.damage *= appliedDamageFactor;
        }
    }

    private void RemoveBonuses()
    {
        PlayerEconomyModifiers.EnergyGainMultiplier /= (1f + EnergyBonus);

        if (buffedWeaponData != null && appliedDamageFactor != 0f)
        {
            buffedWeaponData.damage /= appliedDamageFactor;
        }
        buffedWeaponData = null;
        appliedDamageFactor = 1f;
    }

    private void OnDisable()
    {
        // Don't leave bonuses stuck on if the player is destroyed while low.
        if (active)
        {
            RemoveBonuses();
            active = false;
        }
    }
}

