using System.IO;
using System.Text.Json;
using GameHelper.Services;

namespace GameHelper;

public static class PipelineStore
{
    private static string PipelinesDir => Path.Combine(ProjectPaths.GetProjectRoot(), "Pipelines");

    public static string GetPipelinesDir() => PipelinesDir;

    public static void Save(CraftPipeline pipeline) =>
        SaveToDirectory(pipeline, PipelinesDir);

    public static CraftPipeline? Load(string name) =>
        LoadFromDirectory(name, PipelinesDir);

    public static List<CraftPipeline> LoadAll() =>
        LoadAllFromDirectory(PipelinesDir);

    internal static void SaveToDirectory(CraftPipeline pipeline, string dir)
    {
        Directory.CreateDirectory(dir);
        var safeName = string.Concat(pipeline.Name.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(dir, safeName + ".json");
        var json = JsonSerializer.Serialize(pipeline, SettingsStore.JsonOptions);
        File.WriteAllText(path, json);
    }

    internal static CraftPipeline? LoadFromDirectory(string name, string dir)
    {
        var path = Path.Combine(dir, name + ".json");
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CraftPipeline>(json, SettingsStore.JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    internal static List<CraftPipeline> LoadAllFromDirectory(string dir)
    {
        var result = new List<CraftPipeline>();
        if (!Directory.Exists(dir))
            return result;

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pipeline = JsonSerializer.Deserialize<CraftPipeline>(json, SettingsStore.JsonOptions);
                if (pipeline is not null)
                    result.Add(pipeline);
            }
            catch
            {
                // пропускаем повреждённые файлы
            }
        }
        return result;
    }
}
