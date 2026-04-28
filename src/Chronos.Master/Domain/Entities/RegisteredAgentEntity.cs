namespace Chronos.Master.Domain.Entities;

/// <summary>Heartbeat-registered Chronos.Agent instance.</summary>
public sealed class RegisteredAgentEntity
{
    public string AgentId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string CapabilitiesJson { get; set; } = "{}";
    public DateTimeOffset RegisteredUtc { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public double? DiskPercent { get; set; }
}
