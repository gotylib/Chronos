namespace Chronos.Master.Application.Contracts;

/// <summary>Ответ агента на POST archive (JSON).</summary>
public sealed class ArchiveProjectAgentResponse
{
    public string ArchiveId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTimeOffset ArchivedUtc { get; set; }
    public DateTimeOffset PurgeAfterUtc { get; set; }
    public int RetentionDays { get; set; }
}
