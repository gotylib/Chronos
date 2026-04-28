namespace Chronos.Master.Domain.Entities;

public sealed class VolumePlacementEntity
{
    public string ProjectName { get; set; } = string.Empty;
    public string VolumeName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public long? BytesUsed { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
