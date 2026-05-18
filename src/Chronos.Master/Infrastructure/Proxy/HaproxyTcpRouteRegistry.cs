using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chronos.Master.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Chronos.Master.Infrastructure.Proxy;

/// <summary>
/// Хранит маршруты в JSON и генерирует фрагмент <c>chronos-tcp.cfg</c> для <c>include</c> в основном haproxy.cfg.
/// </summary>
public sealed class HaproxyTcpRouteRegistry : IHaproxyTcpRouteRegistry
{
    private readonly string _directory;
    private readonly ILogger<HaproxyTcpRouteRegistry> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _maxScan;
    private readonly int? _listenMin;
    private readonly int? _listenMax;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>HAProxy не принимает UTF-8 BOM в начале cfg (ошибка «unknown keyword»).</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public HaproxyTcpRouteRegistry(string directory, ILogger<HaproxyTcpRouteRegistry> logger, IConfiguration configuration)
    {
        _directory = directory;
        _logger = logger;
        _configuration = configuration;
        _maxScan = configuration.GetValue("CHRONOS_HAPROXY_TCP_PORT_SCAN_MAX", 500);
        if (_maxScan < 1)
            _maxScan = 500;

        var lo = ParsePort(configuration["CHRONOS_HAPROXY_LISTEN_PORT_MIN"]);
        var hi = ParsePort(configuration["CHRONOS_HAPROXY_LISTEN_PORT_MAX"]);
        if (lo is { } l && hi is { } h && l >= 1 && h >= 1 && l <= h && h <= 65535)
        {
            _listenMin = l;
            _listenMax = h;
        }
    }

    private static int? ParsePort(string? v) =>
        int.TryParse(v, out var p) && p is >= 1 and <= 65535 ? p : null;

    public string? DynamicDirectory => _directory;

    private string RoutesPath => Path.Combine(_directory, "chronos-tcp-routes.json");
    private string CfgPath => Path.Combine(_directory, "chronos-tcp.cfg");

    public async Task<IReadOnlyList<HaproxyTcpRouteDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var routes = await LoadRoutesAsync(cancellationToken).ConfigureAwait(false);
            return routes.Select(ToDto).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HaproxyTcpAddResult> TryAddAsync(AddHaproxyTcpRouteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BackendHost) ||
            request.BackendPort is < 1 or > 65535)
        {
            _logger.LogWarning("Invalid HAProxy TCP route request (host/port).");
            return HaproxyTcpAddResult.Fail("Invalid backendHost or backendPort (1–65535).");
        }

        var backendHost = request.BackendHost.Trim();
        var backendPort = request.BackendPort;
        var mistaken = MistakenDockerAgentHostPublishedPortReason(backendHost, backendPort);
        if (mistaken is not null)
        {
            _logger.LogWarning("HAProxy TCP add rejected: {Reason}", mistaken);
            return HaproxyTcpAddResult.Fail(mistaken);
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{backendHost}:{backendPort}"
            : request.Name.Trim();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var routes = await LoadRoutesAsync(cancellationToken).ConfigureAwait(false);
            var reserved = routes.Select(r => r.ListenPort).ToHashSet();

            int? listen = request.ListenPort;
            if (listen is { } fixedListen)
            {
                if (fixedListen is < 1 or > 65535 || reserved.Contains(fixedListen))
                {
                    _logger.LogWarning("Requested listen port {Port} is unavailable.", fixedListen);
                    return HaproxyTcpAddResult.Fail(
                        "Listen port is invalid, already reserved, or unavailable — try another listenPort.");
                }

                if (!TcpListenPortAllocator.IsListenInPublishedRange(fixedListen, _listenMin, _listenMax))
                {
                    _logger.LogWarning(
                        "Requested listen port {Port} is outside configured HAProxy publish range {Min}-{Max}.",
                        fixedListen,
                        _listenMin,
                        _listenMax);
                    return HaproxyTcpAddResult.Fail(
                        $"Listen port is outside the configured HAProxy publish range ({_listenMin}–{_listenMax}).");
                }

                if (_listenMin is null || _listenMax is null)
                {
                    if (!TcpListenPortAllocator.IsTcpPortFreeOnHost(fixedListen))
                    {
                        _logger.LogWarning("Requested listen port {Port} is unavailable (bind test on Master).", fixedListen);
                        return HaproxyTcpAddResult.Fail("Listen port is unavailable (bind test on Master).");
                    }
                }
            }
            else
            {
                listen = TcpListenPortAllocator.Allocate(backendPort, reserved, _maxScan, _listenMin, _listenMax);
                if (listen == null)
                {
                    _logger.LogWarning("No free TCP listen port found starting from {Preferred}.", backendPort);
                    return HaproxyTcpAddResult.Fail("No free listen port found — narrow reserved range or set listenPort explicitly.");
                }
            }

            var entity = new RouteEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                BackendHost = backendHost,
                BackendPort = backendPort,
                ListenPort = listen.Value,
                AgentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId.Trim(),
                ProjectName = string.IsNullOrWhiteSpace(request.ProjectName) ? null : request.ProjectName.Trim(),
                CreatedUtc = DateTimeOffset.UtcNow
            };

            routes.Add(entity);
            await SaveRoutesAndCfgAsync(routes, cancellationToken).ConfigureAwait(false);
            TryRunPostWriteHookAsync();

            _logger.LogInformation(
                "HAProxy TCP route added: listen *:{Listen} → {BackendHost}:{BackendPort} ({Name})",
                entity.ListenPort,
                entity.BackendHost,
                entity.BackendPort,
                entity.Name);

            return new HaproxyTcpAddResult(ToDto(entity), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Порт вида 5001 в compose — это публикация на хост; HAProxy в той же bridge-сети ходит на контейнерный порт (8080).
    /// Не подменяем молча — возвращаем текст ошибки.
    /// </summary>
    private string? MistakenDockerAgentHostPublishedPortReason(string backendHost, int backendPort)
    {
        var agentDns = _configuration["CHRONOS_HAPROXY_TCP_AGENT_SERVICE_DNS"]?.Trim() ?? "chronos-agent";
        if (!string.Equals(backendHost.Trim(), agentDns, StringComparison.OrdinalIgnoreCase))
            return null;

        var published = _configuration.GetValue("CHRONOS_HAPROXY_TCP_AGENT_HOST_PUBLISHED_PORT", 5001);
        var container = _configuration.GetValue("CHRONOS_HAPROXY_TCP_AGENT_CONTAINER_PORT", 8080);
        if (backendPort != published || published == container)
            return null;

        return
            $"Порт {published} у {agentDns} — это проброс на хост (compose «{published}:{container}»). " +
            $"HAProxy в той же Docker-сети подключается к контейнеру по внутреннему порту {container}. Укажите backend port {container}, не {published}.";
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var routes = await LoadRoutesAsync(cancellationToken).ConfigureAwait(false);
            var n = routes.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (n == 0)
                return false;

            await SaveRoutesAndCfgAsync(routes, cancellationToken).ConfigureAwait(false);
            TryRunPostWriteHookAsync();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ReadGeneratedCfgAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return File.Exists(CfgPath)
                ? await File.ReadAllTextAsync(CfgPath, cancellationToken).ConfigureAwait(false)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryRunPostWriteHookAsync()
    {
        var cmd = Environment.GetEnvironmentVariable("CHRONOS_HAPROXY_POST_WRITE_SHELL");
        if (string.IsNullOrWhiteSpace(cmd))
            return;

        _ = Task.Run(() =>
        {
            try
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo.FileName =
                    OperatingSystem.IsWindows()
                        ? "cmd.exe"
                        : "/bin/sh";
                p.StartInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
                p.StartInfo.ArgumentList.Add(cmd!.Trim());
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                p.WaitForExit(30_000);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CHRONOS_HAPROXY_POST_WRITE_SHELL failed.");
            }
        });
    }

    private async Task<List<RouteEntity>> LoadRoutesAsync(CancellationToken ct)
    {
        List<RouteEntity> routes;
        if (!File.Exists(RoutesPath))
            routes = new List<RouteEntity>();
        else
        {
            await using var fs = File.OpenRead(RoutesPath);
            var list = await JsonSerializer.DeserializeAsync<List<RouteEntity>>(fs, JsonOptions, ct).ConfigureAwait(false);
            routes = list ?? new List<RouteEntity>();
        }

        await NormalizeDockerAgentPublishedPortsAsync(routes, ct).ConfigureAwait(false);
        await RewriteTcpCfgIfUtf8BomAsync(routes, ct).ConfigureAwait(false);
        return routes;
    }

    private async Task RewriteTcpCfgIfUtf8BomAsync(List<RouteEntity> routes, CancellationToken ct)
    {
        if (!File.Exists(CfgPath))
            return;

        await using var fs = new FileStream(
            CfgPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            4096,
            FileOptions.Asynchronous);
        var head = new byte[3];
        var n = await fs.ReadAsync(head.AsMemory(0, 3), ct).ConfigureAwait(false);
        if (n < 3 || head[0] != 0xEF || head[1] != 0xBB || head[2] != 0xBF)
            return;

        _logger.LogWarning("chronos-tcp.cfg had UTF-8 BOM (HAProxy rejects it); rewriting without BOM.");
        await SaveRoutesAndCfgAsync(routes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Docker: порт вида 5001 на chronos-agent — это публикация на хост, в overlay-сети слушает 8080.
    /// Исправляем сохранённые маршруты и перезаписываем cfg.
    /// </summary>
    private async Task NormalizeDockerAgentPublishedPortsAsync(List<RouteEntity> routes, CancellationToken ct)
    {
        var changed = false;
        foreach (var r in routes)
        {
            var p = r.BackendPort;
            RemapDockerAgentPublishedPort(r.BackendHost, ref p);
            if (p != r.BackendPort)
            {
                r.BackendPort = p;
                changed = true;
            }
        }

        if (!changed)
            return;

        _logger.LogWarning(
            "HAProxy TCP routes: migrated backend port(s) from Docker host publish to container port (see CHRONOS_HAPROXY_TCP_AGENT_*).");
        await SaveRoutesAndCfgAsync(routes, ct).ConfigureAwait(false);
    }

    private void RemapDockerAgentPublishedPort(string backendHost, ref int backendPort)
    {
        var agentDns = _configuration["CHRONOS_HAPROXY_TCP_AGENT_SERVICE_DNS"]?.Trim() ?? "chronos-agent";
        if (!string.Equals(backendHost.Trim(), agentDns, StringComparison.OrdinalIgnoreCase))
            return;

        var published = _configuration.GetValue("CHRONOS_HAPROXY_TCP_AGENT_HOST_PUBLISHED_PORT", 5001);
        var container = _configuration.GetValue("CHRONOS_HAPROXY_TCP_AGENT_CONTAINER_PORT", 8080);
        if (backendPort == published && published != container)
        {
            _logger.LogInformation(
                "HAProxy TCP backend {Host}:{Published} is the host-mapped port for Docker service; using container port {Container}.",
                backendHost,
                published,
                container);
            backendPort = container;
        }
    }

    private async Task SaveRoutesAndCfgAsync(List<RouteEntity> routes, CancellationToken ct)
    {
        var tmpJson = RoutesPath + ".tmp";
        await File.WriteAllTextAsync(tmpJson, JsonSerializer.Serialize(routes, JsonOptions), Utf8NoBom, ct)
            .ConfigureAwait(false);
        File.Move(tmpJson, RoutesPath, overwrite: true);

        var cfg = GenerateCfg(routes);
        var tmpCfg = CfgPath + ".tmp";
        await File.WriteAllTextAsync(tmpCfg, cfg, Utf8NoBom, ct).ConfigureAwait(false);
        File.Move(tmpCfg, CfgPath, overwrite: true);
    }

    private static string GenerateCfg(List<RouteEntity> routes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Generated by Chronos.Master — TCP listens → agent backends (do not edit by hand).");
        sb.AppendLine("# Reload HAProxy after changes (e.g. docker compose exec haproxy kill -s HUP 1).");

        foreach (var r in routes.OrderBy(x => x.ListenPort))
        {
            var listenName = SanitizeListenName(r.Id);
            sb.AppendLine();
            sb.AppendLine($"listen chronos_tcp_{listenName}");
            sb.AppendLine($"  bind *:{r.ListenPort}");
            sb.AppendLine("  mode tcp");
            sb.AppendLine("  option tcplog");
            sb.AppendLine($"  server backend_{listenName} {EscapeHost(r.BackendHost)}:{r.BackendPort} check");
        }

        if (routes.Count == 0)
            sb.AppendLine("# (no routes)");

        return sb.ToString();
    }

    private static string EscapeHost(string host)
    {
        var t = host.Trim();
        if (t.StartsWith('['))
            return t;
        if (t.Contains(':', StringComparison.Ordinal))
            return $"[{t}]";
        return t;
    }

    private static string SanitizeListenName(string id)
    {
        var chars = id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars);
        return string.IsNullOrWhiteSpace(s) ? "route" : s;
    }

    private static HaproxyTcpRouteDto ToDto(RouteEntity r) =>
        new()
        {
            Id = r.Id,
            Name = r.Name,
            BackendHost = r.BackendHost,
            BackendPort = r.BackendPort,
            ListenPort = r.ListenPort,
            AgentId = r.AgentId,
            ProjectName = r.ProjectName,
            CreatedUtc = r.CreatedUtc
        };

    private sealed class RouteEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BackendHost { get; set; } = string.Empty;
        public int BackendPort { get; set; }
        public int ListenPort { get; set; }
        public string? AgentId { get; set; }
        public string? ProjectName { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
    }
}
