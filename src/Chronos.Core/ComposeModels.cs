using System.Text.Json.Serialization;
using Chronos.Core.Compose.Implementation;

namespace Chronos.Core;

public sealed class Service
{
    public string Name { get; set; } = string.Empty;

    public string? Image { get; set; }
    public string? BuildContext { get; set; }
    public string? Dockerfile { get; set; }

    public string? ContainerName { get; set; }
    public List<string> Command { get; } = new();

    public List<PortMapping> Ports { get; } = new();

    public Dictionary<string, string> Environment { get; } = new();
    public List<string> EnvFiles { get; } = new();

    public Dictionary<string, string> Labels { get; } = new();

    public List<string> DependsOn { get; } = new();
    public List<string> Networks { get; } = new();

    // docker-compose allows "source:target[:mode]" strings
    public List<string> Volumes { get; } = new();
    public List<string> Secrets { get; } = new();

    public string RestartPolicy { get; set; } = "unless-stopped";

    public HealthCheck? HealthCheck { get; set; }
    public DeployConfig? Deploy { get; set; }

    public Dictionary<string, string> ExtraHosts { get; } = new();
    public List<string> Capabilities { get; } = new();

    public string? User { get; set; }
    public string? WorkingDir { get; set; }

    public string? LoggingDriver { get; set; }
    public Dictionary<string, string> LoggingOptions { get; } = new();

    public bool Init { get; set; } = true;
    public bool Privileged { get; set; } = false;

    /// <summary>Декларативные проверки (HTTP/exec), см. <see cref="ServiceBuilder.UseChecks"/>.</summary>
    public List<DeclarativeCheck> Checks { get; } = new();

    /// <summary>Периодические задания на агенте.</summary>
    public List<JobDefinition> Jobs { get; } = new();

    /// <summary>Методы с <see cref="TestAttribute"/>, см. <see cref="ServiceBuilder.UseTests(System.Type[])"/>.</summary>
    public List<CodeTestEntry> CodeTests { get; } = new();

    /// <summary>Методы с <see cref="JobAttribute"/>, см. <see cref="ServiceBuilder.UseJobs(System.Type[])"/>.</summary>
    public List<CodeJobEntry> CodeJobs { get; } = new();
}

public sealed class PortMapping
{
    public string Host { get; set; } = "0.0.0.0";
    public int HostPort { get; set; }
    public int ContainerPort { get; set; }
    public string Protocol { get; set; } = "tcp";

    public string ToComposeString()
    {
        var proto = string.Equals(Protocol, "tcp", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"/{Protocol}";

        // docker-compose: if Host is 0.0.0.0, "*" or empty - host part can be omitted
        if (string.IsNullOrWhiteSpace(Host) ||
            Host == "*" ||
            Host == "0.0.0.0")
        {
            return $"{HostPort}:{ContainerPort}{proto}";
        }

        return $"{Host}:{HostPort}:{ContainerPort}{proto}";
    }
}

public sealed class HealthCheck
{
    // For docker-compose test: we'll serialize as ["CMD-SHELL", TestCommand]
    public string TestCommand { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 10;
    public int Retries { get; set; } = 3;
}

public sealed class ResourceLimits
{
    public decimal? Cpus { get; set; }
    public int? MemoryMb { get; set; }
}

public sealed class DeployConfig
{
    public int? Replicas { get; set; }
    public ResourceLimits? Resources { get; set; }
}

public sealed class Network
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = "bridge";
}

public sealed class Volume
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = "local";
}

public sealed class Secret
{
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;
}

public sealed class Config
{
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;
}

public sealed class ContainerStatus
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public sealed class DeployResult
{
    public string? DeploymentId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ContainerStatus> Containers { get; set; } = new();
    public DiagnosticsSnapshot? Diagnostics { get; set; }

    /// <summary>
    /// True when POST /deploy|/start|/restart accepted the work; use GET …/status to poll (see <see cref="DeploymentInProgress"/>).
    /// </summary>
    public bool OperationPending { get; set; }

    /// <summary>
    /// True while background compose/start/restart is still running on the agent.
    /// </summary>
    public bool DeploymentInProgress { get; set; }

    public string? Message { get; set; }
}

public sealed class ValidationError
{
    public string Service { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ValidationWarning
{
    public string Service { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
}

