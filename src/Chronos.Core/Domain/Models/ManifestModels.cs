using System.Text.Json;
using System.Text.Json.Serialization;

// Модели манифеста проекта (.chronos/manifest.json): проверки, jobs, кодовые тесты, сериализация для агента.
namespace Chronos.Core;

public enum TestCriticality
{
    Info,
    Warning,
    Critical
}

/// <summary>Декларативная проверка (HTTP или exec в контейнере).</summary>
public sealed class DeclarativeCheck
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "http";
    public string? Url { get; set; }
    public int? ExpectedStatus { get; set; }
    public string? ExecCommand { get; set; }
    public bool OnStartup { get; set; } = true;
    public int? IntervalMinutes { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestCriticality Criticality { get; set; } = TestCriticality.Warning;
}

/// <summary>Периодическое задание (exec в контейнере или скрипт на хосте).</summary>
public sealed class JobDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "exec";
    public string? ExecCommand { get; set; }
    public string? ScriptRelativePath { get; set; }
    public bool OnStartup { get; set; } = false;
    public int IntervalMinutes { get; set; } = 60;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestCriticality Criticality { get; set; } = TestCriticality.Warning;
}

public sealed class ManifestServiceSection
{
    public List<DeclarativeCheck> Tests { get; set; } = new();
    public List<JobDefinition> Jobs { get; set; } = new();
    public List<CodeTestEntry> CodeTests { get; set; } = new();
    public List<CodeJobEntry> CodeJobs { get; set; } = new();
}

/// <summary>Сохраняется как <c>.chronos/manifest.json</c> на агенте.</summary>
public sealed class ProjectManifest
{
    public int Version { get; set; } = 2;

    public Dictionary<string, ManifestServiceSection> Services { get; set; } = new();
}

public sealed class CheckRunRecord
{
    public string Service { get; set; } = string.Empty;
    public string TestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset UtcTime { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestCriticality Criticality { get; set; }
}

public sealed class JobRunRecord
{
    public string Service { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset UtcTime { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestCriticality Criticality { get; set; }
}

public sealed class DiagnosticsSnapshot
{
    public List<CheckRunRecord> RecentTests { get; set; } = new();
    public List<JobRunRecord> RecentJobs { get; set; } = new();
}

public sealed class VolumeSnapshotUploadRequest
{
    public string UploadUrl { get; set; } = string.Empty;
    public string Method { get; set; } = "PUT";
    public Dictionary<string, string>? Headers { get; set; }
    public string? Compress { get; set; }
}

public sealed class VolumeRestoreFromUrlRequest
{
    public string DownloadUrl { get; set; } = string.Empty;
    public Dictionary<string, string>? Headers { get; set; }
    public string? Compress { get; set; }
}

public sealed class VolumeOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long? BytesTransferred { get; set; }
    public string? SavedPath { get; set; }

    /// <summary>S3/MinIO object key после успешного <c>snapshot/to-object-storage</c>.</summary>
    public string? ObjectKey { get; set; }
}

public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
