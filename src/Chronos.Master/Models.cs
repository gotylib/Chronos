namespace Chronos.Master;

public sealed class AgentRegistrationRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Location { get; set; }
    public Dictionary<string, string>? Capabilities { get; set; }
}

public sealed class AgentHeartbeatRequest
{
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public double DiskPercent { get; set; }
}

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

public sealed class VolumePlacementReport
{
    public string ProjectName { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public long? BytesUsed { get; set; }
}

public sealed class VolumePlacementInfo
{
    public string ProjectName { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public long? BytesUsed { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class DeployFromGitRequest
{
    public string RepositoryUrl { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string? ProjectName { get; set; }
}

public sealed class ClusterDeployRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string ComposeYaml { get; set; } = string.Empty;
    public string? PreferredLocation { get; set; }
    public string? ManifestJson { get; set; }
}

public sealed class ClusterDeployResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public object? AgentResponse { get; set; }
}

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

public sealed class ProjectPlacementInfo
{
    public string ProjectName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}
