using System.Text.Json.Serialization;

namespace Chronos.Agent.Application;

/// <summary>Содержимое <c>archive-manifest.json</c> рядом с заархивированной папкой проекта.</summary>
public sealed class ProjectArchiveManifest
{
    [JsonPropertyName("archiveId")]
    public string ArchiveId { get; set; } = string.Empty;

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("archivedUtc")]
    public DateTimeOffset ArchivedUtc { get; set; }

    [JsonPropertyName("purgeAfterUtc")]
    public DateTimeOffset PurgeAfterUtc { get; set; }

    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; set; }
}

public static class ArchivedProjectsPaths
{
    public static string GetArchiveRoot(string appPath) =>
        Path.Combine(appPath, ".chronos", "archived-projects");

    public static string GetArchiveDirectory(string appPath, string archiveId) =>
        Path.Combine(GetArchiveRoot(appPath), archiveId);

    public static string ManifestPath(string archiveDirectory) =>
        Path.Combine(archiveDirectory, "archive-manifest.json");
}
