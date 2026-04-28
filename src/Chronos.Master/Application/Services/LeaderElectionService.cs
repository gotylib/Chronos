using Chronos.Master.Application.Abstractions;
using Chronos.Master.Domain.Entities;
using Chronos.Master.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chronos.Master.Application.Services;

public sealed class LeaderElectionService : ILeaderElectionService
{
    private readonly IDbContextFactory<ChronosMasterDbContext> _dbFactory;
    private readonly TimeSpan _leaseTtl;

    public string InstanceId { get; }

    public bool IsLeader { get; private set; }

    public LeaderElectionService(IDbContextFactory<ChronosMasterDbContext> dbFactory, IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _leaseTtl = TimeSpan.FromSeconds(int.TryParse(configuration["CHRONOS_MASTER_LEASE_TTL_SECONDS"], out var s) ? s : 30);
        InstanceId =
            configuration["CHRONOS_MASTER_INSTANCE_ID"]
            ?? $"{Environment.MachineName}-{Environment.ProcessId}";
    }

    public async Task RenewLeaseAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var until = now + _leaseTtl;

        var lease = await db.LeaderLeases.SingleOrDefaultAsync(l => l.Id == 1, cancellationToken).ConfigureAwait(false);
        if (lease == null)
        {
            db.LeaderLeases.Add(new LeaderLeaseEntity
            {
                Id = 1,
                HolderInstanceId = InstanceId,
                AcquiredUtc = now,
                LeaseUntilUtc = until
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            IsLeader = true;
            return;
        }

        if (lease.LeaseUntilUtc < now || lease.HolderInstanceId == InstanceId)
        {
            lease.HolderInstanceId = InstanceId;
            lease.AcquiredUtc = now;
            lease.LeaseUntilUtc = until;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            IsLeader = true;
            return;
        }

        IsLeader = false;
    }
}
