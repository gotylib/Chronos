using System.Globalization;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Chronos.Core;
using Chronos.Master.Application.Abstractions;
using Chronos.Master.Application.Contracts;
using Chronos.Master.Infrastructure.ObjectStorage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chronos.Master.Application.Services;

/// <summary>
/// У лидера Master: политики резервного копирования томов в S3/MinIO — проверка количества объектов,
/// запуск снимка на агенте и подрезка лишних ключей по MaxCopies. Интервал учитывает размер последнего бэкапа;
/// перед запуском проверяется свободное место на диске агента.
/// </summary>
public sealed class VolumeReplicationHostedService : BackgroundService
{
    private readonly ILeaderElectionService _leader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VolumeReplicationHostedService> _logger;

    public VolumeReplicationHostedService(
        ILeaderElectionService leader,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILogger<VolumeReplicationHostedService> logger)
    {
        _leader = leader;
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweepSeconds = int.TryParse(_configuration["CHRONOS_VOLUME_BACKUP_SWEEP_SECONDS"], out var s)
            ? Math.Clamp(s, 30, 86_400)
            : 90;
        var sweep = TimeSpan.FromSeconds(sweepSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_leader.IsLeader)
                {
                    await Task.Delay(sweep, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var s3Opts = VolumeStorageClusterOptions.FromConfiguration(_configuration);
                if (!s3Opts.IsComplete)
                {
                    await Task.Delay(sweep, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                using var s3 = new AmazonS3Client(
                    s3Opts.AccessKey,
                    s3Opts.SecretKey,
                    new AmazonS3Config
                    {
                        ServiceURL = s3Opts.ServiceUrl.TrimEnd('/'),
                        ForcePathStyle = s3Opts.ForcePathStyle,
                        AuthenticationRegion = "us-east-1"
                    });

                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IMasterPersistence>();
                var policies = await store.ListVolumeBackupPoliciesAsync(stoppingToken).ConfigureAwait(false);
                var placements = await store.ListProjectPlacementsAsync(stoppingToken).ConfigureAwait(false);
                var expectedApiKey = _configuration["CHRONOS_MASTER_API_KEY"];

                foreach (var policy in policies.Where(p => p.Enabled))
                {
                    var placement = placements.FirstOrDefault(p =>
                        string.Equals(p.ProjectName, policy.ProjectName, StringComparison.OrdinalIgnoreCase));
                    if (placement == null)
                    {
                        _logger.LogWarning(
                            "Volume backup policy {PolicyId}: unknown project placement for '{Project}'.",
                            policy.Id,
                            policy.ProjectName);
                        continue;
                    }

                    List<string> volumeNames;
                    try
                    {
                        volumeNames = await FetchAgentVolumeNamesAsync(
                                placement.AgentUrl,
                                policy.ProjectName,
                                expectedApiKey,
                                stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "List volumes failed for project {Project}", policy.ProjectName);
                        continue;
                    }

                    long? agentFreeBytes;
                    try
                    {
                        agentFreeBytes = await FetchAgentDiskFreeAsync(
                                placement.AgentUrl,
                                expectedApiKey,
                                stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Host disk probe failed for project {Project}", policy.ProjectName);
                        agentFreeBytes = null;
                    }

                    foreach (var volume in volumeNames.Where(v => VolumePatternMatcher.Matches(v, policy.VolumeNamePattern)))
                    {
                        var state = await store.GetVolumeBackupStateAsync(policy.ProjectName, volume, stoppingToken)
                            .ConfigureAwait(false);

                        if (agentFreeBytes == null)
                        {
                            _logger.LogDebug(
                                "Skip backup {Project}/{Volume}: agent disk stats unavailable.",
                                policy.ProjectName,
                                volume);
                            continue;
                        }

                        if (!HasEnoughDisk(policy, state, agentFreeBytes.Value))
                        {
                            _logger.LogInformation(
                                "Skip backup {Project}/{Volume}: insufficient free disk on agent ({FreeMb} MiB free).",
                                policy.ProjectName,
                                volume,
                                agentFreeBytes.Value / (1024 * 1024));
                            continue;
                        }

                        var combinedPrefix = VolumeObjectKeyLayout.CombinePrefix(s3Opts.KeyPrefix, policy.ExtraKeyPrefix);
                        var listPrefix = VolumeObjectKeyLayout.ListingPrefix(combinedPrefix, policy.ProjectName, volume);

                        var objects = await S3VolumeBackupCatalog.ListVolumeBackupsDescendingAsync(
                                s3,
                                s3Opts.BucketName,
                                listPrefix,
                                stoppingToken)
                            .ConfigureAwait(false);

                        await PruneExcessAsync(s3, s3Opts.BucketName, objects, policy.MaxCopies, stoppingToken)
                            .ConfigureAwait(false);

                        objects = await S3VolumeBackupCatalog.ListVolumeBackupsDescendingAsync(
                                s3,
                                s3Opts.BucketName,
                                listPrefix,
                                stoppingToken)
                            .ConfigureAwait(false);

                        if (!ShouldScheduleBackup(objects, policy, state))
                            continue;

                        var (ok, transferred) = await TriggerAgentBackupAsync(
                                placement.AgentUrl,
                                policy.ProjectName,
                                volume,
                                policy.ExtraKeyPrefix,
                                expectedApiKey,
                                stoppingToken)
                            .ConfigureAwait(false);

                        if (!ok)
                            continue;

                        var approx = transferred ?? state?.LastApproxBytes ?? LargestObjectSize(objects);
                        await store.UpsertVolumeBackupStateAsync(
                                policy.ProjectName,
                                volume,
                                DateTimeOffset.UtcNow,
                                approx,
                                stoppingToken)
                            .ConfigureAwait(false);

                        var refreshed = await S3VolumeBackupCatalog.ListVolumeBackupsDescendingAsync(
                                s3,
                                s3Opts.BucketName,
                                listPrefix,
                                stoppingToken)
                            .ConfigureAwait(false);

                        await PruneExcessAsync(s3, s3Opts.BucketName, refreshed, policy.MaxCopies, stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Volume backup orchestration sweep failed.");
            }

            await Task.Delay(sweep, stoppingToken).ConfigureAwait(false);
        }
    }

    private bool HasEnoughDisk(VolumeBackupPolicyDto policy, VolumeBackupStateDto? state, long freeBytes)
    {
        var defaultMb = int.TryParse(_configuration["CHRONOS_VOLUME_BACKUP_DEFAULT_MIN_FREE_DISK_MB"], out var x)
            ? x
            : 20_480;
        var minMb = policy.MinimumFreeDiskMb ?? defaultMb;
        var reserveBytes = (long)minMb * 1024L * 1024L;

        var headroomMult = double.TryParse(
                _configuration["CHRONOS_VOLUME_BACKUP_LAST_SNAPSHOT_HEADROOM"],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var m)
            ? m
            : 1.0;
        var tail = state?.LastApproxBytes is long lb ? (long)(lb * headroomMult) : 0L;

        return freeBytes >= reserveBytes + tail;
    }

    private static long LargestObjectSize(IReadOnlyList<S3Object> sortedNewestFirst)
    {
        long max = 0;
        foreach (var o in sortedNewestFirst)
        {
            if (o.Size > max)
                max = o.Size;
        }

        return max;
    }

    /// <summary>Минут между полными бэкапами одного тома: не ниже minMinutesBetweenBackups и масштабируется по размеру.</summary>
    private static int EffectiveBackupIntervalMinutes(
        VolumeBackupPolicyDto policy,
        VolumeBackupStateDto? state,
        IReadOnlyList<S3Object> objectsNewestFirst)
    {
        long approxBytes = state?.LastApproxBytes ?? (objectsNewestFirst.Count > 0 ? objectsNewestFirst[0].Size : 0L);
        var gb = approxBytes / (1024.0 * 1024 * 1024);
        var sizeCooldown = (int)Math.Clamp(
            Math.Floor(gb * policy.MinutesCooldownPerGb),
            0,
            policy.MaxCooldownMinutes);
        return Math.Max(policy.MinMinutesBetweenBackups, sizeCooldown);
    }

    private bool ShouldScheduleBackup(
        IReadOnlyList<S3Object> objects,
        VolumeBackupPolicyDto policy,
        VolumeBackupStateDto? state)
    {
        if (objects.Count == 0)
            return true;

        var newestUtc = objects[0].LastModified.ToUniversalTime();
        var ageMin = (DateTimeOffset.UtcNow - newestUtc).TotalMinutes;
        var interval = EffectiveBackupIntervalMinutes(policy, state, objects);
        return ageMin >= interval;
    }

    private static Task PruneExcessAsync(
        IAmazonS3 s3,
        string bucket,
        IReadOnlyList<S3Object> sortedNewestFirst,
        int maxCopies,
        CancellationToken ct)
    {
        if (sortedNewestFirst.Count <= maxCopies)
            return Task.CompletedTask;

        var drop = sortedNewestFirst.Skip(maxCopies).Select(o => o.Key).ToList();
        return S3VolumeBackupCatalog.DeleteObjectsAsync(s3, bucket, drop, ct);
    }

    private async Task<List<string>> FetchAgentVolumeNamesAsync(
        string agentUrl,
        string projectName,
        string? apiKey,
        CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient(nameof(VolumeReplicationHostedService));
        if (!string.IsNullOrWhiteSpace(apiKey))
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);

        var resp = await http
            .GetAsync(
                $"{agentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volumes",
                ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<string>>(json, ManifestJson.Options) ?? new List<string>();
    }

    private async Task<long?> FetchAgentDiskFreeAsync(string agentUrl, string? apiKey, CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient(nameof(VolumeReplicationHostedService));
        if (!string.IsNullOrWhiteSpace(apiKey))
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);

        using var resp = await http
            .GetAsync($"{agentUrl.TrimEnd('/')}/chronos/host-disk", ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<AgentHostDiskPayload>(json, ManifestJson.Options);
        return dto?.FreeBytes;
    }

    private async Task<(bool Ok, long? BytesTransferred)> TriggerAgentBackupAsync(
        string agentUrl,
        string projectName,
        string volumeName,
        string? extraKeyPrefix,
        string? apiKey,
        CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient(nameof(VolumeReplicationHostedService));
        if (!string.IsNullOrWhiteSpace(apiKey))
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);

        var qs = new StringBuilder("compress=gzip");
        if (!string.IsNullOrWhiteSpace(extraKeyPrefix))
            qs.Append("&keyPrefixExtra=").Append(Uri.EscapeDataString(extraKeyPrefix.Trim()));

        var url =
            $"{agentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/snapshot/to-object-storage?" +
            qs;

        using var resp = await http
            .PostAsync(url, new ByteArrayContent(Array.Empty<byte>()), ct)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<VolumeOperationResult>(body, ManifestJson.Options);

        if (!resp.IsSuccessStatusCode || parsed is not { Success: true })
        {
            _logger.LogWarning(
                "Volume backup POST failed {Project}/{Volume}: http={Status} body={Body}",
                projectName,
                volumeName,
                (int)resp.StatusCode,
                body);
            return (false, null);
        }

        _logger.LogInformation(
            "Volume backup OK {Project}/{Volume} → {Key} ({Bytes} B)",
            projectName,
            volumeName,
            parsed.ObjectKey,
            parsed.BytesTransferred ?? 0);
        return (true, parsed.BytesTransferred);
    }

    private sealed class AgentHostDiskPayload
    {
        public long FreeBytes { get; set; }
    }
}
