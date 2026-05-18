using Chronos.Master.Application.Abstractions;

namespace Chronos.Master.Infrastructure.Proxy;

/// <summary>Если <c>CHRONOS_HAPROXY_DYNAMIC_DIR</c> не задан — TCP-маршруты не сохраняются.</summary>
public sealed class NoOpHaproxyTcpRouteRegistry : IHaproxyTcpRouteRegistry
{
    public string? DynamicDirectory => null;

    public Task<IReadOnlyList<HaproxyTcpRouteDto>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HaproxyTcpRouteDto>>(Array.Empty<HaproxyTcpRouteDto>());

    public Task<HaproxyTcpAddResult> TryAddAsync(AddHaproxyTcpRouteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(HaproxyTcpAddResult.Fail("HAProxy TCP routes are disabled (CHRONOS_HAPROXY_DYNAMIC_DIR is not set)."));

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<string?> ReadGeneratedCfgAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
