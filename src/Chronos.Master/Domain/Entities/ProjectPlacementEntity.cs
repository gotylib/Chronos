namespace Chronos.Master.Domain.Entities;

public sealed class ProjectPlacementEntity
{
    public string ProjectName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}
