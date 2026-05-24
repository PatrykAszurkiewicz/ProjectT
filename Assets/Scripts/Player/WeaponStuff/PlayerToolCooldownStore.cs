using UnityEngine;

// Persistent home for tool cooldown timers that must SURVIVE un-equipping.
// Solves tool subsystems (RevenantNecronomiconSystem, etc.)
// are created by Weapon.HotSwapTool and DESTROYED by CleanupToolSubsystems on
// every weapon-roll scroll. If a cooldown timer lives on the subsystem, then
// scrolling away from a tool and back wipes its cooldown — letting the player
// dodge the cooldown entirely (scroll off, scroll on, reuse instantly).

public class PlayerToolCooldownStore : MonoBehaviour
{
    /// One tool's two-phase timing. Phase 1 = the effect is ACTIVE
    /// (e.g. the book aura is up). Phase 2 = post-effect COOLDOWN. Both must
    /// elapse before the tool is usable again.
    [System.Serializable]
    public class TwoPhaseTimer
    {
        public float activeTimer;     // counts down while the effect runs
        public float activeTotal;
        public float cooldownTimer;   // counts down during the recharge
        public float cooldownTotal;
        // Cooldown length to arm when the active phase ends. Stored up-front
        // (at StartActive) so the store can perform the active→cooldown
        // handoff on its own Update tick, even while the tool is unequipped.
        public float pendingCooldownDuration;

        public bool IsActivePhase => activeTimer > 0f;
        public bool IsCooldownPhase => activeTimer <= 0f && cooldownTimer > 0f;
        public bool IsReady => activeTimer <= 0f && cooldownTimer <= 0f;

        /// 1..0 over the active phase (1 = just started).
        public float ActiveNormalized =>
            (activeTimer > 0f && activeTotal > 0f)
                ? Mathf.Clamp01(activeTimer / activeTotal) : 0f;

        /// 0..1 over the cooldown phase (1 = ready). 1 when not cooling down.
        public float CooldownNormalized =>
            (cooldownTimer > 0f && cooldownTotal > 0f)
                ? 1f - Mathf.Clamp01(cooldownTimer / cooldownTotal) : 1f;

        // Begin the active phase. `cooldownDuration` is the recharge that will
        // be armed automatically when the active phase ends.
        public void StartActive(float duration, float cooldownDuration)
        {
            activeTotal = Mathf.Max(0.0001f, duration);
            activeTimer = activeTotal;
            cooldownTimer = 0f;
            cooldownTotal = 0f;
            pendingCooldownDuration = Mathf.Max(0.0001f, cooldownDuration);
        }

        // End the active phase NOW and arm the cooldown phase.
        public void EndActiveStartCooldown(float cooldownDuration)
        {
            activeTimer = 0f;
            cooldownTotal = Mathf.Max(0.0001f, cooldownDuration);
            cooldownTimer = cooldownTotal;
        }

        // Advance both timers by dt. Performs the active→cooldown handoff
        // automatically using pendingCooldownDuration.
        public void Tick(float dt)
        {
            if (activeTimer > 0f)
            {
                activeTimer -= dt;
                if (activeTimer <= 0f)
                {
                    activeTimer = 0f;
                    cooldownTotal = Mathf.Max(0.0001f, pendingCooldownDuration);
                    cooldownTimer = cooldownTotal;
                }
                return;
            }
            if (cooldownTimer > 0f)
            {
                cooldownTimer -= dt;
                if (cooldownTimer < 0f) cooldownTimer = 0f;
            }
        }
    }

    // One timer per tool. Public fields so they're inspectable while playing.
    public TwoPhaseTimer book = new TwoPhaseTimer();

    // Tick every timer here — independently of whatever tool is equipped — so
    // an in-progress cooldown keeps advancing while the player has scrolled
    // away to another tool. This is the whole point of the persistent store.
    void Update()
    {
        book.Tick(Time.deltaTime);
    }

    /// Find (or create) the store on the player. Pass any component/transform
    /// under the player; resolves the player root via PlayerStats.
    public static PlayerToolCooldownStore GetOrCreate(Component anyPlayerComponent)
    {
        Transform player = ResolvePlayer(anyPlayerComponent);
        if (player == null) return null;

        var store = player.GetComponent<PlayerToolCooldownStore>();
        if (store == null)
            store = player.gameObject.AddComponent<PlayerToolCooldownStore>();
        return store;
    }

    private static Transform ResolvePlayer(Component c)
    {
        if (c != null)
        {
            var statsUp = c.GetComponentInParent<PlayerStats>();
            if (statsUp != null) return statsUp.transform;
        }
        var stats = FindFirstObjectByType<PlayerStats>();
        if (stats != null) return stats.transform;
        var movement = FindFirstObjectByType<PlayerMovement>();
        if (movement != null) return movement.transform;
        var tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }
}
