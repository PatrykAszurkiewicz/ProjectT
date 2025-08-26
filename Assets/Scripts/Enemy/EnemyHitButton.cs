using UnityEngine;

public class EnemyHitButton : MonoBehaviour
{
    EnemyStats estats;
    public float dmg = 35f;

    void Start()
    {
        // Try to find enemy on parent/sibling first, then fallback to any enemy
        estats = GetComponentInParent<EnemyStats>() ?? GetComponentInChildren<EnemyStats>() ?? FindAnyObjectByType<EnemyStats>();
    }

    public void HitE()
    {
        // Refresh reference if it became null
        if (estats == null)
        {
            estats = FindAnyObjectByType<EnemyStats>();
        }

        // Safety check before dealing damage
        if (estats != null)
        {
            estats.TakeDamage(dmg);
        }
    }
}