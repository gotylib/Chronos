using Chronos.Agent.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chronos.Agent.Infrastructure.Persistence;

/// <summary>Фабрика для dotnet ef migrations (переменная CHRONOS_AGENT_PG_CONNECTION или дефолт localhost).</summary>
public sealed class ChronosAgentDbContextFactory : IDesignTimeDbContextFactory<ChronosAgentDbContext>
{
    public ChronosAgentDbContext CreateDbContext(string[] args)
    {
        var cs =
            Environment.GetEnvironmentVariable("CHRONOS_AGENT_PG_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=chronos_agent;Username=chronos;Password=chronos";

        var opts = new DbContextOptionsBuilder<ChronosAgentDbContext>()
            .UseNpgsql(cs)
            .Options;

        return new ChronosAgentDbContext(opts);
    }
}
