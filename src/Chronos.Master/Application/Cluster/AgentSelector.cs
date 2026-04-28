using Chronos.Master;

namespace Chronos.Master.Application.Cluster;

public static class AgentSelector
{
    public static AgentInfo? SelectBestAgent(IReadOnlyList<AgentInfo> agents, string? preferredLocation)
    {
        var candidates = agents.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(preferredLocation))
        {
            var filtered = candidates
                .Where(a => string.Equals(a.Location, preferredLocation, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count > 0)
                candidates = filtered;
        }

        return candidates
            .OrderBy(a => (a.CpuPercent * 0.5) + (a.MemoryPercent * 0.3) + (a.DiskPercent * 0.2))
            .FirstOrDefault();
    }
}
