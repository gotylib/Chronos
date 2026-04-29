// DTO для REST Chronos.Master: реестр агентов, деплой в кластер, аудит, размещение проектов и томов.
namespace Chronos.Master;

/// <summary>Тело POST /agents/register.</summary>
public sealed class AgentRegistrationRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Location { get; set; }
    public Dictionary<string, string>? Capabilities { get; set; }
}

/// <summary>Метрики хоста от агента при heartbeat.</summary>
public sealed class AgentHeartbeatRequest
{
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public double DiskPercent { get; set; }
}

/// <summary>Снимок строки реестра агентов для API и выбора AgentSelector.</summary>
public sealed class AgentInfo
{
    public string AgentId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string CapabilitiesJson { get; set; } = "{}";
    public DateTimeOffset RegisteredUtc { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public double DiskPercent { get; set; }
}

/// <summary>Отчёт агента о размещении реплицируемого тома.</summary>
public sealed class VolumePlacementReport
{
    public string ProjectName { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public long? BytesUsed { get; set; }
}

/// <summary>Запись в БД: какой том какого проекта на каком агенте.</summary>
public sealed class VolumePlacementInfo
{
    public string ProjectName { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public long? BytesUsed { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>Запрос массового деплоя из Git (bootstrap).</summary>
public sealed class DeployFromGitRequest
{
    public string RepositoryUrl { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string? ProjectName { get; set; }
}

/// <summary>Тело /cluster/deploy и /cluster/publish: YAML и опционально манифест.</summary>
public sealed class ClusterDeployRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string ComposeYaml { get; set; } = string.Empty;
    public string? PreferredLocation { get; set; }
    public string? ManifestJson { get; set; }
}

/// <summary>Ответ Master после проксирования деплоя на выбранного агента.</summary>
public sealed class ClusterDeployResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public object? AgentResponse { get; set; }
}

/// <summary>Строка журнала аудита операций API.</summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset UtcTime { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public string? ClientIp { get; set; }
    public string? Details { get; set; }
}

/// <summary>На каком агенте размещён проект (имя → URL агента).</summary>
public sealed class ProjectPlacementInfo
{
    public string ProjectName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}
