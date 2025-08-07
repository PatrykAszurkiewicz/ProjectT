using UnityEngine;

public class EnemyDamageSystem : MonoBehaviour
{
    public static EnemyDamageSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void DamageEnergyConsumer(GameObject consumer, float damage, GameObject attacker)
    {
        // Przyk�ad: znajd� komponent zdrowia i zadaj obra�enia
        var health = consumer.GetComponent<Health>();
        if (health != null)
        {
            health.TakeDamage(damage);
        }

        // Mo�na te� doda� logik� np. podbicia (knockback) lub efekt�w wizualnych
    }
}
