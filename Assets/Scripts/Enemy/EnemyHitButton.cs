using UnityEngine;

public class EnemyHitButton : MonoBehaviour
{
    EnemyStats estats;
    public float dmg = 35f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        estats = FindAnyObjectByType<EnemyStats>();
    }

    public void HitE()
    {
        estats.TakeDamage(dmg);
    }
}
