using System.IO;
using System.Linq;

namespace GameHelper;

/// <summary>Поиск корня проекта (где лежит .csproj) и каталога Log рядом с ним.</summary>
public static class ProjectPaths
{
    public static string GetProjectRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (dir.EnumerateFiles("*.csproj").Any())
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
            // ignored
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>Каталог <c>Log</c> в корне проекта (создаётся при необходимости).</summary>
    public static string GetLogDirectory()
    {
        var log = Path.Combine(GetProjectRoot(), "Log");
        Directory.CreateDirectory(log);
        return log;
    }
}
