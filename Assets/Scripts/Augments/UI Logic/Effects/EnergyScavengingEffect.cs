using UnityEngine;

public class EnergyScavengingEffect : MonoBehaviour
{
    [System.NonSerialized]
    public int energyAmount = 3;
    private const float GENERATOR_RANGE = 4f;

    void Start()
    {
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent += OnEnemyKilled;
            //Debug.Log($"[ENERGY_SCAVENGING] Effect started with {energyAmount} energy per kill near generator");
        }
    }

    void OnDestroy()
    {
        if (EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnEnemyKilledEvent -= OnEnemyKilled;
        }
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (enemy == null || EnergyManager.Instance == null) return;

        Vector3 deathPosition = enemy.transform.position;

        // Find all generator towers
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed() || !tower.IsGenerator()) continue;

            float distance = Vector3.Distance(deathPosition, tower.transform.position);
            if (distance <= GENERATOR_RANGE)
            {
                // Enemy died near a generator - award energy
                int reward = Mathf.RoundToInt(energyAmount * EnergyManager.Instance.globalResourceMultiplier);
                EnergyManager.Instance.GivePlayerEnergy(reward);
                //Debug.Log($"[ENERGY_SCAVENGING] Enemy killed near generator at distance {distance:F1}, awarded {reward} energy!");
                return; // Only award once per kill
            }
        }
    }
}