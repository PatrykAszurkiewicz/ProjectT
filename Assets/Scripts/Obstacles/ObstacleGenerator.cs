using UnityEngine;

public class ObstacleGenerator : MonoBehaviour
{
    [Header("Ustawienia")]
    public GameObject[] obstaclePrefabs;
    public int obstacleCount = 5;
    public float minDistanceFromCore = 5f;
    public float minDistanceBetweenObstacles = 2f;
    public float mapRange = 20f;

    private Transform coreTransform;

    public void GenerateObstacles()
    {
        coreTransform = GameObject.FindGameObjectWithTag("Core").transform;

        int placed = 0;
        int safetyCounter = 0;

        while (placed < obstacleCount && safetyCounter < obstacleCount * 20)
        {
            safetyCounter++;

            Vector2 randomPos = new Vector2(
                Random.Range(-mapRange, mapRange),
                Random.Range(-mapRange, mapRange)
            );



            if (Vector2.Distance(randomPos, coreTransform.position) < minDistanceFromCore)
            {
                continue;
            }

            Collider2D hit = Physics2D.OverlapCircle(randomPos, minDistanceBetweenObstacles);
            if (hit != null && (hit.CompareTag("Obstacle") || hit.CompareTag("Player")))
            {
                continue;
            }

            GameObject prefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];

            GameObject obs = Instantiate(prefab, randomPos, Quaternion.identity);

            // Y-Sort dynamically sort against grass based on Y position.
            if (obs.GetComponent<YSortEntity>() == null)
            {
                var ysort = obs.AddComponent<YSortEntity>();
                ysort.sortPrecision = 10f;
                ysort.sortOrderBase = 1000;
                ysort.sortYOffset = -0.5f;
            }

            placed++;
        }
    }

    private void OnDrawGizmos()
    {
        if (coreTransform == null)
        {
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null) coreTransform = core.transform;
        }

        if (coreTransform != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(coreTransform.position, minDistanceFromCore);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(coreTransform.position, mapRange);
        }

        Gizmos.color = Color.cyan;

        GameObject[] allObstacles = GameObject.FindGameObjectsWithTag("Obstacle");
        foreach (var obstacle in allObstacles)
        {
            Gizmos.DrawWireSphere(obstacle.transform.position, minDistanceBetweenObstacles);
        }
    }
}