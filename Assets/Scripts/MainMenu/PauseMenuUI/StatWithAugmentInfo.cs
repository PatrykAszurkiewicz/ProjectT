using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;

public class StatWithAugmentInfo : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Stat Info")]
    [SerializeField] private string statName;
    [SerializeField] private string targetType; // "Player", "Tower", "Enemy", etc.

    [Header("Tooltip")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TextMeshProUGUI tooltipText;
    [SerializeField] private Vector3 tooltipOffset = new Vector3(10, 10, 0);

    public void OnPointerEnter(PointerEventData eventData)
    {
        ShowAugmentInfo();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideTooltip();
    }

    private void ShowAugmentInfo()
    {
        if (AugmentRegistry.Instance == null || tooltipPanel == null) return;

        var appliedAugments = AugmentRegistry.Instance.GetAppliedAugments();
        var relevantAugments = new List<string>();

        foreach (int augmentId in appliedAugments)
        {
            var augmentData = AugmentRegistry.Instance.GetAugmentData(augmentId);
            if (augmentData == null) continue;

            // Check if this augment affects this stat
            if (augmentData.ParsedModifications != null)
            {
                foreach (var mod in augmentData.ParsedModifications)
                {
                    if (mod.StatName.Contains(statName, System.StringComparison.OrdinalIgnoreCase) &&
                        mod.TargetType.Contains(targetType, System.StringComparison.OrdinalIgnoreCase))
                    {
                        string modText = GetModificationText(mod);
                        relevantAugments.Add($"• {augmentData.Name}: {modText}");
                    }
                }
            }
        }

        if (relevantAugments.Count > 0)
        {
            string tooltip = $"<b>{statName} Modifications:</b>\n\n";
            tooltip += string.Join("\n", relevantAugments);

            tooltipText.text = tooltip;
            tooltipPanel.SetActive(true);
            tooltipPanel.transform.position = transform.position + tooltipOffset;
        }
    }

    private string GetModificationText(StatModification mod)
    {
        string operation = mod.OperationType switch
        {
            StatModification.ModificationType.Add => "+",
            StatModification.ModificationType.Multiply => "x",
            StatModification.ModificationType.Percentage => "%",
            _ => "="
        };

        return $"{operation}{mod.Value:F2}";
    }

    private void HideTooltip()
    {
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }
}
