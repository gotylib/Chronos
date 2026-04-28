namespace Chronos.Master.Domain.Entities;

public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset UtcTime { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public string? ClientIp { get; set; }
    public string? Details { get; set; }
}
