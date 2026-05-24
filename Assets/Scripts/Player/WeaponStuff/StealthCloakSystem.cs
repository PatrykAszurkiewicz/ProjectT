using UnityEngine;

// Stealth Cloak tool system. Right-click toggles invisibility for the player:
// the first right-click cloaks, a second right-click while cloaked uncloaks

public class StealthCloakSystem
{
    private readonly Weapon weapon;
    private readonly WeaponData data;

    private PlayerCloakEffect cloakEffect;

    // True for one Weapon.Update tick after the player connected an attack on
    // an enemy, so Update() can break invisibility. We funnel through a flag
    // rather than calling the effect directly from the hit code path to keep
    // all cloak logic on the main system tick.
    private bool _attackHitPending = false;

    public StealthCloakSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;

        EnsureEffect();
    }

    // Resolve the player GameObject without  assuming any hierarchy.

    private static Transform ResolvePlayerTransform(Weapon weapon)
    {
        // Prefer walking up from the weapon — then fall back to a global search.
        if (weapon != null)
        {
            var statsUp = weapon.GetComponentInParent<PlayerStats>();
            if (statsUp != null) return statsUp.transform;
        }

        var stats = Object.FindFirstObjectByType<PlayerStats>();
        if (stats != null) return stats.transform;

        var movement = Object.FindFirstObjectByType<PlayerMovement>();
        if (movement != null) return movement.transform;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged.transform;

        return null;
    }

    /// <summary>Resolve (or create) the PlayerCloakEffect on the player.</summary>
    private void EnsureEffect()
    {
        if (cloakEffect != null) return;

        Transform playerTransform = ResolvePlayerTransform(weapon);
        if (playerTransform == null)
        {
            Debug.LogError("[StealthCloakSystem] Could not find the player " +
                           "(no PlayerStats / PlayerMovement / 'Player' tag in scene). " +
                           "Cloak cannot activate.");
            return;
        }

        cloakEffect = playerTransform.GetComponent<PlayerCloakEffect>();
        if (cloakEffect == null)
            cloakEffect = playerTransform.gameObject.AddComponent<PlayerCloakEffect>();

        // Push tuning from WeaponData every time we (re)resolve, so editing the
        // asset and re-equipping picks up new values.
        if (cloakEffect != null && data != null)
            cloakEffect.Configure(data.cloakDuration, data.cloakCooldown, data.cloakPlayerAlpha);
    }

    /// Called from Weapon.Update. Forwards a pending attack-hit signal to the
    /// effect so invisibility breaks on the same frame the player lands a hit.
    public void Update()
    {
        if (_attackHitPending)
        {
            _attackHitPending = false;
            if (cloakEffect != null)
                cloakEffect.NotifyPlayerAttacked();
        }
    }

    /// Right-click handler — TOGGLES the cloak.
    ///   Not cloaked, off cooldown  goes invisible, returns true.
    ///   Currently invisible        uncloaks early, returns false.
    ///   On cooldown                does nothing, returns false.
    private float lastActivateTime = -999f;
    private const float ACTIVATE_DEBOUNCE = 0.12f;

    public bool Activate()
    {
        // Debounce double-fires of the same click.
        if (Time.unscaledTime - lastActivateTime < ACTIVATE_DEBOUNCE)
            return false;
        lastActivateTime = Time.unscaledTime;

        EnsureEffect();
        if (cloakEffect == null) return false;

        // Second right-click while invisible → uncloak now.
        if (cloakEffect.IsInvisible)
        {
            cloakEffect.Deactivate();
            return false;
        }

        // First right-click → try to go invisible (false if still on cooldown).
        return cloakEffect.TryActivate();
    }

    // Called by Weapon whenever the player connects a damaging hit on an enemy or boss. Queues the break for the next Update tick.
    public void NotifyPlayerAttackedEnemy()
    {
        // Only meaningful while invisible; cheap to flag regardless.
        if (cloakEffect != null && cloakEffect.IsInvisible)
            _attackHitPending = true;
    }

    public bool IsInvisible => cloakEffect != null && cloakEffect.IsInvisible;
    public bool IsOnCooldown => cloakEffect != null && cloakEffect.IsOnCooldown;

    // 1..0 progress of the active invisibility (1 = just cloaked, 0 = about
    // to expire). The UI draws this as a depleting countdown clock.
    public float ActiveNormalized => cloakEffect != null ? cloakEffect.ActiveNormalized : 0f;

    // 0..1 readiness of the post-cloak recharge (0 = just spent, 1 = ready).
    // The UI draws this as a rising fill gauge. Only meaningful while
    // IsOnCooldown is true.
    public float CooldownNormalized => cloakEffect != null ? cloakEffect.CooldownNormalized : 1f;

    // Tool swap cleanup. We deliberately do NOT destroy PlayerCloakEffect:
    // keeping it alive preserves an in-progress cooldown across swaps and
    // guarantees the player sprite is restored. 
    public void Cleanup()
    {
        if (cloakEffect != null)
            cloakEffect.ForceClear();
        _attackHitPending = false;
    }
}
