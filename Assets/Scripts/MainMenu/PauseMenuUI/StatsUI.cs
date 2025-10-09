using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class StatsUI : MonoBehaviour
{
    [Header("Referencje")]
    public PlayerStats playerStats;
    public GameObject statTextPrefab; // prefab z TMP_Text
    public Transform container; // rodzic (np. z GridLayoutGroup)

    [HideInInspector]
    public List<TextMeshProUGUI> textObjects = new List<TextMeshProUGUI>();

    private void Start()
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

    private TextMeshProUGUI GetOrCreateTextAt(int index)
    {
        // jeœli lista za krótka – uzupe³nij j¹ prefabami
        while (textObjects.Count <= index)
        {
            GameObject newTextObj = Instantiate(statTextPrefab, container);
            TextMeshProUGUI tmp = newTextObj.GetComponent<TextMeshProUGUI>();
            textObjects.Add(tmp);
        }

        return textObjects[index];
    }

    public void InitializeUI()
    {
        // U¿ywaj dowolnej liczby indeksów – reszta wygeneruje siê automatycznie
        GetOrCreateTextAt(0).text = $"Health: {playerStats.maxHealth:F1}";
        GetOrCreateTextAt(1).text = $"Armor: {playerStats.currentArmor:F1}";
        GetOrCreateTextAt(2).text = $"Speed: {playerStats.moveSpeed:F1}";
    }

    public void RefreshUI()
    {
        InitializeUI();
    }
}
