namespace GameHelper;

/// <summary>
/// DI-обёртка над статическим <see cref="ProjectPaths"/>.
/// </summary>
public sealed class ProjectPathsAdapter : IProjectPaths
{
    public string GetProjectRoot() => ProjectPaths.GetProjectRoot();
    public string GetLogDirectory() => ProjectPaths.GetLogDirectory();
}
