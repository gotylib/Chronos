using Chronos.Master.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chronos.Master.Application.Services;

/// <summary>Leader-only: periodically touches registered agents to support future cross-agent volume replication orchestration.</summary>
public sealed class VolumeReplicationHostedService : BackgroundService
{
    private readonly ILeaderElectionService _leader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<VolumeReplicationHostedService> _logger;

    public VolumeReplicationHostedService(
        ILeaderElectionService leader,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        ILogger<VolumeReplicationHostedService> logger)
    {
        _leader = leader;
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_leader.IsLeader)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var persist = scope.ServiceProvider.GetRequiredService<IMasterPersistence>();
                    var agents = await persist.ListAgentsAsync(stoppingToken).ConfigureAwait(false);

                    foreach (var agent in agents)
                    {
                        try
                        {
                            using var http = _httpFactory.CreateClient();
                            http.Timeout = TimeSpan.FromSeconds(15);
                            var resp = await http
                                .GetAsync($"{agent.BaseUrl.TrimEnd('/')}/projects", stoppingToken)
                                .ConfigureAwait(false);
                            _logger.LogInformation(
                                "Replication sweep: agent {AgentId} GET /projects → {Status}",
                                agent.AgentId,
                                (int)resp.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Replication sweep: agent {AgentId} unreachable", agent.AgentId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Volume replication sweep failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
