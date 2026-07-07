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

    /// <summary>Разбирает все моды SaleRecord (explicit, fractured, desecrate, crafted) против библиотеки аффиксов.</summary>
    public static List<ParsedModInfo> ParseMods(SaleRecord record, IReadOnlyList<AffixLibraryEntry> entries)
    {
        var runeEntries = RuneAffixLibrary.GetAllEntries();
        var result = new List<ParsedModInfo>();
        result.AddRange(AffixResolver.Resolve(record.ExplicitMods, isFractured: false, entries, runeEntries));
        result.AddRange(AffixResolver.Resolve(record.FracturedMods, isFractured: true, entries, runeEntries));
        result.AddRange(AffixResolver.Resolve(record.DesecrateMods, isFractured: false, entries, runeEntries));
        result.AddRange(AffixResolver.Resolve(record.CraftedMods, isFractured: false, entries, runeEntries));
        result.AddRange(AffixResolver.Resolve(record.ImplicitMods, isFractured: false, entries, runeEntries));
        return result;
    }
}
