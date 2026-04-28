using System.Text.Json;
using Chronos.Master.Application.Abstractions;
using Chronos.Master.Domain.Entities;
using Chronos.Master.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chronos.Master.Application.Services;

public sealed class MasterPersistenceService : IMasterPersistence
{
    private readonly ChronosMasterDbContext _db;

    public MasterPersistenceService(ChronosMasterDbContext db)
    {
        _db = db;
    }

    public async Task UpsertAgentAsync(AgentRegistrationRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var caps = JsonSerializer.Serialize(request.Capabilities ?? new Dictionary<string, string>());
        var existing = await _db.Agents.FindAsync([request.AgentId], ct).ConfigureAwait(false);
        if (existing == null)
        {
            _db.Agents.Add(new RegisteredAgentEntity
            {
                AgentId = request.AgentId,
                BaseUrl = request.BaseUrl,
                Location = request.Location,
                CapabilitiesJson = caps,
                RegisteredUtc = now,
                LastHeartbeatUtc = now,
                CpuPercent = null,
                MemoryPercent = null,
                DiskPercent = null
            });
        }
        else
        {
            existing.BaseUrl = request.BaseUrl;
            existing.Location = request.Location;
            existing.CapabilitiesJson = caps;
            existing.LastHeartbeatUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateHeartbeatAsync(string agentId, AgentHeartbeatRequest request, CancellationToken ct)
    {
        var entity = await _db.Agents.FindAsync([agentId], ct).ConfigureAwait(false);
        if (entity == null)
            return;

        entity.LastHeartbeatUtc = DateTimeOffset.UtcNow;
        entity.CpuPercent = request.CpuPercent;
        entity.MemoryPercent = request.MemoryPercent;
        entity.DiskPercent = request.DiskPercent;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AgentInfo>> ListAgentsAsync(CancellationToken ct)
    {
        var rows = await _db.Agents.AsNoTracking().OrderBy(a => a.AgentId).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToAgentInfo).ToList();
    }

    public async Task DeleteStaleAgentsAsync(TimeSpan ttl, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - ttl;
        await _db.Agents.Where(a => a.LastHeartbeatUtc < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task AppendAuditAsync(AuditLogEntry entry, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLogEntity
        {
            UtcTime = entry.UtcTime,
            Action = entry.Action,
            Result = entry.Result,
            Actor = entry.Actor,
            ClientIp = entry.ClientIp,
            Details = entry.Details
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AuditLogEntry>> ListAuditAsync(int limit, CancellationToken ct)
    {
        var take = Math.Max(1, limit);
        var rows = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(e => new AuditLogEntry
        {
            Id = e.Id,
            UtcTime = e.UtcTime,
            Action = e.Action,
            Result = e.Result,
            Actor = e.Actor,
            ClientIp = e.ClientIp,
            Details = e.Details
        }).ToList();
    }

    public async Task DeleteOldAuditAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;
        await _db.AuditLogs.Where(a => a.UtcTime < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertProjectPlacementAsync(string projectName, string agentId, string agentUrl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.ProjectPlacements.FindAsync([projectName], ct).ConfigureAwait(false);
        if (existing == null)
        {
            _db.ProjectPlacements.Add(new ProjectPlacementEntity
            {
                ProjectName = projectName,
                AgentId = agentId,
                AgentUrl = agentUrl,
                UpdatedUtc = now
            });
        }
        else
        {
            existing.AgentId = agentId;
            existing.AgentUrl = agentUrl;
            existing.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ProjectPlacementInfo?> GetProjectPlacementAsync(string projectName, CancellationToken ct)
    {
        var row = await _db.ProjectPlacements.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectName == projectName, ct).ConfigureAwait(false);
        return row == null
            ? null
            : new ProjectPlacementInfo
            {
                ProjectName = row.ProjectName,
                AgentId = row.AgentId,
                AgentUrl = row.AgentUrl,
                UpdatedUtc = row.UpdatedUtc
            };
    }

    public async Task<List<ProjectPlacementInfo>> ListProjectPlacementsAsync(CancellationToken ct)
    {
        var rows = await _db.ProjectPlacements.AsNoTracking().OrderBy(p => p.ProjectName).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new ProjectPlacementInfo
        {
            ProjectName = r.ProjectName,
            AgentId = r.AgentId,
            AgentUrl = r.AgentUrl,
            UpdatedUtc = r.UpdatedUtc
        }).ToList();
    }

    public async Task UpsertVolumePlacementAsync(VolumePlacementReport request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.VolumePlacements.FindAsync(
            [request.ProjectName, request.VolumeName, request.AgentId],
            ct).ConfigureAwait(false);
        if (existing == null)
        {
            _db.VolumePlacements.Add(new VolumePlacementEntity
            {
                ProjectName = request.ProjectName,
                VolumeName = request.VolumeName,
                AgentId = request.AgentId,
                Role = request.Role,
                BytesUsed = request.BytesUsed,
                UpdatedUtc = now
            });
        }
        else
        {
            existing.Role = request.Role;
            existing.BytesUsed = request.BytesUsed;
            existing.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<VolumePlacementInfo>> ListVolumePlacementsAsync(string? projectName, CancellationToken ct)
    {
        var query = _db.VolumePlacements.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(projectName))
            query = query.Where(v => v.ProjectName == projectName);

        var rows = await query
            .OrderBy(v => v.ProjectName).ThenBy(v => v.VolumeName).ThenBy(v => v.AgentId)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(v => new VolumePlacementInfo
        {
            ProjectName = v.ProjectName,
            VolumeName = v.VolumeName,
            AgentId = v.AgentId,
            Role = v.Role,
            BytesUsed = v.BytesUsed,
            UpdatedUtc = v.UpdatedUtc
        }).ToList();
    }

    private static AgentInfo ToAgentInfo(RegisteredAgentEntity a) =>
        new()
        {
            AgentId = a.AgentId,
            BaseUrl = a.BaseUrl,
            Location = a.Location,
            CapabilitiesJson = string.IsNullOrEmpty(a.CapabilitiesJson) ? "{}" : a.CapabilitiesJson,
            RegisteredUtc = a.RegisteredUtc,
            LastHeartbeatUtc = a.LastHeartbeatUtc,
            CpuPercent = a.CpuPercent ?? 0,
            MemoryPercent = a.MemoryPercent ?? 0,
            DiskPercent = a.DiskPercent ?? 0
        };
}
