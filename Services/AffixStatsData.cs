using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

public sealed class AffixStatsData
{
    // v10: ClassStats tracks SnapshotsByFracturedAffix to allow fracture-corrected frequency display.
    public int Version { get; set; } = 10;

    /// <summary>
    /// Item classes for which <see cref="MakeClassKey"/> includes the armour subtype segment.
    /// Other classes (weapons, jewellery, …) always use an empty subtype.
    /// </summary>
    public static readonly HashSet<string> SubTypeClasses = new(StringComparer.Ordinal)
    {
        "Body Armours", "Gloves", "Helmets", "Boots",
    };

    [JsonPropertyName("processedLogFiles")]
    public HashSet<string> ProcessedLogFiles { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("perClass")]
    public Dictionary<string, ClassStats> PerClass { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Строит ключ для <see cref="PerClass"/>:
    /// "ItemClass|SubType|OrbName", пропуская пустые сегменты.
    /// </summary>
    public static string MakeClassKey(string itemClass, string itemSubType, string orbName = "")
    {
        var parts = new List<string>(3) { itemClass };
        if (!string.IsNullOrEmpty(itemSubType)) parts.Add(itemSubType);
        if (!string.IsNullOrEmpty(orbName)) parts.Add(orbName);
        return string.Join("|", parts);
    }

    public ClassStats GetOrCreate(string itemClass, string itemSubType = "", string orbName = "")
    {
        var key = MakeClassKey(itemClass, itemSubType, orbName);
        if (!PerClass.TryGetValue(key, out var s))
            PerClass[key] = s = new ClassStats();
        return s;
    }

    /// <summary>Returns (count of appearances, total item snapshots for this class+subtype+orb).</summary>
    public (int count, int total) GetCounts(string itemClass, string itemSubType, string affixName, string orbName = "")
    {
        var key = MakeClassKey(itemClass, itemSubType, orbName);
        if (!PerClass.TryGetValue(key, out var cs)) return (0, 0);
        cs.AffixCounts.TryGetValue(affixName, out var count);
        return (count, cs.TotalSnapshots);
    }

    /// <summary>
    /// При загрузке данных старой версии сбрасывает статистику — данные пересобираются
    /// из лог-файлов с новым форматом ключа.
    /// </summary>
    public void MigrateIfNeeded()
    {
        if (Version < 5)
        {
            PerClass.Clear();
            ProcessedLogFiles.Clear();
            Version = 5;
        }
    }
}

public sealed class ClassStats
{
    [JsonPropertyName("totalSnapshots")]
    public int TotalSnapshots { get; set; }

    /// <summary>
    /// Снапшоты, где был хотя бы один зафрактурированный аффикс.
    /// Снапшоты без фрактур исключаются из коррекции — они могут быть из другого крафта.
    /// </summary>
    [JsonPropertyName("snapshotsWithFracture")]
    public int SnapshotsWithFracture { get; set; }

    /// <summary>
    /// Сколько снапшотов имели данный стат-вариант зафрактурированным.
    /// Ключ — статный ключ вида "AffixName|normalizedStatText" (как MakeStatKey).
    /// Позволяет различать "of Potency (Crit Dmg Bonus)" и "of Potency (Effect of Small Passives)".
    /// </summary>
    [JsonPropertyName("snapshotsByFracturedAffix")]
    public Dictionary<string, int> SnapshotsByFracturedAffix { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Сколько раз стат-вариант X выпал в снапшотах, где была зафрактурирована ДРУГАЯ позиция.
    /// Ключ — статный ключ "AffixName|normalizedStatText".
    /// </summary>
    [JsonPropertyName("affixCountsInFracturedSnapshots")]
    public Dictionary<string, int> AffixCountsInFracturedSnapshots { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Знаменатель для скорректированной частоты стат-варианта X:
    /// снапшоты с фрактурой, где X сам не был зафрактурирован.
    /// <paramref name="statKey"/> — ключ вида "AffixName|normalizedStatText".
    /// </summary>
    public int GetAvailableSamples(string statKey) =>
        SnapshotsWithFracture - SnapshotsByFracturedAffix.GetValueOrDefault(statKey, 0);

    /// <summary>Количество предметов, на которых встречался аффикс с данным именем (любой вариант).</summary>
    [JsonPropertyName("affixCounts")]
    public Dictionary<string, int> AffixCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Количество предметов, на которых встречался конкретный стат-шаблон аффикса.
    /// Ключ: <c>"AffixName|normalizedStatText"</c> — позволяет различать, например,
    /// «Thrud's — Speed» от «Thrud's — Critical» у одного и того же аффикса.
    /// </summary>
    [JsonPropertyName("statTemplateCounts")]
    public Dictionary<string, int> StatTemplateCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Точное количество для данного аффикса + шаблона стата.
    /// Шаблон нормализуется так же, как при сохранении (lowercase, trim, без '#').
    /// </summary>
    public int GetStatCount(string affixName, string statTemplate)
    {
        StatTemplateCounts.TryGetValue(MakeStatKey(affixName, statTemplate), out var c);
        return c;
    }

    // Matches standalone x/y variable tokens used by ItemParser for roll placeholders.
    private static readonly Regex VarTokens = new(@"\b[xy]\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches PoE2 item text roll format: "10(5-10)" or "2.5(1.0-3.0)" → replaces with "#".
    private static readonly Regex RollPattern = new(
        @"\d+(?:\.\d+)?\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Заменяет конкретные значения роллов на "#", чтобы все варианты одного мода
    /// давали одинаковый стат-ключ независимо от выпавшего числа.
    /// Например: "10(5-10)% increased Crit" → "#% increased Crit".
    /// </summary>
    public static string NormalizeRolls(string rawStatText) =>
        RollPattern.Replace(rawStatText, "#");

    /// <summary>Формирует ключ словаря из имени аффикса и шаблона стата.</summary>
    public static string MakeStatKey(string affixName, string statTemplate)
    {
        var norm = statTemplate.Trim().ToLowerInvariant()
            .Replace("#", "", StringComparison.Ordinal);
        // Strip x/y variable placeholders (ItemParser) so they match # templates from the library.
        norm = VarTokens.Replace(norm, "").Trim();
        // Strip "— unscalable value" suffix added by poe2db for fixed-roll stats.
        const string unscalable = " — unscalable value";
        if (norm.EndsWith(unscalable, StringComparison.Ordinal))
            norm = norm[..^unscalable.Length].TrimEnd();
        while (norm.Contains("  ", StringComparison.Ordinal))
            norm = norm.Replace("  ", " ", StringComparison.Ordinal);
        return affixName + "|" + norm.Trim();
    }
}
