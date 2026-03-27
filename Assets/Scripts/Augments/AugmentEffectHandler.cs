using UnityEngine;

public class AugmentEffectHandler : MonoBehaviour
{
    private ObstacleGenerator obstacleGenerator;

    private void Awake()
    {
        obstacleGenerator = FindAnyObjectByType<ObstacleGenerator>();
    }

    public void ApplyAugmentEffect(int augmentId)
    {
        switch (augmentId)
        {
            // ── Weapon unlocks ─────────────────────────────────────────────
            // These augments have NULL effects in the CSV so AugmentRegistry
            // never fires OnAugmentApplied for them. We call WeaponUnlockRegistry
            // directly here — it unlocks the slot and WeaponRollController
            // automatically hot-swaps the weapon via its OnUnlocksChanged handler.
            case 2:
            case 4:
            case 65:
            case 66:
            case 81:
                WeaponUnlockRegistry.Instance?.TryUnlock(augmentId);
                break;

            // ── Obstacle generation ────────────────────────────────────────
            case 3:
                if (obstacleGenerator != null)
                    obstacleGenerator.GenerateObstacles();
                break;

            // ── Other augments ─────────────────────────────────────────────
            case 11:
                ApplyImmunityPhases();
                break;

            case 37:
                ApplyQuickRevive();
                break;

            default:
                Debug.Log($"No special effect defined for augment {augmentId}");
                break;
        }
    }

    private void ApplyQuickRevive()
    {
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[AUGMENT] Could not find PlayerStats for Quick Revive");
            return;
        }

        if (playerStats.gameObject.GetComponent<QuickReviveEffect>() != null)
        {
            Debug.LogWarning("[AUGMENT] Quick Revive already active - cannot stack");
            return;
        }

        playerStats.gameObject.AddComponent<QuickReviveEffect>();
    }

    private void ApplyImmunityPhases()
    {
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[AUGMENT] Could not find PlayerStats for Immunity Phases!");
            return;
        }

        if (playerStats.gameObject.GetComponent<ImmunityPhasesEffect>() != null)
        {
            Debug.LogWarning("[AUGMENT] Immunity Phases already active - cannot stack");
            return;
        }

        playerStats.gameObject.AddComponent<ImmunityPhasesEffect>();
    }
}
