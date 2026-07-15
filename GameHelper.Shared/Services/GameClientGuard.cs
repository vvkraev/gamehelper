using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Отслеживает доступность процесса игры:
/// — <see cref="EnsureRunning"/> — проверка при старте сессии крафта/торговли;
/// — <see cref="RecordSuccess"/> / <see cref="RecordMiss"/> — счётчик пустых ответов подряд;
///   при достижении лимита и мёртвом процессе выбрасывает <see cref="GameNotRunningException"/>.
/// </summary>
public sealed class GameClientGuard
{
    private readonly string _processName;
    private readonly int _maxConsecutiveMisses;
    private int _consecutiveMisses;

    public GameClientGuard(
        string processName = ProcessForeground.PathOfExile2SteamProcessName,
        int maxConsecutiveMisses = 5)
    {
        _processName = processName;
        _maxConsecutiveMisses = maxConsecutiveMisses;
    }

    /// <summary>Выбрасывает <see cref="GameNotRunningException"/>, если процесс не найден.</summary>
    public void EnsureRunning()
    {
        if (!ProcessForeground.IsProcessRunning(_processName))
            throw new GameNotRunningException(_processName);
    }

    /// <summary>Сбрасывает счётчик — вызывать, когда получен ожидаемый ответ от игры.</summary>
    public void RecordSuccess() => _consecutiveMisses = 0;

    /// <summary>
    /// Регистрирует пустой/неожиданный ответ. При достижении лимита:
    /// если процесс мёртв — <see cref="GameNotRunningException"/>;
    /// если жив — логирует предупреждение и сбрасывает счётчик.
    /// </summary>
    public void RecordMiss(IProgress<string>? log = null)
    {
        _consecutiveMisses++;
        if (_consecutiveMisses < _maxConsecutiveMisses)
            return;

        if (!ProcessForeground.IsProcessRunning(_processName))
            throw new GameNotRunningException(_processName, _consecutiveMisses);

        log?.Report($"  ⚠ {_consecutiveMisses} пустых ответов подряд, но процесс жив — продолжаем");
        _consecutiveMisses = 0;
    }

    public bool IsRunning() => ProcessForeground.IsProcessRunning(_processName);
}

/// <summary>Выбрасывается, когда процесс игры не найден.</summary>
public sealed class GameNotRunningException : Exception
{
    public string ProcessName { get; }
    public int ConsecutiveMisses { get; }

    public GameNotRunningException(string processName)
        : base($"Процесс игры «{processName}» не запущен.")
    {
        ProcessName = processName;
    }

    public GameNotRunningException(string processName, int consecutiveMisses)
        : base($"Процесс «{processName}» не найден после {consecutiveMisses} пустых ответов подряд.")
    {
        ProcessName = processName;
        ConsecutiveMisses = consecutiveMisses;
    }
}
