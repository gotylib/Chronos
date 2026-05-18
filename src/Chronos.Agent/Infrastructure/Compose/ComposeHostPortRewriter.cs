using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chronos.Core;
using Chronos.Core.Compose.Implementation;
using Microsoft.Extensions.Configuration;

namespace Chronos.Agent.Infrastructure.Compose;

/// <summary>
/// Перед <c>docker compose up</c> подбирает свободные host-порты, перезаписывает compose и сохраняет сопоставление в <c>.chronos/published-host-ports.json</c>.
/// </summary>
public static class ComposeHostPortRewriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task TryRewriteAsync(string composeFilePath, IConfiguration configuration, CancellationToken ct)
    {
        if (!configuration.GetValue("CHRONOS_AGENT_HOST_PORT_AUTO_RESOLVE", true))
            return;

        if (string.IsNullOrWhiteSpace(composeFilePath) || !File.Exists(composeFilePath))
            return;

        var maxScan = configuration.GetValue("CHRONOS_AGENT_HOST_PORT_MAX_SCAN", 5000);
        if (maxScan < 1)
            maxScan = 5000;

        string yaml;
        try
        {
            yaml = await File.ReadAllTextAsync(composeFilePath, ct).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        ComposeBuilder builder;
        try
        {
            builder = ComposeYamlParser.Parse(yaml);
        }
        catch
        {
            return;
        }

        var bindings = new List<PublishedHostPortBinding>();
        var reserved = new HashSet<int>();

        foreach (var kv in builder.Services)
        {
            var serviceName = kv.Key;
            var service = kv.Value;
            foreach (var pm in service.Ports)
            {
                var requested = pm.HostPort;
                int actual;

                if (!string.Equals(pm.Protocol, "tcp", StringComparison.OrdinalIgnoreCase))
                {
                    actual = AllocateNonTcpPort(requested, reserved, maxScan);
                    pm.HostPort = actual;
                }
                else
                {
                    actual = AllocateTcpHostPort(requested, reserved, maxScan);
                    pm.HostPort = actual;
                }

                if (actual != requested)
                {
                    Console.WriteLine(
                        $"[agent] Host port remap: service={serviceName} requested={requested} actual={actual} container={pm.ContainerPort}/{pm.Protocol}");
                }

                bindings.Add(new PublishedHostPortBinding
                {
                    ServiceName = serviceName,
                    ContainerPort = pm.ContainerPort,
                    RequestedHostPort = requested,
                    ActualHostPort = actual
                });
            }
        }

        await builder.SaveToFileAsync(composeFilePath, ct).ConfigureAwait(false);

        var projectRoot = Path.GetDirectoryName(Path.GetFullPath(composeFilePath))!;
        var chronosDir = Path.Combine(projectRoot, ".chronos");
        Directory.CreateDirectory(chronosDir);
        var jsonPath = Path.Combine(chronosDir, "published-host-ports.json");
        try
        {
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(bindings, JsonOptions), ct).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }

    private static int AllocateTcpHostPort(int preferred, HashSet<int> reserved, int maxScan)
    {
        if (!reserved.Contains(preferred) && IsTcpPortFree(preferred))
        {
            reserved.Add(preferred);
            return preferred;
        }

        for (var i = 1; i <= maxScan; i++)
        {
            var candidate = preferred + i;
            if (candidate > 65535)
                break;
            if (reserved.Contains(candidate))
                continue;
            if (IsTcpPortFree(candidate))
            {
                reserved.Add(candidate);
                return candidate;
            }
        }

        Console.WriteLine($"[agent] No free TCP host port found near {preferred}; leaving requested port in compose.");
        if (!reserved.Contains(preferred))
            reserved.Add(preferred);
        return preferred;
    }

    private static int AllocateNonTcpPort(int preferred, HashSet<int> reserved, int maxScan)
    {
        if (!reserved.Contains(preferred))
        {
            reserved.Add(preferred);
            return preferred;
        }

        for (var i = 1; i <= maxScan; i++)
        {
            var candidate = preferred + i;
            if (candidate > 65535)
                break;
            if (reserved.Contains(candidate))
                continue;
            reserved.Add(candidate);
            return candidate;
        }

        if (!reserved.Contains(preferred))
            reserved.Add(preferred);
        return preferred;
    }

    private static bool IsTcpPortFree(int port)
    {
        try
        {
            using var s = new Socket(SocketType.Stream, ProtocolType.Tcp);
            s.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
