using UnityEngine;
using System.Collections.Generic;


// Trap weapon system. Player clicks to place bear traps that root enemies.
//
// COOLDOWN MODEL: traps have NO active phase. Each trap simply persists until
// it is triggered (or bumped out by the max-count limit). What's gated is
// RE-PLACEMENT: after every trap the player must wait trapCooldown seconds
// before placing the next one. That flat recharge lives on
// PlayerToolCooldownStore (a component on the player) so it survives
// un-equipping — scrolling off the Trap tool and back doesn't wipe it and let
// the player re-place instantly. The WeaponRollUI draws it as the same rising
// fill gauge the book/cloak use for their recharge.
public class TrapLauncherSystem
{
    // References
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;
    private readonly int playerIndex; // per-player cooldown reduction

    // State
    private readonly LinkedList<TrapMine> activeTraps = new LinkedList<TrapMine>();
    private Camera mainCam;

    // Persistent re-placement cooldown. Lives on the player.
    private PlayerToolCooldownStore store;

    // Public accessors
    public int ActiveTrapCount => activeTraps.Count;

    public TrapLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;

        var ownerRef = weapon.GetComponentInParent<PlayerRef>();
        this.playerIndex = ownerRef != null ? ownerRef.PlayerIndex : 0;

        store = PlayerToolCooldownStore.GetOrCreate(weapon);
    }

    //  Cooldown queries (drive the WeaponRollUI overlay) 
    public bool IsOnCooldown => store != null && store.trapCooldownTimer > 0f;

    /// 0..1 recharge progress (1 = ready). 1 when not cooling down.
    public float CooldownNormalized =>
        (store != null && store.trapCooldownTimer > 0f && store.trapCooldownTotal > 0f)
            ? 1f - Mathf.Clamp01(store.trapCooldownTimer / store.trapCooldownTotal)
            : 1f;

    /// A trap can be placed when the re-placement cooldown has elapsed.
    public bool CanFire() => store == null || store.trapCooldownTimer <= 0f;

    private float TrapCooldownDuration()
    {
        if (data.trapCooldown > 0f) return CooldownModifier.Apply(data.trapCooldown, playerIndex);
        if (data.attackCooldown > 0f) return CooldownModifier.Apply(data.attackCooldown, playerIndex);
        return CooldownModifier.Apply(2f, playerIndex);
    }

    public void Cleanup()
    {
        foreach (var trap in activeTraps)
        {
            if (trap != null && trap.gameObject != null)
                Object.Destroy(trap.gameObject);
        }
        activeTraps.Clear();
    }

    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        // Clean up destroyed traps (triggered and faded, or disintegrated)
        var node = activeTraps.First;
        while (node != null)
        {
            var next = node.Next;
            if (node.Value == null || node.Value.gameObject == null)
                activeTraps.Remove(node);
            node = next;
        }
    }

    public void PlaceTrap()
    {
        if (mainCam == null) mainCam = Camera.main;

        // Gate on the persistent re-placement cooldown.
        if (!CanFire()) return;

        Vector3 spawnPos = playerTransform.position;

        // If at max traps, disintegrate the oldest
        while (activeTraps.Count >= data.trapMaxCount)
        {
            var oldest = activeTraps.First;
            if (oldest != null)
            {
                if (oldest.Value != null && oldest.Value.gameObject != null)
                    oldest.Value.Disintegrate();

                activeTraps.RemoveFirst();
            }
        }

        // Create trap
        GameObject trapObj = new GameObject("TrapMine");
        trapObj.transform.position = spawnPos;
        trapObj.layer = LayerMask.NameToLayer("Default");

        TrapMine trap = trapObj.AddComponent<TrapMine>();
        trap.Initialize(
            trapDuration: data.trapHoldDuration,
            bossTrapDuration: data.trapBossHoldDuration,
            proximityRadius: data.trapProximityRadius,
            armDelay: data.trapArmDelay
        );

        activeTraps.AddLast(trap);

        // Arm the flat re-placement cooldown on the persistent store.
        if (store != null)
        {
            float dur = TrapCooldownDuration();
            store.trapCooldownTotal = dur;
            store.trapCooldownTimer = dur;
        }

        // Plant SFX
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.trapSetup.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.trapSetup, spawnPos);
        }
    }
}


