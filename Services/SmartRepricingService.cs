using GameHelper.Native;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>
/// Одна позиция из reprice_plan.json, сгенерированного reprice.py.
/// </summary>
public sealed class RepricePlanItem
{
    public string       ListingId       { get; set; } = "";
    public int          Col             { get; set; }
    public int          Row             { get; set; }
    public string       BaseType        { get; set; } = "";
    public List<string> Mods            { get; set; } = new();
    public double       CurrentPrice    { get; set; }
    public string       CurrentCurrency { get; set; } = "divine";
    public double       NewPrice        { get; set; }
    public string       NewCurrency     { get; set; } = "divine";
    public string       Reason          { get; set; } = "";
    /// <summary>"reprice" (по умолчанию) или "delist_for_reforge" (цена ≤ флор).</summary>
    public string       Action          { get; set; } = "reprice";
}

/// <summary>
/// Выполняет умную переоценку по плану из reprice_plan.json.
///
/// Два режима:
///   SamePrice  — та же валюта, только цена меняется:
///                ПКМ на ячейке → Ctrl+A → новая цена → Enter
///   CurrencySwitch — смена divine→chaos:
///                Ctrl+ЛКМ на ячейке → диалог → цена → выбор chaos → подтверждение
/// </summary>
public sealed class SmartRepricingService
{
    private const double DelayJitterFraction = 0.25;

    public int ActionDelayMs    { get; set; } = 300;
    public int ClipboardDelayMs { get; set; } = 220;
    public int DialogSettleMs   { get; set; } = 500;
    public int DropdownSettleMs { get; set; } = 700;
    public int HoverSettleMs    { get; set; } = 120;

    private int WithJitter(int ms)
    {
        if (ms <= 0) return 0;
        var d = (int)Math.Round(ms * DelayJitterFraction);
        return d <= 0 ? ms : Math.Max(0, ms + Random.Shared.Next(-d, d + 1));
    }

    private Task DelayAsync(int ms, CancellationToken ct) =>
        Task.Delay(WithJitter(ms), ct);

    private static async Task ClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });

    private static async Task<string> ReadClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { return System.Windows.Clipboard.ContainsText()
                      ? System.Windows.Clipboard.GetText() : string.Empty; }
            catch { return string.Empty; }
        });

    /// <summary>
    /// Сканирует вкладки магазина Ange через Ctrl+Alt+C, для каждого найденного предмета
    /// ищет совпадение в плане переоценки по base_type + моды, и выполняет переоценку.
    /// Для каждой вкладки: кликает по TabRect (если Width > 0), затем обходит ячейки.
    /// </summary>
    /// <param name="plan">Позиции из reprice_plan.json (с полем mods).</param>
    /// <param name="shopTabs">Вкладки магазина Ange: (TabRect, Cells). TabRect.Width==0 → клик не нужен.</param>
    /// <param name="chaosOrbOcrRect">Область OCR для поиска "Chaos Orb" в дропдауне.</param>
    /// <param name="priceInputRect">Поле ввода цены в диалоге листинга.</param>
    /// <param name="currencyDropdownRect">Дропдаун валюты в диалоге листинга.</param>
    /// <param name="listItemBtnRect">Кнопка LIST ITEM / UPDATE PRICE.</param>
    /// <param name="log">Прогресс-репортер.</param>
    public async Task<(int done, int skipped, int delisted)> ExecutePlanAsync(
        IReadOnlyList<RepricePlanItem> plan,
        IReadOnlyList<(ScreenRect TabRect, IReadOnlyList<ScreenRect> Cells)> shopTabs,
        ScreenRect chaosOrbOcrRect,
        ScreenRect priceInputRect,
        ScreenRect currencyDropdownRect,
        ScreenRect listItemBtnRect,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        var remaining = plan.ToList();
        int done = 0, skipped = 0, delisted = 0;

        try
        {
            foreach (var (tabRect, cells) in shopTabs)
            {
                ct.ThrowIfCancellationRequested();
                if (remaining.Count == 0) break;

                // ── Навигация к вкладке ───────────────────────────────────
                if (tabRect.Width > 0 && tabRect.Height > 0)
                {
                    var (tx, ty) = tabRect.GetInteriorPoint(1);
                    Win32Input.MoveTo(tx, ty);
                    await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
                    Win32Input.ClickLeft();
                    await DelayAsync(DialogSettleMs, ct).ConfigureAwait(false);
                }

                // ── Сканирование ячеек вкладки ────────────────────────────
                for (int i = 0; i < cells.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (remaining.Count == 0) break;

                    var cell = cells[i];
                    var (hx, hy) = cell.GetInteriorPoint(1);

                    // Читаем предмет через Ctrl+Alt+C
                    Win32Input.MoveTo(hx, hy);
                    await DelayAsync(HoverSettleMs, ct).ConfigureAwait(false);
                    await ClearClipboardAsync().ConfigureAwait(false);
                    Win32Input.SendCtrlAltC();
                    await DelayAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
                    Win32Input.ReleaseCtrlAlt(); // обязательно до любых кликов

                    var itemText = await ReadClipboardAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(itemText)) continue;

                    var parsed = ItemParser.Parse(itemText);
                    if (parsed is null || string.IsNullOrWhiteSpace(parsed.Base)) continue;

                    var cellMods = TabletListingsIndex.ExtractMods(parsed);
                    var cellBase = parsed.Base.Trim().ToLowerInvariant();

                    var match = FindPlanItem(remaining, cellBase, cellMods);
                    if (match is null)
                    {
                        log?.Report($"[Переоценка] [{i+1}] {parsed.Base} — не в плане, пропуск");
                        continue;
                    }

                    if (match.Action == "delist_for_reforge")
                    {
                        log?.Report($"[Переоценка] [{i+1}] {match.BaseType} @ {match.CurrentPrice}{match.CurrentCurrency[0]} → снятие на рефордж");
                        // Ctrl+ЛКМ перемещает предмет из витрины Ange в инвентарь
                        Win32Input.SendCtrlLeftClick();
                        Win32Input.CtrlUp();
                        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
                        TabletReforgeQueue.Add(match.ListingId, match.BaseType, match.Mods);
                        remaining.Remove(match);
                        delisted++;
                        continue;
                    }

                    log?.Report($"[Переоценка] [{i+1}] {match.BaseType} {match.CurrentPrice}{match.CurrentCurrency[0]} → {match.NewPrice}{match.NewCurrency[0]}  ({match.Reason})");

                    bool switchCurrency = match.CurrentCurrency != match.NewCurrency;
                    bool ok = switchCurrency
                        ? await RepriceCurrencySwitchAsync(match, cell, chaosOrbOcrRect,
                              priceInputRect, currencyDropdownRect, listItemBtnRect, log, ct)
                        : await RepriceSameCurrencyAsync(match, cell, priceInputRect, log, ct);

                    if (ok)
                    {
                        TabletListingsIndex.LogRepriceById(match.ListingId, (int)match.NewPrice, match.NewCurrency);
                        remaining.Remove(match);
                        done++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }
        }
        finally
        {
            Win32Input.ReleaseCtrlAlt();
        }

        log?.Report($"[Переоценка] Готово: переоценено {done}, снято на рефордж {delisted}, пропущено {skipped}.");
        return (done, skipped, delisted);
    }

    /// <summary>
    /// Ищет в оставшемся плане предмет по base_type и набору модов.
    /// Сравниваем нормализованные моды; достаточно точного совпадения множеств.
    /// </summary>
    private static RepricePlanItem? FindPlanItem(
        List<RepricePlanItem> remaining, string cellBase, List<string> cellMods)
    {
        var cellSet = new HashSet<string>(cellMods.Select(TabletListingsIndex.NormalizeMod));

        foreach (var item in remaining)
        {
            if (!string.Equals(item.BaseType, cellBase, StringComparison.OrdinalIgnoreCase))
                continue;
            var planSet = new HashSet<string>(item.Mods.Select(TabletListingsIndex.NormalizeMod));
            if (cellSet.SetEquals(planSet))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Та же валюта — ПКМ → Ctrl+C (детекция, без клика) → поле цены → Ctrl+A → ввод → Enter.
    /// Детекция: если Ctrl+C вернул нечисловой текст → PoE2 скопировал текст предмета →
    /// диалог не открылся (предмет заблокирован) → ESC, пропуск.
    /// Та же схема, что в RepricingService (проверенная).
    /// </summary>
    private async Task<bool> RepriceSameCurrencyAsync(
        RepricePlanItem item, ScreenRect cell, ScreenRect priceInputRect,
        IProgress<string>? log, CancellationToken ct)
    {
        var (cx, cy) = cell.GetInteriorPoint(1);
        var priceText = FormatPrice(item.NewPrice);

        // 1. ПКМ — открываем диалог изменения цены
        Win32Input.MoveTo(cx, cy);
        await DelayAsync(HoverSettleMs, ct).ConfigureAwait(false);
        Win32Input.ClickRight();
        await DelayAsync(DialogSettleMs, ct).ConfigureAwait(false);

        // 2. Детекция без кликов: Ctrl+C сразу после ПКМ.
        //    Если диалог НЕ открылся — PoE2 скопирует текст предмета (нечисловой).
        //    Если открылся — буфер пустой или число (цена из поля).
        //    Ctrl+A НЕ посылаем — он взаимодействует с shop UI и даёт ложный positive.
        await ClearClipboardAsync().ConfigureAwait(false);
        Win32Input.SendCtrlC();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        var check = await ReadClipboardAsync().ConfigureAwait(false);

        var isItemText = !string.IsNullOrWhiteSpace(check)
            && !decimal.TryParse(check.Trim(),
                   System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out _);
        if (isItemText)
        {
            log?.Report($"[Переоценка] {item.BaseType} — заблокирован (диалог не открылся), пропуск");
            return false;
        }

        // 3. Диалог открыт (буфер пустой или содержит цену).
        //    Кликаем на поле цены, выделяем всё, вводим новую цену.
        log?.Report($"[Переоценка] {item.BaseType} {item.CurrentPrice}{item.CurrentCurrency[0]} → {item.NewPrice}{item.NewCurrency[0]}  ({item.Reason})");
        var (px, py) = priceInputRect.GetInteriorPoint(1);
        Win32Input.MoveTo(px, py);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.SendCtrlA();
        await DelayAsync(80, ct).ConfigureAwait(false);
        Win32Input.TypeText(priceText);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.PressEnter();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Смена валюты divine→chaos: ПКМ → детекция диалога → поле цены → chaos → подтверждение.
    /// Ctrl+ЛКМ в магазине Ange перемещает предмет в инвентарь — использовать только ПКМ.
    /// </summary>
    private async Task<bool> RepriceCurrencySwitchAsync(
        RepricePlanItem item, ScreenRect cell,
        ScreenRect chaosOrbOcrRect,
        ScreenRect priceInputRect,
        ScreenRect currencyDropdownRect,
        ScreenRect listItemBtnRect,
        IProgress<string>? log, CancellationToken ct)
    {
        var (cx, cy) = cell.GetInteriorPoint(1);
        var priceText = FormatPrice(item.NewPrice);

        // 1. ПКМ — открываем диалог "Set Item Price" (Ctrl+ЛКМ в магазине Ange перемещает предмет!)
        Win32Input.MoveTo(cx, cy);
        await DelayAsync(HoverSettleMs, ct).ConfigureAwait(false);
        Win32Input.ClickRight();
        await DelayAsync(DialogSettleMs, ct).ConfigureAwait(false);

        // 2. Детекция без кликов (та же схема что в RepriceSameCurrencyAsync)
        await ClearClipboardAsync().ConfigureAwait(false);
        Win32Input.SendCtrlC();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        var check = await ReadClipboardAsync().ConfigureAwait(false);

        var isItemText = !string.IsNullOrWhiteSpace(check)
            && !decimal.TryParse(check.Trim(),
                   System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out _);
        if (isItemText)
        {
            log?.Report($"[Переоценка] {item.BaseType} — заблокирован (диалог не открылся), пропуск");
            return false;
        }

        log?.Report($"[Переоценка] {item.BaseType} {item.CurrentPrice}{item.CurrentCurrency[0]} → {item.NewPrice}c (chaos)  ({item.Reason})");

        // 3. Поле цены
        var (px, py) = priceInputRect.GetInteriorPoint(1);
        Win32Input.MoveTo(px, py);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.SendCtrlA();
        await DelayAsync(80, ct).ConfigureAwait(false);
        Win32Input.TypeText(priceText);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

        // 3. Дропдаун валюты → Chaos Orb
        var (dx, dy) = currencyDropdownRect.GetInteriorPoint(1);
        Win32Input.MoveTo(dx, dy);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(WithJitter(DropdownSettleMs), ct).ConfigureAwait(false);

        var normalized = WindowsOcrTextLocator.NormalizeForMatch("chaos orb");
        var match = await WindowsOcrTextLocator
            .TryFindNormalizedSubstringAsync(chaosOrbOcrRect, normalized, null, ct)
            .ConfigureAwait(false);

        if (match is not { } found)
        {
            log?.Report($"[Переоценка] {item.BaseType} — Chaos Orb не найден в дропдауне, ESC, пропуск");
            Win32Input.PressKey(0x1B); // Escape
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            return false;
        }

        var (ox, oy) = found.BoundsOnScreen.GetInteriorPoint(1);
        Win32Input.MoveTo(ox, oy);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

        // 4. Подтверждение
        var (lx, ly) = listItemBtnRect.GetInteriorPoint(1);
        Win32Input.MoveTo(lx, ly);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

        log?.Report($"[Переоценка] {item.BaseType} ✓ переведено в chaos: {item.NewPrice}c");
        return true;
    }

    private static string FormatPrice(double price) =>
        price == Math.Floor(price)
            ? ((long)price).ToString()
            : price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Запускает reprice.py через WSL и возвращает готовый план + последнюю строку лога скрипта.
    /// Вызывается из UI-обработчика кнопки «Умная переоценка».
    /// </summary>
    public static async Task<(List<RepricePlanItem> plan, string scriptLog)> GeneratePlanAsync(
        string projectRoot, CancellationToken ct)
    {
        var wslRoot = projectRoot.Replace('\\', '/');
        if (wslRoot.Length >= 2 && wslRoot[1] == ':')
            wslRoot = "/mnt/" + char.ToLower(wslRoot[0]) + wslRoot[2..];

        var scriptDir = $"{wslRoot}/scripts/tabflow";
        var python    = $"{scriptDir}/.venv/bin/python3";
        var script    = $"{scriptDir}/reprice.py";

        var psi = new ProcessStartInfo("wsl.exe", $"-e {python} {script}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        string stdout;
        using (var proc = Process.Start(psi)!)
        {
            stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }

        var planPath = Path.Combine(projectRoot, "vault", "tabflow", "reprice_plan.json");
        var plan = LoadPlan(planPath);
        return (plan, stdout.Trim());
    }

    /// <summary>
    /// Загружает plan из reprice_plan.json.
    /// </summary>
    public static List<RepricePlanItem> LoadPlan(string planPath)
    {
        if (!File.Exists(planPath)) return [];
        var json = File.ReadAllText(planPath, System.Text.Encoding.UTF8);
        return JsonSerializer.Deserialize<List<RepricePlanItem>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
