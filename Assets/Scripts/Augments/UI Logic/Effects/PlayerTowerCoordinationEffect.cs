using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System.Collections;

public class PlayerTowerCoordinationEffect : MonoBehaviour
{
    [System.NonSerialized]
    public float playerArmorBonus = 0f; // Flat armor for player (from CSV)
    [System.NonSerialized]
    public float towerArmorBonus = 0f; // Percentage armor for towers (from CSV)

    private const float RANGE = 3.5f;

    private PlayerStats playerStats;
    private float basePlayerArmor;

    void Start()
    {
        playerStats = GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            basePlayerArmor = playerStats.currentArmor;
            Debug.Log($"[COORDINATION] Effect started. Base player armor: {basePlayerArmor}, Bonus: +{playerArmorBonus:F1} flat, Tower bonus: +{towerArmorBonus:F3}");
        }

        InvokeRepeating(nameof(UpdateCoordination), 0f, 0.3f);
    }

    void UpdateCoordination()
    {
        if (playerStats == null) return;

        Tower[] towers = FindObjectsByType<Tower>(FindObjectsSortMode.None);
        Vector3 playerPos = transform.position;
        int nearbyCount = 0;

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsDestroyed()) continue;

            float distance = Vector3.Distance(playerPos, tower.transform.position);
            bool shouldBoost = distance <= RANGE;

            var boostComp = tower.GetComponent<CoordinationArmorBoost>();

            if (shouldBoost)
            {
                nearbyCount++;

                if (boostComp == null)
                {
                    float towerArmorBefore = tower.armorReduction;

                    boostComp = tower.gameObject.AddComponent<CoordinationArmorBoost>();
                    boostComp.armorBonus = towerArmorBonus; // Use the CSV value
                    boostComp.ApplyBoost(tower);

                    float towerArmorAfter = tower.armorReduction;
                    Debug.Log($"[COORDINATION] Tower '{tower.towerName}': {towerArmorBefore:F3} -> {towerArmorAfter:F3} (clamped to max 1.0)");

                    var renderer = tower.GetComponent<SpriteRenderer>();
                    if (renderer != null)
                    {
                        renderer.color = Color.Lerp(Color.white, new Color(0.7f, 0.8f, 1f), 0.35f);
                    }
                }
                else
                {
                    // Update existing boost if the bonus changed
                    boostComp.armorBonus = towerArmorBonus;
                    boostComp.ApplyBoost(tower);
                }
            }
            else if (!shouldBoost && boostComp != null)
            {
                boostComp.RemoveBoost(tower);

                var renderer = tower.GetComponent<SpriteRenderer>();
                if (renderer != null) renderer.color = Color.white;

                Destroy(boostComp);
            }
        }

        // Apply player armor bonus
        float newPlayerArmor = nearbyCount > 0 ? basePlayerArmor + playerArmorBonus : basePlayerArmor;

        if (Mathf.Abs(playerStats.currentArmor - newPlayerArmor) > 0.01f)
        {
            playerStats.currentArmor = newPlayerArmor;
            Debug.Log($"[COORDINATION] Player armor: {newPlayerArmor:F1} ({nearbyCount} towers, bonus: +{playerArmorBonus:F1} flat)");
        }
    }

    void OnDestroy()
    {
        if (playerStats != null)
        {
            playerStats.currentArmor = basePlayerArmor;
        }
    }
}

public class CoordinationArmorBoost : MonoBehaviour
{
    public float armorBonus = 0f; // From CSV, will be added to tower's armor
    private float originalArmor = -1f;

    public void ApplyBoost(Tower tower)
    {
        if (tower == null) return;
        if (originalArmor < 0f) originalArmor = tower.armorReduction;

        // Add bonus and CLAMP to prevent going above 1.0 (which would cause extra damage)
        float newArmor = originalArmor + armorBonus;
        tower.armorReduction = Mathf.Clamp(newArmor, 0f, 1.0f);

        if (newArmor > 1.0f)
        {
            Debug.LogWarning($"[COORDINATION] Tower armor capped at 100% (was trying to set {newArmor:F3})");
        }
    }

    public void RemoveBoost(Tower tower)
    {
        if (tower == null || originalArmor < 0f) return;
        tower.armorReduction = originalArmor;
    }
}


