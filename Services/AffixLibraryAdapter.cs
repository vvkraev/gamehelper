namespace GameHelper.Services;

/// <summary>
/// DI-обёртка над статическим <see cref="AffixLibrary"/>.
/// Позволяет инъектировать <see cref="IAffixLibrary"/> в сервисы и подменять в тестах.
/// </summary>
public sealed class AffixLibraryAdapter : IAffixLibrary
{
    public string FilePath => AffixLibrary.FilePath;
    public IReadOnlyList<AffixLibraryEntry> GetEntries() => AffixLibrary.GetEntries();
    public int EntryCount => AffixLibrary.EntryCount;
    public int MergeFromParsedItem(ParsedItem? item) => AffixLibrary.MergeFromParsedItem(item);
    public void ReloadFromDisk() => AffixLibrary.ReloadFromDisk();
    public void ReloadFromDisk(string filePath) => AffixLibrary.ReloadFromDisk(filePath);
    public void SaveToDisk() => AffixLibrary.SaveToDisk();
}
