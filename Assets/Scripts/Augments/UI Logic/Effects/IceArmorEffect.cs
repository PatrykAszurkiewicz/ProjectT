using UnityEngine;

public class IceArmorEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float freezeDuration = 0f; // Set from CSV via StatApplicator

    [Header("Visual Feedback")]
    public Color iceEffectColor = Color.cyan;

    private PlayerStats playerStats;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[ICE_ARMOR] PlayerStats not found!");
            enabled = false;
        }
    }

    void Start()
    {
        // Validate that freeze duration was set from CSV
        if (Mathf.Approximately(freezeDuration, 0f))
        {
            Debug.LogError("[ICE_ARMOR] freezeDuration is 0! CSV value was NOT applied by StatApplicator!");
            enabled = false;
            return;
        }

        //Debug.Log($"[ICE_ARMOR] Active - freezes attackers for {freezeDuration}s when player is hit");
    }

    // This method is called when the player takes damage from an attacker
    public void FreezeAttacker(GameObject attacker)
    {
        if (attacker == null || freezeDuration <= 0f) return;

        var enemyController = attacker.GetComponent<EnemyController>();
        if (enemyController != null)
        {
            enemyController.ApplyFreeze(freezeDuration);

            //Debug.Log($"[ICE_ARMOR] Froze {attacker.name} for {freezeDuration}s");

            // Visual/audio feedback
            PlayIceEffect(attacker.transform.position);
        }
        else
        {
            Debug.LogWarning($"[ICE_ARMOR] {attacker.name} has no EnemyController - cannot freeze");
        }
    }

    private void PlayIceEffect(Vector3 position)
    {
        // Play ice armor sound if available
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            // TODO: Add ice armor sound to FMODEvents
            // AudioManager.instance.PlayOneShot(FMODEvents.instance.iceArmorProc, position);
        }

        // TODO: Add particle effect at attacker position
        // GameObject iceEffect = Instantiate(iceEffectPrefab, position, Quaternion.identity);
    }

    // Public getter for UI
    public float GetFreezeDuration() => freezeDuration;
}
