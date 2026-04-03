using System.IO;
using System.Text;

namespace GameHelper.Services;

/// <summary>Один файл на запуск крафта: сохраняется при завершении RunAsync (успех, отмена, ошибка, лимит).</summary>
public sealed class CraftRunFileLog : IDisposable
{
    private readonly string _wipPath;
    private readonly DateTime _runStart;
    private readonly StreamWriter _writer;
    private ChaosCraftResult _result = ChaosCraftResult.Error;
    private string? _note;
    private bool _disposed;
    private int _cellIndex;
    private int _cellTotal;

    public static CraftRunFileLog Begin(
        string orbLabel,
        ScreenRect orb,
        ScreenRect? orb2,
        string? orb2Label,
        ScreenRect item,
        int maxOps,
        string conditionSummary,
        IReadOnlyList<ScreenRect>? allItemCells = null)
    {
        var logDir = GameHelper.ProjectPaths.GetLogDirectory();
        var start = DateTime.Now;
        var wip = Path.Combine(logDir, $"craft_{FormatStamp(start)}_wip.tmp");
        var stream = new FileStream(wip, FileMode.Create, FileAccess.Write, FileShare.Read);
        var w = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        w.WriteLine($"=== Запуск крафта {start:yyyy-MM-dd HH:mm:ss} ===");
        w.WriteLine($"Общий лимит операций (N) на всю сессию (все ячейки): {maxOps}");
        w.WriteLine($"Область {orbLabel}: X={orb.X}, Y={orb.Y}, W={orb.Width}, H={orb.Height}");
        if (orb2 is { } o2)
            w.WriteLine($"Область {orb2Label ?? "Orb2"}: X={o2.X}, Y={o2.Y}, W={o2.Width}, H={o2.Height}");
        w.WriteLine($"Область предмета (первая ячейка): X={item.X}, Y={item.Y}, W={item.Width}, H={item.Height}");
        if (allItemCells is { Count: > 1 })
        {
            w.WriteLine($"Ячеек предмета (по порядку обхода): {allItemCells.Count}");
            for (var i = 0; i < allItemCells.Count; i++)
            {
                var c = allItemCells[i];
                w.WriteLine($"  #{i + 1}: X={c.X}, Y={c.Y}, W={c.Width}, H={c.Height}");
            }
        }

        w.WriteLine("Условие остановки (разбор предмета, ItemParser):");
        w.WriteLine(string.IsNullOrWhiteSpace(conditionSummary) ? "(не задано)" : QuoteMultiline(conditionSummary));
        w.WriteLine();
        w.WriteLine("--- Сравнения по попыткам ---");
        w.WriteLine();

        return new CraftRunFileLog(wip, start, w);
    }

    public static CraftRunFileLog Begin(
        ScreenRect orb,
        ScreenRect item,
        int maxOps,
        string conditionSummary,
        IReadOnlyList<ScreenRect>? allItemCells = null) =>
        Begin("Chaos Orb", orb, null, null, item, maxOps, conditionSummary, allItemCells);

    private CraftRunFileLog(string wipPath, DateTime runStart, StreamWriter writer)
    {
        _wipPath = wipPath;
        _runStart = runStart;
        _writer = writer;
    }

    public void SetCurrentCell(int cellIndex1Based, int cellTotal)
    {
        _cellIndex = Math.Max(0, cellIndex1Based);
        _cellTotal = Math.Max(0, cellTotal);
    }

    /// <summary>Запись одной итерации: буфер, шаблон, порог, пояснение, успех.</summary>
    public void WriteComparison(
        int attempt,
        int maxOps,
        string clipboardRaw,
        string conditionSummary,
        bool success,
        string explanation)
    {
        _writer.WriteLine($"========== Попытка {attempt} / {maxOps} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
        if (_cellIndex > 0 && _cellTotal > 0)
            _writer.WriteLine($"Ячейка: {_cellIndex} / {_cellTotal}");
        _writer.WriteLine("Условие:");
        _writer.WriteLine(string.IsNullOrWhiteSpace(conditionSummary) ? "(пусто)" : conditionSummary);
        _writer.WriteLine();
        _writer.WriteLine("Содержимое буфера обмена после Ctrl+Alt+C:");
        _writer.WriteLine(clipboardRaw.Length == 0 ? "(пусто — текста нет или не текстовый формат)" : clipboardRaw);
        _writer.WriteLine();
        _writer.WriteLine("Пояснение проверки:");
        _writer.WriteLine(explanation);
        _writer.WriteLine();
        _writer.WriteLine(success ? "true" : "false");
        _writer.WriteLine();
    }

    public void WriteValidationError(string message)
    {
        _writer.WriteLine($"[Валидация] {message}");
        _note = message;
    }

    public void SetOutcome(ChaosCraftResult result, string? note = null)
    {
        _result = result;
        if (note != null)
            _note = note;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var end = DateTime.Now;

        try
        {
            _writer.WriteLine();
            _writer.WriteLine($"=== Завершение крафта {end:yyyy-MM-dd HH:mm:ss} ===");
            _writer.WriteLine($"Итог: {_result}");
            if (!string.IsNullOrEmpty(_note))
                _writer.WriteLine($"Примечание: {_note}");
        }
        finally
        {
            _writer.Dispose();
        }

        try
        {
            var dir = Path.GetDirectoryName(_wipPath);
            if (string.IsNullOrEmpty(dir) || !File.Exists(_wipPath))
                return;

            var finalName = $"craft_{FormatStamp(_runStart)}--{FormatStamp(end)}.txt";
            var finalPath = Path.Combine(dir, finalName);
            if (File.Exists(finalPath))
                finalPath = Path.Combine(dir, $"craft_{FormatStamp(_runStart)}--{FormatStamp(end)}_{Guid.NewGuid():N}.txt");

            File.Move(_wipPath, finalPath);
        }
        catch
        {
            // оставляем _wip.tmp
        }
    }

    private static string FormatStamp(DateTime t) => t.ToString("yyyy-MM-dd_HH-mm-ss");

    private static string QuoteMultiline(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);
}
