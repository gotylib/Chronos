using System.Text.Json;
using Chronos.Agent.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chronos.Agent.Infrastructure.Hosted;

/// <summary>Периодически удаляет каталоги архивов, у которых истёк <see cref="ProjectArchiveManifest.PurgeAfterUtc"/>.</summary>
public sealed class ArchivedProjectPurgeHostedService : BackgroundService
{
    private readonly ILogger<ArchivedProjectPurgeHostedService> _logger;
    private readonly string _appPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ArchivedProjectPurgeHostedService(ILogger<ArchivedProjectPurgeHostedService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _appPath = configuration["CHRONOS_AGENT_APP_PATH"] ?? "/app";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken).ConfigureAwait(false);
                PurgeExpiredSync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Archived project purge sweep failed.");
            }
        }
    }

    private void PurgeExpiredSync()
    {
        var root = ArchivedProjectsPaths.GetArchiveRoot(_appPath);
        if (!Directory.Exists(root))
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                var manifestPath = ArchivedProjectsPaths.ManifestPath(dir);
                if (!File.Exists(manifestPath))
                    continue;

                var json = File.ReadAllText(manifestPath);
                var m = JsonSerializer.Deserialize<ProjectArchiveManifest>(json, JsonOptions);
                if (m == null || now < m.PurgeAfterUtc)
                    continue;

                Directory.Delete(dir, recursive: true);
                _logger.LogInformation("Purged expired archived project directory {Dir} (project={Project}).", dir, m.ProjectName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skip purge for {Dir}", dir);
            }
        }
    }
}
