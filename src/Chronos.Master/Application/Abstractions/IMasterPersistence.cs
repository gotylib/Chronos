using Chronos.Master;

namespace Chronos.Master.Application.Abstractions;

/// <summary>Agent registry, audit, placement, and volume placement persistence.</summary>
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
    Task UpsertVolumePlacementAsync(VolumePlacementReport request, CancellationToken ct);
    Task<List<VolumePlacementInfo>> ListVolumePlacementsAsync(string? projectName, CancellationToken ct);
}
