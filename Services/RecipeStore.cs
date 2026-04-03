using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

public static class RecipeStore
{
    public static string RecipesDirectory
    {
        get
        {
            var dir = Path.Combine(ProjectPaths.GetProjectRoot(), "Recipes");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SanitizeRecipeName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
            return "";

        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');

        // защитимся от очень длинных имён (Windows path + UX)
        if (name.Length > 80)
            name = name[..80].Trim();

        return name;
    }

    public static string GetRecipePath(string recipeName)
    {
        var safe = SanitizeRecipeName(recipeName);
        if (safe.Length == 0)
            throw new ArgumentException("Имя рецепта пустое.");
        return Path.Combine(RecipesDirectory, safe + ".json");
    }

    private sealed record RecipeFile(string Name, DateTime SavedAt, string CraftMode, CraftConditionPlan Plan);

    public static void Save(string recipeName, string craftMode, CraftConditionPlan plan)
    {
        var path = GetRecipePath(recipeName);
        var payload = new RecipeFile(recipeName.Trim(), DateTime.Now, craftMode.Trim(), plan ?? new CraftConditionPlan());
        var json = JsonSerializer.Serialize(payload, SettingsStore.JsonOptions);
        File.WriteAllText(path, json);
    }

    public static void SaveToFile(string path, string craftMode, CraftConditionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Путь сохранения пустой.");

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? RecipesDirectory);

        var name = Path.GetFileNameWithoutExtension(path).Trim();
        if (name.Length == 0)
            name = "Recipe";

        var payload = new RecipeFile(name, DateTime.Now, (craftMode ?? "").Trim(), plan ?? new CraftConditionPlan());
        var json = JsonSerializer.Serialize(payload, SettingsStore.JsonOptions);
        File.WriteAllText(path, json);
    }

    public static (string CraftMode, CraftConditionPlan Plan) LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);

        // основной формат: обёртка RecipeFile
        try
        {
            var wrapper = JsonSerializer.Deserialize<RecipeFile>(json, SettingsStore.JsonOptions);
            if (wrapper?.Plan != null)
                return (string.IsNullOrWhiteSpace(wrapper.CraftMode) ? "Хаос" : wrapper.CraftMode, wrapper.Plan);
        }
        catch
        {
            // fallback ниже
        }

        // fallback: чистый CraftConditionPlan (на будущее / ручные файлы)
        var plan = JsonSerializer.Deserialize<CraftConditionPlan>(json, SettingsStore.JsonOptions);
        return ("Хаос", plan ?? new CraftConditionPlan());
    }
}

