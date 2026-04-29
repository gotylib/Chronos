using Chronos.Master.Application.Abstractions;

namespace Chronos.Master.Infrastructure.Proxy;

/// <summary>Заглушка, если CHRONOS_TRAEFIK_DYNAMIC_DIR не задан — маршруты edge не пишутся.</summary>
public sealed class NoOpReverseProxyConfigurator : IReverseProxyConfigurator
{
    public Task RemoveRouteAsync(string routeName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task UpsertHttpRouteAsync(
        string routeName,
        string rule,
        IReadOnlyList<string> backendUrls,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
