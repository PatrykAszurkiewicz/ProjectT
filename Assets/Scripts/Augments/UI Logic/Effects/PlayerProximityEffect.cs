using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System.Collections;


// Base class for player proximity effects on towers
public abstract class PlayerProximityEffect : MonoBehaviour
{
    protected const float RANGE = 2.5f;
    protected float updateInterval = 0.3f;

    protected virtual float GetRange() => RANGE;

    void Start()
    {
        OnEffectStart();
        InvokeRepeating(nameof(UpdateTowers), 0f, updateInterval);
    }

    protected virtual void OnEffectStart() { }

    void UpdateTowers()
    {
        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        Vector3 playerPos = transform.position;

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed()) continue;

            float distance = Vector3.Distance(playerPos, tower.transform.position);
            bool shouldBoost = distance <= GetRange();

            UpdateTowerBoost(tower, shouldBoost);
        }

        // TODO - potentially update player based on nearby tower count
        OnProximityUpdate(towers, playerPos);
    }

    protected abstract void UpdateTowerBoost(Tower tower, bool shouldBoost);
    protected virtual void OnProximityUpdate(Tower[] allTowers, Vector3 playerPos) { }
}

