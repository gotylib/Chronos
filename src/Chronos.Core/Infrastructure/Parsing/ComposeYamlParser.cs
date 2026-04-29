using System.Globalization;
using System.Text.RegularExpressions;
using Chronos.Core.Compose.Implementation;
using YamlDotNet.RepresentationModel;

// Разбор существующего docker-compose.yml в Fluent ComposeBuilder (best-effort, не все поля compose).
namespace Chronos.Core;

public static class ComposeYamlParser
{
    public static ComposeBuilder Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Invalid docker-compose yaml: root mapping not found.");

        var version = GetScalar(root, "version") ?? "3.8";
        var builder = new ComposeBuilder().WithVersion(version);

        // networks
        if (TryGetMapping(root, "networks", out var networksNode))
        {
            foreach (var entry in networksNode.Children)
            {
                var name = (entry.Key as YamlScalarNode)?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (entry.Value is YamlMappingNode networkMap)
                {
                    var driver = GetScalar(networkMap, "driver") ?? "bridge";
                    builder.AddNetwork(new Network { Name = name, Driver = driver });
                }
                else
                {
                    builder.AddNetwork(new Network { Name = name, Driver = "bridge" });
                }
            }
        }

        // volumes
        if (TryGetMapping(root, "volumes", out var volumesNode))
        {
            foreach (var entry in volumesNode.Children)
            {
                var name = (entry.Key as YamlScalarNode)?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var driver = "local";
                if (entry.Value is YamlMappingNode volMap)
                    driver = GetScalar(volMap, "driver") ?? "local";

                builder.AddVolume(new Volume { Name = name, Driver = driver });
            }
        }

        // secrets
        if (TryGetMapping(root, "secrets", out var secretsNode))
        {
            foreach (var entry in secretsNode.Children)
            {
                var name = (entry.Key as YamlScalarNode)?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (entry.Value is YamlMappingNode secMap)
                {
                    var file = GetScalar(secMap, "file");
                    if (!string.IsNullOrWhiteSpace(file))
                        builder.AddSecret(new Secret { Name = name, File = file });
                }
            }
        }

        // configs
        if (TryGetMapping(root, "configs", out var configsNode))
        {
            foreach (var entry in configsNode.Children)
            {
                var name = (entry.Key as YamlScalarNode)?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (entry.Value is YamlMappingNode cfgMap)
                {
                    var file = GetScalar(cfgMap, "file");
                    if (!string.IsNullOrWhiteSpace(file))
                        builder.AddConfig(new Config { Name = name, File = file });
                }
            }
        }

        // services
        if (TryGetMapping(root, "services", out var servicesNode))
        {
            foreach (var entry in servicesNode.Children)
            {
                var serviceName = (entry.Key as YamlScalarNode)?.Value;
                if (string.IsNullOrWhiteSpace(serviceName))
                    continue;

                if (entry.Value is not YamlMappingNode svcMap)
                    continue;

                var svc = new Service { Name = serviceName };

                var image = GetScalar(svcMap, "image");
                if (!string.IsNullOrWhiteSpace(image))
                    svc.Image = image;

                // build
                if (TryGetNode(svcMap, "build", out var buildNode))
                {
                    if (buildNode is YamlScalarNode buildScalar)
                    {
                        svc.BuildContext = buildScalar.Value;
                    }
                    else if (buildNode is YamlMappingNode buildMap)
                    {
                        svc.BuildContext = GetScalar(buildMap, "context");
                        svc.Dockerfile = GetScalar(buildMap, "dockerfile");
                    }
                }

                svc.ContainerName = GetScalar(svcMap, "container_name");

                // command
                if (TryGetNode(svcMap, "command", out var commandNode))
                {
                    if (commandNode is YamlScalarNode cmdScalar && !string.IsNullOrWhiteSpace(cmdScalar.Value))
                        svc.Command.Add(cmdScalar.Value);
                    else if (commandNode is YamlSequenceNode cmdSeq)
                    {
                        foreach (var item in cmdSeq)
                        {
                            if (item is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value))
                                svc.Command.Add(scalar.Value);
                        }
                    }
                }

                // ports
                if (TryGetSequence(svcMap, "ports", out var portsSeq) && portsSeq is not null && portsSeq.Any())
                {
                        foreach (var portNode in portsSeq)
                        {
                            if (portNode is not YamlScalarNode portScalar ||
                                string.IsNullOrWhiteSpace(portScalar.Value))
                                continue;

                            var pm = ParsePort(portScalar.Value);
                            if (pm != null)
                                svc.Ports.Add(pm);
                        }
                }

                // environment
                if (TryGetMapping(svcMap, "environment", out var envNode) && envNode is not null && envNode.Any())
                {
                    foreach (var envEntry in envNode.Children)
                    {
                        var key = (envEntry.Key as YamlScalarNode)?.Value;
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        var val = (envEntry.Value as YamlScalarNode)?.Value ?? "";
                        svc.Environment[key] = val;
                    }
                }

                // labels
                if (TryGetMapping(svcMap, "labels", out var labelsNode) && labelsNode is not null && labelsNode.Any())
                {
                    foreach (var lblEntry in labelsNode.Children )
                    {
                        var key = (lblEntry.Key as YamlScalarNode)?.Value;
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        var val = (lblEntry.Value as YamlScalarNode)?.Value ?? "";
                        svc.Labels[key] = val;
                    }
                }

                // depends_on
                if (TryGetSequence(svcMap, "depends_on", out var dependsSeq) && dependsSeq is not null && dependsSeq.Any())
                {
                    foreach (var depNode in dependsSeq)
                    {
                        if (depNode is YamlScalarNode d && !string.IsNullOrWhiteSpace(d.Value))
                            svc.DependsOn.Add(d.Value);
                    }
                }
                else if (TryGetMapping(svcMap, "depends_on", out var dependsMap) && dependsMap is not null && dependsMap.Any())
                {
                    foreach (var depEntry in dependsMap.Children)
                    {
                        var depName = (depEntry.Key as YamlScalarNode)?.Value;
                        if (!string.IsNullOrWhiteSpace(depName))
                            svc.DependsOn.Add(depName);
                    }
                }

                // networks
                if (TryGetSequence(svcMap, "networks", out var netSeq))
                {
                    foreach (var netNode in netSeq)
                    {
                        if (netNode is YamlScalarNode n && !string.IsNullOrWhiteSpace(n.Value))
                            svc.Networks.Add(n.Value);
                    }
                }
                else if (TryGetMapping(svcMap, "networks", out var netMap))
                {
                    foreach (var netEntry in netMap.Children)
                    {
                        var netName = (netEntry.Key as YamlScalarNode)?.Value;
                        if (!string.IsNullOrWhiteSpace(netName))
                            svc.Networks.Add(netName);
                    }
                }

                // volumes
                if (TryGetSequence(svcMap, "volumes", out var volSeq))
                {
                    foreach (var volNode in volSeq)
                    {
                        if (volNode is YamlScalarNode v && !string.IsNullOrWhiteSpace(v.Value))
                            svc.Volumes.Add(v.Value);
                    }
                }

                // secrets
                if (TryGetSequence(svcMap, "secrets", out var secSeq))
                {
                    foreach (var secNode in secSeq)
                    {
                        if (secNode is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value))
                            svc.Secrets.Add(s.Value);
                    }
                }

                svc.RestartPolicy = GetScalar(svcMap, "restart");

                // healthcheck
                if (TryGetMapping(svcMap, "healthcheck", out var healthMap))
                {
                    var intervalSeconds = ParseSeconds(GetScalar(healthMap, "interval")) ?? 30;
                    var timeoutSeconds = ParseSeconds(GetScalar(healthMap, "timeout")) ?? 10;
                    var retries = ParseInt(GetScalar(healthMap, "retries")) ?? 3;

                    var testNode = healthMap.Children.FirstOrDefault(c => c.Key is YamlScalarNode k && k.Value == "test").Value;
                    if (testNode != null)
                    {
                        var testCommand = ParseHealthcheckTestCommand(testNode);
                        svc.HealthCheck = new HealthCheck
                        {
                            TestCommand = testCommand,
                            IntervalSeconds = intervalSeconds,
                            TimeoutSeconds = timeoutSeconds,
                            Retries = retries
                        };
                    }
                }

                // deploy
                if (TryGetMapping(svcMap, "deploy", out var deployMap))
                {
                    var replicas = ParseInt(GetScalar(deployMap, "replicas"));
                    if (replicas.HasValue || deployMap.Children.Any(c => (c.Key as YamlScalarNode)?.Value == "resources"))
                    {
                        var deploy = new DeployConfig { Replicas = replicas };

                        if (TryGetMapping(deployMap, "resources", out var resourcesMap) &&
                            TryGetMapping(resourcesMap, "limits", out var limitsMap))
                        {
                            decimal? cpus = null;
                            int? memoryMb = null;

                            var cpusStr = GetScalar(limitsMap, "cpus");
                            if (cpusStr != null && decimal.TryParse(cpusStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var cp))
                                cpus = cp;

                            var memStr = GetScalar(limitsMap, "memory");
                            memoryMb = ParseMemoryMb(memStr);

                            deploy.Resources = new ResourceLimits { Cpus = cpus, MemoryMb = memoryMb };
                        }

                        svc.Deploy = deploy;
                    }
                }

                // extra_hosts
                if (TryGetSequence(svcMap, "extra_hosts", out var extraHostsSeq))
                {
                    foreach (var exNode in extraHostsSeq)
                    {
                        if (exNode is not YamlScalarNode exScalar || string.IsNullOrWhiteSpace(exScalar.Value))
                            continue;

                        var parts = exScalar.Value.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                            svc.ExtraHosts[parts[0]] = parts[1];
                    }
                }

                // cap_add
                if (TryGetSequence(svcMap, "cap_add", out var capSeq))
                {
                    foreach (var capNode in capSeq)
                    {
                        if (capNode is YamlScalarNode c && !string.IsNullOrWhiteSpace(c.Value))
                            svc.Capabilities.Add(c.Value);
                    }
                }

                svc.User = GetScalar(svcMap, "user");
                svc.WorkingDir = GetScalar(svcMap, "working_dir");

                // logging
                if (TryGetMapping(svcMap, "logging", out var loggingMap))
                {
                    svc.LoggingDriver = GetScalar(loggingMap, "driver");
                    if (TryGetMapping(loggingMap, "options", out var optionsMap))
                    {
                        foreach (var optEntry in optionsMap.Children)
                        {
                            var key = (optEntry.Key as YamlScalarNode)?.Value;
                            if (string.IsNullOrWhiteSpace(key))
                                continue;

                            var val = (optEntry.Value as YamlScalarNode)?.Value ?? "";
                            svc.LoggingOptions[key] = val;
                        }
                    }
                }

                // init / privileged
                var initVal = ParseBool(GetScalar(svcMap, "init"));
                if (initVal.HasValue)
                    svc.Init = initVal.Value;

                var privVal = ParseBool(GetScalar(svcMap, "privileged"));
                if (privVal.HasValue)
                    svc.Privileged = privVal.Value;

                builder.AddService(svc);
            }
        }

        return builder;
    }
    
    private static bool TryGetNode(YamlMappingNode node, string key, out YamlNode? value)
    {
        value = null;
        foreach (var kv in node.Children)
        {
            if (kv.Key is YamlScalarNode ks && string.Equals(ks.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetSequence(YamlMappingNode node, string key, out YamlSequenceNode? seq)
    {
        seq = null;
        if (!TryGetNode(node, key, out var v))
            return false;
        if (v is YamlSequenceNode s)
        {
            seq = s;
            return true;
        }
        return false;
    }

    private static bool TryGetMapping(YamlMappingNode node, string key, out YamlMappingNode? map)
    {
        map = null;
        if (!TryGetNode(node, key, out var v))
            return false;
        if (v is YamlMappingNode m)
        {
            map = m;
            return true;
        }
        return false;
    }

    private static string? GetScalar(YamlMappingNode map, string key)
    {
        if (!TryGetNode(map, key, out var node))
            return null;

        return (node as YamlScalarNode)?.Value;
    }

    private static int? ParseInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    private static bool? ParseBool(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (bool.TryParse(s.Trim(), out var v))
            return v;
        return null;
    }

    private static int? ParseSeconds(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var trimmed = s.Trim();
        trimmed = trimmed.TrimEnd('s', 'S');
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    private static int? ParseMemoryMb(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var t = s.Trim();
        // common: 1024M / 1G / 512K
        if (t.EndsWith("G", StringComparison.OrdinalIgnoreCase))
        {
            var num = t.Substring(0, t.Length - 1);
            if (decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var g))
                return (int)Math.Round(g * 1024m);
        }

        if (t.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            var num = t.Substring(0, t.Length - 1);
            if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
                return m;
            if (decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var md))
                return (int)Math.Round(md);
        }

        if (t.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            var num = t.Substring(0, t.Length - 1);
            if (decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var k))
                return (int)Math.Round(k / 1024m);
        }

        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
            return plain;

        return null;
    }

    private static string ParseHealthcheckTestCommand(YamlNode testNode)
    {
        if (testNode is YamlScalarNode scalar)
            return scalar.Value ?? "";

        if (testNode is not YamlSequenceNode seq || seq.Children.Count == 0)
            return "";

        // docker-compose: ["CMD-SHELL", "curl ... || exit 1"]
        var first = (seq.Children[0] as YamlScalarNode)?.Value;
        if (string.Equals(first, "CMD-SHELL", StringComparison.OrdinalIgnoreCase) && seq.Children.Count >= 2)
            return (seq.Children[1] as YamlScalarNode)?.Value ?? "";

        // Fallback: join all scalars
        var parts = seq.Children
            .OfType<YamlScalarNode>()
            .Select(n => n.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();
        return string.Join(" ", parts);
    }

    private static PortMapping? ParsePort(string port)
    {
        port = port.Trim();
        if (string.IsNullOrWhiteSpace(port))
            return null;

        var proto = "tcp";
        var protoSplit = port.Split('/', 2, StringSplitOptions.TrimEntries);
        var portPart = protoSplit[0];
        if (protoSplit.Length == 2 && !string.IsNullOrWhiteSpace(protoSplit[1]))
            proto = protoSplit[1];

        var seg = portPart.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (seg.Length == 2)
        {
            if (!int.TryParse(seg[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostPort))
                return null;
            if (!int.TryParse(seg[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var containerPort))
                return null;

            return new PortMapping
            {
                Host = "0.0.0.0",
                HostPort = hostPort,
                ContainerPort = containerPort,
                Protocol = proto
            };
        }

        if (seg.Length == 3)
        {
            var host = seg[0];
            if (!int.TryParse(seg[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostPort))
                return null;
            if (!int.TryParse(seg[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var containerPort))
                return null;

            return new PortMapping
            {
                Host = host,
                HostPort = hostPort,
                ContainerPort = containerPort,
                Protocol = proto
            };
        }

        // "80" (no mapping) or other complex forms are not supported yet.
        return null;
    }
}

