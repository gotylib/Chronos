using System.Net;
using Chronos.Master.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chronos.Master.Application.Services;

/// <summary>После <see cref="ArchivedProjectInfo.PurgeAfterUtc"/> дергает агента на удаление каталога архива и убирает строку из БД.</summary>
public sealed class ArchivedProjectPurgeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArchivedProjectPurgeHostedService> _logger;
    private readonly string? _expectedApiKey;

    public ArchivedProjectPurgeHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ArchivedProjectPurgeHostedService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _expectedApiKey = configuration["CHRONOS_MASTER_API_KEY"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
                await PurgeExpiredAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Archived project purge (master) failed.");
            }
        }
    }

    private async Task PurgeExpiredAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMasterPersistence>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var expired = await store.ListArchivedProjectsReadyForPurgeAsync(DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        foreach (var row in expired)
        {
            try
            {
                using var http = httpFactory.CreateClient("MasterApiProxy");
                if (!string.IsNullOrWhiteSpace(_expectedApiKey))
                    http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _expectedApiKey);
                using var resp = await http
                    .DeleteAsync($"{row.AgentUrl.TrimEnd('/')}/projects/archived/{Uri.EscapeDataString(row.ArchiveId)}", ct)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotFound)
                {
                    await store.DeleteArchivedProjectAsync(row.ArchiveId, ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Purged archived project {Project} archive={Archive} agent={Agent} HTTP={Status}",
                        row.ProjectName,
                        row.ArchiveId,
                        row.AgentId,
                        (int)resp.StatusCode);
                }
                else
                {
                    _logger.LogWarning(
                        "Agent purge failed for archive {Archive}: HTTP {Status}",
                        row.ArchiveId,
                        (int)resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Purge attempt skipped for archive {Archive}", row.ArchiveId);
            }
        }
    }
}
