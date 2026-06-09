using UnityEngine;

// Torch tool system (right-click slot). While equipped it casts a flickering
// circle of light around the PLAYER (in addition to the night-mode hand-torch
// cone), and on use it drops a PlacedTorch on the map. Up to data.torchMaxPlaced
// torches can exist at once.
public class TorchPlacerSystem
{
    // References
    private readonly Weapon weapon;
    private readonly WeaponData data;
    private readonly Transform playerTransform;

    // The equip-bound circle of light around the player.
    private NightOverlay.NightLightHandle playerLight;
    private readonly float flickerSeed;

    public TorchPlacerSystem(Weapon weapon, WeaponData data)
    {
        this.weapon = weapon;
        this.data = data;
        this.playerTransform = weapon.transform.parent ?? weapon.transform;
        this.flickerSeed = Random.value * 100f;

        TryRegisterPlayerLight();
    }

    public bool CanFire() => true;

    public void Update()
    {
        // The night overlay may not have existed when we equipped (e.g. it's
        // daytime). Keep trying so the aura appears the moment night falls.
        if (playerLight == null) TryRegisterPlayerLight();

        if (playerLight != null)
        {
            float t = Time.time;
            float speed = data.torchFlickerSpeed > 0f ? data.torchFlickerSpeed : 6f;
            float amount = Mathf.Clamp01(data.torchFlickerAmount);

            float noise = Mathf.PerlinNoise(flickerSeed, t * speed) * 2f - 1f;
            float breath = Mathf.Sin(t * speed * 0.4f + flickerSeed);
            float flicker = (noise * 0.7f + breath * 0.3f) * amount;

            playerLight.position = playerTransform.position;
            playerLight.radius = data.torchPlayerLightRadius * (1f + flicker);
            playerLight.intensity = data.torchPlayerLightIntensity * (1f + flicker);
            playerLight.color = data.torchLightColor;
        }
    }

    public void Cleanup()
    {
        // The player aura is bound to having the torch equipped — drop it.
        if (playerLight != null)
        {
            NightOverlay.UnregisterLight(playerLight);
            playerLight = null;
        }

    }

    public void PlaceTorch()
    {
        Vector3 spawnPos = playerTransform.position;

        int maxPlaced = data.torchMaxPlaced > 0 ? data.torchMaxPlaced : 3;

        PlacedTorch.Spawn(
            position: spawnPos,
            bodySprite: data.sprite,
            lightRadius: data.torchPlacedLightRadius,
            lightIntensity: data.torchPlacedLightIntensity,
            lightColor: data.torchLightColor,
            flickerSpeed: data.torchFlickerSpeed,
            flickerAmount: data.torchFlickerAmount,
            maxActive: maxPlaced);
    }

    private void TryRegisterPlayerLight()
    {
        if (playerLight != null) return;
        playerLight = NightOverlay.RegisterLight(
            position: playerTransform.position,
            radius: data.torchPlayerLightRadius,
            intensity: data.torchPlayerLightIntensity,
            color: data.torchLightColor,
            warmTintStrength: 0.5f);
    }
}
