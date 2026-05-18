using System.Text.Json;
using Chronos.Agent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Chronos.Agent.Domain;

/// <summary>EF Core: таблица метаданных архивов томов (<see cref="VolumeArchiveEntity"/>).</summary>
public sealed class ChronosAgentDbContext(DbContextOptions<ChronosAgentDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions ListJsonOptions = new(JsonSerializerDefaults.General);

    public DbSet<VolumeArchiveEntity> VolumeArchives => Set<VolumeArchiveEntity>();
    public DbSet<ServiceEntity> Services => Set<ServiceEntity>();

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
            e.Property(x => x.DockerComposeFile).HasMaxLength(512);
            e.Property(x => x.DockerComposeFilePath).HasMaxLength(4096);
            var stringListComparer = new ValueComparer<List<string>>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v.Aggregate(0, (h, x) => HashCode.Combine(h, StringComparer.Ordinal.GetHashCode(x))),
                v => v.ToList());

            e.Property(x => x.ImageNames).HasConversion(
                    v => SerializeStringList(v),
                    v => DeserializeStringList(v))
                .Metadata.SetValueComparer(stringListComparer);

            e.Property(x => x.VolumeNames).HasConversion(
                    v => SerializeStringList(v),
                    v => DeserializeStringList(v))
                .Metadata.SetValueComparer(stringListComparer);
            e.HasIndex(x => x.ServiceName).IsUnique();
        });
    }

    private static string SerializeStringList(List<string> v) => JsonSerializer.Serialize(v, ListJsonOptions);

    private static List<string> DeserializeStringList(string v) =>
        JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>();
}
