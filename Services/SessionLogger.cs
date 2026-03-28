using System.IO;
using ProjectRoot = GameHelper.ProjectPaths;

namespace GameHelper.Services;

/// <summary>
/// Лог сеанса: пишет в файл с момента запуска; при завершении приложения файл переименовывается
/// в «время_старта--время_закрытия.txt». Двоеточия в имени файла Windows не допускает — используется HH-mm-ss.
/// </summary>
public static class SessionLogger
{
    private static readonly object Gate = new();
    private static readonly List<string> SnapshotLines = new();

    private static DateTime _sessionStart;
    private static string _logsDirectory = "";
    private static string _wipPath = "";
    private static StreamWriter? _writer;

    /// <summary>Строка уже с префиксом времени (yyyy-MM-dd HH:mm:ss).</summary>
    public static event Action<string>? NewLine;

    public static void Initialize()
    {
        lock (Gate)
        {
            _sessionStart = DateTime.Now;
            _logsDirectory = ProjectRoot.GetLogDirectory();

            var startStamp = FormatStamp(_sessionStart);
            _wipPath = Path.Combine(_logsDirectory, $"session_{startStamp}_wip.tmp");

            SnapshotLines.Clear();

            _writer = new StreamWriter(
                new FileStream(_wipPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        Info("Приложение запущено.");
    }

    public static IReadOnlyList<string> GetSnapshot()
    {
        lock (Gate)
        {
            return SnapshotLines.ToArray();
        }
    }

    public static void Info(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{message}";
        lock (Gate)
        {
            SnapshotLines.Add(line);
            _writer?.WriteLine(line);
        }

        NewLine?.Invoke(line);
    }

    public static void Shutdown()
    {
        string wipCopy;
        string logsDirCopy;
        DateTime startCopy;
        var end = DateTime.Now;

        lock (Gate)
        {
            var footer = $"{end:yyyy-MM-dd HH:mm:ss}\tПриложение закрыто.";
            SnapshotLines.Add(footer);
            _writer?.WriteLine(footer);
            _writer?.Dispose();
            _writer = null;

            wipCopy = _wipPath;
            logsDirCopy = _logsDirectory;
            startCopy = _sessionStart;
            _wipPath = "";
        }

        NewLine?.Invoke($"{end:yyyy-MM-dd HH:mm:ss}\tПриложение закрыто.");

        if (string.IsNullOrEmpty(wipCopy) || !File.Exists(wipCopy))
            return;

        var finalName = $"session_{FormatStamp(startCopy)}--{FormatStamp(end)}.txt";
        var finalPath = Path.Combine(logsDirCopy, finalName);

        try
        {
            if (File.Exists(finalPath))
                finalPath = Path.Combine(logsDirCopy, $"{FormatStamp(startCopy)}--{FormatStamp(end)}_{Guid.NewGuid():N}.txt");

            File.Move(wipCopy, finalPath);
        }
        catch
        {
            // оставляем .tmp
        }
    }

    private static string FormatStamp(DateTime t) => t.ToString("yyyy-MM-dd_HH-mm-ss");
}
