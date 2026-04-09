using UnityEngine;


// Decoy weapon system. Player clicks to deploy a decoy at their position.

public class DecoyLauncherSystem
{
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    private DecoyDevice activeDecoy;
    private Camera mainCam;

    public bool HasActiveDecoy => activeDecoy != null && activeDecoy.gameObject != null
                                  && !activeDecoy.IsExpired && !activeDecoy.IsDisintegrating;

    public DecoyLauncherSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.mainCam = Camera.main;
    }

    public void Cleanup()
    {
        if (activeDecoy != null && activeDecoy.gameObject != null)
            Object.Destroy(activeDecoy.gameObject);
        activeDecoy = null;
    }

    public void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        if (activeDecoy != null && (activeDecoy.gameObject == null || activeDecoy.IsExpired))
            activeDecoy = null;
    }

    public bool CanFire() => true;

    public void PlaceDecoy()
    {
        if (mainCam == null) mainCam = Camera.main;

        Vector3 spawnPos = playerTransform.position;

        // Disintegrate existing decoy
        if (activeDecoy != null && activeDecoy.gameObject != null && !activeDecoy.IsDisintegrating)
            activeDecoy.Disintegrate();

        GameObject decoyObj = new GameObject("Decoy");
        decoyObj.transform.position = spawnPos;
        decoyObj.layer = LayerMask.NameToLayer("Default");

        DecoyDevice decoy = decoyObj.AddComponent<DecoyDevice>();
        decoy.Initialize(
            attractRadius: data.decoyAttractRadius,
            duration: data.decoyDuration,
            bossDuration: data.decoyBossDuration,
            armDelay: data.decoyArmDelay,
            bossVFXOffset: data.decoyBossVFXOffset
        );

        activeDecoy = decoy;
    }
}
