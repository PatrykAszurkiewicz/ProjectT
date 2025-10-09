using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StatsUI : MonoBehaviour
{
    [Header("Referencje")]
    public PlayerStats playerStats;

    public TextMeshProUGUI healthText;
    public TextMeshProUGUI armorText;

    public void Start()
    {
        if (playerStats == null)
        {
            playerStats = FindAnyObjectByType<PlayerStats>();
            if (playerStats == null)
            {
                Debug.LogError("Nie znaleziono PlayerStats!");
                enabled = false;
                return;
            }
        }
        InitializeUI();
    }

    public void InitializeUI()
    {
        healthText.text = $"Health: {playerStats.maxHealth:F1}";
        playerStats.currentHealth = playerStats.maxHealth;

        armorText.text = $"Armor: {playerStats.currentArmor:F1}";
    }

    public void RefreshUI()
    {
        InitializeUI();
    }

}
