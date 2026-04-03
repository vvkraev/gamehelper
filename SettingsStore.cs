using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameHelper.Services;

namespace GameHelper;

public static class SettingsStore
{
    /// <summary>Те же параметры, что для <c>settings.json</c> (в т.ч. enum).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string FilePath => Path.Combine(ProjectPaths.GetProjectRoot(), "settings.json");

    public static CraftConditionPlan CloneCraftConditionPlan(CraftConditionPlan? p)
    {
        if (p == null)
            return new CraftConditionPlan();
        var json = JsonSerializer.Serialize(p, JsonOptions);
        return JsonSerializer.Deserialize<CraftConditionPlan>(json, JsonOptions) ?? new CraftConditionPlan();
    }

    /// <summary>
    /// Краткое описание и полный JSON плана — для вставки в сообщение об ошибке или в чат.
    /// </summary>
    public static string FormatCraftConditionForClipboard(CraftConditionPlan? plan)
    {
        plan ??= new CraftConditionPlan();
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        var summary = CraftConditionEvaluator.FormatSummary(plan);
        return "GameHelper — условие остановки крафта\r\n"
             + "Кратко: " + summary + "\r\n\r\n"
             + "JSON:\r\n"
             + json;
    }

    public static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return s ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // игнорируем ошибки записи (нет прав и т.д.)
        }
    }
}
