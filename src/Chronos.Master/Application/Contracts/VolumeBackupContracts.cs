namespace Chronos.Master.Application.Contracts;

public sealed class VolumeBackupPolicyDto
{
    public Guid Id { get; set; }
    public string ProjectName { get; set; } = "";
    public string VolumeNamePattern { get; set; } = "*";
    public int MinCopies { get; set; }
    public int MaxCopies { get; set; }
    public int MinMinutesBetweenBackups { get; set; }
    public int MinutesCooldownPerGb { get; set; }
    public int MaxCooldownMinutes { get; set; }
    public int? MinimumFreeDiskMb { get; set; }
    public bool Enabled { get; set; }
    public string? ExtraKeyPrefix { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class VolumeBackupPolicyCreateRequest
{
    public string ProjectName { get; set; } = "";
    public string VolumeNamePattern { get; set; } = "*";
    public int MinCopies { get; set; } = 1;
    public int MaxCopies { get; set; } = 7;
    public int MinMinutesBetweenBackups { get; set; } = 1440;
    public int MinutesCooldownPerGb { get; set; } = 15;
    public int MaxCooldownMinutes { get; set; } = 10_080;
    public int? MinimumFreeDiskMb { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ExtraKeyPrefix { get; set; }
}

public sealed class VolumeBackupStateDto
{
    public string ProjectName { get; set; } = "";
    public string VolumeName { get; set; } = "";
    public DateTimeOffset? LastBackupUtc { get; set; }
    public long? LastApproxBytes { get; set; }
}
