using System.IO;

namespace GameHelper.Services;

using GameHelper;

/// <summary>Список подписей валют для сканирования золота I WANT: по одной строке в файле в корне проекта.</summary>
public static class CurrencyIWantGoldScanList
{
    public const string DefaultFileName = "currency_iwant_gold_scan_list.txt";

    public static string GetDefaultPath() => Path.Combine(ProjectPaths.GetProjectRoot(), DefaultFileName);

    /// <summary>Непустые строки; пустые и строки с # в начале пропускаются.</summary>
    public static IReadOnlyList<string> ReadLabels(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var raw in File.ReadLines(path))
        {
            var t = raw.Trim();
            if (t.Length == 0 || t[0] == '#')
                continue;
            list.Add(t);
        }

        return list;
    }
}
