using Chronos.Agent.Infrastructure.EmbeddedMinio;

namespace Chronos.Agent.Application.Configuration;

/// <summary>Параметры S3-совместимого хранилища (MinIO / AWS) для выгрузки снимков томов.</summary>
public sealed class VolumeObjectStorageOptions
{
    public bool Enabled { get; init; }
    public string ServiceUrl { get; init; } = "";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string BucketName { get; init; } = "chronos-volume-backups";
    public string KeyPrefix { get; init; } = "volumes";
    public bool ForcePathStyle { get; init; } = true;

    /// <summary>Явные параметры из <c>CHRONOS_VOLUME_STORAGE_*</c>.</summary>
    public static VolumeObjectStorageOptions FromExplicit(IConfiguration configuration)
    {
        static bool Truthy(string? v) =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";

        return new VolumeObjectStorageOptions
        {
            Enabled = Truthy(configuration["CHRONOS_VOLUME_STORAGE_ENABLED"]),
            ServiceUrl = configuration["CHRONOS_VOLUME_STORAGE_SERVICE_URL"]?.Trim() ?? "",
            AccessKey = configuration["CHRONOS_VOLUME_STORAGE_ACCESS_KEY"] ?? "",
            SecretKey = configuration["CHRONOS_VOLUME_STORAGE_SECRET_KEY"] ?? "",
            BucketName = configuration["CHRONOS_VOLUME_STORAGE_BUCKET"] ?? "chronos-volume-backups",
            KeyPrefix = configuration["CHRONOS_VOLUME_STORAGE_PREFIX"] ?? "volumes",
            ForcePathStyle = !Truthy(configuration["CHRONOS_VOLUME_STORAGE_VIRTUAL_HOST_STYLE"])
        };
    }

    /// <summary>Внешняя конфигурация имеет приоритет над встроенным MinIO на агенте.</summary>
    public static VolumeObjectStorageOptions FromConfiguration(IConfiguration configuration, EmbeddedMinioOutcome? embedded = null)
    {
        var explicitOpts = FromExplicit(configuration);
        if (explicitOpts.IsComplete)
            return explicitOpts;

        if (embedded is { StartedOrVerified: true } em &&
            !string.IsNullOrWhiteSpace(em.ServiceUrl) &&
            !string.IsNullOrWhiteSpace(em.AccessKey) &&
            !string.IsNullOrWhiteSpace(em.SecretKey))
        {
            return new VolumeObjectStorageOptions
            {
                Enabled = true,
                ServiceUrl = em.ServiceUrl.TrimEnd('/'),
                AccessKey = em.AccessKey,
                SecretKey = em.SecretKey,
                BucketName = configuration["CHRONOS_VOLUME_STORAGE_BUCKET"] ?? "chronos-volume-backups",
                KeyPrefix = configuration["CHRONOS_VOLUME_STORAGE_PREFIX"] ?? "volumes",
                ForcePathStyle = explicitOpts.ForcePathStyle
            };
        }

        return explicitOpts;
    }

    public bool IsComplete =>
        Enabled
        && !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}
