namespace Chronos.Master.Infrastructure.ObjectStorage;

/// <summary>Те же переменные окружения, что на агенте: Master листает bucket и оркеструет загрузки.</summary>
public sealed class VolumeStorageClusterOptions
{
    public bool Enabled { get; init; }
    public string ServiceUrl { get; init; } = "";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string BucketName { get; init; } = "chronos-volume-backups";
    public string KeyPrefix { get; init; } = "volumes";
    public bool ForcePathStyle { get; init; } = true;

    public static VolumeStorageClusterOptions FromConfiguration(IConfiguration configuration)
    {
        static bool Truthy(string? v) =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";

        return new VolumeStorageClusterOptions
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

    public bool IsComplete =>
        Enabled
        && !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}
