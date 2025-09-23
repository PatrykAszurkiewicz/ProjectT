using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChosenAugmentsUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform panel;
    [SerializeField] private GameObject augmentPrefab;

    private List<GameObject> currentIcons = new List<GameObject>();

    public void RefreshUI()
    {

        foreach (var icon in currentIcons)
        {
            Destroy(icon);
        }
        currentIcons.Clear();

        var chosenAugments = AugmentRegistry.Instance.GetAppliedAugments();

        Debug.Log("Chosen augments: " + chosenAugments.Count);

        foreach (int id in chosenAugments)
        {
            var augmentData = AugmentRegistry.Instance.GetAugmentData(id);
            if (augmentData == null) continue;

            //spawn new icon place
            GameObject iconObj = Instantiate(augmentPrefab, panel);
            currentIcons.Add(iconObj);

            //set icon
            Image img = iconObj.GetComponent<Image>();
            if (img != null && augmentData.Icon != null)
                img.sprite = augmentData.Icon;

            //set text
            TextMeshProUGUI text = iconObj.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.text = augmentData.Name;
        }
    }
}
