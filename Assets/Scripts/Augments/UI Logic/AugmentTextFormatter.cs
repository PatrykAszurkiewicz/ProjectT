using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;

// =============================================================================
//  AugmentTextFormatter
// -----------------------------------------------------------------------------
//  Turns a plain augment Description into TMP rich-text that reads like a real
//  card (à la Bazaar): numbers/percentages/multipliers highlighted, stat and
//  entity keywords colour-coded by category, buff/nerf verbs tinted green/red,
//  durations muted, and the lead-in phrase (before an em-dash) bolded.
//
//  It ALSO builds a compact, data-driven "stat chip" line straight from the
//  augment's ParsedModifications — the authoritative list of properties an
//  augment changes — so the important numbers are always surfaced even when the
//  prose is vague.
//
//  100% programmatic. No CSV edits, no per-augment work, no inspector wiring.
//  Just call AugmentTextFormatter.Format(augmentData) where you currently assign
//  augmentData.Description to a TMP_Text, and make sure richText is enabled.
//
//  Safe by construction:
//    • Colouring is computed against the ORIGINAL string in ONE pass using a
//      non-overlapping span merge, so tags are never nested badly or corrupted.
//    • Any rich-text tags already present in the description are treated as
//      reserved regions and left untouched.
//    • Every public method is null-safe and falls back to the raw text.
//
//  Tune the palette / toggles in the static config block below.
// =============================================================================
public static class AugmentTextFormatter
{
    // ----- Toggles ----------------------------------------------------------
    public static bool ColorProse = true;   // colour numbers + keywords in the prose
    public static bool EmphasizeLeadIn = false;   // bold the phrase before the first em-dash
    public static bool ShowStatChips = false;   // append the data-driven property line
    public static bool BoldNumbers = false;  // wrap highlighted numbers in <b> (off: colour only)

    // Relative size of the stat-chip line (TMP percentage).
    public static float StatChipSizePercent = 88f;

    // ----- Padding ----------------------------------------------------------
    // Insets the text from its box on all four sides so it stops hugging the
    // card border. These are TMP "margin" units (pixels in a UI canvas). Tune
    // to taste; larger side values wrap sooner, so keep them modest on small
    // cards. Call ApplyPadding(tmp) once wherever you set the text.
    public static float PadLeft = 16f;
    public static float PadTop = 12f;
    public static float PadRight = 16f;
    public static float PadBottom = 12f;

    // Inset a TMP text within its own rect (works no matter how the rect is
    // anchored, unlike editing offsets by hand). Null-safe; call it every time
    // you assign the text — it's idempotent.
    public static void ApplyPadding(TMP_Text tmp)
    {
        if (tmp == null) return;
        tmp.margin = new Vector4(PadLeft, PadTop, PadRight, PadBottom);
    }

    // Convenience for a symmetric inset (horizontal, vertical).
    public static void SetPadding(float horizontal, float vertical)
    {
        PadLeft = PadRight = horizontal;
        PadTop = PadBottom = vertical;
    }

    // ----- Palette (hex, no '#') -------------------------------------------
    public static class Palette
    {
        public const string Number = "F5C542"; // gold — every raw number / % / x
        public const string Positive = "6EE787"; // green — buff verbs, "+" values
        public const string Negative = "FF6B6B"; // red   — nerf verbs, "-" values
        public const string Duration = "9AA0A6"; // grey  — seconds / "for the fight"

        public const string Damage = "FF8A5B"; // orange
        public const string Health = "6EE787"; // green
        public const string Energy = "4FC3F7"; // cyan
        public const string Tower = "FFCA5F"; // amber
        public const string Enemy = "FF6B6B"; // red
        public const string Speed = "57D0E0"; // teal (speed / haste / cooldown)
        public const string Range = "8AB4FF"; // blue
        public const string Armor = "B0BEC5"; // steel
        public const string Special = "C792EA"; // purple (poison / parry / decoy…)
    }

    // =========================================================================
    //  PUBLIC API
    // =========================================================================

    // Full card body: pretty prose + (optional) data-driven stat-chip line.
    public static string Format(AugmentData data)
    {
        if (data == null) return string.Empty;

        string prose = FormatDescription(data.Description);
        string chips = ShowStatChips ? BuildStatChips(data) : string.Empty;

        if (string.IsNullOrEmpty(chips)) return prose;
        if (string.IsNullOrEmpty(prose)) return chips;

        return prose + "\n\n" + chips;
    }

    // Prose only. Use this if you only have the string, not the AugmentData.
    public static string FormatDescription(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (!ColorProse) return raw;

        try
        {
            // Optionally split off a short lead-in before the first dash.
            // (Colour-only — no bold, per design.)
            if (EmphasizeLeadIn && TrySplitLeadIn(raw, out string lead, out string sep, out string rest))
                return ApplyRules(lead) + sep + ApplyRules(rest);

            return ApplyRules(raw);
        }
        catch
        {
            return raw; // never break the UI over formatting
        }
    }

    // =========================================================================
    //  STAT CHIPS  (from ParsedModifications — the real augment properties)
    // =========================================================================
    public static string BuildStatChips(AugmentData data)
    {
        var mods = data?.ParsedModifications;
        if (mods == null || mods.Count == 0) return string.Empty;

        var chips = new List<string>();
        var seen = new HashSet<string>();

        foreach (var mod in mods)
        {
            if (mod == null || string.IsNullOrEmpty(mod.StatName)) continue;

            string label = PrettyStatName(mod.StatName);
            string swatch = StatCategoryColor(mod.StatName);
            (string valueText, string valueColor) = DescribeValue(mod);

            string chip = $"<color=#{swatch}>\u2022</color> {label} " +
                          $"<color=#{valueColor}>{valueText}</color>";

            if (seen.Add(chip)) chips.Add(chip);
        }

        if (chips.Count == 0) return string.Empty;

        // Thin gap between chips; wrapped small so it reads as a footer.
        string body = string.Join("   ", chips);
        return $"<size={StatChipSizePercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}%>{body}</size>";
    }

    private static (string text, string color) DescribeValue(StatModification mod)
    {
        // For a handful of stats (cost, cooldown, generation interval…) a LOWER
        // value is the buff, so flip the green/red sense for those.
        bool lowerBetter = IsLowerBetter(mod.StatName);

        switch (mod.OperationType)
        {
            case StatModification.ModificationType.Add:
                {
                    bool positiveNumber = mod.Value >= 0f;
                    bool good = lowerBetter ? !positiveNumber : positiveNumber;
                    string sign = positiveNumber ? "+" : "";
                    return ($"{sign}{Fmt(mod.Value)}", good ? Palette.Positive : Palette.Negative);
                }
            case StatModification.ModificationType.Percentage:
                {
                    bool positiveNumber = mod.Value >= 0f;
                    bool good = lowerBetter ? !positiveNumber : positiveNumber;
                    string sign = positiveNumber ? "+" : "";
                    return ($"{sign}{Fmt(mod.Value)}%", good ? Palette.Positive : Palette.Negative);
                }
            case StatModification.ModificationType.Multiply:
                {
                    bool aboveOne = mod.Value >= 1f;
                    bool good = lowerBetter ? !aboveOne : aboveOne;
                    return ($"\u00d7{Fmt(mod.Value)}", good ? Palette.Positive : Palette.Negative);
                }
            case StatModification.ModificationType.Set:
                return ($"={Fmt(mod.Value)}", Palette.Number);
            default:
                return (Fmt(mod.Value), Palette.Number);
        }
    }

    private static bool IsLowerBetter(string statName)
    {
        string s = (statName ?? string.Empty).ToLowerInvariant();
        return s.Contains("cost") || s.Contains("consumption") ||
               s.Contains("cooldown") || s.Contains("interval");
    }

    private static string Fmt(float v)
    {
        // Trim trailing zeros: 2.00 -> 2, 1.50 -> 1.5
        return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    // =========================================================================
    //  PROSE COLOURING — non-overlapping span merge (single pass, tag-safe)
    // =========================================================================
    private sealed class Rule
    {
        public int Priority;
        public Regex Rx;
        public Func<Match, string> Wrap;
    }

    private static string ApplyRules(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Reserve any pre-existing rich-text tags so we never split/nest them wrong.
        var reserved = new List<(int s, int e)>();
        foreach (Match t in Regex.Matches(text, "<[^>]+>"))
            reserved.Add((t.Index, t.Index + t.Length));

        // Collect candidate spans from every rule.
        var spans = new List<(int s, int e, int pri, string rep)>();
        foreach (var rule in Rules)
        {
            foreach (Match m in rule.Rx.Matches(text))
            {
                int s = m.Index, e = m.Index + m.Length;
                if (e <= s) continue;

                bool blocked = false;
                foreach (var r in reserved)
                    if (s < r.e && r.s < e) { blocked = true; break; }
                if (blocked) continue;

                spans.Add((s, e, rule.Priority, rule.Wrap(m)));
            }
        }

        if (spans.Count == 0) return text;

        // Order so that, on overlap, the earliest start wins; ties break by higher
        // priority then longer span. Greedy accept of non-overlapping spans.
        spans.Sort((a, b) =>
        {
            if (a.s != b.s) return a.s.CompareTo(b.s);
            if (a.pri != b.pri) return b.pri.CompareTo(a.pri);
            return (b.e - b.s).CompareTo(a.e - a.s);
        });

        var sb = new StringBuilder(text.Length + spans.Count * 24);
        int last = 0;
        foreach (var sp in spans)
        {
            if (sp.s < last) continue;           // overlaps an accepted span — drop
            sb.Append(text, last, sp.s - last);  // untouched gap
            sb.Append(sp.rep);                   // wrapped match
            last = sp.e;
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    // ----- Rule construction helpers ---------------------------------------
    private static Func<Match, string> Color(string hex)
        => m => $"<color=#{hex}>{m.Value}</color>";

    private static Func<Match, string> ColorNum(string hex)
        => m => BoldNumbers
              ? $"<color=#{hex}><b>{m.Value}</b></color>"
              : $"<color=#{hex}>{m.Value}</color>";

    private static Func<Match, string> ColorItalic(string hex)
        => m => $"<color=#{hex}><i>{m.Value}</i></color>";

    private static Regex Words(params string[] words)
    {
        string body = string.Join("|", words.OrderByDescending(w => w.Length)
                                             .Select(Regex.Escape));
        return new Regex(@"(?<![\w])(?:" + body + @")(?![\w])",
                         RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static Regex Raw(string pattern)
        => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ----- The rule table (higher Priority wins ties) ----------------------
    private static readonly List<Rule> Rules = BuildRules();

    private static List<Rule> BuildRules()
    {
        var list = new List<Rule>
        {
            // Numbers first (most important visual anchors).
            new Rule { Priority = 100, Rx = Raw(@"[+\-]?\d+(?:\.\d+)?\s?%"),              Wrap = ColorNum(Palette.Number) },
            new Rule { Priority = 95,  Rx = Raw(@"(?<![\w.])(?:[x\u00d7]\s?\d+(?:\.\d+)?|\d+(?:\.\d+)?\s?[x\u00d7])(?![\w])"), Wrap = ColorNum(Palette.Number) },
            // "15s" / "30 s" cooldown shorthand: gold number + muted 's'.
            new Rule { Priority = 92,  Rx = Raw(@"(?<![\w.])\d+\s?s(?![\w])"), Wrap = m => {
                var g = System.Text.RegularExpressions.Regex.Match(m.Value, @"^(\d+)");
                return $"<color=#{Palette.Number}>{g.Groups[1].Value}</color><color=#{Palette.Duration}><i>s</i></color>"; } },
            new Rule { Priority = 90,  Rx = Raw(@"(?<![\w.])[+\-]?\d+(?:\.\d+)?(?![\w])"), Wrap = ColorNum(Palette.Number) },

            // Durations / timing.
            new Rule { Priority = 85, Rx = Words("second","seconds","sec","secs","minute","minutes"), Wrap = ColorItalic(Palette.Duration) },
            new Rule { Priority = 85, Rx = Raw(@"(?:for the rest of the fight|for the fight|once per wave|once per game|per second|over time|per wave|per game|each wave|this wave|permanently)"), Wrap = ColorItalic(Palette.Duration) },

            // Multi-word stat phrases (must beat the single-word rules below).
            new Rule { Priority = 80, Rx = Words("max health","maximum health","maximum health","health regen","hit points","hit point"), Wrap = Color(Palette.Health) },
            new Rule { Priority = 80, Rx = Words("fire rate","rate of fire","attack speed","firing speed","attack rate","movement speed","move speed","reload speed"), Wrap = Color(Palette.Speed) },
            new Rule { Priority = 80, Rx = Words("attack power","attack damage","physical damage","melee damage","melee attack"), Wrap = Color(Palette.Damage) },
            new Rule { Priority = 80, Rx = Words("critical hit","critical chance","critical damage"), Wrap = Color(Palette.Special) },
            new Rule { Priority = 80, Rx = Words("grappling hook","life steal","area of effect"), Wrap = Color(Palette.Special) },
            new Rule { Priority = 80, Rx = Words("central core","tower placement"), Wrap = Color(Palette.Tower) },
            new Rule { Priority = 80, Rx = Words("energy generation","energy decay","tower energy"), Wrap = Color(Palette.Energy) },

            // Single-word stat / entity keywords.
            new Rule { Priority = 70, Rx = Words("damage","damages","attack","attacks","dps","melee","projectile"), Wrap = Color(Palette.Damage) },
            new Rule { Priority = 70, Rx = Words("health","hp","heal","heals","healing","regeneration","regen","regenerate","regenerates","lifesteal","vitality","revive","revives","revived","resurrect","resurrects","resurrected"), Wrap = Color(Palette.Health) },
            new Rule { Priority = 70, Rx = Words("energy","power","mana","stamina","resource","resources","scrap","materials","ammo","charge"), Wrap = Color(Palette.Energy) },
            new Rule { Priority = 70, Rx = Words("tower","towers","turret","turrets","generator","generators","laser","core"), Wrap = Color(Palette.Tower) },
            new Rule { Priority = 70, Rx = Words("enemy","enemies","monster","monsters","boss","bosses","wave","waves","horde","attacker","attackers"), Wrap = Color(Palette.Enemy) },
            new Rule { Priority = 70, Rx = Words("speed","haste","cooldown","cooldowns","reload","slow","slower","freeze","frozen","stun","stunned","swiftness"), Wrap = Color(Palette.Speed) },
            new Rule { Priority = 70, Rx = Words("range","ranged","sight","detection","radius","area","splash"), Wrap = Color(Palette.Range) },
            new Rule { Priority = 70, Rx = Words("armor","armour","defense","defence","shield","shields","resistance","block","blocks","barrier","absorb"), Wrap = Color(Palette.Armor) },
            new Rule { Priority = 70, Rx = Words("poison","poisoned","bleed","burn","burning","ignite","crit","critical","dodge","parry","parried","stealth","cloak","invisibility","invisible","decoy","grapple","trap","traps","root","rooted","obstacle","obstacles","lightning","aura","pierce","knockback","thorns","explosion","shockwave"), Wrap = Color(Palette.Special) },

            // Buff / nerf verbs (lowest priority — they sit beside stat words).
            new Rule { Priority = 50, Rx = Words("gain","gains","gained","increase","increases","increased","boost","boosts","boosted","restore","restores","grant","grants","granted","bonus","extra","additional","more","plus","faster","stronger","improved","improves","doubles","double","yields","yield"), Wrap = Color(Palette.Positive) },
            new Rule { Priority = 50, Rx = Words("reduce","reduces","reduced","decrease","decreases","decreased","lose","loses","lost","cost","costs","consume","consumes","penalty","penalties","sacrifice","sacrifices","drain","drains","less","fewer","weaker","lower","lowers","minus","halve","halved","halves"), Wrap = Color(Palette.Negative) },
        };
        return list;
    }

    // =========================================================================
    //  LEAD-IN SPLIT
    // =========================================================================
    private static readonly string[] DashSeparators = { " \u2014 ", "\u2014", " \u2013 ", "\u2013", " - " };

    private static bool TrySplitLeadIn(string text, out string lead, out string sep, out string rest)
    {
        lead = sep = rest = null;
        foreach (var d in DashSeparators)
        {
            int idx = text.IndexOf(d, StringComparison.Ordinal);
            if (idx <= 0) continue;

            string candidate = text.Substring(0, idx).Trim();
            // Keep it tasteful: only bold a genuine short headline, not half a sentence.
            if (candidate.Length == 0 || candidate.Length > 48) return false;

            lead = candidate;
            sep = " \u2014 ";                       // normalise to a spaced em-dash
            rest = text.Substring(idx + d.Length).Trim();
            return true;
        }
        return false;
    }

    // =========================================================================
    //  STAT-NAME PRETTIFYING + CATEGORY COLOUR
    // =========================================================================
    private static readonly Dictionary<string, string> PrettyNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "maxHealth", "Max Health" }, { "health", "Health" }, { "currentHealth", "Health" },
        { "maxStamina", "Max Stamina" }, { "maxMana", "Max Mana" },
        { "moveSpeed", "Move Speed" }, { "speed", "Speed" },
        { "damage", "Damage" }, { "range", "Range" },
        { "fireRate", "Fire Rate" }, { "rotationSpeed", "Rotation Speed" },
        { "armorReduction", "Armor" }, { "currentArmor", "Armor" },
        { "maxEnergy", "Tower Energy" }, { "currentEnergy", "Tower Energy" },
        { "energyGenerationRate", "Energy Gen" }, { "generationRange", "Gen Range" },
        { "generationInterval", "Gen Interval" }, { "freezeChance", "Freeze Chance" },
        { "healthRegenRate", "Health Regen" }, { "energyCostMultiplier", "Energy Cost" },
        { "globalResourceMultiplier", "Resource Gen" }, { "cost", "Cost" },
    };

    private static string PrettyStatName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        if (PrettyNames.TryGetValue(raw, out string nice)) return nice;

        // Generic fallback: split camelCase and underscores, Title Case.
        string spaced = Regex.Replace(raw, @"[_\-]+", " ");
        spaced = Regex.Replace(spaced, @"(?<=[a-z0-9])(?=[A-Z])", " ");
        spaced = Regex.Replace(spaced, @"\s+", " ").Trim();
        if (spaced.Length == 0) return raw;
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }

    private static string StatCategoryColor(string statName)
    {
        string s = (statName ?? string.Empty).ToLowerInvariant();
        if (s.Contains("damage") || s.Contains("attack")) return Palette.Damage;
        if (s.Contains("health") || s.Contains("heal") || s.Contains("hp") || s.Contains("regen")) return Palette.Health;
        if (s.Contains("energy") || s.Contains("mana") || s.Contains("stamina") || s.Contains("resource")) return Palette.Energy;
        if (s.Contains("generation") || s.Contains("tower") || s.Contains("cost")) return Palette.Tower;
        if (s.Contains("range") || s.Contains("sight") || s.Contains("detection")) return Palette.Range;
        if (s.Contains("speed") || s.Contains("rate") || s.Contains("rotation") || s.Contains("freeze") || s.Contains("interval")) return Palette.Speed;
        if (s.Contains("armor") || s.Contains("armour") || s.Contains("defense") || s.Contains("shield")) return Palette.Armor;
        return Palette.Number;
    }
}


