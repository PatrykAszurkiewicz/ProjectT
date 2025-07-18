using System.Collections;
using UnityEngine;

public class Weapon : MonoBehaviour
{
    [SerializeField] private PolygonCollider2D attackCollider;
    [SerializeField] private GameObject visual; //sprite
    [SerializeField] private float attackDuration = 0.2f;

    private void Awake()
    {
        attackCollider.enabled = false;
    }

    public void PerformAttack()
    {
        StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        attackCollider.enabled = true;

        yield return new WaitForSeconds(attackDuration);

        attackCollider.enabled = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Enemy"))
        {
            Debug.Log("Trafiono przeciwnika: " + other.name);
            // Mo¿esz tu dodaæ np. DamageSystem lub event
        }
    }
}
