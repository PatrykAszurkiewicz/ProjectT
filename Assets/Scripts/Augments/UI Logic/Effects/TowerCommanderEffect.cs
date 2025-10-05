using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System.Collections;


public class TowerCommanderEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float energyDecayMultiplier = 1.0f; // This will be set from CSV (e.g., 0.8 for 20% reduction)
    private const float RANGE = 2.5f;

    void Start()
    {
        Debug.Log($"[TOWER_COMMANDER] Start() called! energyDecayMultiplier={energyDecayMultiplier}");
        Debug.Log($"[TOWER_COMMANDER] Component on object: {gameObject.name}, enabled: {enabled}");

        //Debug.Log($"[TOWER_COMMANDER] TowerCommanderEffect started! Decay multiplier: {energyDecayMultiplier}, Range: {RANGE}");
        // Validate the energy decay multiplier value
        if (energyDecayMultiplier <= 0f)
        {
            //Debug.LogWarning($"[TOWER_COMMANDER] Invalid energyDecayMultiplier value: {energyDecayMultiplier}, setting to 1.0 (no reduction)");
            energyDecayMultiplier = 1.0f;
        }

        // Do an immediate update and then start the repeating updates
        UpdateTowers();
        InvokeRepeating(nameof(UpdateTowers), 0.1f, 0.3f);
        //Debug.Log($"[TOWER_COMMANDER] InvokeRepeating set up successfully");

    }
    void UpdateTowers()
    {
        //Debug.Log($"[TOWER_COMMANDER] === UpdateTowers called! ===");

        try
        {
            Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
            Vector3 playerPos = transform.position;

            //Debug.Log($"[TOWER_COMMANDER] Found {towers.Length} towers, player at {playerPos}");

            foreach (Tower tower in towers)
            {
                if (tower == null || tower.IsDestroyed())
                {
                    //Debug.Log($"[TOWER_COMMANDER] Skipping null/destroyed tower");
                    continue;
                }

                float distance = Vector3.Distance(playerPos, tower.transform.position);
                bool shouldBoost = distance <= RANGE;

                //Debug.Log($"[TOWER_COMMANDER] Tower '{tower.towerName}' at distance {distance:F2}, shouldBoost={shouldBoost}, RANGE={RANGE}");

                var boostComp = tower.GetComponent<TowerCommanderBoost>();

                if (shouldBoost && boostComp == null)
                {
                    //Debug.Log($"[TOWER_COMMANDER] ✓ ADDING boost to {tower.towerName}");
                    boostComp = tower.gameObject.AddComponent<TowerCommanderBoost>();
                    boostComp.energyDecayMultiplier = energyDecayMultiplier;

                    var renderer = tower.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.color = Color.Lerp(Color.white, Color.cyan, 0.3f);
                    }
                }
                else if (!shouldBoost && boostComp != null)
                {
                    //Debug.Log($"[TOWER_COMMANDER] ✗ REMOVING boost from {tower.towerName}");
                    var renderer = tower.GetComponent<SpriteRenderer>();
                    if (renderer != null) renderer.color = Color.white;

                    Destroy(boostComp);
                }
                else if (shouldBoost && boostComp != null)
                {
                    //Debug.Log($"[TOWER_COMMANDER] Tower {tower.towerName} already has boost");
                }
                else
                {
                    //Debug.Log($"[TOWER_COMMANDER] Tower {tower.towerName} out of range, no boost to remove");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TOWER_COMMANDER] UpdateTowers exception: {e.Message}\n{e.StackTrace}");
        }
    }

    // Public method to force an immediate update when new towers are built
    public void ForceUpdate()
    {
        UpdateTowers();
    }
    void OnDisable()
    {
        CancelInvoke(nameof(UpdateTowers));
    }
}

public class TowerCommanderBoost : MonoBehaviour
{
    public float energyDecayMultiplier = 1.0f;

    public float GetEnergyDecayMultiplier()
    {
        return energyDecayMultiplier;
    }
}

