using UnityEngine;

// Tool subsystem for the Time Clock (right-click tool, augment 326, slot 15).
// Right-click rewinds the run to the start of the current wave (via
// GameOrchestrator.RewindToCurrentWaveStart), then arms a cooldown. The cooldown
// is parked on PlayerToolCooldownStore so it SURVIVES scrolling the tool off and
// back — same anti-exploit pattern the book/cloak use.

public class ClockSystem
{
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly float cooldownDuration;

    public ClockSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        // Use the asset's attackCooldown if authored, else default to 30s.
        cooldownDuration = (data != null && data.attackCooldown > 0f) ? data.attackCooldown : 30f;
    }

    private PlayerToolCooldownStore Store => PlayerToolCooldownStore.GetOrCreate(weapon);

    public bool IsOnCooldown
    {
        get { var s = Store; return s != null && s.clockCooldownTimer > 0f; }
    }

    /// 0..1 cooldown progress (1 = ready). Drives the WeaponRollUI gauge.
    public float CooldownNormalized
    {
        get
        {
            var s = Store;
            if (s == null || s.clockCooldownTotal <= 0f || s.clockCooldownTimer <= 0f) return 1f;
            return 1f - Mathf.Clamp01(s.clockCooldownTimer / s.clockCooldownTotal);
        }
    }

    /// Right-click: perform the wave-start rewind, then start the cooldown.
    public void Activate()
    {
        //Debug.Log("[CLOCK] ClockSystem.Activate() called.");

        var s = Store;
        if (s != null && s.clockCooldownTimer > 0f)
        {
            Debug.Log($"[CLOCK] On cooldown ({s.clockCooldownTimer:F1}s left) — ignoring.");
            return;
        }

        if (GameOrchestrator.Instance == null)
        {
            Debug.LogError("[CLOCK] GameOrchestrator.Instance is NULL — cannot rewind.");
            return;
        }

        bool rewound = GameOrchestrator.Instance.RewindToCurrentWaveStart();
        if (!rewound) return; // reason logged inside RewindToCurrentWaveStart

        if (s != null)
        {
            int ownerIndex = weapon != null
                ? (weapon.GetComponentInParent<PlayerRef>()?.PlayerIndex ?? 0) : 0;
            float cd = CooldownModifier.Apply(cooldownDuration, ownerIndex);
            s.clockCooldownTotal = cd;
            s.clockCooldownTimer = cd;
        }
        RewindVFX.Play(); // screen disturbance + spinning clock icon

        // Rewind SFX
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.rewindActivate.IsNull)
        {
            Vector3 pos = weapon != null ? weapon.transform.position : Vector3.zero;
            AudioManager.instance.PlayOneShot(FMODEvents.instance.rewindActivate, pos);
        }
        //Debug.Log($"[CLOCK] Rewind succeeded — cooldown armed ({cooldownDuration:F0}s).");
    }

    // Cooldown ticks in PlayerToolCooldownStore.Update(); nothing to do per-frame here.
    public void Update() { }
    public void Cleanup() { }
}
