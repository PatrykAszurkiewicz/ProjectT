using UnityEngine;

public class ObstacleGenerator : MonoBehaviour
{
    [Header("Ustawienia")]
    public GameObject[] obstaclePrefabs; // lista prefabów przeszkód
    public int obstacleCount = 5;       // ile przeszkód wygenerowaæ
    public float minDistanceFromCore = 5f; // minimalna odleg³oœæ od Core
    public float minDistanceBetweenObstacles = 2f; // minimalny odstêp miêdzy przeszkodami
    public float mapRange = 20f; // zasiêg generacji (kwadrat: -mapRange do +mapRange)

    private Transform coreTransform;

    private void Start()
    {
        
    }

    public void GenerateObstacles()
    {
        coreTransform = GameObject.FindGameObjectWithTag("Core").transform;

        int placed = 0;
        int safetyCounter = 0; // zabezpieczenie przed nieskoñczon¹ pêtl¹

        while (placed < obstacleCount && safetyCounter < obstacleCount * 20)
        {
            safetyCounter++;

            // losowa pozycja w kwadracie
            Vector2 randomPos = new Vector2(
                Random.Range(-mapRange, mapRange),
                Random.Range(-mapRange, mapRange)
            );



            // sprawdzenie odleg³oœci od Core
            if (Vector2.Distance(randomPos, coreTransform.position) < minDistanceFromCore)
            {
                continue;
            }

            // sprawdzenie czy w pobli¿u nie ma innej przeszkody
            Collider2D hit = Physics2D.OverlapCircle(randomPos, minDistanceBetweenObstacles);
            if (hit != null && (hit.CompareTag("Obstacle") || hit.CompareTag("Player")))
            {
                continue;
            }

            // losowy prefab
            GameObject prefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];

            // stworzenie przeszkody
            Instantiate(prefab, randomPos, Quaternion.identity);
            placed++;
        }
    }

    private void OnDrawGizmos()
    {
        // znajdŸ Core, jeœli nie jest przypisany
        if (coreTransform == null)
        {
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null) coreTransform = core.transform;
        }

        if (coreTransform != null)
        {
            // promieñ, w którym przeszkody NIE mog¹ siê pojawiæ
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(coreTransform.position, minDistanceFromCore);

            // promieñ maksymalny mapRange
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(coreTransform.position, mapRange);
        }

        Gizmos.color = Color.cyan;

        // Zak³adamy, ¿e wszystkie przeszkody s¹ w warstwie "Obstacles"
        GameObject[] allObstacles = GameObject.FindGameObjectsWithTag("Obstacle");
        foreach (var obstacle in allObstacles)
        {
            Gizmos.DrawWireSphere(obstacle.transform.position, minDistanceBetweenObstacles);
        }
    }
}
