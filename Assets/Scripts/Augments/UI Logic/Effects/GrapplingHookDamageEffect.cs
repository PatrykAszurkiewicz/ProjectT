using UnityEngine;

public class GrapplingHookDamageEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float damage = 0f;

    private Weapon weapon;

    private void Awake()
    {
        weapon = GetComponentInChildren<Weapon>();
        if (weapon == null)
        {
            weapon = FindFirstObjectByType<Weapon>();
        }
    }

    private void Start()
    {
        if (weapon == null)
        {
            Debug.LogError("[GRAPPLING_DAMAGE] Could not find Weapon component");
            enabled = false;
            return;
        }

        // Check if weapon is actually a grappling hook
        var weaponData = weapon.GetWeaponData();
        if (weaponData == null || !weaponData.isGrapplingHook)
        {
            Debug.LogWarning("[GRAPPLING_DAMAGE] Weapon is not a grappling hook - effect will have no impact");
        }

        ApplyDamageToWeapon();
    }

    private void ApplyDamageToWeapon()
    {
        if (weapon == null) return;

        // Add damage to weapon's grappling damage
        weapon.grapplingDamage += damage;

        //Debug.Log($"[GRAPPLING_DAMAGE] Applied {damage} grappling hook damage. Total: {weapon.grapplingDamage}");
    }

    private void OnDestroy()
    {
        // Remove damage when effect is removed
        if (weapon != null)
        {
            weapon.grapplingDamage -= damage;
            weapon.grapplingDamage = Mathf.Max(0f, weapon.grapplingDamage);
        }
    }

    // Public getter for UI
    public float GetDamage() => damage;
}
