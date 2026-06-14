using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GameHelper.Services;

/// <summary>Распознавание текста в области экрана (Windows OCR) и поиск подстроки с координатами.</summary>
public static class WindowsOcrTextLocator
{
    public readonly record struct OcrMatch(string MatchedLineText, ScreenRect BoundsOnScreen);

    /// <summary>Слово OCR в координатах исходного изображения.</summary>
    public readonly record struct OcrWordGeom(string Text, Windows.Foundation.Rect Bounds);

    /// <summary>Строка OCR с прямоугольником в координатах изображения (после обратного масштаба ×2, если использовался).</summary>
    public readonly record struct OcrImageLine(string Text, string NormText, Windows.Foundation.Rect Bounds, IReadOnlyList<OcrWordGeom>? Words);

    /// <summary>
    /// Нормализация для сравнения: без пробелов, без различия регистра (инвариант культуры не используем — латиница в именах NPC).
    /// </summary>
    public static string NormalizeForMatch(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            // OCR часто путает строчную 'l' (эль) с прописной 'I' в шрифтах PoE2 UI
            if (c == 'l') { sb.Append('I'); continue; }
            sb.Append(char.ToUpperInvariant(c));
        }

        return ApplyCommonWindowsOcrLetterFixes(sb.ToString());
    }

    /// <summary>
    /// Windows OCR часто путает латинские и кириллические символы, а также «O» и «0».
    /// Применяем замены после нормализации (без пробелов, в верхнем регистре).
    /// </summary>
    private static string ApplyCommonWindowsOcrLetterFixes(string normalizedUpperNoSpaces)
    {
        if (string.IsNullOrEmpty(normalizedUpperNoSpaces))
            return normalizedUpperNoSpaces;

        // Латинская O vs цифра 0: «Orb» → «0rb» → после нормализации «0RB»
        var s = normalizedUpperNoSpaces.Replace("0RB", "ORB", StringComparison.Ordinal);

        // Кириллические lookalike → латинские (OCR путает при смешанном шрифте PoE2 UI):
        // В→B, Е→E, С→C, А→A, О→O, Р→P, Н→H, Х→X, К→K, М→M, Т→T
        s = s
            .Replace('В', 'B')  // Cyrillic В → Latin B
            .Replace('Е', 'E')  // Cyrillic Е → Latin E
            .Replace('С', 'C')  // Cyrillic С → Latin C
            .Replace('А', 'A')  // Cyrillic А → Latin A
            .Replace('О', 'O')  // Cyrillic О → Latin O
            .Replace('Р', 'P')  // Cyrillic Р → Latin P
            .Replace('Н', 'H')  // Cyrillic Н → Latin H
            .Replace('Х', 'X')  // Cyrillic Х → Latin X
            .Replace('К', 'K')  // Cyrillic К → Latin K
            .Replace('М', 'M')  // Cyrillic М → Latin M
            .Replace('Т', 'T')  // Cyrillic Т → Latin T
            .Replace('Г', 'N')  // Cyrillic Г → Latin N (OCR-замена в PoE2 UI)
            .Replace('Ч', 'C'); // Cyrillic Ч → Latin C (OCR-замена в PoE2 UI)

        return s;
    }

    private static OcrEngine? TryCreateOcrEngine(IProgress<string>? log)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine == null)
            log?.Report("OCR: не удалось создать OcrEngine (языковые пакеты Windows?).");
        return engine;
    }

    /// <summary>
    /// Весь распознанный текст в области (без поиска подстроки). При пустом ответе — повтор снимка ×2.
    /// </summary>
    /// <param name="logFullCollapsedReadout">
    /// Если false — в лог не пишется длинная строка «…», только размер (для стакана см. <see cref="OrderBookOcrLogFormatter"/>).
    /// </param>
    public static async Task<string> RecognizeRegionRawTextAsync(
        ScreenRect searchArea,
        IProgress<string>? log,
        CancellationToken cancellationToken,
        bool logFullCollapsedReadout = true)
    {
        var engine = TryCreateOcrEngine(log);
        if (engine == null)
            return "";

        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = ScreenCaptureHelper.CaptureRegion(searchArea);
        var text = CollapseInternalWhitespace(await RecognizeBitmapPlainTextAsync(engine, bitmap, cancellationToken).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (logFullCollapsedReadout)
                log?.Report($"OCR [readout] 1× ({searchArea.Width}×{searchArea.Height}): «{text}»");
            else
                log?.Report($"OCR [readout] 1× ({searchArea.Width}×{searchArea.Height}): получено {text.Length} симв. (разбор стакана — отдельным блоком [Стакан]).");
            return text;
        }

        log?.Report("OCR [readout]: пусто в 1×, повтор ×2.");
        cancellationToken.ThrowIfCancellationRequested();
        using var scaled = ScreenCaptureHelper.ScaleByIntegerFactor(bitmap, 2);
        text = CollapseInternalWhitespace(await RecognizeBitmapPlainTextAsync(engine, scaled, cancellationToken).ConfigureAwait(false));
        if (logFullCollapsedReadout)
        {
            log?.Report(string.IsNullOrWhiteSpace(text)
                ? "OCR [readout] ×2: (пусто)"
                : $"OCR [readout] ×2: «{text}»");
        }
        else
        {
            log?.Report(string.IsNullOrWhiteSpace(text)
                ? "OCR [readout] ×2: (пусто)"
                : $"OCR [readout] ×2: получено {text.Length} симв. (разбор стакана — блок [Стакан]).");
        }

        return text;
    }

    /// <summary>
    /// Весь текст из файла изображения (тесты/отладка): одна строка, внутренние пробелы схлопнуты; при пустом ответе — ×2.
    /// </summary>
    public static async Task<string> RecognizeImageFileCollapsedAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return "";

        var engine = TryCreateOcrEngine(null);
        if (engine == null)
            return "";

        using var bitmap = new Bitmap(absolutePath);
        var text = CollapseInternalWhitespace(await RecognizeBitmapPlainTextAsync(engine, bitmap, cancellationToken).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        cancellationToken.ThrowIfCancellationRequested();
        using var scaled = ScreenCaptureHelper.ScaleByIntegerFactor(bitmap, 2);
        return CollapseInternalWhitespace(await RecognizeBitmapPlainTextAsync(engine, scaled, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Схлопнутый текст с кропа (1×, при пустом — 2×).</summary>
    public static async Task<string> RecognizeBitmapCollapsedAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var engine = TryCreateOcrEngine(null);
        if (engine == null)
            return "";

        var text = CollapseInternalWhitespace(await RecognizeBitmapPlainTextAsync(engine, bitmap, cancellationToken).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        cancellationToken.ThrowIfCancellationRequested();
        using var scaled = ScreenCaptureHelper.ScaleByIntegerFactor(bitmap, 2);
        return CollapseInternalWhitespace(await RecognizeBitmapPlainTextAsync(engine, scaled, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Строки OCR с геометрией для разметки таблицы (стакан).</summary>
    public static async Task<(IReadOnlyList<OcrImageLine> Lines, int CoordinateScale)> RecognizeBitmapGeometryAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var engine = TryCreateOcrEngine(null);
        if (engine == null)
            return (Array.Empty<OcrImageLine>(), 1);

        async Task<(OcrResult? Result, int Scale)> RunAsync(Bitmap src, int scale)
        {
            using var softwareBitmap = await BitmapToSoftwareBitmapAsync(src).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);
            return (result, scale);
        }

        var (ocr1, sc1) = await RunAsync(bitmap, 1).ConfigureAwait(false);
        var lines1 = BuildOcrImageLines(ocr1, sc1);
        if (lines1.Count >= 3)
            return (lines1, 1);

        cancellationToken.ThrowIfCancellationRequested();
        using var scaled = ScreenCaptureHelper.ScaleByIntegerFactor(bitmap, 2);
        var (ocr2, sc2) = await RunAsync(scaled, 2).ConfigureAwait(false);
        var lines2 = BuildOcrImageLines(ocr2, sc2);
        return (lines2.Count > lines1.Count ? lines2 : lines1, lines2.Count > lines1.Count ? 2 : 1);
    }

    private static List<OcrImageLine> BuildOcrImageLines(OcrResult? ocrResult, int coordinateScale)
    {
        var list = new List<OcrImageLine>();
        if (ocrResult?.Lines == null || ocrResult.Lines.Count == 0)
            return list;

        foreach (var line in ocrResult.Lines)
        {
            if (line.Words == null || line.Words.Count == 0)
                continue;
            var u = UnionWordRects(line.Words);
            var inOrig = ScaleOcrRectToCaptureCoords(u, coordinateScale);
            var wordGeoms = new List<OcrWordGeom>(line.Words.Count);
            foreach (var w in line.Words)
            {
                var wr = ScaleOcrRectToCaptureCoords(w.BoundingRect, coordinateScale);
                wordGeoms.Add(new OcrWordGeom(w.Text ?? "", wr));
            }

            var raw = line.Text ?? "";
            list.Add(new OcrImageLine(raw, NormalizeForMatch(raw), inOrig, wordGeoms));
        }

        list.Sort((a, b) =>
        {
            var dy = a.Bounds.Y.CompareTo(b.Bounds.Y);
            return dy != 0 ? dy : a.Bounds.X.CompareTo(b.Bounds.X);
        });
        return list;
    }

    private static string CollapseInternalWhitespace(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" : Regex.Replace(s.Trim(), @"\s+", " ");

    private static async Task<string> RecognizeBitmapPlainTextAsync(OcrEngine engine, Bitmap bitmap, CancellationToken cancellationToken)
    {
        using var softwareBitmap = await BitmapToSoftwareBitmapAsync(bitmap).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);
        return result?.Text ?? "";
    }

    /// <summary>
    /// Ищет строку OCR, в которой после нормализации встречается <paramref name="targetNormalized"/>.
    /// Координаты — экранные (смещение области поиска учтено).
    /// </summary>
    public static async Task<OcrMatch?> TryFindNormalizedSubstringAsync(
        ScreenRect searchArea,
        string targetNormalized,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetNormalized))
            throw new ArgumentException("Целевая подстрока не может быть пустой.", nameof(targetNormalized));

        var engine = TryCreateOcrEngine(log);
        if (engine == null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = ScreenCaptureHelper.CaptureRegion(searchArea);

        var match = await TryRecognizeAndMatchAsync(engine, bitmap, searchArea, targetNormalized, coordinateScale: 1, log, cancellationToken)
            .ConfigureAwait(false);
        if (match != null)
            return match;

        log?.Report("OCR: одна строка и склейка соседних не дали совпадения; повтор распознавания с масштабом ×2 (мелкий шрифт UI).");
        cancellationToken.ThrowIfCancellationRequested();
        using var scaled = ScreenCaptureHelper.ScaleByIntegerFactor(bitmap, 2);
        match = await TryRecognizeAndMatchAsync(engine, scaled, searchArea, targetNormalized, coordinateScale: 2, log, cancellationToken)
            .ConfigureAwait(false);
        if (match != null)
            return match;

        log?.Report($"OCR: подстрока «{targetNormalized}» не найдена (в т.ч. многострочно и в ×2).");
        return null;
    }

    private static Windows.Foundation.Rect ScaleOcrRectToCaptureCoords(Windows.Foundation.Rect r, int coordinateScale)
    {
        if (coordinateScale <= 1)
            return r;
        var inv = 1.0 / coordinateScale;
        return new Windows.Foundation.Rect(r.X * inv, r.Y * inv, r.Width * inv, r.Height * inv);
    }

    private static Windows.Foundation.Rect UnionTwoRects(Windows.Foundation.Rect a, Windows.Foundation.Rect b)
    {
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
        var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Windows.Foundation.Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool HorizontalOverlapFraction(Windows.Foundation.Rect a, Windows.Foundation.Rect b, double minFraction)
    {
        var aR = a.X + a.Width;
        var bR = b.X + b.Width;
        var overlap = Math.Max(0.0, Math.Min(aR, bR) - Math.Max(a.X, b.X));
        var minW = Math.Min(a.Width, b.Width);
        if (minW <= 1)
            return true;
        return overlap >= minW * minFraction;
    }

    private sealed record LineInfo(string RawText, string NormText, Windows.Foundation.Rect Bounds);

    private static List<LineInfo> BuildSortedLineInfos(IReadOnlyList<OcrLine> lines)
    {
        var list = new List<LineInfo>();
        foreach (var line in lines)
        {
            if (line.Words == null || line.Words.Count == 0)
                continue;
            var u = UnionWordRects(line.Words);
            list.Add(new LineInfo(line.Text ?? "", NormalizeForMatch(line.Text ?? ""), u));
        }

        list.Sort((a, b) =>
        {
            var dy = a.Bounds.Y.CompareTo(b.Bounds.Y);
            return dy != 0 ? dy : a.Bounds.X.CompareTo(b.Bounds.X);
        });
        return list;
    }

    private static OcrMatch? TryMultilineAdjacentMatch(
        IReadOnlyList<OcrLine> lines,
        ScreenRect searchArea,
        string targetNormalized,
        int coordinateScale,
        IProgress<string>? log)
    {
        var infos = BuildSortedLineInfos(lines);
        if (infos.Count == 0)
            return null;

        var heights = infos.Select(i => i.Bounds.Height).OrderBy(h => h).ToList();
        var medianH = heights[heights.Count / 2];
        var mergeGap = Math.Max(10.0, medianH * 1.55);

        for (var i = 0; i < infos.Count; i++)
        {
            var union = infos[i].Bounds;
            var mergedNorm = infos[i].NormText;
            var mergedDisplay = infos[i].RawText.Trim();

            if (mergedNorm.Contains(targetNormalized, StringComparison.Ordinal))
            {
                var inCapture = ScaleOcrRectToCaptureCoords(union, coordinateScale);
                var onScreen = RectToScreenRect(searchArea, inCapture);
                log?.Report($"OCR: найдено (одна линия OCR) «{mergedDisplay}» → экран {onScreen.X},{onScreen.Y} {onScreen.Width}×{onScreen.Height}");
                return new OcrMatch(mergedDisplay, onScreen);
            }

            for (var j = i + 1; j < Math.Min(i + 6, infos.Count); j++)
            {
                var prevBottom = union.Y + union.Height;
                var gap = infos[j].Bounds.Y - prevBottom;
                if (gap > mergeGap)
                    break;
                if (!HorizontalOverlapFraction(union, infos[j].Bounds, 0.12))
                    break;

                union = UnionTwoRects(union, infos[j].Bounds);
                mergedNorm += infos[j].NormText;
                mergedDisplay = $"{mergedDisplay} {infos[j].RawText.Trim()}".Trim();

                if (!mergedNorm.Contains(targetNormalized, StringComparison.Ordinal))
                    continue;

                var inCapture = ScaleOcrRectToCaptureCoords(union, coordinateScale);
                var onScreen = RectToScreenRect(searchArea, inCapture);
                log?.Report($"OCR: найдено (склейка {j - i + 1} соседних строк) «{mergedDisplay}» → экран {onScreen.X},{onScreen.Y} {onScreen.Width}×{onScreen.Height}");
                return new OcrMatch(mergedDisplay, onScreen);
            }
        }

        // Word-level fallback: проверяем отдельные слова OCR.
        // Нужен когда таргет длиннее нормализованного line.Text (например, «REFORGINGBENCH»
        // не содержится в «REFORGINGBEC», но слово «Reforging» → «REFORGING» содержит в себе
        // любой короткий таргет типа «REFORGING»).
        foreach (var line in lines)
        {
            if (line.Words == null || line.Words.Count == 0)
                continue;
            foreach (var word in line.Words)
            {
                var wNorm = NormalizeForMatch(word.Text);
                if (string.IsNullOrEmpty(wNorm))
                    continue;
                // Слово содержит таргет ИЛИ таргет содержит слово (≥5 симв. — защита от коротких мусорных слов)
                if (!wNorm.Contains(targetNormalized, StringComparison.Ordinal) &&
                    !(targetNormalized.Contains(wNorm, StringComparison.Ordinal) && wNorm.Length >= 5))
                    continue;

                var wordRect = word.BoundingRect;
                var inCapture = ScaleOcrRectToCaptureCoords(wordRect, coordinateScale);
                var onScreen = RectToScreenRect(searchArea, inCapture);
                log?.Report($"OCR: найдено (word-fallback) «{word.Text}» → экран {onScreen.X},{onScreen.Y} {onScreen.Width}×{onScreen.Height}");
                return new OcrMatch(word.Text ?? "", onScreen);
            }
        }

        return null;
    }

    private static async Task<OcrMatch?> TryRecognizeAndMatchAsync(
        OcrEngine engine,
        Bitmap bitmap,
        ScreenRect searchArea,
        string targetNormalized,
        int coordinateScale,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        using var softwareBitmap = await BitmapToSoftwareBitmapAsync(bitmap).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var ocrResult = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken).ConfigureAwait(false);
        var passTag = coordinateScale <= 1 ? "оригинал" : "×2";

        if (ocrResult?.Lines == null || ocrResult.Lines.Count == 0)
        {
            log?.Report($"OCR [{passTag}]: нет строк после распознавания.");
            LogOcrRecognitionDump(ocrResult, passTag, log);
            return null;
        }

        var match = TryMultilineAdjacentMatch(ocrResult.Lines, searchArea, targetNormalized, coordinateScale, log);
        if (match != null)
            return match;

        log?.Report($"OCR [{passTag}]: совпадения нет — дамп того, что вернул OCR:");
        LogOcrRecognitionDump(ocrResult, passTag, log);
        return null;
    }

    /// <summary>Пишет в лог полный текст и построчно (и слова, если их немного) — для отладки промахов.</summary>
    private static void LogOcrRecognitionDump(OcrResult? ocrResult, string passTag, IProgress<string>? log)
    {
        if (log == null)
            return;

        if (ocrResult == null)
        {
            log.Report($"OCR [{passTag}] дамп: результат null.");
            return;
        }

        var full = ocrResult.Text ?? "";
        if (full.Length == 0)
            log.Report($"OCR [{passTag}] OcrResult.Text: (пусто)");
        else
        {
            const int maxChars = 8000;
            if (full.Length <= maxChars)
                log.Report($"OCR [{passTag}] OcrResult.Text ({full.Length} симв.):\n{full}");
            else
                log.Report($"OCR [{passTag}] OcrResult.Text (первые {maxChars} из {full.Length}):\n{full[..maxChars]}…");
        }

        var lines = ocrResult.Lines;
        if (lines == null || lines.Count == 0)
        {
            log.Report($"OCR [{passTag}] Lines: нет или Count=0.");
            return;
        }

        log.Report($"OCR [{passTag}] Lines.Count={lines.Count}");
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var raw = line.Text ?? "";
            var oneLine = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
            var norm = NormalizeForMatch(raw);
            var wc = line.Words?.Count ?? 0;
            log.Report($"OCR [{passTag}]  #{i + 1} ({wc} слов): «{oneLine}» → «{norm}»");

            if (line.Words is { Count: > 0 and <= 40 })
            {
                for (var w = 0; w < line.Words.Count; w++)
                {
                    var word = line.Words[w];
                    var wt = word.Text ?? "";
                    var wr = word.BoundingRect;
                    log.Report($"OCR [{passTag}]     слово {w + 1}: «{wt}» @ {wr.X:F0},{wr.Y:F0} {wr.Width:F0}×{wr.Height:F0}");
                }
            }
        }
    }

    private static Windows.Foundation.Rect UnionWordRects(IReadOnlyList<OcrWord> words)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var w in words)
        {
            var r = w.BoundingRect;
            minX = Math.Min(minX, r.X);
            minY = Math.Min(minY, r.Y);
            maxX = Math.Max(maxX, r.X + r.Width);
            maxY = Math.Max(maxY, r.Y + r.Height);
        }

        if (minX > maxX)
            return new Windows.Foundation.Rect(0, 0, 1, 1);

        return new Windows.Foundation.Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static ScreenRect RectToScreenRect(ScreenRect searchArea, Windows.Foundation.Rect r)
    {
        var x = searchArea.X + (int)Math.Floor(r.X);
        var y = searchArea.Y + (int)Math.Floor(r.Y);
        var w = Math.Max(1, (int)Math.Ceiling(r.Width));
        var h = Math.Max(1, (int)Math.Ceiling(r.Height));
        return new ScreenRect(x, y, w, h);
    }

    private static async Task<SoftwareBitmap> BitmapToSoftwareBitmapAsync(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var bytes = ms.ToArray();

        using var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            await writer.FlushAsync().AsTask().ConfigureAwait(false);
        }

        ras.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask().ConfigureAwait(false);
        return await decoder.GetSoftwareBitmapAsync().AsTask().ConfigureAwait(false);
    }
}
