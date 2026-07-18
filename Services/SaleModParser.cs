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
    /// Известные крафтовые моды с точным совпадением текста.
    /// Ключ — stripped-текст, значение — (affixName, familyId).
    /// </summary>
    private static readonly Dictionary<string, (string AffixName, string FamilyId)> KnownCraftedExact =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["+1 Prefix Modifier allowed"]   = ("Ancient Potent Liquid Contempt", "MaxPrefixCount"),
            ["+1 Suffix Modifier allowed"]   = ("Ancient Potent Liquid Contempt", "MaxSuffixCount"),
            ["-1 Prefix Modifier allowed"]   = ("Prefix Limiter",                 "MaxPrefixCount"),
            ["-1 Suffix Modifier allowed"]   = ("Suffix Limiter",                 "MaxSuffixCount"),
            ["+20% to Maximum Quality"]      = ("Quality Crafted",                "CraftedQuality"),
            ["Upgrades Radius to Very Large"] = ("Ancient Potent Liquid Melancholy", "JewelRadiusUpgrade"),
        };

    /// <summary>
    /// Известные крафтовые моды с числовым роллом — шаблон в формате affix_library (#% вместо числа).
    /// </summary>
    private static readonly (string Template, string AffixName, string FamilyId)[] KnownCraftedTemplates =
    [
        ("#% increased Effect of Suffixes",              "Potent Liquid Ferocity",   "DeliriumEffectSuffixes"),
        ("#% increased Effect of Prefixes",              "Potent Liquid Ferocity",   "DeliriumEffectPrefixes"),
        ("#% increased effect of Socketed Augment Items","Socketed Augment Crafted", "SocketedAugmentEffect"),
    ];

    /// <summary>
    /// Разрешает крафтовый мод: сначала проверяет известные точные и шаблонные моды,
    /// потом библиотеку (VL-radius и другие crafted из library).
    /// </summary>
    private static ParsedModInfo ResolveCraftedMod(string raw)
    {
        var stripped = StripMarkup(raw);

        if (KnownCraftedExact.TryGetValue(stripped, out var exact))
            return MakeCrafted(stripped, exact.AffixName, exact.FamilyId);

        foreach (var (template, affixName, familyId) in KnownCraftedTemplates)
        {
            if (ParsedItemCraftEvaluator.StatLineMatchesTemplate(stripped, template))
                return MakeCrafted(stripped, affixName, familyId);
        }

        var entries = AffixLibrary.GetEntries();
        var runeEntries = RuneAffixLibrary.GetAllEntries();
        return AffixResolver.ResolvePlainPublic(stripped, isFractured: false, entries, runeEntries);
    }

    private static ParsedModInfo MakeCrafted(string stripped, string affixName, string familyId) =>
        new() { Raw = stripped, Stripped = stripped, AffixName = affixName,
                AffixType = "Crafted Prefix Modifier", FamilyId = familyId, Unmatched = false };

    /// <summary>Разбирает все моды SaleRecord (explicit, fractured, desecrate, crafted) против библиотеки аффиксов.</summary>
    public static List<ParsedModInfo> ParseMods(SaleRecord record, IReadOnlyList<AffixLibraryEntry> entries)
    {
        var runeEntries = RuneAffixLibrary.GetAllEntries();
        var result = new List<ParsedModInfo>();

        // Старый формат: crafted-моды (AncPLC, QualityCrafted и т.д.) могли попасть в ExplicitMods
        // или FracturedMods — пробуем crafted-fallback для неразобранных.
        var explicitParsed = AffixResolver.Resolve(record.ExplicitMods, isFractured: false, entries, runeEntries);
        result.AddRange(explicitParsed.Select(m => m.Unmatched ? ResolveCraftedMod(m.Raw) : m));

        var fracParsed = AffixResolver.Resolve(record.FracturedMods, isFractured: true, entries, runeEntries);
        result.AddRange(fracParsed.Select(m => m.Unmatched ? ResolveCraftedMod(m.Raw) : m));
        result.AddRange(AffixResolver.Resolve(record.DesecrateMods, isFractured: false, entries, runeEntries));
        result.AddRange(record.CraftedMods.Select(ResolveCraftedMod));
        result.AddRange(AffixResolver.Resolve(record.ImplicitMods, isFractured: false, entries, runeEntries));
        return result;
    }
}
