namespace Chronos.Agent.Domain.Entities;

/// <summary>Запись об архиве тома: метаданные в PostgreSQL, файл по StoredRelativePath на диске агента.</summary>
public sealed class VolumeArchiveEntity
{
    public Guid Id { get; set; }

    public string VolumeName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string StoredRelativePath { get; set; } = string.Empty;
    public long? BytesApprox { get; set; }
    public string CompressMode { get; set; } = "gzip";
    public DateTimeOffset CreatedUtc { get; set; }
}
