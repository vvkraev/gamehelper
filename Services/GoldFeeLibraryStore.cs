using System.Globalization;
using System.IO;
using System.Text;

namespace GameHelper.Services;

using GameHelper;

/// <summary>
/// Библиотека комиссии в золоте за единицу выбранной валюты I WANT (фиксирована патчем, не рынком).
/// Файл <c>Log/currency_iwant_gold_fee_library.csv</c>: валюта, стоимость, дата сканирования.
/// Новая строка добавляется только если пары (валюта, стоимость) ещё не было — чтобы видеть смену стоимости патчем.
/// </summary>
public static class GoldFeeLibraryStore
{
    private static readonly object Gate = new();

    public const string FileName = "currency_iwant_gold_fee_library.csv";

    private const string Header = "currency_label,gold_fee,scan_started_utc";

    public static string GetFilePath() => Path.Combine(ProjectPaths.GetLogDirectory(), FileName);

    /// <summary>Единица золота × количество I WANT (если оба заданы).</summary>
    public static int? TryComputeTotalGold(int? unitGoldFee, int quantityIWant)
    {
        if (unitGoldFee is not { } u || quantityIWant < 0)
            return null;
        if (quantityIWant == 0)
            return 0;
        try
        {
            checked
            {
                return u * quantityIWant;
            }
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Последняя по дате запись для валюты (сопоставление по <see cref="WindowsOcrTextLocator.NormalizeForMatch"/>).
    /// </summary>
    public static bool TryGetLatestGoldFeeForCurrency(string currencyLabel, out int goldFee, out DateTime scanUtc)
    {
        goldFee = 0;
        scanUtc = default;

        var wantNorm = WindowsOcrTextLocator.NormalizeForMatch(currencyLabel);
        if (string.IsNullOrEmpty(wantNorm))
            return false;

        GoldFeeRow? best = null;
        lock (Gate)
        {
            var path = GetFilePath();
            if (!File.Exists(path))
                return false;

            foreach (var row in ReadRowsUnlocked(path))
            {
                if (!string.Equals(WindowsOcrTextLocator.NormalizeForMatch(row.CurrencyLabel), wantNorm, StringComparison.Ordinal))
                    continue;
                if (best is null || row.ScanUtc > best.Value.ScanUtc)
                    best = row;
            }
        }

        if (best is not { } b)
            return false;
        goldFee = b.GoldFee;
        scanUtc = b.ScanUtc;
        return true;
    }

    /// <summary>
    /// Если в файле уже есть строка с той же валютой (нормализованно) и тем же <paramref name="goldFee"/> — не пишем.
    /// Иначе добавляем строку (новая пара или смена стоимости).
    /// </summary>
    public static bool TryAppendIfNewCurrencyFeePair(string currencyLabel, int goldFee, DateTime scanStartedUtc)
    {
        if (string.IsNullOrWhiteSpace(currencyLabel))
            return false;

        var labelNorm = WindowsOcrTextLocator.NormalizeForMatch(currencyLabel);
        if (string.IsNullOrEmpty(labelNorm))
            return false;

        lock (Gate)
        {
            var path = GetFilePath();
            if (File.Exists(path))
            {
                foreach (var row in ReadRowsUnlocked(path))
                {
                    if (row.GoldFee != goldFee)
                        continue;
                    if (string.Equals(WindowsOcrTextLocator.NormalizeForMatch(row.CurrencyLabel), labelNorm, StringComparison.Ordinal))
                        return false;
                }
            }

            var line = string.Join(
                ',',
                CsvEscape(currencyLabel.Trim()),
                CsvEscape(goldFee.ToString(CultureInfo.InvariantCulture)),
                CsvEscape(scanStartedUtc.ToString("o", CultureInfo.InvariantCulture)));

            var needHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            if (needHeader)
            {
                File.WriteAllText(
                    path,
                    Header + Environment.NewLine + line + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            else
            {
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }

            return true;
        }
    }

    private readonly struct GoldFeeRow
    {
        public string CurrencyLabel { get; init; }
        public int GoldFee { get; init; }
        public DateTime ScanUtc { get; init; }
    }

    private static IEnumerable<GoldFeeRow> ReadRowsUnlocked(string path)
    {
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("currency_label", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = SplitCsvLine(line);
            if (parts.Count < 3)
                continue;
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fee))
                continue;
            if (!DateTime.TryParse(parts[2].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var scan))
                continue;

            yield return new GoldFeeRow
            {
                CurrencyLabel = parts[0].Trim(),
                GoldFee = fee,
                ScanUtc = scan.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(scan, DateTimeKind.Utc) : scan.ToUniversalTime()
            };
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        result.Add(sb.ToString());
        return result;
    }

    private static string CsvEscape(string? value)
    {
        value ??= "";
        var mustQuote = value.IndexOfAny("\",\r\n".ToCharArray()) >= 0;
        if (!mustQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
