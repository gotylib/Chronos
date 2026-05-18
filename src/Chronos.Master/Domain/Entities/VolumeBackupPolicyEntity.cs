namespace Chronos.Master.Domain.Entities;

/// <summary>Политика резервного копирования томов проекта в объектное хранилище (MinIO/S3).</summary>
public sealed class VolumeBackupPolicyEntity
{
    public Guid Id { get; set; }

    public string ProjectName { get; set; } = "";

    /// <summary><c>*</c> или пусто — все тома из списка агента; иначе точное имя или шаблон с <c>*</c>.</summary>
    public string VolumeNamePattern { get; set; } = "*";

    public int MinCopies { get; set; } = 1;
    public int MaxCopies { get; set; } = 7;
    public int MinMinutesBetweenBackups { get; set; } = 1440;

    /// <summary>Минут «паузы» на каждый полный GB последнего бэкапа (ограничивается <see cref="MaxCooldownMinutes"/>).</summary>
    public int MinutesCooldownPerGb { get; set; } = 15;

    /// <summary>Верхняя граница интервала между полными бэкапами одного тома (минуты).</summary>
    public int MaxCooldownMinutes { get; set; } = 10_080;

    /// <summary>Минимум свободного места на диске агента (MiB); null — брать глобальный default из env Master.</summary>
    public int? MinimumFreeDiskMb { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Дополнительный сегмент к префиксу ключей (после глобального PREFIX из env).</summary>
    public string? ExtraKeyPrefix { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
