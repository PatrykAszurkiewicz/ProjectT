using UnityEngine;
using TMPro;


// Helper that builds and styles a single line of stat text.
// Used by StatsPanelUI for both the basic (left column) and detailed (mid panel) views.
// It does NOT need a custom prefab — it just needs SOME TextMeshProUGUI prefab to clone.
// The same statTextPrefab the old StatsUI already referenced works fine.
// Colour coding rules (kept deliberately simple):
//   multiplier  > 1  -> green   (buffed)
//   multiplier  < 1  -> red     (nerfed)        ... unless lowerIsBetter, then swapped
//   multiplier == 1  -> grey    (unchanged)

public static class StatRowBuilder
{

    public static readonly Color HeaderColor = new Color(1f, 1f, 1f);
    public static readonly Color LabelColor = new Color(0.82f, 0.82f, 0.85f);
    public static readonly Color ValueColor = new Color(1f, 1f, 1f);
    public static readonly Color GoodColor = new Color(0.45f, 0.95f, 0.45f); // green
    public static readonly Color BadColor = new Color(0.96f, 0.36f, 0.36f); // red
    public static readonly Color NeutralColor = new Color(0.70f, 0.70f, 0.72f); // grey
    public static readonly Color WarningColor = new Color(1f, 0.78f, 0.30f);    // amber
    public static readonly Color AccentColor = new Color(0.55f, 0.80f, 1f);    // cyan/info


    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);


    /// Returns green / red / grey depending on how a multiplier compares to 1.

    public static Color MultiplierColor(float multiplier, bool lowerIsBetter = false)
    {
        const float epsilon = 0.001f;
        if (Mathf.Abs(multiplier - 1f) < epsilon) return NeutralColor;

        bool isUp = multiplier > 1f;
        if (lowerIsBetter) isUp = !isUp;
        return isUp ? GoodColor : BadColor;
    }


    /// Formats a multiplier as a compact coloured suffix, e.g. "x1.4".
    /// Hidden entirely when the multiplier is effectively 1 (keeps rows clean).

    public static string FormatMultiplierSuffix(float multiplier, bool lowerIsBetter = false, bool hideWhenNeutral = false)
    {
        const float epsilon = 0.005f;
        bool neutral = Mathf.Abs(multiplier - 1f) < epsilon;
        // Always hide a neutral (x1.0) multiplier — it carries no information
        // and just eats horizontal space in a narrow panel.
        if (neutral) return string.Empty;

        Color c = MultiplierColor(multiplier, lowerIsBetter);
        // Compact: one decimal, no parentheses, small size.
        return $" <size=80%><color=#{Hex(c)}>x{multiplier:0.0}</color></size>";
    }


    /// Returns the LEFT (label) and RIGHT (value + multiplier) parts of a stat
    /// row as separate rich-text strings. The caller places them in two text
    /// objects — left-aligned and right-aligned — for a clean two-column row.

    public static (string left, string right) ComposeParts(
        string label, string value, float multiplier = 1f,
        bool lowerIsBetter = false, bool hideWhenNeutral = false)
    {
        string suffix = FormatMultiplierSuffix(multiplier, lowerIsBetter, hideWhenNeutral);
        string left = $"<color=#{Hex(LabelColor)}>{label}</color>";
        string right = $"<color=#{Hex(ValueColor)}>{value}</color>{suffix}";
        return (left, right);
    }


    /// Single-string form (label + value together), kept for the LEFT panel
    /// rows which are single pre-existing TMP objects.

    public static string Compose(string label, string value, float multiplier = 1f,
                                 bool lowerIsBetter = false, bool hideWhenNeutral = false)
    {
        string suffix = FormatMultiplierSuffix(multiplier, lowerIsBetter, hideWhenNeutral);
        return $"<color=#{Hex(LabelColor)}>{label}:</color> " +
               $"<color=#{Hex(ValueColor)}>{value}</color>{suffix}";
    }


    public static string ComposeHeader(string text)
    {
        return $"<b><size=115%><color=#{Hex(HeaderColor)}>{text}</color></size></b>";
    }


    public static string ComposeNote(string text, Color color)
    {
        return $"<b><color=#{Hex(color)}>{text}</color></b>";
    }
}

