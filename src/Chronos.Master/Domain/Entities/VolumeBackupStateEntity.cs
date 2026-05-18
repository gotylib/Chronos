namespace Chronos.Master.Domain.Entities;

/// <summary>Последний успешный бэкап тома (для throttling по размеру и диску).</summary>
public sealed class VolumeBackupStateEntity
{
    public string ProjectName { get; set; } = "";

    public string VolumeName { get; set; } = "";

    public DateTimeOffset? LastBackupUtc { get; set; }

    public long? LastApproxBytes { get; set; }
}
