namespace Chronos.Master.Domain.Entities;

/// <summary>Одна строка лиза для HA: какой экземпляр Master — лидер фоновых задач.</summary>
public sealed class LeaderLeaseEntity
{
    /// <summary>Always 1 — singleton row.</summary>
    public int Id { get; set; } = 1;

    public string HolderInstanceId { get; set; } = string.Empty;
    public DateTimeOffset AcquiredUtc { get; set; }
    public DateTimeOffset LeaseUntilUtc { get; set; }
}
