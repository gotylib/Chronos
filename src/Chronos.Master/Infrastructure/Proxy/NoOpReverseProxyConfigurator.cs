using Chronos.Master.Application.Abstractions;

namespace Chronos.Master.Infrastructure.Proxy;

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
