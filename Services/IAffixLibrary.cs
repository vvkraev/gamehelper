namespace GameHelper.Services;

public interface IAffixLibrary
{
    string FilePath { get; }
    IReadOnlyList<AffixLibraryEntry> GetEntries();
    int EntryCount { get; }
    int MergeFromParsedItem(ParsedItem? item);
    void ReloadFromDisk();
    void ReloadFromDisk(string filePath);
    void SaveToDisk();
}
