using Chronos.Agent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chronos.Agent.Infrastructure.Persistence;

public sealed class ChronosAgentDbContext : DbContext
{
    public ChronosAgentDbContext(DbContextOptions<ChronosAgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<VolumeArchiveEntity> VolumeArchives => Set<VolumeArchiveEntity>();

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
    }
}
