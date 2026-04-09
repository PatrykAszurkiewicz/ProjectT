using UnityEngine;


// Turret weapon system. Player clicks to deploy a small auto-targeting turret.

public class TurretLauncherSystem
{
    // References
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    // State
    private TurretUnit activeTurret;
    private Camera mainCam;

    public TurretLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;
    }

    public void Cleanup()
    {
        if (activeTurret != null && activeTurret.gameObject != null)
            Object.Destroy(activeTurret.gameObject);
        activeTurret = null;
    }

    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        // Clean up if turret was destroyed externally
        if (activeTurret != null && activeTurret.gameObject == null)
            activeTurret = null;
    }

    public bool CanFire() => true;

    public void PlaceTurret()
    {
        if (mainCam == null) mainCam = Camera.main;

        Vector3 spawnPos = playerTransform.position;

        // Disintegrate existing turret
        if (activeTurret != null && activeTurret.gameObject != null)
            activeTurret.Disintegrate();

        // Create turret
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
            rotationSpeed: data.turretRotationSpeed
        );

        activeTurret = turret;
    }
}



