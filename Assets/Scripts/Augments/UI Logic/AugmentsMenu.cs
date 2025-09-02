using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class AugmentsMenu : MonoBehaviour
{
    public GameObject augmentsMenu;
    public GameObject hideShow;
    private bool hide = false;
    private bool augments = false;

    public int maxRerolls = 2;
    private int[] rerollsleft;

    [Header("UI - 3 sloty augmentów")]
    [SerializeField] private Image[] augmentImages;
    [SerializeField] private Image[] rarityImages;
    [SerializeField] private TextMeshProUGUI[] nameTexts;
    [SerializeField] private TextMeshProUGUI[] descriptionTexts;
    [SerializeField] private TextMeshProUGUI[] rerollNumberText;

    private static Sprite[] allSprites;
    private static Dictionary<int, AugmentData> augmentDatabase = new Dictionary<int, AugmentData>();

    private int[] currentAugmentIDs;
    private string[] currentSelectedRarities; // Przechowuje wylosowane rarity dla ka¿dego slotu

    
    private void Awake()
    {
        //LOAD csv info
        LoadAugmentsFromCSV();
        // load all sprites once (cache)
        if (allSprites == null)
            allSprites = Resources.LoadAll<Sprite>("Sprites/Augments");

        currentAugmentIDs = new int[augmentImages.Length];
        currentSelectedRarities = new string[augmentImages.Length];
        rerollsleft = new int[augmentImages.Length];

        for (int i = 0; i < rerollsleft.Length; i++)
        {
            rerollsleft[i] = maxRerolls;
        }

        // random augments to slots
        if (augmentImages != null && augmentImages.Length > 0)
        {
            List<int> keys = new List<int>(augmentDatabase.Keys);

            for (int i = 0; i < augmentImages.Length; i++)
            {
                int randomId = GetUniqueRandomId(keys, new List<int>(currentAugmentIDs));
                currentAugmentIDs[i] = randomId;

                // Wylosuj rarity dla tego augmentu
                string selectedRarity = GetRandomRarityForAugment(randomId);
                currentSelectedRarities[i] = selectedRarity;

                AssignAugmentToImage(augmentImages[i], randomId, selectedRarity);

                SetRarityColor(i, selectedRarity);

                SetAugmentName(i, randomId);

                SetAugmentDescription(i, randomId);

                UpdateRerollText(i);
            }
        }
    }

    void Start()
    {
        augmentsMenu.SetActive(false);
    }

    public void ActivateAugments()
    {
        if (augments == false)
        {
            augmentsMenu.SetActive(true);
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
    }

    public void HideShowButton()
    {
        if (hide == false)
        {
            hide = true;
            hideShow.SetActive(false);
        }
        else
        {
            hide = false;
            hideShow.SetActive(true);
        }
    }

    public void ChooseAugment()
    {
        //logic

        //----- hide menu, unpause game -----
        augmentsMenu.SetActive(false);
        Cursor.visible = false;
        Time.timeScale = 1f;
    }

    // ================= CSV Loader =================
    private void LoadAugmentsFromCSV()
    {
        if (augmentDatabase.Count > 0) return; //if loaded already

        //find file
        TextAsset csvFile = Resources.Load<TextAsset>("Data/Tower Defense Augments - AugmentsPrior0Only");
        if (csvFile == null)
        {
            Debug.LogError("CSV NOT FOUND!");
            return;
        }

        string[] lines = csvFile.text.Split('\n');
        for (int i = 1; i < lines.Length; i++) // skip 1st line
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] values = line.Split(',');

            // SprawdŸ czy s¹ wystarczaj¹ce dane
            if (values.Length >= 7)
            {
                AugmentData augment = new AugmentData
                {
                    ID = int.Parse(values[0]),
                    Name = values[2],
                    Rarity = values[4], // To bêdzie zawieraæ wszystkie mo¿liwe rarity (np. "Common, Epic")
                    Description = values[6]
                };

                if (!augmentDatabase.ContainsKey(augment.ID))
                    augmentDatabase.Add(augment.ID, augment);
            }
        }
    }

    // ================= Rarity Selection =================
    private Dictionary<string, float> rarityWeights = new Dictionary<string, float>
    {
        {"Common", 50f},    // 50% szansy
        {"Rare", 30f},      // 30% szansy  
        {"Epic", 15f},      // 15% szansy
        {"Legendary", 5f}   // 5% szansy
    };
    private Dictionary<string, Color> rarityColors = new Dictionary<string, Color>
    {
        {"Common", Color.green},                    // Green
        {"Rare", Color.blue},                      // Blue  
        {"Epic", new Color(0.8f, 0f, 1f)},        // Purple
        {"Legendary", new Color(1f, 0.6f, 0f)}    // Gold
    };
    private void SetRarityColor(int slotIndex, string rarity)
    {
        Color targetColor = Color.green;

        if (rarityColors.ContainsKey(rarity))
            targetColor = rarityColors[rarity];
        else
            Debug.LogWarning($"Color not found for: {rarity}");

        rarityImages[slotIndex].color = targetColor;
    }
    private string GetRandomRarityForAugment(int augmentId)
    {
        if (!augmentDatabase.ContainsKey(augmentId))
            return "Common"; // fallback

        string rarityString = augmentDatabase[augmentId].Rarity;

        // Usuñ cudzys³owy jeœli s¹ obecne
        rarityString = rarityString.Trim('"');

        // Podziel po przecinkach i wyczyœæ ka¿dy element
        string[] rarities = rarityString.Split(',');
        List<string> cleanRarities = new List<string>();

        foreach (string rarity in rarities)
        {
            string cleanRarity = rarity.Trim();
            if (!string.IsNullOrEmpty(cleanRarity))
                cleanRarities.Add(cleanRarity);
        }

        // Jeœli nie ma ¿adnych rarity, zwróæ domyœln¹
        if (cleanRarities.Count == 0)
            return "Common";

        // Jeœli jest tylko jedna rarity, zwróæ j¹
        if (cleanRarities.Count == 1)
            return cleanRarities[0];

        // Wylosuj z wagami - zbierz tylko wagi dla dostêpnych rarity
        List<float> weights = new List<float>();
        foreach (string rarity in cleanRarities)
        {
            if (rarityWeights.ContainsKey(rarity))
                weights.Add(rarityWeights[rarity]);
            else
                weights.Add(10f); // domyœlna waga jeœli rarity nie jest w s³owniku
        }

        // Weighted random selection
        float totalWeight = 0f;
        foreach (float weight in weights)
            totalWeight += weight;

        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < cleanRarities.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
                return cleanRarities[i];
        }

        // Fallback - zwróæ pierwszy element
        return cleanRarities[0];
    }
    //---------------------- AUGMENT NAME ----------------------
    private void SetAugmentName(int slotIndex, int augmentId)
    {
        if (nameTexts == null || slotIndex < 0 || slotIndex >= nameTexts.Length)
        {
            Debug.LogWarning($"NameTexts array nie jest ustawiona lub nieprawid³owy slotIndex: {slotIndex}");
            return;
        }

        if (nameTexts[slotIndex] == null)
        {
            Debug.LogWarning($"NameText dla slotu {slotIndex} jest null!");
            return;
        }

        if (!augmentDatabase.ContainsKey(augmentId))
        {
            Debug.LogWarning($"Nie znaleziono augmentu o ID: {augmentId}");
            nameTexts[slotIndex].text = "Unknown Augment";
            return;
        }

        string augmentName = augmentDatabase[augmentId].Name;
        nameTexts[slotIndex].text = augmentName;

        Debug.Log($"Ustawiono nazwê '{augmentName}' dla slotu {slotIndex}");
    }
    // ---------------- AUGMENT DESCRIPTION ----------------
    private void SetAugmentDescription(int slotIndex, int augmentId)
    {
        if (descriptionTexts == null || slotIndex < 0 || slotIndex >= descriptionTexts.Length)
        {
            Debug.LogWarning($"DescriptionTexts array nie jest ustawiona lub nieprawid³owy slotIndex: {slotIndex}");
            return;
        }

        if (descriptionTexts[slotIndex] == null)
        {
            Debug.LogWarning($"DescriptionText dla slotu {slotIndex} jest null!");
            return;
        }

        if (!augmentDatabase.ContainsKey(augmentId))
        {
            Debug.LogWarning($"Nie znaleziono augmentu o ID: {augmentId}");
            descriptionTexts[slotIndex].text = "Unknown Description";
            return;
        }

        string augmentDescription = augmentDatabase[augmentId].Description;
        descriptionTexts[slotIndex].text = augmentDescription;

        Debug.Log($"Ustawiono opis '{augmentDescription}' dla slotu {slotIndex}");
    }
    // ================= Augment Assign =================
    private void AssignAugmentToImage(Image target, int augmentId, string selectedRarity)
    {
        if (!augmentDatabase.ContainsKey(augmentId)) return;

        // sprite
        Sprite sprite = System.Array.Find(allSprites, s => s.name == augmentId.ToString());
        if (sprite != null)
            target.sprite = sprite;
        else
            Debug.LogWarning($"SPRITE not found for augment ID={augmentId}");

        //for TMP text later:
        var data = augmentDatabase[augmentId];
        Debug.Log($"Rolled augment: {data.Name} with rarity: {selectedRarity} - {data.Description}");
    }

    // Overload dla kompatybilnoœci wstecznej
    private void AssignAugmentToImage(Image target, int augmentId)
    {
        string selectedRarity = GetRandomRarityForAugment(augmentId);
        AssignAugmentToImage(target, augmentId, selectedRarity);
    }

    // get unique ID (not used one)
    private int GetUniqueRandomId(List<int> pool, List<int> alreadyUsed)
    {
        List<int> available = new List<int>(pool);
        foreach (int used in alreadyUsed)
            available.Remove(used);

        if (available.Count == 0)
            return pool[Random.Range(0, pool.Count)]; // fallback

        return available[Random.Range(0, available.Count)];
    }
    
    // ================= Reroll =================
    public void Reroll(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= augmentImages.Length) return;

        List<int> keys = new List<int>(augmentDatabase.Keys);

        if (rerollsleft[slotIndex] <= 0)
        {
            return;
        }
        //use rest of not used IDs
        List<int> alreadyUsed = new List<int>();
        for (int i = 0; i < currentAugmentIDs.Length; i++)
        {
            if (i != slotIndex)
                alreadyUsed.Add(currentAugmentIDs[i]);
        }

        int newId = GetUniqueRandomId(keys, alreadyUsed);
        currentAugmentIDs[slotIndex] = newId;

        // Wylosuj now¹ rarity dla tego augmentu
        string newRarity = GetRandomRarityForAugment(newId);
        currentSelectedRarities[slotIndex] = newRarity;

        AssignAugmentToImage(augmentImages[slotIndex], newId, newRarity);

        SetRarityColor(slotIndex, newRarity);

        SetAugmentName(slotIndex, newId);

        SetAugmentDescription(slotIndex, newId);

        rerollsleft[slotIndex]--;
        UpdateRerollText(slotIndex);
    }
    private void UpdateRerollText(int slotIndex)
    {
        if (rerollNumberText != null && slotIndex < rerollNumberText.Length && rerollNumberText[slotIndex] != null)
        {
            rerollNumberText[slotIndex].text = $"{rerollsleft[slotIndex]}";
        }
    }
    public void ResetRerolls() //for Later
    {
        for (int i = 0; i < rerollsleft.Length; i++)
        {
            rerollsleft[i] = maxRerolls;
        }
        //EnableRerollButtons();

        // Aktualizuj wszystkie teksty rerollów
        for (int i = 0; i < rerollNumberText.Length; i++)
        {
            UpdateRerollText(i);
        }
    }
    // ================= Gettery dla aktualnych danych =================
    public string GetCurrentAugmentRarity(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < currentSelectedRarities.Length)
            return currentSelectedRarities[slotIndex];
        return "Common";
    }

    public AugmentData GetCurrentAugmentData(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < currentAugmentIDs.Length)
            return augmentDatabase[currentAugmentIDs[slotIndex]];
        return null;
    }
}