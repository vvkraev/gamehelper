namespace GameHelper.Services;

/// <summary>
/// DI-обёртка над статическим <see cref="SessionLogger"/>.
/// Позволяет инъектировать <see cref="ISessionLogger"/> в сервисы и подменять в тестах.
/// </summary>
public sealed class SessionLoggerAdapter : ISessionLogger
{
    public event Action<string>? NewLine
    {
        add    => SessionLogger.NewLine += value;
        remove => SessionLogger.NewLine -= value;
    }

    public void Info(string message) => SessionLogger.Info(message);
    public void WriteFileOnly(string message) => SessionLogger.WriteFileOnly(message);
    public void InfoClipboard(string tag, string? clipboardText, int maxChars = 12000) =>
        SessionLogger.InfoClipboard(tag, clipboardText, maxChars);
    public IReadOnlyList<string> GetSnapshot() => SessionLogger.GetSnapshot();
}
