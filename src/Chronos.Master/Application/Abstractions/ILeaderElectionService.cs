namespace Chronos.Master.Application.Abstractions;

/// <summary>Блокировка-лизинг в PostgreSQL: только один экземпляр Master выполняет фоновые задачи лидера.</summary>
public interface ILeaderElectionService
{
    string InstanceId { get; }
    bool IsLeader { get; }
    Task RenewLeaseAsync(CancellationToken cancellationToken = default);
}
