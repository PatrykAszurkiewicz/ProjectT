using UnityEngine;

// Insect —  enemy that completely ignores the player and walks straight
// toward the nearest tower or the central core, then melee-attacks that structure.
[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyStats))]
public class InsectController : MonoBehaviour
{
    private EnemyController enemyController;
    private Transform coreTarget;

    private void Awake()
    {
        // Assign in Awake so the provider is in place before EnemyController.Start
        // schedules its first UpdateTarget tick — guarantees the very first target
        // resolution already skips the player.
        enemyController = GetComponent<EnemyController>();
        if (enemyController != null)
            enemyController.PriorityTargetProvider = GetClosestStructure;
    }

    private void Start()
    {
        CacheCore();
    }

    private void OnDestroy()
    {
        // Be a good citizen: drop the delegate so nothing holds a stale reference
        // to this (now destroyed) component.
        if (enemyController != null)
            enemyController.PriorityTargetProvider = null;
    }

    private void CacheCore()
    {
        GameObject core = GameObject.FindGameObjectWithTag("Core");
        if (core != null) coreTarget = core.transform;
    }

    // Returns the  closest live structure: any non-destroyed tower or the core
    private Transform GetClosestStructure()
    {
        float bestDist = Mathf.Infinity;
        Transform best = null;

        GameObject[] towers = GameObject.FindGameObjectsWithTag("Tower");
        foreach (var t in towers)
        {
            if (t == null || !t.activeInHierarchy) continue;

            var tower = t.GetComponent<Tower>();
            if (tower != null && tower.IsDestroyed()) continue;

            float d = Vector2.Distance(transform.position, t.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = t.transform;
            }
        }

        // Core may not have been found yet (spawned before the core, or scene
        // reload) — try once more on demand.
        if (coreTarget == null) CacheCore();

        if (coreTarget != null)
        {
            float dc = Vector2.Distance(transform.position, coreTarget.position);
            if (dc < bestDist)
            {
                bestDist = dc;
                best = coreTarget;
            }
        }

        return best != null ? best : coreTarget;
    }
}
