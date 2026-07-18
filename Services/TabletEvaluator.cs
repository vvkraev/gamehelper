namespace GameHelper.Services;

/// <summary>
/// Оценивает таблички через Python-скрипт evaluate_clipboard.py (WSL).
/// Статический хелпер: не зависит ни от UI, ни от MainWindow.
/// </summary>
public static class TabletEvaluator
{
    /// <summary>Regex для извлечения цены из вывода скрипта: «~3.5d».</summary>
    public static readonly System.Text.RegularExpressions.Regex PriceRegex =
        new(@"~(\d+\.?\d*)d", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Оценивает несколько предметов одним вызовом evaluate_clipboard.py --batch.
    /// Модель загружается один раз на весь батч → ×N быстрее одиночных вызовов.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EvaluateBatchAsync(IReadOnlyList<string> itemTexts)
    {
        if (itemTexts.Count == 0) return [];

        var tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(itemTexts);
            await System.IO.File.WriteAllTextAsync(tmpFile, json, System.Text.Encoding.UTF8);

            var normalized  = NormalizePathForWsl(tmpFile);
            var projectRoot = NormalizePathForWsl(ProjectPaths.GetProjectRoot());
            var scriptDir   = $"{projectRoot}/scripts/tabflow";
            var python      = $"{scriptDir}/.venv/bin/python3";
            var script      = $"{scriptDir}/evaluate_clipboard.py";

            var psi = new System.Diagnostics.ProcessStartInfo(
                "wsl.exe", $"-e {python} {script} --batch {normalized}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8,
            };

            using var proc = System.Diagnostics.Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var output = stdout.Trim();
            if (string.IsNullOrEmpty(output))
            {
                var err = stderr.Trim();
                var msg = string.IsNullOrEmpty(err) ? "Нет ответа от скрипта" : $"Ошибка скрипта: {err}";
                return itemTexts.Select(_ => msg).ToArray();
            }

            var parsed = System.Text.Json.JsonSerializer.Deserialize<string[]>(output);
            return parsed ?? itemTexts.Select(_ => "Нет ответа от скрипта").ToArray();
        }
        catch (System.Text.Json.JsonException jex)
        {
            return itemTexts.Select(_ => $"Ошибка разбора ответа скрипта: {jex.Message}").ToArray();
        }
        catch (Exception ex)
        {
            return itemTexts.Select(_ => $"Ошибка: {ex.Message}").ToArray();
        }
        finally
        {
            try { System.IO.File.Delete(tmpFile); } catch { }
        }
    }

    /// <summary>Оценивает один предмет; удобная обёртка над батч-методом.</summary>
    public static async Task<string> EvaluateAsync(string itemText)
    {
        var results = await EvaluateBatchAsync([itemText]).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : "Нет ответа от скрипта";
    }

    /// <summary>Извлекает цену в divine из вывода скрипта. Возвращает null если не распознано.</summary>
    public static double? ParsePrice(string evalOutput)
    {
        var m = PriceRegex.Match(evalOutput);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string NormalizePathForWsl(string winPath)
    {
        var p = winPath.Replace('\\', '/');
        if (p.Length >= 2 && p[1] == ':')
            p = "/mnt/" + char.ToLower(p[0]) + p[2..];
        return p;
    }
}
