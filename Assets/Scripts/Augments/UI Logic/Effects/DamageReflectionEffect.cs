using UnityEngine;

public class DamageReflectionEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float reflectionPercentage = 0f;

    private PlayerStats playerStats;

    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        if (playerStats == null)
        {
            Debug.LogError("[DAMAGE_REFLECTION] PlayerStats not found!");
            enabled = false;
        }
    }

    void Start()
    {
        // Validate that percentage was set from CSV
        if (Mathf.Approximately(reflectionPercentage, 0f))
        {
            Debug.LogWarning("[DAMAGE_REFLECTION] reflectionPercentage is 0! CSV value may not have been applied.");
        }

        //Debug.Log($"[DAMAGE_REFLECTION] Active with {reflectionPercentage * 100f:F1}% damage reflection");
    }

    // This method should be called when the player takes damage
    public void ReflectDamage(float damageTaken, GameObject attacker)
    {
        if (attacker == null || reflectionPercentage <= 0f) return;

        // Calculate reflected damage
        float reflectedDamage = damageTaken * reflectionPercentage;

        if (reflectedDamage <= 0f) return;

        // Try to damage the attacker
        var enemyStats = attacker.GetComponent<EnemyStats>();
        if (enemyStats != null)
        {
            enemyStats.TakeDamage(reflectedDamage);
            //Debug.Log($"[DAMAGE_REFLECTION] Reflected {reflectedDamage:F1} damage ({reflectionPercentage * 100f:F1}% of {damageTaken:F1}) to {attacker.name}");

            // Visual/audio feedback
            PlayReflectionEffect(attacker.transform.position);
        }
        else
        {
            // Try IDamageable for special enemies like Gremlin
            var damageable = attacker.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(reflectedDamage, gameObject);
                //Debug.Log($"[DAMAGE_REFLECTION] Reflected {reflectedDamage:F1} damage to {attacker.name} via IDamageable");
                PlayReflectionEffect(attacker.transform.position);
            }
        }
    }

    private void PlayReflectionEffect(Vector3 position)
    {
        // TODO: Add particle effect at reflection point
        // GameObject reflectionEffect = Instantiate(reflectionEffectPrefab, position, Quaternion.identity);

        // Play reflection sound if available
        if (AudioManager.instance != null && FMODEvents.instance != null)
        {
            // TODO: Add reflection sound to FMODEvents
            // AudioManager.instance.PlayOneShot(FMODEvents.instance.damageReflection, position);
        }
    }

    // Public getter for UI
    public float GetReflectionPercentage() => reflectionPercentage;
}
