using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chronos.Master.Infrastructure.Persistence;

/// <summary>Design-time factory for EF Core CLI migrations.</summary>
public sealed class ChronosMasterDbContextFactory : IDesignTimeDbContextFactory<ChronosMasterDbContext>
{
    public ChronosMasterDbContext CreateDbContext(string[] args)
    {
        var conn =
            Environment.GetEnvironmentVariable("CHRONOS_MASTER_PG_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=chronos_master;Username=chronos;Password=chronos";

        var opts = new DbContextOptionsBuilder<ChronosMasterDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new ChronosMasterDbContext(opts);
    }
}
