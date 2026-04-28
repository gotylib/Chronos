using Chronos.Master.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chronos.Master.Infrastructure.Persistence;

public sealed class ChronosMasterDbContext : DbContext
{
    public ChronosMasterDbContext(DbContextOptions<ChronosMasterDbContext> options)
        : base(options)
    {
    }

    public DbSet<RegisteredAgentEntity> Agents => Set<RegisteredAgentEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<ProjectPlacementEntity> ProjectPlacements => Set<ProjectPlacementEntity>();
    public DbSet<VolumePlacementEntity> VolumePlacements => Set<VolumePlacementEntity>();
    public DbSet<LeaderLeaseEntity> LeaderLeases => Set<LeaderLeaseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisteredAgentEntity>(e =>
        {
            e.ToTable("agents");
            e.HasKey(a => a.AgentId);
            e.Property(a => a.AgentId).HasColumnName("agent_id").HasMaxLength(256);
            e.Property(a => a.BaseUrl).HasColumnName("base_url").HasMaxLength(2048);
            e.Property(a => a.Location).HasColumnName("location").HasMaxLength(256);
            e.Property(a => a.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("text");
            e.Property(a => a.RegisteredUtc).HasColumnName("registered_utc");
            e.Property(a => a.LastHeartbeatUtc).HasColumnName("last_heartbeat_utc");
            e.Property(a => a.CpuPercent).HasColumnName("cpu_percent");
            e.Property(a => a.MemoryPercent).HasColumnName("memory_percent");
            e.Property(a => a.DiskPercent).HasColumnName("disk_percent");
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(a => a.UtcTime).HasColumnName("utc_time");
            e.Property(a => a.Action).HasColumnName("action").HasMaxLength(256);
            e.Property(a => a.Result).HasColumnName("result").HasMaxLength(64);
            e.Property(a => a.Actor).HasColumnName("actor").HasMaxLength(256);
            e.Property(a => a.ClientIp).HasColumnName("client_ip").HasMaxLength(64);
            e.Property(a => a.Details).HasColumnName("details").HasColumnType("text");
        });

        modelBuilder.Entity<ProjectPlacementEntity>(e =>
        {
            e.ToTable("project_placements");
            e.HasKey(p => p.ProjectName);
            e.Property(p => p.ProjectName).HasColumnName("project_name").HasMaxLength(512);
            e.Property(p => p.AgentId).HasColumnName("agent_id").HasMaxLength(256);
            e.Property(p => p.AgentUrl).HasColumnName("agent_url").HasMaxLength(2048);
            e.Property(p => p.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<VolumePlacementEntity>(e =>
        {
            e.ToTable("volume_placements");
            e.HasKey(v => new { v.ProjectName, v.VolumeName, v.AgentId });
            e.Property(v => v.ProjectName).HasColumnName("project_name").HasMaxLength(512);
            e.Property(v => v.VolumeName).HasColumnName("volume_name").HasMaxLength(512);
            e.Property(v => v.AgentId).HasColumnName("agent_id").HasMaxLength(256);
            e.Property(v => v.Role).HasColumnName("role").HasMaxLength(64);
            e.Property(v => v.BytesUsed).HasColumnName("bytes_used");
            e.Property(v => v.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<LeaderLeaseEntity>(e =>
        {
            e.ToTable("leader_lease");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasColumnName("id");
            e.Property(l => l.HolderInstanceId).HasColumnName("holder_instance_id").HasMaxLength(256);
            e.Property(l => l.AcquiredUtc).HasColumnName("acquired_utc");
            e.Property(l => l.LeaseUntilUtc).HasColumnName("lease_until_utc");
        });
    }
}
