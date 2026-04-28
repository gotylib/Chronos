namespace Chronos.Master.Domain.Entities;

/// <summary>Single-row lease for HA master background jobs (volume replication, backups).</summary>
public sealed class LeaderLeaseEntity
{
    /// <summary>Always 1 — singleton row.</summary>
    public int Id { get; set; } = 1;

    public string HolderInstanceId { get; set; } = string.Empty;
    public DateTimeOffset AcquiredUtc { get; set; }
    public DateTimeOffset LeaseUntilUtc { get; set; }
}
