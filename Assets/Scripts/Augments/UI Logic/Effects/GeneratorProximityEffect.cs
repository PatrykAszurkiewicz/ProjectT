using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System.Collections;

public class GeneratorProximityEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float energyEfficiencyMultiplier = 1.0f; // 0.8 = 20% reduction
    private const float RANGE = 4f;

    void Start()
    {
        //Debug.Log($"[GENERATOR_PROXIMITY] Started with {energyEfficiencyMultiplier:F2}x efficiency, Range: {RANGE}");
        InvokeRepeating(nameof(UpdateTowers), 0.1f, 0.3f);
    }

    void UpdateTowers()
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        Tower[] generators = System.Array.FindAll(towers, t => t != null && !t.IsDestroyed() && t.IsGenerator());

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed() || tower.IsGenerator()) continue;

            bool nearGenerator = false;
            foreach (Tower generator in generators)
            {
                float distance = Vector3.Distance(tower.transform.position, generator.transform.position);
                if (distance <= RANGE)
                {
                    nearGenerator = true;
                    break;
                }
            }

            var boostComp = tower.GetComponent<GeneratorProximityBoost>();

            if (nearGenerator && boostComp == null)
            {
                boostComp = tower.gameObject.AddComponent<GeneratorProximityBoost>();
                boostComp.energyEfficiencyMultiplier = energyEfficiencyMultiplier;

                var renderer = tower.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.color = Color.Lerp(Color.white, new Color(0.3f, 0.7f, 1f), 0.3f); // Light blue tint
                }

                //Debug.Log($"[GENERATOR_PROXIMITY] Added boost to {tower.towerName} with {energyEfficiencyMultiplier:F2}x efficiency");
            }
            else if (!nearGenerator && boostComp != null)
            {
                var renderer = tower.GetComponent<SpriteRenderer>();
                if (renderer != null) renderer.color = Color.white;
                Destroy(boostComp);
                //Debug.Log($"[GENERATOR_PROXIMITY] Removed boost from {tower.towerName}");
            }
            else if (nearGenerator && boostComp != null)
            {
                // Update boost value if it changed
                if (Mathf.Abs(boostComp.energyEfficiencyMultiplier - energyEfficiencyMultiplier) > 0.01f)
                {
                    boostComp.energyEfficiencyMultiplier = energyEfficiencyMultiplier;
                }
            }
        }
    }

    void OnDisable()
    {
        CancelInvoke(nameof(UpdateTowers));
    }
}

public class GeneratorProximityBoost : MonoBehaviour
{
    public float energyEfficiencyMultiplier = 1.0f; // Values < 1.0 reduce consumption

    public float GetEnergyEfficiencyMultiplier()
    {
        return energyEfficiencyMultiplier;
    }
}