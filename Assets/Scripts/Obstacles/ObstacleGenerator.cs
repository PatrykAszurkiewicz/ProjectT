using UnityEngine;

public class ObstacleGenerator : MonoBehaviour
{
    [Header("Ustawienia")]
    public GameObject[] obstaclePrefabs; // lista prefab�w przeszk�d
    public int obstacleCount = 5;       // ile przeszk�d wygenerowa�
    public float minDistanceFromCore = 5f; // minimalna odleg�o�� od Core
    public float minDistanceBetweenObstacles = 2f; // minimalny odst�p mi�dzy przeszkodami
    public float mapRange = 20f; // zasi�g generacji (kwadrat: -mapRange do +mapRange)

    private Transform coreTransform;

    private void Start()
    {
        
    }

    public void GenerateObstacles()
    {
        coreTransform = GameObject.FindGameObjectWithTag("Core").transform;

        int placed = 0;
        int safetyCounter = 0; // zabezpieczenie przed niesko�czon� p�tl�

        while (placed < obstacleCount && safetyCounter < obstacleCount * 20)
        {
            safetyCounter++;

            // losowa pozycja w kwadracie
            Vector2 randomPos = new Vector2(
                Random.Range(-mapRange, mapRange),
                Random.Range(-mapRange, mapRange)
            );



            // sprawdzenie odleg�o�ci od Core
            if (Vector2.Distance(randomPos, coreTransform.position) < minDistanceFromCore)
            {
                continue;
            }

            // sprawdzenie czy w pobli�u nie ma innej przeszkody
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
        // znajd� Core, je�li nie jest przypisany
        if (coreTransform == null)
        {
            GameObject core = GameObject.FindGameObjectWithTag("Core");
            if (core != null) coreTransform = core.transform;
        }

        if (coreTransform != null)
        {
            // promie�, w kt�rym przeszkody NIE mog� si� pojawi�
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(coreTransform.position, minDistanceFromCore);

            // promie� maksymalny mapRange
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(coreTransform.position, mapRange);
        }

        Gizmos.color = Color.cyan;

        // Zak�adamy, �e wszystkie przeszkody s� w warstwie "Obstacles"
        GameObject[] allObstacles = GameObject.FindGameObjectsWithTag("Obstacle");
        foreach (var obstacle in allObstacles)
        {
            Gizmos.DrawWireSphere(obstacle.transform.position, minDistanceBetweenObstacles);
        }
    }
}
