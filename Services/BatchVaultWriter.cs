using System.IO;
using System.Text;

namespace GameHelper.Services;

/// <summary>
/// Пишет таблицу расходов батча в MD-файл в каталоге vault.
/// Вызывается периодически из <see cref="BatchPipelineRunner"/> в процессе крафта.
/// </summary>
public sealed class BatchVaultWriter
{
    private readonly string _outPath;
    private readonly decimal _baseCostEach;
    private readonly int _baseCount;
    private readonly string _priceDate;

    // Фиксированный порядок вывода валют.
    private static readonly string[] DisplayOrder =
    [
        "Preserved Cranium", "Ancient Collarbone", "Ancient Jawbone", "Altered Collarbone",
        "Ancient Potent Liquid Contempt", "Ancient Potent Liquid Melancholy",
        "Potent Liquid Contempt", "Potent Liquid Melancholy",
        "Chaos Orb", "Greater Chaos Orb", "Perfect Chaos Orb",
        "Orb of Annulment", "Exalted Orb", "Divine Orb",
        "Fracturing Orb",
        "Omen of Abyssal Echoes", "Omen of Dextral Exaltation", "Omen of Sinistral Exaltation",
        "Omen of Dextral Annulment", "Omen of Sinistral Annulment",
        "Omen of Dextral Erasure", "Omen of Sinistral Erasure",
        "Omen of Whittling", "Omen of Sanctification",
    ];

    public BatchVaultWriter(string vaultRelDir, int baseCount, decimal baseCostEach)
    {
        var root = ProjectPaths.GetProjectRoot();
        var dir  = Path.Combine(root, vaultRelDir.TrimStart('/', '\\', '.'));
        Directory.CreateDirectory(dir);
        _outPath      = Path.Combine(dir, "_costs.md");
        _baseCount    = baseCount;
        _baseCostEach = baseCostEach;
        _priceDate    = PoeNinjaPriceService.LastFetchedAt?.ToString("yyyy-MM-dd") ?? "?";
    }

    /// <summary>
    /// Пересчитывает суммарные расходы из CostRecords всех предметов и перезаписывает файл.
    /// Потокобезопасен при условии что список <paramref name="items"/> в этот момент не изменяется.
    /// </summary>
    public void Write(IReadOnlyList<BatchItem> items)
    {
        // Суммируем количества по валютам
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var rec in item.CostRecords)
            {
                totals.TryGetValue(rec.CurrencyName, out var existing);
                totals[rec.CurrencyName] = existing + rec.Attempts;
            }

        if (totals.Count == 0) return;

        var done    = items.Count(i => i.Status == BatchItemStatus.Done);
        var failed  = items.Count(i => i.Status == BatchItemStatus.Failed);
        var active  = items.Count(i => i.Status == BatchItemStatus.Active);
        var updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var sb = new StringBuilder();
        sb.AppendLine("## Расходы батча");
        sb.AppendLine();
        sb.AppendLine($"> Цены: poe.ninja {_priceDate} | Обновлено: {updated}");
        sb.AppendLine($"> Статус: Done={done}, Failed={failed}, Active={active}");
        sb.AppendLine();

        // Упорядочиваем ключи: сначала в DisplayOrder, потом остальные
        var orderedKeys  = DisplayOrder.Where(totals.ContainsKey).ToList();
        var extraKeys    = totals.Keys.Except(DisplayOrder, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var allKeys = orderedKeys.Concat(extraKeys).ToList();

        // Вычисляем ширины колонок
        int colName  = Math.Max("Валюта".Length, allKeys.Max(k => k.Length));
        int colQty   = 7;
        int colPrice = 9;
        int colTotal = 10;

        string Sep() => $"|{new string('-', colName + 2)}|{new string('-', colQty + 2)}|{new string('-', colPrice + 2)}|{new string('-', colTotal + 2)}|";

        Row("Валюта".PadRight(colName), "Кол-во".PadLeft(colQty), "Цена".PadLeft(colPrice), "Сумма".PadLeft(colTotal));
        sb.AppendLine(Sep());

        decimal craftTotal = 0m;
        foreach (var key in allKeys)
        {
            var qty        = totals[key];
            var unitPrice  = PoeNinjaPriceService.GetPrice(key)?.DivineValue ?? 0m;
            var cost       = qty * unitPrice;
            craftTotal    += cost;
            var priceStr   = unitPrice > 0 ? $"{unitPrice:F4}d" : "—";
            var costStr    = unitPrice > 0 ? $"{cost:F2}d" : "—";
            Row(key.PadRight(colName), qty.ToString().PadLeft(colQty), priceStr.PadLeft(colPrice), costStr.PadLeft(colTotal));
        }

        sb.AppendLine(Sep());
        Row("**ИТОГО крафт**".PadRight(colName), "".PadLeft(colQty), "".PadLeft(colPrice), $"**{craftTotal:F2}d**".PadLeft(colTotal));

        if (_baseCostEach > 0)
        {
            var baseTotal  = _baseCostEach * _baseCount;
            var baseLabel  = $"Базы ({_baseCount} × {_baseCostEach:F0}d)";
            var grandTotal = craftTotal + baseTotal;
            Row(baseLabel.PadRight(colName), _baseCount.ToString().PadLeft(colQty), $"{_baseCostEach:F0}d".PadLeft(colPrice), $"**{baseTotal:F2}d**".PadLeft(colTotal));
            sb.AppendLine(Sep());
            Row("**ИТОГО С БАЗАМИ**".PadRight(colName), "".PadLeft(colQty), "".PadLeft(colPrice), $"**{grandTotal:F2}d**".PadLeft(colTotal));
            Row("**На предмет**".PadRight(colName), _baseCount.ToString().PadLeft(colQty), "".PadLeft(colPrice), $"**{grandTotal / _baseCount:F1}d**".PadLeft(colTotal));
        }

        void Row(string c1, string c2, string c3, string c4) =>
            sb.AppendLine($"| {c1} | {c2} | {c3} | {c4} |");

        try
        {
            File.WriteAllText(_outPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            SessionLogger.WriteFileOnly($"[BatchVaultWriter] Ошибка записи {_outPath}: {ex.Message}");
        }
    }
}
