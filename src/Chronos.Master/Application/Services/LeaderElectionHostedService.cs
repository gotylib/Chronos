using Chronos.Master.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Chronos.Master.Application.Services;

public sealed class LeaderElectionHostedService : BackgroundService
{
    private readonly ILeaderElectionService _leader;

    public LeaderElectionHostedService(ILeaderElectionService leader)
    {
        _leader = leader;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _leader.RenewLeaseAsync(stoppingToken).ConfigureAwait(false);
            }
            catch
            {
                // best-effort; next tick retries
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }
    }
}
