using UnityEngine;


// Turret weapon system. Player clicks to deploy a small auto-targeting turret.
//
// COOLDOWN MODEL (mirrors the Revenant Necronomicon book / decoy):
//   The turret has a TWO-PHASE life. Phase 1 (ACTIVE) it is deployed and
//   firing; phase 2 (COOLDOWN) is a recharge that must elapse before it can be
//   re-deployed. Both timers live on PlayerToolCooldownStore (a component on
//   the player) so they keep advancing while the player has scrolled to
//   another tool — the cooldown can't be dodged by scrolling off the tool and
//   back, and a deployed turret still expires on time.
//
//   Re-placing while the turret is still ACTIVE just RELOCATES it: the active
//   countdown CONTINUES from where it was rather than restarting. Re-placing
//   during the COOLDOWN does nothing.
public class TurretLauncherSystem
{
    // References
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;
    private readonly int playerIndex; // per-player cooldown reduction

    // State
    private TurretUnit activeTurret;
    private Camera mainCam;

    // Two-phase timing — PERSISTENT (survives un-equipping). Lives on the player.
    private PlayerToolCooldownStore store;

    public enum TurretPhase { Ready, Active, CoolingDown }

    public bool HasActiveTurret => activeTurret != null && activeTurret.gameObject != null
                                   && !activeTurret.IsDisintegrating;

    public TurretLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;

        var ownerRef = weapon.GetComponentInParent<PlayerRef>();
        this.playerIndex = ownerRef != null ? ownerRef.PlayerIndex : 0;

        store = PlayerToolCooldownStore.GetOrCreate(weapon);

        // Re-attach to a turret that may still be running from before a swap.
        if (store != null && store.turret.IsActivePhase)
        {
            var existing = Object.FindFirstObjectByType<TurretUnit>();
            if (existing != null && !existing.IsDisintegrating)
                activeTurret = existing;
        }
    }

    //  Phase / gauge queries (drive the WeaponRollUI overlay) 
    public TurretPhase CurrentPhase
    {
        get
        {
            if (store == null) return TurretPhase.Ready;
            if (store.turret.IsActivePhase) return TurretPhase.Active;
            if (store.turret.IsCooldownPhase) return TurretPhase.CoolingDown;
            return TurretPhase.Ready;
        }
    }

    /// 1..0 over the active phase (1 = just deployed, 0 = about to expire).
    public float ActiveNormalized => store != null ? store.turret.ActiveNormalized : 0f;
    /// 0..1 over the cooldown phase (1 = ready).
    public float CooldownNormalized => store != null ? store.turret.CooldownNormalized : 1f;

    public bool IsOnCooldown => CurrentPhase == TurretPhase.CoolingDown;

    /// A placement is allowed unless we're mid-recharge (Ready = new deploy,
    /// Active = relocate).
    public bool CanFire() => CurrentPhase != TurretPhase.CoolingDown;

    //  Durations 
    private float ActiveDuration()
    {
        if (data.turretActiveDuration > 0f) return data.turretActiveDuration;
        return 15f;
    }

    private float CooldownDuration()
    {
        if (data.turretCooldown > 0f) return CooldownModifier.Apply(data.turretCooldown, playerIndex);
        if (data.attackCooldown > 0f) return CooldownModifier.Apply(data.attackCooldown, playerIndex);
        return CooldownModifier.Apply(7f, playerIndex);
    }

    public void Cleanup()
    {
        // Do NOT destroy the deployed turret — like the book's aura it lives on
        // (and keeps firing) while the player is on another tool, and its
        // timers keep running on the persistent store. The turret self-expires
        // via its own life timer (set to the active duration), and the store
        // arms the cooldown automatically when the active phase ends.
        activeTurret = null;
    }

    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        // Drop the reference once the turret is gone.
        if (activeTurret != null && activeTurret.gameObject == null)
            activeTurret = null;

        // Safety: if the persistent timer says the active phase is over but a
        // stray turret still lingers, disintegrate it so the world matches the
        // store (mirrors the book's stray-aura cleanup).
        if (store != null && !store.turret.IsActivePhase
            && activeTurret != null && activeTurret.gameObject != null
            && !activeTurret.IsDisintegrating)
        {
            activeTurret.Disintegrate();
            activeTurret = null;
        }
    }

    // Deploy (Ready) or relocate (Active) the turret. Returns true if a turret
    // was actually placed/relocated, false if blocked by the cooldown.
    public bool PlaceTurret()
    {
        if (mainCam == null) mainCam = Camera.main;

        if (store == null)
        {
            SpawnTurret(ActiveDuration());
            return true;
        }

        TurretPhase phase = CurrentPhase;
        if (phase == TurretPhase.CoolingDown) return false;

        float lifetime;
        if (phase == TurretPhase.Active)
        {
            // RELOCATE: keep the running active countdown.
            lifetime = Mathf.Max(0.0001f, store.turret.activeTimer);
        }
        else
        {
            // READY: start a new active window (auto-arms the cooldown on end).
            lifetime = ActiveDuration();
            store.turret.StartActive(lifetime, CooldownDuration());
        }

        SpawnTurret(lifetime);
        return true;
    }

    private void SpawnTurret(float lifetime)
    {
        Vector3 spawnPos = playerTransform.position;

        // Disintegrate existing turret (relocate).
        if (activeTurret != null && activeTurret.gameObject != null && !activeTurret.IsDisintegrating)
            activeTurret.Disintegrate();

        GameObject turretObj = new GameObject("TurretUnit");
        turretObj.transform.position = spawnPos;
        turretObj.layer = LayerMask.NameToLayer("Default");

        TurretUnit turret = turretObj.AddComponent<TurretUnit>();
        turret.Initialize(
            damage: data.damage,
            range: data.turretRange,
            fireRate: data.turretFireRate,
            projectileSpeed: data.turretProjectileSpeed,
            armDelay: data.turretArmDelay,
            rotationSpeed: data.turretRotationSpeed,
            lifetime: lifetime
        );

        activeTurret = turret;

        // Deploy SFX
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.turretSetup.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.turretSetup, spawnPos);
        }
    }
}


