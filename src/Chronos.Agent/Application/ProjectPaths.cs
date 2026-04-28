namespace Chronos.Agent.Application;

public static class ProjectPaths
{
    public static string GetProjectDirectory(string baseDir, string projectName) =>
        Path.Combine(baseDir, SafeProjectName(projectName));

    public static string SafeProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("projectName is required.", nameof(projectName));

        if (projectName.Contains('/') || projectName.Contains('\\') || projectName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid projectName.", nameof(projectName));

        return projectName;
    }
}
