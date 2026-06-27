using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHelper.Services;

public enum StackableItemKind { Catalyst, SoulCore, Delirium, Rune, Abyss, AncientAugment, Idol, Unknown }

public sealed class StackableItemType
{
    public string Id          { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public StackableItemKind Kind { get; set; }
}

/// <summary>
/// Реестр известных стакующихся предметов для перековки (катализаторы, ядра душ).
/// Персистируется в <c>stackable_item_types.json</c>.
/// </summary>
public static class StackableItemRegistry
{
    public static readonly string FilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stackable_item_types.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static List<StackableItemType> _items = new();

    public static IReadOnlyList<StackableItemType> Items => _items;

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            _items = JsonSerializer.Deserialize<List<StackableItemType>>(json, JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"StackableItemRegistry.Load error: {ex.Message}");
        }
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_items, JsonOpts)); }
        catch (Exception ex) { SessionLogger.Info($"StackableItemRegistry.Save error: {ex.Message}"); }
    }

    /// <summary>
    /// Определяет тип стакающегося предмета по <see cref="ParsedItem.Name"/>.
    /// Возвращает <see cref="StackableItemKind.Unknown"/> если это не каталог/soul core.
    /// </summary>
    public static StackableItemKind DetectKind(ParsedItem item)
    {
        var name = item.Name;
        if (string.IsNullOrWhiteSpace(name)) return StackableItemKind.Unknown;
        if (name.Contains("Catalyst",  StringComparison.OrdinalIgnoreCase)) return StackableItemKind.Catalyst;
        if (name.Contains("Soul Core", StringComparison.OrdinalIgnoreCase)) return StackableItemKind.SoulCore;
        if (name.Contains("Liquid",    StringComparison.OrdinalIgnoreCase)) return StackableItemKind.Delirium;
        if (name.Contains("Rune",      StringComparison.OrdinalIgnoreCase)) return StackableItemKind.Rune;
        if (name.Contains("Idol",      StringComparison.OrdinalIgnoreCase)) return StackableItemKind.Idol;
        // Abyssal Bones (Stackable Currency) — по ключевым словам
        if (name.Contains("Jawbone",    StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Collarbone", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cranium",    StringComparison.OrdinalIgnoreCase) ||
            name.Contains(" Rib",       StringComparison.OrdinalIgnoreCase)) return StackableItemKind.Abyss;
        // Abyss Omens — только специфичные для абисса
        if (name.StartsWith("Omen of ", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("Abyssal",     StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Necromancy",  StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Putrefaction",StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, "Omen of Light", StringComparison.OrdinalIgnoreCase)))
            return StackableItemKind.Abyss;
        // Уникальные абисс-джувелы (Ancient Augments sub-tab)
        if (name.EndsWith("'s Gaze", StringComparison.OrdinalIgnoreCase))
            return StackableItemKind.AncientAugment;
        return StackableItemKind.Unknown;
    }

    /// <summary>
    /// Пытается добавить предмет в реестр.
    /// Возвращает (true, item) если добавлен новый, (false, existing) если уже был.
    /// Возвращает (false, null) если предмет не является стакующимся.
    /// </summary>
    public static (bool Added, StackableItemType? Entry) TryRegister(ParsedItem item)
    {
        var kind = DetectKind(item);
        if (kind == StackableItemKind.Unknown) return (false, null);

        var name = item.Name.Trim();
        var existing = _items.Find(x => x.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return (false, existing);

        var entry = new StackableItemType
        {
            Id          = ToId(name),
            DisplayName = name,
            Kind        = kind,
        };
        _items.Add(entry);
        _items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return (true, entry);
    }

    public static void Clear()
    {
        _items.Clear();
        Save();
    }

    private static string ToId(string displayName) =>
        displayName.ToLowerInvariant()
                   .Replace(' ', '_')
                   .Replace("'", "")
                   .Replace("-", "_");
}
