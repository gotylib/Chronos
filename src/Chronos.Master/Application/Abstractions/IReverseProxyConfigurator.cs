namespace Chronos.Master.Application.Abstractions;

/// <summary>Registers HTTP routes with the edge reverse proxy (Traefik file provider or future NPM).</summary>
public interface IReverseProxyConfigurator
{
    /// <summary>Writes or updates a Traefik v3 file-provider fragment for routing to backend URL(s).</summary>
    Task UpsertHttpRouteAsync(
        string routeName,
        string rule,
        IReadOnlyList<string> backendUrls,
        CancellationToken cancellationToken = default);

    Task RemoveRouteAsync(string routeName, CancellationToken cancellationToken = default);
}
