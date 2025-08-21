using UnityEngine;
using System;

public class CharacterStats : MonoBehaviour
{
    [Header("HP")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float maxArmor = 0f;
    public float currentArmor = 0f;

    // Event UI
    public event Action<float, float> OnHealthChanged;
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
}
