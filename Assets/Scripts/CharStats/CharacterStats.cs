using UnityEngine;
using System;

public class CharacterStats : MonoBehaviour
{
    [Header("HP")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float currentArmor = 0f;

    public event Action<float, float> OnHealthChanged;
    // parametry: currentHealth, maxHealth

    public virtual void TakeDamage(float amount)
    {
        float mitigated = Mathf.Max(amount - currentArmor, 0f);
        currentHealth -= mitigated;
        currentHealth = Mathf.Max(currentHealth, 0f);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (IsDead())
        {
            Die();
        }
    }

    public virtual void Heal(float amount)
    {
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public virtual void Die()
    {
        Destroy(gameObject);
    }

    public virtual bool IsDead()
    {
        return currentHealth <= 0;
    }

    // Allows derived classes to trigger health changed event
    protected void TriggerHealthChangedEvent()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

}
