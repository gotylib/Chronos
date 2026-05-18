namespace Chronos.Master.Application.Abstractions;

/// <summary>
/// Реестр TCP-маршрутов на HAProxy (мастер): listen на порту мастера → бэкенд host:port агента.
/// Состояние в JSON + генерация <c>chronos-tcp.cfg</c> в каталоге <c>CHRONOS_HAPROXY_DYNAMIC_DIR</c>.
/// </summary>
public interface IHaproxyTcpRouteRegistry
{
    /// <summary>Каталог записи или null, если интеграция выключена.</summary>
    string? DynamicDirectory { get; }

    Task<IReadOnlyList<HaproxyTcpRouteDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Добавляет маршрут; при необходимости подбирает свободный listen-порт на мастере.</summary>
    Task<HaproxyTcpAddResult> TryAddAsync(AddHaproxyTcpRouteRequest request, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Сырой сгенерированный фрагмент cfg для отладки в UI.</summary>
    Task<string?> ReadGeneratedCfgAsync(CancellationToken cancellationToken = default);
}

public sealed class HaproxyTcpRouteDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BackendHost { get; init; } = string.Empty;
    public int BackendPort { get; init; }
    public int ListenPort { get; init; }
    public string? AgentId { get; init; }
    public string? ProjectName { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record HaproxyTcpAddResult(HaproxyTcpRouteDto? Route, string? Error)
{
    public bool Ok => Route != null;

    public static HaproxyTcpAddResult Fail(string message) => new(null, message);
}

public sealed class AddHaproxyTcpRouteRequest
{
    public string Name { get; set; } = string.Empty;
    public string BackendHost { get; set; } = string.Empty;
    public int BackendPort { get; set; }
    /// <summary>Явный порт на мастере; если null — подбирается (от backend порта вверх).</summary>
    public int? ListenPort { get; set; }
    public string? AgentId { get; set; }
    public string? ProjectName { get; set; }
}
