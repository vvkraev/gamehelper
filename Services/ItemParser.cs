using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Represents a single affix modifier on an item
/// </summary>
public class AffixInfo
{
    public string Type { get; set; } = ""; // "Prefix Modifier" or "Suffix Modifier"
    public string Name { get; set; } = "";
    public int Tier { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Effects { get; set; } = new();
    /// <summary>Разбор строк эффектов: значение, диапазон в скобках, текст стата.</summary>
    public List<AffixEffectLine> EffectDetails { get; set; } = new();
}

/// <summary>Одна строка эффекта: одно вхождение <c>42(39-42)% …</c> или несколько <c>1(1-2) to 38(33-40) …</c>.</summary>
public sealed class AffixEffectLine
{
    public string Raw { get; set; } = "";
    public string StatText { get; set; } = "";
    public string? Range { get; set; }
    public string? RolledValue { get; set; }
}

/// <summary>
/// Represents a parsed item with all its properties
/// </summary>
public class ParsedItem
{
    public bool IsValid { get; set; }
    public string ItemClass { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Name { get; set; } = "";
    public string Base { get; set; } = "";
    public string Quality { get; set; } = "";
    public List<string> Characteristics { get; set; } = new();
    /// <summary>Строки с пометкой <c>(rune)</c> — отдельно от базовых характеристик.</summary>
    public List<string> Augments { get; set; } = new();
    public string Requirements { get; set; } = "";
    public string Sockets { get; set; } = "";
    public int ItemLevel { get; set; }
    public List<string> InsertedItems { get; set; } = new();
    public List<AffixInfo> Affixes { get; set; } = new();
    public string State { get; set; } = ""; // Corrupted, Sanctified, etc.
    /// <summary>Текущий размер стака (из «Stack Size: X/Y»). 0 — не стакующийся предмет.</summary>
    public int StackSize { get; set; }
}

/// <summary>
/// Parses item text according to PoE2 item format as defined in ITEM_PARSING.md
/// </summary>
public static class ItemParser
{
    private const string SectionSeparator = "--------";

    public static ParsedItem? Parse(string itemText)
    {
        if (string.IsNullOrWhiteSpace(itemText))
        {
            return null;
        }

        var item = new ParsedItem();

        try
        {
            var lines = itemText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            if (lines.Count == 0)
                return null;

            var sections = SplitIntoSections(lines);

            // Заголовок предмета
            if (sections.Count > 0 && !ParseSection1(sections[0], item))
                return null;

            // Все блоки после заголовка — по содержимому (порядок и наличие блоков могут меняться).
            for (var i = 1; i < sections.Count; i++)
                ParseDynamicSection(sections[i], item);

            // Секции могут отсутствовать — дочитываем Item Level / Requires / Sockets с любой позиции.
            ApplyGlobalKeyedFields(sections, item);

            item.IsValid = true;
            return item;
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"ItemParser error: {ex.Message}");
            return null;
        }
    }

    private static List<List<string>> SplitIntoSections(List<string> lines)
    {
        var sections = new List<List<string>>();
        var currentSection = new List<string>();

        foreach (var line in lines)
        {
            if (line == SectionSeparator)
            {
                if (currentSection.Count > 0)
                {
                    sections.Add(currentSection);
                    currentSection = new List<string>();
                }
            }
            else
            {
                currentSection.Add(line);
            }
        }

        if (currentSection.Count > 0)
        {
            sections.Add(currentSection);
        }

        return sections;
    }

    /// <summary>
    /// Извлекает поля вида «Item Level:», «Requires:», «Sockets:» из любой секции — порядок блоков в буфере не фиксирован.
    /// </summary>
    private static void ApplyGlobalKeyedFields(List<List<string>> sections, ParsedItem item)
    {
        foreach (var section in sections)
        {
            foreach (var line in section)
            {
                if (line.StartsWith("Item Level:", StringComparison.Ordinal))
                {
                    var levelStr = line["Item Level:".Length..].Trim();
                    if (int.TryParse(levelStr, out var level))
                        item.ItemLevel = level;
                }
                else if (item.Requirements.Length == 0 && line.StartsWith("Requires:", StringComparison.Ordinal))
                {
                    item.Requirements = line["Requires:".Length..].Trim();
                }
                else if (item.Sockets.Length == 0 && line.StartsWith("Sockets:", StringComparison.Ordinal))
                {
                    item.Sockets = line["Sockets:".Length..].Trim();
                }
                else if (item.StackSize == 0 && line.StartsWith("Stack Size:", StringComparison.Ordinal))
                {
                    var val = line["Stack Size:".Length..].Trim();
                    var slash = val.IndexOf('/');
                    var numStr = slash >= 0 ? val[..slash] : val;
                    // PoE2 форматирует тысячи через запятую или пробел (1,700 / 1 700)
                    var clean = numStr.Trim().Replace(",", "").Replace(" ", "").Replace(" ", "");
                    if (int.TryParse(clean, out var sz))
                        item.StackSize = sz;
                }
            }
        }
    }

    /// <summary>
    /// Обрабатывает один блок после <c>--------</c> по содержимому, а не по фиксированному номеру секции.
    /// </summary>
    private static void ParseDynamicSection(List<string> section, ParsedItem item)
    {
        var nonEmpty = section.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmpty.Count == 0)
            return;

        if (nonEmpty.Any(static l =>
                l.StartsWith('{') &&
                (l.Contains("Prefix Modifier", StringComparison.Ordinal) ||
                 l.Contains("Suffix Modifier", StringComparison.Ordinal))))
        {
            ParseSection7(nonEmpty, item);
            return;
        }

        if (nonEmpty.Count == 1 &&
            (string.Equals(nonEmpty[0], "Corrupted", StringComparison.Ordinal) ||
             string.Equals(nonEmpty[0], "Sanctified", StringComparison.Ordinal)))
        {
            item.State = nonEmpty[0];
            return;
        }

        if (nonEmpty.All(static l => l.StartsWith("Requires:", StringComparison.Ordinal)))
        {
            ParseSection3(nonEmpty, item);
            return;
        }

        if (nonEmpty.Any(static l => l.StartsWith("Sockets:", StringComparison.Ordinal)))
        {
            ParseSection4(nonEmpty, item);
            return;
        }

        if (nonEmpty.All(static l => l.StartsWith("Item Level:", StringComparison.Ordinal)))
        {
            foreach (var line in nonEmpty)
            {
                var levelStr = line["Item Level:".Length..].Trim();
                if (int.TryParse(levelStr, out var level))
                    item.ItemLevel = level;
            }

            return;
        }

        ParseSection2(nonEmpty, item);
    }

    private static bool ParseSection1(List<string> section, ParsedItem item)
    {
        // Section 1: Item Class;Rarity;Artistic Name(optional);Base Item
        var classLine = section.FirstOrDefault(l => l.StartsWith("Item Class:"));
        if (classLine == null)
            return false;

        item.ItemClass = classLine.Replace("Item Class:", "").Trim();

        var rarityLine = section.FirstOrDefault(l => l.StartsWith("Rarity:"));
        if (rarityLine != null)
        {
            item.Rarity = rarityLine.Replace("Rarity:", "").Trim();
        }

        // Name and Base are the remaining non-special lines
        var otherLines = section.Where(l => !l.StartsWith("Item Class:") && !l.StartsWith("Rarity:")).ToList();
        if (otherLines.Count >= 1)
            item.Name = otherLines[0];
        if (otherLines.Count >= 2)
            item.Base = otherLines[1];

        return true;
    }

    private static void ParseSection2(List<string> section, ParsedItem item)
    {
        // Section 2: Quality and Characteristics
        foreach (var line in section)
        {
            if (line.StartsWith("Quality:"))
            {
                item.Quality = line.Replace("Quality:", "").Trim();
            }
            else if (!line.StartsWith("{") && !string.IsNullOrEmpty(line))
            {
                if (IsRuneAugmentLine(line))
                    item.Augments.Add(line);
                else
                    item.Characteristics.Add(line);
            }
        }
    }

    private static bool IsRuneAugmentLine(string line) =>
        line.Contains("(rune)", StringComparison.OrdinalIgnoreCase);

    private static void ParseSection3(List<string> section, ParsedItem item)
    {
        // Section 3: Requires
        var requiresLine = section.FirstOrDefault(l => l.StartsWith("Requires:"));
        if (requiresLine != null)
        {
            item.Requirements = requiresLine.Replace("Requires:", "").Trim();
        }
    }

    private static void ParseSection4(List<string> section, ParsedItem item)
    {
        // Section 4: Sockets
        var socketsLine = section.FirstOrDefault(l => l.StartsWith("Sockets:"));
        if (socketsLine != null)
        {
            item.Sockets = socketsLine.Replace("Sockets:", "").Trim();
        }
    }

    private static void ParseSection7(List<string> section, ParsedItem item)
    {
        // Section 7: Affixes
        AffixInfo? currentAffix = null;

        foreach (var line in section)
        {
            // Parse affix header: { Prefix Modifier "Name" (Tier: N) — Tags }
            if (line.StartsWith("{") && line.EndsWith("}"))
            {
                if (currentAffix != null)
                {
                    item.Affixes.Add(currentAffix);
                }

                currentAffix = ParseAffixHeader(line);
            }
            else if (currentAffix != null && !string.IsNullOrWhiteSpace(line))
            {
                currentAffix.Effects.Add(line);
                currentAffix.EffectDetails.Add(ParseAffixEffectLine(line));
            }
        }

        if (currentAffix != null)
        {
            item.Affixes.Add(currentAffix);
        }
    }

    private static AffixInfo ParseAffixHeader(string headerLine)
    {
        var affix = new AffixInfo();

        // Remove braces
        var content = headerLine.Trim('{', '}').Trim();

        // Тип модификатора (длинные варианты — раньше коротких)
        if (content.StartsWith("Desecrated Prefix Modifier", StringComparison.Ordinal))
        {
            affix.Type = "Desecrated Prefix Modifier";
            content = content["Desecrated Prefix Modifier".Length..].Trim();
        }
        else if (content.StartsWith("Desecrated Suffix Modifier", StringComparison.Ordinal))
        {
            affix.Type = "Desecrated Suffix Modifier";
            content = content["Desecrated Suffix Modifier".Length..].Trim();
        }
        else if (content.StartsWith("Fractured Prefix Modifier", StringComparison.Ordinal))
        {
            // Для условий крафта и подсчёта слотов считаем это обычным Prefix Modifier.
            affix.Type = "Prefix Modifier";
            content = content["Fractured Prefix Modifier".Length..].Trim();
        }
        else if (content.StartsWith("Fractured Suffix Modifier", StringComparison.Ordinal))
        {
            affix.Type = "Suffix Modifier";
            content = content["Fractured Suffix Modifier".Length..].Trim();
        }
        else if (content.StartsWith("Prefix Modifier", StringComparison.Ordinal))
        {
            affix.Type = "Prefix Modifier";
            content = content["Prefix Modifier".Length..].Trim();
        }
        else if (content.StartsWith("Suffix Modifier", StringComparison.Ordinal))
        {
            affix.Type = "Suffix Modifier";
            content = content["Suffix Modifier".Length..].Trim();
        }

        // Extract name (in quotes)
        var nameMatch = Regex.Match(content, "\"([^\"]+)\"");
        if (nameMatch.Success)
        {
            affix.Name = nameMatch.Groups[1].Value;
            content = content.Substring(nameMatch.Index + nameMatch.Length).Trim();
        }

        // Extract tier
        var tierMatch = Regex.Match(content, @"\(Tier:\s*(\d+)\)");
        if (tierMatch.Success)
        {
            if (int.TryParse(tierMatch.Groups[1].Value, out var tier))
            {
                affix.Tier = tier;
            }
            content = content.Substring(tierMatch.Index + tierMatch.Length).Trim();
        }

        // Extract tags (after —)
        var tagsSplit = content.Split('—');
        if (tagsSplit.Length > 1)
        {
            var tagsStr = tagsSplit[1].Trim();
            affix.Tags = tagsStr.Split(',').Select(t => t.Trim()).ToList();
        }

        return affix;
    }

    /// <summary>
    /// Перекат и тир: целые или дробные, напр. <c>85(75-89)</c>, <c>+4.47(4.41-5)</c>, <c>10(1-13)</c>.
    /// </summary>
    private static readonly Regex RollWithTierRange = new(
        @"(\+?\d+(?:\.\d+)?)\(([\d.]+)-([\d.]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Ведущее число со знаком (целое или дробное), без скобок тира: <c>+4 to …</c>, <c>+4.5 …</c>.</summary>
    private static readonly Regex FlatLeadingSignedNumber = new(
        @"^\s*(\+|-)?(\d+(?:\.\d+)?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] RollPlaceholders =
    {
        "X", "Y", "Z", "W", "V", "U", "T", "S", "R", "Q", "P", "O", "N", "M", "L", "K",
    };

    /// <summary>
    /// Разбор строки эффекта: <c>N(мин-макс)</c> (одно или несколько), иначе ведущее <c>±число</c> без скобок — Stat без переката (<c>+ to Level …</c>), Range и Value — число.
    /// </summary>
    private static AffixEffectLine ParseAffixEffectLine(string line)
    {
        var raw = line.Trim();
        var matches = RollWithTierRange.Matches(raw);
        if (matches.Count == 0)
        {
            var flat = FlatLeadingSignedNumber.Match(raw);
            if (flat.Success)
            {
                var sign = flat.Groups[1].Value;
                var num = flat.Groups[2].Value;
                var display = sign == "-" ? $"-{num}" : num;
                var rest = raw.Substring(flat.Index + flat.Length).TrimStart();
                var statText = rest;
                if (sign == "+")
                    statText = "+ " + rest;
                else if (sign == "-")
                    statText = "- " + rest;

                return new AffixEffectLine
                {
                    Raw = raw,
                    StatText = statText,
                    Range = display,
                    RolledValue = display,
                };
            }

            return new AffixEffectLine
            {
                Raw = raw,
                StatText = raw,
                Range = null,
                RolledValue = null,
            };
        }

        if (matches.Count == 1)
        {
            var m = matches[0];
            var before = raw.Substring(0, m.Index).TrimEnd();
            var after = raw.Substring(m.Index + m.Length).TrimStart();

            // Шаблон стата: префикс до переката + хвост после «N(мин-макс)», напр.
            // «Map has 49(35-50)% increased …» → «Map has % increased …»; не только «% increased …».
            string statText;
            if (string.IsNullOrEmpty(before))
            {
                statText = after;
                if (raw.StartsWith("+", StringComparison.Ordinal) && !after.StartsWith("%", StringComparison.Ordinal))
                    statText = "+ " + after.TrimStart();
            }
            else
            {
                statText = before + " " + after;
            }

            return new AffixEffectLine
            {
                Raw = raw,
                StatText = statText,
                Range = $"{m.Groups[2].Value}-{m.Groups[3].Value}",
                RolledValue = m.Groups[1].Value.TrimStart('+'),
            };
        }

        // Несколько перекатов в одной строке, напр. 1(1-2) to 38(33-40)
        var statTextMulti = raw;
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var m = matches[i];
            var label = PlaceholderForSlot(i);
            statTextMulti = statTextMulti.Remove(m.Index, m.Length).Insert(m.Index, label);
        }

        var rangeParts = new List<string>(matches.Count);
        var valParts = new List<string>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var label = PlaceholderForSlot(i);
            rangeParts.Add($"{label}({m.Groups[2].Value}-{m.Groups[3].Value})");
            valParts.Add($"{label}:{m.Groups[1].Value.TrimStart('+')}");
        }

        return new AffixEffectLine
        {
            Raw = raw,
            StatText = statTextMulti,
            Range = string.Join("; ", rangeParts),
            RolledValue = string.Join("; ", valParts),
        };
    }

    private static string PlaceholderForSlot(int matchIndex)
    {
        return matchIndex < RollPlaceholders.Length
            ? RollPlaceholders[matchIndex]
            : $"X{matchIndex + 1}";
    }
}