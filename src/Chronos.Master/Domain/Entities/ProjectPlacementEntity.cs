namespace Chronos.Master.Domain.Entities;

/// <summary>Соответствие имени проекта агенту, выбранному при деплое.</summary>
public sealed class ProjectPlacementEntity
{
    public string ProjectName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}
