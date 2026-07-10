using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Распарсенная информация об одном моде из SaleRecord.</summary>
public sealed class ParsedModInfo
{
    [JsonPropertyName("raw")]         public string Raw { get; set; } = "";
    [JsonPropertyName("stripped")]    public string Stripped { get; set; } = "";
    [JsonPropertyName("affixName")]   public string AffixName { get; set; } = "";
    [JsonPropertyName("affixType")]   public string AffixType { get; set; } = "";
    [JsonPropertyName("affixTier")]   public int AffixTier { get; set; }
    [JsonPropertyName("familyId")]    public string? FamilyId { get; set; }
    [JsonPropertyName("isFractured")] public bool IsFractured { get; set; }
    [JsonPropertyName("unmatched")]   public bool Unmatched { get; set; }
}

/// <summary>
/// Парсит моды из SaleRecord: снимает GGG-разметку и ищет совпадение в AffixLibrary
/// через ParsedItemCraftEvaluator.StatLineMatchesTemplate.
/// </summary>
public static class SaleModParser
{
    // [Key|DisplayText] → DisplayText
    private static readonly Regex WithDisplay    = new(@"\[([^|\]]*)\|([^\]]*)\]", RegexOptions.Compiled);
    // [Key] (без pipe) → Key
    private static readonly Regex WithoutDisplay = new(@"\[([^\]|]+)\]",            RegexOptions.Compiled);

    /// <summary>Удаляет GGG-разметку: [Key|Display] → Display; [Key] → Key.</summary>
    public static string StripMarkup(string raw)
    {
        var s = WithDisplay.Replace(raw, m => m.Groups[2].Value);
        return WithoutDisplay.Replace(s, m => m.Groups[1].Value);
    }

    /// <summary>
    /// Ищет AffixLibraryEntry, любой stat-шаблон которого совпадает с stripped-строкой.
    /// Использует ParsedItemCraftEvaluator.StatLineMatchesTemplate для нормализованного сравнения.
    /// </summary>
    public static AffixLibraryEntry? FindLibraryMatch(string stripped, IReadOnlyList<AffixLibraryEntry> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var template in entry.AffixStats)
            {
                if (ParsedItemCraftEvaluator.StatLineMatchesTemplate(stripped, template))
                    return entry;
            }
        }
        return null;
    }

    /// <summary>
    /// Известные крафтовые моды, которых нет в affix_library.json.
    /// Ключ — stripped-текст (без GGG-разметки), значение — (affixName, familyId).
    /// </summary>
    private static readonly Dictionary<string, (string AffixName, string FamilyId)> KnownCraftedMods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["+1 Prefix Modifier allowed"]  = ("Ancient Potent Liquid Contempt", "MaxPrefixCount"),
            ["-1 Prefix Modifier allowed"]  = ("Prefix Limiter",                 "MaxPrefixCount"),
            ["-1 Suffix Modifier allowed"]  = ("Suffix Limiter",                 "MaxSuffixCount"),
            ["+20% to Maximum Quality"]     = ("Quality Crafted",                "CraftedQuality"),
        };

    /// <summary>
    /// Разрешает крафтовый мод: сначала проверяет KnownCraftedMods, потом библиотеку.
    /// </summary>
    private static ParsedModInfo ResolveCraftedMod(string raw)
    {
        var stripped = StripMarkup(raw);
        if (KnownCraftedMods.TryGetValue(stripped, out var known))
        {
            return new ParsedModInfo
            {
                Raw        = stripped,
                Stripped   = stripped,
                AffixName  = known.AffixName,
                AffixType  = "Crafted Prefix Modifier",
                FamilyId   = known.FamilyId,
                Unmatched  = false,
            };
        }
        // Fallback — пытаемся через библиотеку (VL-radius и другие crafted из library)
        var entries = AffixLibrary.GetEntries();
        var runeEntries = RuneAffixLibrary.GetAllEntries();
        return AffixResolver.ResolvePlainPublic(stripped, isFractured: false, entries, runeEntries);
    }

    /// <summary>Разбирает все моды SaleRecord (explicit, fractured, desecrate, crafted) против библиотеки аффиксов.</summary>
    public static List<ParsedModInfo> ParseMods(SaleRecord record, IReadOnlyList<AffixLibraryEntry> entries)
    {
        var runeEntries = RuneAffixLibrary.GetAllEntries();
        var result = new List<ParsedModInfo>();
        result.AddRange(AffixResolver.Resolve(record.ExplicitMods, isFractured: false, entries, runeEntries));
        result.AddRange(AffixResolver.Resolve(record.FracturedMods, isFractured: true, entries, runeEntries));
        result.AddRange(AffixResolver.Resolve(record.DesecrateMods, isFractured: false, entries, runeEntries));
        result.AddRange(record.CraftedMods.Select(ResolveCraftedMod));
        result.AddRange(AffixResolver.Resolve(record.ImplicitMods, isFractured: false, entries, runeEntries));
        return result;
    }
}
