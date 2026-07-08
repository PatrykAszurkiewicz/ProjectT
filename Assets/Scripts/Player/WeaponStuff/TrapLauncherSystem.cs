using UnityEngine;
using System.Collections.Generic;


// Trap weapon system. Player clicks to place bear traps that root enemies.

public class TrapLauncherSystem
{
    // References
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    // State
    private readonly LinkedList<TrapMine> activeTraps = new LinkedList<TrapMine>();
    private Camera mainCam;

    // Public accessors
    public int ActiveTrapCount => activeTraps.Count;

    public TrapLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;
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

    public bool CanFire() => true;

    public void PlaceTrap()
    {
        if (mainCam == null) mainCam = Camera.main;

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

        // Plant SFX
        if (AudioManager.instance != null && FMODEvents.instance != null
            && !FMODEvents.instance.trapSetup.IsNull)
        {
            AudioManager.instance.PlayOneShot(FMODEvents.instance.trapSetup, spawnPos);
        }
    }
}

