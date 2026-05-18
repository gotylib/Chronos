using Chronos.Master;
using Chronos.Master.Application.Contracts;

namespace Chronos.Master.Application.Abstractions;

/// <summary>Слой данных Master: агенты, аудит, привязка проектов к агентам, тома.</summary>
public interface IMasterPersistence
{
    Task UpsertAgentAsync(AgentRegistrationRequest request, CancellationToken ct);
    Task UpdateHeartbeatAsync(string agentId, AgentHeartbeatRequest request, CancellationToken ct);
    Task<List<AgentInfo>> ListAgentsAsync(CancellationToken ct);
    Task DeleteStaleAgentsAsync(TimeSpan ttl, CancellationToken ct);
    Task AppendAuditAsync(AuditLogEntry entry, CancellationToken ct);
    Task<List<AuditLogEntry>> ListAuditAsync(int limit, CancellationToken ct);
    Task DeleteOldAuditAsync(TimeSpan retention, CancellationToken ct);
    Task UpsertProjectPlacementAsync(string projectName, string agentId, string agentUrl, CancellationToken ct);
    Task<ProjectPlacementInfo?> GetProjectPlacementAsync(string projectName, CancellationToken ct);
    Task<List<ProjectPlacementInfo>> ListProjectPlacementsAsync(CancellationToken ct);
    Task<bool> DeleteProjectPlacementAsync(string projectName, CancellationToken ct);
    Task DeleteVolumePlacementsForProjectAsync(string projectName, CancellationToken ct);
    Task AddArchivedProjectAsync(ArchivedProjectInfo row, CancellationToken ct);
    Task<List<ArchivedProjectInfo>> ListArchivedProjectsAsync(CancellationToken ct);
    Task<ArchivedProjectInfo?> GetArchivedProjectAsync(string archiveId, CancellationToken ct);
    Task<bool> DeleteArchivedProjectAsync(string archiveId, CancellationToken ct);
    Task<List<ArchivedProjectInfo>> ListArchivedProjectsReadyForPurgeAsync(DateTimeOffset nowUtc, CancellationToken ct);
    Task UpsertVolumePlacementAsync(VolumePlacementReport request, CancellationToken ct);
    Task<List<VolumePlacementInfo>> ListVolumePlacementsAsync(string? projectName, CancellationToken ct);

    Task<List<VolumeBackupPolicyDto>> ListVolumeBackupPoliciesAsync(CancellationToken ct);
    Task<Guid> CreateVolumeBackupPolicyAsync(VolumeBackupPolicyCreateRequest request, CancellationToken ct);
    Task<bool> DeleteVolumeBackupPolicyAsync(Guid id, CancellationToken ct);
    Task<VolumeBackupStateDto?> GetVolumeBackupStateAsync(string projectName, string volumeName, CancellationToken ct);

    Task UpsertVolumeBackupStateAsync(string projectName, string volumeName, DateTimeOffset completedUtc,
        long? approxBytes, CancellationToken ct);
}
