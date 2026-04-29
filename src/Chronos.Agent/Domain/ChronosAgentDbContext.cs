using Chronos.Agent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chronos.Agent.Domain;

/// <summary>EF Core: таблица метаданных архивов томов (<see cref="VolumeArchiveEntity"/>).</summary>
public sealed class ChronosAgentDbContext(DbContextOptions<ChronosAgentDbContext> options) : DbContext(options)
{
    public DbSet<VolumeArchiveEntity> VolumeArchives => Set<VolumeArchiveEntity>();
    public DbSet<ServiceEntity>  Services => Set<ServiceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VolumeArchiveEntity>(e =>
        {
            e.ToTable("volume_archives");
            e.HasKey(x => x.Id);
            e.Property(x => x.VolumeName).HasMaxLength(512);
            e.Property(x => x.ProjectName).HasMaxLength(512);
            e.Property(x => x.StoredRelativePath).HasMaxLength(2048);
            e.Property(x => x.CompressMode).HasMaxLength(32);
            e.HasIndex(x => new { x.ProjectName, x.VolumeName });
        });

        modelBuilder.Entity<ServiceEntity>(e =>
        {
            e.ToTable("services");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceName).HasMaxLength(512);
        });
    }
}
