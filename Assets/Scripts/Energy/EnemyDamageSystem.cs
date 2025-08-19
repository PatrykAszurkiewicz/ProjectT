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
        if (consumer == null || EnergyManager.Instance == null)
        {
            Debug.LogWarning("Cannot damage energy consumer - consumer or EnergyManager is null");
            return;
        }

        // Try to get the IEnergyConsumer component from the GameObject
        var energyConsumer = consumer.GetComponent<IEnergyConsumer>();
        if (energyConsumer != null)
        {
            // Use EnergyManager's existing damage system
            bool wasDestroyed = EnergyManager.Instance.DamageEnergyConsumer(energyConsumer, damage, attacker);

            if (wasDestroyed)
            {
                Debug.Log($"Energy consumer {consumer.name} was destroyed by {attacker?.name ?? "unknown attacker"}");
            }
        }
        else
        {
            Debug.LogWarning($"GameObject {consumer.name} does not have an IEnergyConsumer component - cannot damage with energy system");
        }
    }

    // Overload that takes IEnergyConsumer directly
    public void DamageEnergyConsumer(IEnergyConsumer consumer, float damage, GameObject attacker)
    {
        if (consumer == null || EnergyManager.Instance == null)
        {
            Debug.LogWarning("Cannot damage energy consumer - consumer or EnergyManager is null");
            return;
        }

        bool wasDestroyed = EnergyManager.Instance.DamageEnergyConsumer(consumer, damage, attacker);

        if (wasDestroyed)
        {
            Debug.Log($"Energy consumer was destroyed by {attacker?.name ?? "unknown attacker"}");
        }
    }

    // Helper method to check if a GameObject can be damaged by the energy system
    public bool CanDamageWithEnergySystem(GameObject target)
    {
        return target != null && target.GetComponent<IEnergyConsumer>() != null;
    }

    // Helper method for enemies to use - automatically detects the right damage system
    public void DamageTarget(GameObject target, float damage, GameObject attacker)
    {
        if (target == null) return;

        // Check if it's an energy consumer (Tower/Central Core)
        var energyConsumer = target.GetComponent<IEnergyConsumer>();
        if (energyConsumer != null)
        {
            DamageEnergyConsumer(energyConsumer, damage, attacker);
            return;
        }

        // Check if it's a character with health (Player/Enemy)
        var characterStats = target.GetComponent<CharacterStats>();
        if (characterStats != null)
        {
            characterStats.TakeDamage(damage);
            return;
        }

        Debug.LogWarning($"Cannot damage {target.name} - no valid damage system found");
    }
}
