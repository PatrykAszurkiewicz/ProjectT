using UnityEngine;


// Decoy weapon system. Player clicks to deploy a decoy at their position.
//
// COOLDOWN MODEL (mirrors the Revenant Necronomicon book):
//   The decoy has a TWO-PHASE life. Phase 1 (ACTIVE) it is deployed and
//   attracting enemies; phase 2 (COOLDOWN) is a recharge that must elapse
//   before it can be re-deployed. Both timers live on PlayerToolCooldownStore
//   (a component on the player) so they keep advancing while the player has
//   scrolled to another tool — the cooldown can't be dodged by scrolling off
//   the tool and back, and a deployed decoy still expires on time.
//
//   Re-placing while the decoy is still ACTIVE just RELOCATES it: the active
//   countdown CONTINUES from where it was rather than restarting (place at 7s
//   into a 10s window → the relocated decoy lives 3 more seconds, then the
//   cooldown kicks in). Re-placing during the COOLDOWN does nothing.
public class DecoyLauncherSystem
{
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;
    private readonly int playerIndex; // per-player cooldown reduction

    private DecoyDevice activeDecoy;
    private Camera mainCam;

    // Two-phase timing — PERSISTENT (survives un-equipping). Lives on the player.
    private PlayerToolCooldownStore store;

    public enum DecoyPhase { Ready, Active, CoolingDown }

    // A decoy is currently deployed and running.
    public bool HasActiveDecoy => activeDecoy != null && activeDecoy.gameObject != null
                                  && !activeDecoy.IsExpired && !activeDecoy.IsDisintegrating;

    public DecoyLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;

        var ownerRef = weapon.GetComponentInParent<PlayerRef>();
        this.playerIndex = ownerRef != null ? ownerRef.PlayerIndex : 0;

        // Resolve (or create) the persistent cooldown store on the player.
        store = PlayerToolCooldownStore.GetOrCreate(weapon);

        // Re-attach to a decoy that may still be running from before a tool
        // swap, so relocation / cleanup keep working after scrolling back.
        if (store != null && store.decoy.IsActivePhase)
        {
            var existing = Object.FindFirstObjectByType<DecoyDevice>();
            if (existing != null && !existing.IsExpired && !existing.IsDisintegrating)
                activeDecoy = existing;
        }
    }

    //  Phase / gauge queries (drive the WeaponRollUI overlay) 
    public DecoyPhase CurrentPhase
    {
        get
        {
            if (store == null) return DecoyPhase.Ready;
            if (store.decoy.IsActivePhase) return DecoyPhase.Active;
            if (store.decoy.IsCooldownPhase) return DecoyPhase.CoolingDown;
            return DecoyPhase.Ready;
        }
    }

    /// 1..0 over the active phase (1 = just deployed, 0 = about to expire).
    public float ActiveNormalized => store != null ? store.decoy.ActiveNormalized : 0f;
    /// 0..1 over the cooldown phase (1 = ready).
    public float CooldownNormalized => store != null ? store.decoy.CooldownNormalized : 1f;

    /// True while re-deployment is blocked (i.e. during the recharge).
    public bool IsOnCooldown => CurrentPhase == DecoyPhase.CoolingDown;

    /// A placement is allowed unless we're mid-recharge (Ready = new deploy,
    /// Active = relocate).
    public bool CanFire() => CurrentPhase != DecoyPhase.CoolingDown;

    //  Durations 
    // Active duration is raw (like the book's aura duration — not reduced by
    // the cooldown modifier).
    private float ActiveDuration()
    {
        if (data.decoyActiveDuration > 0f) return data.decoyActiveDuration;
        return 10f;
    }

    // Post-active recharge (reduced by the per-player cooldown modifier).
    private float CooldownDuration()
    {
        if (data.decoyCooldown > 0f) return CooldownModifier.Apply(data.decoyCooldown, playerIndex);
        if (data.attackCooldown > 0f) return CooldownModifier.Apply(data.attackCooldown, playerIndex);
        return CooldownModifier.Apply(6f, playerIndex);
    }

    public void Cleanup()
    {
        // Do NOT destroy the deployed decoy — like the book's aura it lives on
        // (and keeps attracting) while the player is on another tool, and its
        // timers keep running on the persistent store. Only drop our transient
        // reference. The decoy self-expires via its own life timer, and the
        // store arms the cooldown automatically when the active phase ends.
        activeDecoy = null;
    }

    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        // Drop the reference once the decoy is gone or has expired.
        if (activeDecoy != null && (activeDecoy.gameObject == null || activeDecoy.IsExpired))
            activeDecoy = null;

        // Safety: if the persistent timer says the active phase is over but a
        // stray decoy still lingers, disintegrate it so the world matches the
        // store (mirrors the book's stray-aura cleanup).
        if (store != null && !store.decoy.IsActivePhase
            && activeDecoy != null && activeDecoy.gameObject != null
            && !activeDecoy.IsDisintegrating)
        {
            activeDecoy.Disintegrate();
            activeDecoy = null;
        }
    }

    // Deploy (Ready) or relocate (Active) the decoy. Returns true if a decoy
    // was actually placed/relocated, false if blocked by the cooldown.
    public bool PlaceDecoy()
    {
        if (mainCam == null) mainCam = Camera.main;

        // No persistent store (shouldn't happen on a real player) — fall back
        // to a plain single-shot placement with no cooldown.
        if (store == null)
        {
            SpawnDecoy(ActiveDuration());
            return true;
        }

        DecoyPhase phase = CurrentPhase;
        if (phase == DecoyPhase.CoolingDown) return false;

        float lifetime;
        if (phase == DecoyPhase.Active)
        {
            // RELOCATE: keep the running active countdown; the fresh decoy only
            // lives for the time remaining in the current window.
            lifetime = Mathf.Max(0.0001f, store.decoy.activeTimer);
        }
        else
        {
            // READY: start a new active window (which auto-arms the cooldown
            // when it ends), and the decoy lives the full active duration.
            lifetime = ActiveDuration();
            store.decoy.StartActive(lifetime, CooldownDuration());
        }

        SpawnDecoy(lifetime);
        return true;
    }

    private void SpawnDecoy(float lifetime)
    {
        Vector3 spawnPos = playerTransform.position;

        // Disintegrate existing decoy (relocate).
        if (activeDecoy != null && activeDecoy.gameObject != null && !activeDecoy.IsDisintegrating)
            activeDecoy.Disintegrate();

        GameObject decoyObj = new GameObject("Decoy");
        decoyObj.transform.position = spawnPos;
        decoyObj.layer = LayerMask.NameToLayer("Default");

        DecoyDevice decoy = decoyObj.AddComponent<DecoyDevice>();
        decoy.Initialize(
            attractRadius: data.decoyAttractRadius,
            duration: lifetime,
            bossDuration: data.decoyBossDuration,
            armDelay: data.decoyArmDelay,
            bossVFXOffset: data.decoyBossVFXOffset
        );

        activeDecoy = decoy;
    }
}

