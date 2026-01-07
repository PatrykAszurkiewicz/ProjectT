using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PheromoneControlEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float confusionChance = 0.25f;

    [Header("Aura Settings")]
    [SerializeField] private float auraRadius = 5f;
    [SerializeField] private float confusionDuration = 5f;
    [SerializeField] private float checkInterval = 1f;

    private float checkTimer = 0f;

    private void Start()
    {
        //Debug.Log($"[PHEROMONE_CONTROL] Effect started - Aura radius: {auraRadius}m, Confusion chance: {confusionChance * 100f:F1}%, Duration: {confusionDuration}s");
    }

    private void Update()
    {
        checkTimer += Time.deltaTime;

        if (checkTimer >= checkInterval)
        {
            checkTimer = 0f;
            CheckForConfusion();
        }
    }

    private void CheckForConfusion()
    {
        // Find all enemies within aura radius
        var nearbyEnemies = FindObjectsByType<EnemyStats>(FindObjectsSortMode.None)
            .Where(e => e != null &&
                        !e.IsDead() &&
                        Vector2.Distance(transform.position, e.transform.position) <= auraRadius &&
                        e.GetComponent<ConfusedEnemy>() == null && // Don't re-confuse
                        e.GetComponent<BerserkEnemy>() == null && // Don't confuse already berserk
                        e.GetComponent<GremlinController>() == null) // Exclude gremlins
            .ToList();

        foreach (var enemy in nearbyEnemies)
        {
            float roll = UnityEngine.Random.value;
            if (roll <= confusionChance)
            {
                ConfuseEnemy(enemy);
            }
        }
    }

    private void ConfuseEnemy(EnemyStats enemy)
    {
        if (enemy == null) return;

        // Decide confusion type: ignore player (50%) OR attack others (50%)
        bool shouldAttackOthers = UnityEngine.Random.value > 0.5f;

        if (shouldAttackOthers)
        {
            // Make enemy go berserk (attack other enemies)
            var berserk = enemy.gameObject.AddComponent<BerserkEnemy>();
            berserk.Initialize(confusionDuration);
            //Debug.Log($"[PHEROMONE_CONTROL] {enemy.gameObject.name} confused - attacking others");
        }
        else
        {
            // Make enemy ignore player
            var confused = enemy.gameObject.AddComponent<ConfusedEnemy>();
            confused.Initialize(confusionDuration);
            //Debug.Log($"[PHEROMONE_CONTROL] {enemy.gameObject.name} confused - ignoring player");
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw aura radius in purple
        Gizmos.color = new Color(1f, 0f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, auraRadius);
        UnityEditor.Handles.Label(transform.position + Vector3.up * (auraRadius + 0.5f),
            $"Pheromone Aura\n{confusionChance * 100f:F0}% confusion");
    }
#endif
}
