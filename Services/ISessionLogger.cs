namespace GameHelper.Services;

public interface ISessionLogger
{
    event Action<string>? NewLine;
    void Info(string message);
    void WriteFileOnly(string message);
    void InfoClipboard(string tag, string? clipboardText, int maxChars = 12000);
    IReadOnlyList<string> GetSnapshot();
}
