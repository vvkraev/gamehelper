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

    public static CraftRunFileLog Begin(
        ScreenRect orb,
        ScreenRect item,
        int maxOps,
        string affixPattern,
        int minRoll)
    {
        var logDir = GameHelper.ProjectPaths.GetLogDirectory();
        var start = DateTime.Now;
        var wip = Path.Combine(logDir, $"craft_{FormatStamp(start)}_wip.tmp");
        var stream = new FileStream(wip, FileMode.Create, FileAccess.Write, FileShare.Read);
        var w = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        w.WriteLine($"=== Запуск крафта {start:yyyy-MM-dd HH:mm:ss} ===");
        w.WriteLine($"Максимум операций (N): {maxOps}");
        w.WriteLine($"Область Chaos Orb: X={orb.X}, Y={orb.Y}, W={orb.Width}, H={orb.Height}");
        w.WriteLine($"Область предмета: X={item.X}, Y={item.Y}, W={item.Width}, H={item.Height}");
        w.WriteLine($"Шаблон аффикса: {QuoteMultiline(affixPattern)}");
        w.WriteLine(affixPattern.Contains('n', StringComparison.Ordinal)
            ? $"Минимальное значение числа на месте «n»: {minRoll}"
            : "Режим без «n»: проверка вхождения подстроки (порог min не используется).");
        w.WriteLine();
        w.WriteLine("--- Сравнения по попыткам ---");
        w.WriteLine();

        return new CraftRunFileLog(wip, start, w);
    }

    private CraftRunFileLog(string wipPath, DateTime runStart, StreamWriter writer)
    {
        _wipPath = wipPath;
        _runStart = runStart;
        _writer = writer;
    }

    /// <summary>Запись одной итерации: буфер, шаблон, порог, пояснение, успех.</summary>
    public void WriteComparison(
        int attempt,
        int maxOps,
        string clipboardRaw,
        string affixPattern,
        int minRoll,
        bool success,
        string explanation)
    {
        _writer.WriteLine($"========== Попытка {attempt} / {maxOps} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
        _writer.WriteLine("Шаблон аффикса:");
        _writer.WriteLine(string.IsNullOrWhiteSpace(affixPattern) ? "(пусто)" : affixPattern);
        _writer.WriteLine(affixPattern.Contains('n', StringComparison.Ordinal)
            ? $"Минимальный порог числа (min): {minRoll}"
            : "Режим подстроки (без n).");
        _writer.WriteLine();
        _writer.WriteLine("Содержимое буфера обмена после Ctrl+C:");
        _writer.WriteLine(clipboardRaw.Length == 0 ? "(пусто — текста нет или не текстовый формат)" : clipboardRaw);
        _writer.WriteLine();
        _writer.WriteLine("Пояснение проверки:");
        _writer.WriteLine(explanation);
        _writer.WriteLine();
        _writer.WriteLine(success
            ? "Итог попытки: УСПЕХ — условие остановки выполнено."
            : "Итог попытки: условие не выполнено — крафт продолжается.");
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
