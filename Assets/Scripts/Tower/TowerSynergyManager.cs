using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
#if UNITY_EDITOR
using UnityEditor;
#endif
public class TowerSynergyManager : MonoBehaviour
{
    [System.NonSerialized]
    public float damageMultiplier = 1.0f;
    private const float SYNERGY_RANGE = 4f;

    void Start()
    {
        UpdateTowerSynergies();
        InvokeRepeating(nameof(UpdateTowerSynergies), 0.5f, 0.5f);
    }

    void UpdateTowerSynergies()
    {
        Tower[] allTowers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        //Debug.Log($"[SYNERGY] Checking {allTowers.Length} towers, multiplier={damageMultiplier}");

        foreach (Tower tower in allTowers)
        {
            if (tower == null || tower.IsDestroyed()) continue;

            int nearbyCount = 0;
            foreach (Tower otherTower in allTowers)
            {
                if (otherTower == tower || otherTower == null || otherTower.IsDestroyed())
                    continue;

                float distance = Vector3.Distance(tower.transform.position, otherTower.transform.position);
                if (distance <= SYNERGY_RANGE)
                {
                    nearbyCount++;
                    //Debug.Log($"[SYNERGY] Tower at {tower.transform.position} has nearby tower at distance {distance}");
                }
            }

            //Debug.Log($"[SYNERGY] Tower '{tower.towerName}' has {nearbyCount} nearby towers");

            var synergyComp = tower.GetComponent<TowerSynergyBoost>();

            if (nearbyCount > 0 && synergyComp == null)
            {
                synergyComp = tower.gameObject.AddComponent<TowerSynergyBoost>();
                synergyComp.damageMultiplier = Mathf.Pow(damageMultiplier, nearbyCount);

                //Debug.Log($"[SYNERGY] ADDED boost to '{tower.towerName}': {synergyComp.damageMultiplier}x (from {nearbyCount} nearby towers)");

                var renderer = tower.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.color = Color.Lerp(Color.white, Color.yellow, 0.3f);
                }
            }
            else if (nearbyCount > 0 && synergyComp != null)
            {
                float newMultiplier = Mathf.Pow(damageMultiplier, nearbyCount);
                if (Mathf.Abs(synergyComp.damageMultiplier - newMultiplier) > 0.01f)
                {
                    //Debug.Log($"[SYNERGY] UPDATED boost on '{tower.towerName}': {synergyComp.damageMultiplier}x -> {newMultiplier}x");
                    synergyComp.damageMultiplier = newMultiplier;
                }
            }
            else if (nearbyCount == 0 && synergyComp != null)
            {
                //Debug.Log($"[SYNERGY] REMOVED boost from '{tower.towerName}'");
                var renderer = tower.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.color = Color.white;
                }
                Destroy(synergyComp);
            }
        }
    }
}

public class TowerSynergyBoost : MonoBehaviour
{
    public float damageMultiplier = 1.0f;

    public float GetDamageMultiplier()
    {
        return damageMultiplier;
    }
}