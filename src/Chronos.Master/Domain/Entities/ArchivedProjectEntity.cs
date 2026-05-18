namespace Chronos.Master.Domain.Entities;

/// <summary>Запись о проекте, перенесённом в архив на агенте (compose + данные на диске); удаление с диска после <see cref="PurgeAfterUtc"/>.</summary>
public sealed class ArchivedProjectEntity
{
    public string ArchiveId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public DateTimeOffset ArchivedUtc { get; set; }
    public DateTimeOffset PurgeAfterUtc { get; set; }
}
