using UnityEngine;
using System.Collections.Generic;


/// Bomb Launcher weapon system. Player clicks to drop proximity mines.

public class BombLauncherSystem
{
    // References
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    // State
    private readonly LinkedList<BombMine> activeMines = new LinkedList<BombMine>();
    private Camera mainCam;

    // Public accessors
    public int ActiveMineCount => activeMines.Count;

    public BombLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;
    }

    public void Cleanup()
    {
        foreach (var mine in activeMines)
        {
            if (mine != null && mine.gameObject != null)
                Object.Destroy(mine.gameObject);
        }
        activeMines.Clear();
    }

    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        // Clean up destroyed mines (exploded or disintegrated)
        var node = activeMines.First;
        while (node != null)
        {
            var next = node.Next;
            if (node.Value == null || node.Value.gameObject == null)
                activeMines.Remove(node);
            node = next;
        }
    }

    public bool CanFire() => true;

    public void PlaceMine()
    {
        if (mainCam == null) mainCam = Camera.main;

        Vector3 spawnPos = playerTransform.position;

        // If at max mines, disintegrate the oldest instead of instant destroy
        while (activeMines.Count >= data.bombMaxMines)
        {
            var oldest = activeMines.First;
            if (oldest != null)
            {
                if (oldest.Value != null && oldest.Value.gameObject != null)
                    oldest.Value.Disintegrate(); // smooth removal

                activeMines.RemoveFirst();
            }
        }

        // Create mine
        GameObject mineObj = new GameObject("BombMine");
        mineObj.transform.position = spawnPos;
        mineObj.layer = LayerMask.NameToLayer("Default");

        BombMine mine = mineObj.AddComponent<BombMine>();
        mine.Initialize(
            damage: data.damage,
            proximityRadius: data.bombProximityRadius,
            explosionRadius: data.bombExplosionRadius,
            friendlyFire: data.bombFriendlyFire,
            armDelay: data.bombArmDelay
        );

        activeMines.AddLast(mine);
    }
}
