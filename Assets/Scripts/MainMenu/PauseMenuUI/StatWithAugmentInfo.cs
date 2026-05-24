using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Hover tooltip for a single stat row. When the pointer enters, it lists every
/// applied augment that modifies this stat.
///
/// Unchanged in spirit from the original — just updated to share colour /
/// formatting with the new stats panel and to format multipliers consistently.
///
/// Attach to a stat row GameObject that also has a Graphic with Raycast Target
/// enabled (so OnPointerEnter fires). Set statName + targetType to match the
/// augment data (e.g. "maxHealth" / "Player").
/// </summary>
public class StatWithAugmentInfo : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Stat Info")]
    [SerializeField] private string statName;
    [SerializeField] private string targetType; // "Player", "Weapon", "Tower", "Enemy", "Global"

    [Header("Tooltip")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TextMeshProUGUI tooltipText;
    [SerializeField] private Vector3 tooltipOffset = new Vector3(10, 10, 0);

    public void OnPointerEnter(PointerEventData eventData) => ShowAugmentInfo();
    public void OnPointerExit(PointerEventData eventData)  => HideTooltip();

    private void ShowAugmentInfo()
    {
        if (AugmentRegistry.Instance == null || tooltipPanel == null || tooltipText == null)
            return;

        var lines = new List<string>();

        foreach (int augmentId in AugmentRegistry.Instance.GetAppliedAugments())
        {
            var augmentData = AugmentRegistry.Instance.GetAugmentData(augmentId);
            if (augmentData?.ParsedModifications == null) continue;

            foreach (var mod in augmentData.ParsedModifications)
            {
                if (!AugmentMath.StatMatches(mod.StatName, statName)) continue;
                if (!mod.TargetType.Contains(targetType, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                lines.Add($"• {augmentData.Name}: {DescribeModification(mod)}");
            }
        }

        if (lines.Count == 0)
        {
            HideTooltip();
            return;
        }

        string body = string.Join("\n", lines);
        tooltipText.richText = true;
        tooltipText.text =
            $"<b>{statName} Modifications:</b>\n\n{body}";

        tooltipPanel.SetActive(true);
        tooltipPanel.transform.position = transform.position + tooltipOffset;
    }

    /// <summary>Human-readable, colour-coded text for one modification.</summary>
    private string DescribeModification(StatModification mod)
    {
        switch (mod.OperationType)
        {
            case StatModification.ModificationType.Add:
            {
                bool good = mod.Value >= 0f;
                Color c = good ? StatRowBuilder.GoodColor : StatRowBuilder.BadColor;
                string sign = good ? "+" : "";
                return $"<color=#{StatRowBuilder.Hex(c)}>{sign}{mod.Value:0.##}</color>";
            }
            case StatModification.ModificationType.Multiply:
            {
                Color c = StatRowBuilder.MultiplierColor(mod.Value);
                return $"<color=#{StatRowBuilder.Hex(c)}>x{mod.Value:0.00}</color>";
            }
            case StatModification.ModificationType.Percentage:
            {
                bool good = mod.Value >= 0f;
                Color c = good ? StatRowBuilder.GoodColor : StatRowBuilder.BadColor;
                string sign = good ? "+" : "";
                return $"<color=#{StatRowBuilder.Hex(c)}>{sign}{mod.Value:0.#}%</color>";
            }
            default:
                return $"{mod.Value:0.##}";
        }
    }

    private void HideTooltip()
    {
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }
}
