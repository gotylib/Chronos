namespace Chronos.Master.Application.Abstractions;

/// <summary>PostgreSQL-backed lease so only one master replica runs exclusive background work.</summary>
public interface ILeaderElectionService
{
    string InstanceId { get; }
    bool IsLeader { get; }
    Task RenewLeaseAsync(CancellationToken cancellationToken = default);
}
