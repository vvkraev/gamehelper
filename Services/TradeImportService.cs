using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Парсит JSON-ответ DevTools (fetch /api/trade2/fetch/...) и сохраняет в trade_data/ и vault/market/.</summary>
public class TradeImportService
{
    public sealed record ImportResult(
        string TradeDataFile,
        string VaultMdRelPath,
        int ItemCount,
        string Error = "");

    private readonly string _projectRoot;

    public TradeImportService(string projectRoot) => _projectRoot = projectRoot;

    public ImportResult Import(string rawJson, string slug = "")
    {
        try
        {
            var doc = JsonNode.Parse(rawJson) ?? throw new InvalidOperationException("Пустой JSON");
            var resultArr = doc["result"]?.AsArray() ?? throw new InvalidOperationException("Нет поля 'result'");
            if (resultArr.Count == 0)
                return new ImportResult("", "", 0, "Список result пустой");

            var firstItem = resultArr[0]?["item"];
            var baseType = firstItem?["baseType"]?.GetValue<string>()
                        ?? firstItem?["typeLine"]?.GetValue<string>()
                        ?? "item";

            if (string.IsNullOrWhiteSpace(slug))
                slug = MakeSlug(baseType);

            var now = DateTime.Now;
            var date = now.ToString("yyyy-MM-dd");
            var stem = $"{date}_{now:HH-mm-ss}_{slug}";

            var items = ParseItems(resultArr);

            var tradeDir = Path.Combine(_projectRoot, "trade_data");
            Directory.CreateDirectory(tradeDir);
            var jsonPath = EnsureUnique(Path.Combine(tradeDir, $"{stem}.json"));
            SaveJson(jsonPath, items, baseType, date, slug);

            var vaultDir = Path.Combine(_projectRoot, "vault", "market");
            Directory.CreateDirectory(vaultDir);
            var mdPath = EnsureUnique(Path.Combine(vaultDir, $"{stem}.md"));
            File.WriteAllText(mdPath, BuildMarkdown(items, baseType, date, Path.GetFileName(jsonPath)), Encoding.UTF8);

            return new ImportResult(
                TradeDataFile: Path.GetFileName(jsonPath),
                VaultMdRelPath: Path.GetRelativePath(_projectRoot, mdPath).Replace('\\', '/'),
                ItemCount: items.Count);
        }
        catch (Exception ex)
        {
            return new ImportResult("", "", 0, ex.Message);
        }
    }

    private static void SaveJson(string path, List<TradeItem> items, string baseType, string date, string slug)
    {
        var payload = new
        {
            _meta = new { date, source = "poe2_trade_fetch_devtools", base_type = baseType, slug },
            listings = items.ConvertAll(it => (object)new
            {
                id = it.Id,
                name = it.Name,
                base_type = it.BaseType,
                price_divine = it.PriceAmount,
                price_currency = it.PriceCurrency,
                quality = it.Quality,
                ilvl = it.Ilvl,
                corrupted = it.Corrupted,
                fractured = it.Fractured,
                evasion = it.Evasion,
                energy_shield = it.EnergyShield,
                armour = it.Armour,
                mods_fractured = it.FracturedMods,
                mods_desecrated = it.DesecrateMods,
                mods_implicit = it.ImplicitMods,
                mods_explicit = it.ExplicitMods,
                mods_crafted = it.CraftedMods,
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static List<TradeItem> ParseItems(JsonArray arr)
    {
        var list = new List<TradeItem>();
        foreach (var node in arr)
        {
            if (node is null) continue;
            var item = node["item"];
            if (item is null) continue;

            var priceNode = node["listing"]?["price"];
            var it = new TradeItem
            {
                Id = node["id"]?.GetValue<string>() ?? "",
                Name = item["name"]?.GetValue<string>() ?? "",
                BaseType = item["typeLine"]?.GetValue<string>() ?? item["baseType"]?.GetValue<string>() ?? "",
                Ilvl = item["ilvl"]?.GetValue<int>() ?? 0,
                Quality = item["quality"]?.GetValue<int>() ?? 0,
                Corrupted = item["corrupted"]?.GetValue<bool>() ?? false,
                Fractured = item["fractured"]?.GetValue<bool>() ?? false,
                PriceAmount = priceNode?["amount"]?.GetValue<double>() ?? 0,
                PriceCurrency = priceNode?["currency"]?.GetValue<string>() ?? "?",
            };

            ReadProperties(item["properties"]?.AsArray(), it);

            // implicitMods и fracturedMods — обычно простые строки с [Tag|Display] разметкой
            it.ImplicitMods = PlainStringList(item["implicitMods"]?.AsArray());
            it.FracturedMods = PlainStringList(item["fracturedMods"]?.AsArray());

            // explicitMods в PoE2 API — массив объектов; desecrated/crafted идут туда же,
            // определяются по flags.desecrated / flags.crafted / prefix хеша
            ClassifyExplicitMods(item["explicitMods"]?.AsArray(), it);

            list.Add(it);
        }
        return list;
    }

    private static void ReadProperties(JsonArray? props, TradeItem it)
    {
        if (props is null) return;
        foreach (var p in props)
        {
            var name = p?["name"]?.GetValue<string>() ?? "";
            var outerArr = p?["values"]?.AsArray();
            if (outerArr is null || outerArr.Count == 0) continue;
            var innerArr = outerArr[0]?.AsArray();
            if (innerArr is null || innerArr.Count == 0) continue;
            var raw = innerArr[0]?.GetValue<string>() ?? "0";
            var cleaned = raw.Replace(",", "").Replace("%", "").Replace("+", "").Split(' ')[0];
            if (!int.TryParse(cleaned, out var v)) continue;
            if (name.Contains("Evasion")) it.Evasion = v;
            else if (name.Contains("Energy Shield")) it.EnergyShield = v;
            else if (name.Contains("Armour") || name.Contains("Armor")) it.Armour = v;
            else if (name == "Quality") it.Quality = v;
        }
    }

    // implicitMods / fracturedMods приходят как простые строки с [Tag|Display] разметкой
    private static List<string> PlainStringList(JsonArray? arr)
    {
        var list = new List<string>();
        if (arr is null) return list;
        foreach (var n in arr)
            if (n is not null) list.Add(CleanMarkup(n.ToString()));
        return list;
    }

    // explicitMods — массив объектов { description, hash, flags?, mods[] }
    // desecrated и crafted также попадают сюда с соответствующими флагами
    private static void ClassifyExplicitMods(JsonArray? arr, TradeItem it)
    {
        if (arr is null) return;
        foreach (var n in arr)
        {
            if (n is null) continue;

            // Простая строка (старый формат)
            if (n is JsonValue)
            {
                it.ExplicitMods.Add(CleanMarkup(n.ToString()));
                continue;
            }

            var hash = n["hash"]?.GetValue<string>() ?? "";
            var flagsNode = n["flags"];
            var isDesecrated = (flagsNode?["desecrated"]?.GetValue<bool>() ?? false)
                            || hash.StartsWith("stat.desecrated");
            var isCrafted = (flagsNode?["crafted"]?.GetValue<bool>() ?? false)
                         || hash.StartsWith("stat.crafted");
            var isFractured = (flagsNode?["fractured"]?.GetValue<bool>() ?? false)
                           || hash.StartsWith("stat.fractured");

            var label = BuildModLabel(n);

            if (isDesecrated)
                it.DesecrateMods.Add(label);
            else if (isCrafted)
                it.CraftedMods.Add(label);
            else if (isFractured)
                it.FracturedMods.Add(label);
            else
                it.ExplicitMods.Add(label);
        }
    }

    // "Mystic P1 — 12% increased Spell Damage"
    private static string BuildModLabel(JsonNode modNode)
    {
        var desc = CleanMarkup(modNode["description"]?.GetValue<string>() ?? "");
        var modsArr = modNode["mods"]?.AsArray();
        if (modsArr is null || modsArr.Count == 0) return desc;
        var name = modsArr[0]?["name"]?.GetValue<string>();
        var tier = modsArr[0]?["tier"]?.GetValue<string>();
        return (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(tier))
            ? $"{name} {tier} — {desc}"
            : desc;
    }

    // Убирает [Tag|Display] → Display, [Tag] → убирает скобки
    private static string CleanMarkup(string s) =>
        Regex.Replace(s, @"\[([^\|\]]+)\|([^\]]+)\]", "$2")
             .Replace("[", "").Replace("]", "");

    private static string BuildMarkdown(List<TradeItem> items, string baseType, string date, string jsonFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {baseType} — срез {date}");
        sb.AppendLine();
        sb.AppendLine($"**Источник:** trade_data/{jsonFile}  ");
        sb.AppendLine($"**Листингов:** {items.Count}");
        sb.AppendLine();

        sb.AppendLine("## Таблица");
        sb.AppendLine();

        // Определяем максимальное число модов каждого типа по всем листингам
        var maxFrac  = items.Count > 0 ? items.Max(it => it.FracturedMods.Count)  : 0;
        var maxDes   = items.Count > 0 ? items.Max(it => it.DesecrateMods.Count)  : 0;
        var maxImpl  = items.Count > 0 ? items.Max(it => it.ImplicitMods.Count)   : 0;
        var maxExpl  = items.Count > 0 ? items.Max(it => it.ExplicitMods.Count)   : 0;
        var maxCraft = items.Count > 0 ? items.Max(it => it.CraftedMods.Count)    : 0;

        // Заголовок таблицы — каждый мод в отдельном столбце
        sb.Append("| Цена | Имя | Q | ilvl | Крп | Evas | ES | Armour |");
        for (var i = 1; i <= maxFrac;  i++) sb.Append($" Фрак {i} |");
        for (var i = 1; i <= maxImpl;  i++) sb.Append($" Impl {i} |");
        for (var i = 1; i <= maxDes;   i++) sb.Append($" Дес {i} |");
        for (var i = 1; i <= maxExpl;  i++) sb.Append($" Mod {i} |");
        for (var i = 1; i <= maxCraft; i++) sb.Append($" Craft {i} |");
        sb.AppendLine();

        var dynCols = maxFrac + maxImpl + maxDes + maxExpl + maxCraft;
        sb.Append("|---|---|---|---|---|---|---|---|");
        for (var i = 0; i < dynCols; i++) sb.Append("---|");
        sb.AppendLine();

        // Строки
        foreach (var it in items)
        {
            var corr = it.Corrupted ? "да" : "нет";
            sb.Append($"| {it.PriceAmount}{CurrencyShort(it.PriceCurrency)} | {EscapeCell(it.Name)} | {it.Quality}% | {it.Ilvl} | {corr} | {it.Evasion} | {it.EnergyShield} | {it.Armour} |");
            AppendModCells(sb, it.FracturedMods,  maxFrac);
            AppendModCells(sb, it.ImplicitMods,   maxImpl);
            AppendModCells(sb, it.DesecrateMods,  maxDes);
            AppendModCells(sb, it.ExplicitMods,   maxExpl);
            AppendModCells(sb, it.CraftedMods,    maxCraft);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendModCells(StringBuilder sb, List<string> mods, int maxCount)
    {
        for (var i = 0; i < maxCount; i++)
        {
            var val = i < mods.Count ? EscapeCell(mods[i]) : "—";
            sb.Append($" {val} |");
        }
    }

    private static string EscapeCell(string s) => s.Replace("|", "\\|");

    private static string MakeSlug(string s) =>
        s.Replace(" ", "_").Replace("'", "").Replace(",", "").ToLowerInvariant();

    private static string CurrencyShort(string c) => c switch
    {
        "divine" => "d",
        "exalted" => "e",
        "chaos" => "c",
        _ => c[..Math.Min(3, c.Length)]
    };

    private static string EnsureUnique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name}_{n}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private sealed class TradeItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string BaseType { get; set; } = "";
        public int Ilvl { get; set; }
        public int Quality { get; set; }
        public bool Corrupted { get; set; }
        public bool Fractured { get; set; }
        public int Evasion { get; set; }
        public int EnergyShield { get; set; }
        public int Armour { get; set; }
        public double PriceAmount { get; set; }
        public string PriceCurrency { get; set; } = "";
        public List<string> ExplicitMods { get; set; } = new();
        public List<string> FracturedMods { get; set; } = new();
        public List<string> CraftedMods { get; set; } = new();
        public List<string> ImplicitMods { get; set; } = new();
        public List<string> DesecrateMods { get; set; } = new();
    }
}
