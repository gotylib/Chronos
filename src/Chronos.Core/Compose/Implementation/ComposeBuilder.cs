using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chronos.Core;
using Chronos.Core.Compose.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// Реализация Fluent compose: словари сервисов/сетей/volumes → YAML, LocalTester, HttpDeployAgent и кластер.
namespace Chronos.Core.Compose.Implementation;

/// <summary>
/// Fluent API для сборки docker-compose: YAML, валидация, локальный запуск и публикация на Chronos.Agent / кластер.
/// </summary>
public sealed class ComposeBuilder : IComposeBuilder
{
    private string _version = "3.8";
    private readonly Dictionary<string, Service> _services = new();
    private readonly Dictionary<string, Network> _networks = new();
    private readonly Dictionary<string, Volume> _volumes = new();
    private readonly Dictionary<string, Secret> _secrets = new();
    private readonly Dictionary<string, Config> _configs = new();
    private readonly Dictionary<string, object> _xFields = new();
    private readonly List<DeployArtifact> _deployArtifacts = new();

    private ReplicaPolicy? _replicaPolicy;

    // local runtime settings (NOT used by agent remote endpoints)
    private string _composeFilePath = "docker-compose.yml";
    private string _projectName = "chronos";
    /// <summary><c>auto</c> = detect <c>docker compose</c> vs <c>docker-compose</c> on this machine.</summary>
    private string _dockerComposeExecutable = "auto";

    public ComposeBuilder()
    {
    }

    /// <summary>Shallow snapshot for <see cref="Build"/>.</summary>
    internal ComposeBuilder(ComposeBuilder other)
    {
        _version = other._version;
        _composeFilePath = other._composeFilePath;
        _projectName = other._projectName;
        _dockerComposeExecutable = other._dockerComposeExecutable;

        foreach (var kv in other._services)
            _services[kv.Key] = kv.Value;
        foreach (var kv in other._networks)
            _networks[kv.Key] = kv.Value;
        foreach (var kv in other._volumes)
            _volumes[kv.Key] = kv.Value;
        foreach (var kv in other._secrets)
            _secrets[kv.Key] = kv.Value;
        foreach (var kv in other._configs)
            _configs[kv.Key] = kv.Value;
        foreach (var kv in other._xFields)
            _xFields[kv.Key] = kv.Value;
        _deployArtifacts.AddRange(other._deployArtifacts);
        if (other._replicaPolicy is not null)
        {
            _replicaPolicy = new ReplicaPolicy
            {
                Count = other._replicaPolicy.Count,
                PortOffsetPerReplica = other._replicaPolicy.PortOffsetPerReplica
            };
            foreach (var v in other._replicaPolicy.SharedNamedVolumes)
                _replicaPolicy.SharedNamedVolumes.Add(v);
        }
    }

    /// <summary>Версия спецификации compose (поле <c>version</c> в YAML).</summary>
    public string ComposeSpecificationVersion => _version;

    /// <summary>Относительное имя файла compose для локальных операций.</summary>
    public string ComposeFileRelativePath => _composeFilePath;

    /// <summary>Имя проекта Docker Compose (<c>-p</c>).</summary>
    public string ProjectName => _projectName;

    /// <summary>
    /// Настроенное значение CLI compose (<c>auto</c> или явная команда); для резолва на машине см. <see cref="DockerComposeExecutableResolver"/>.
    /// </summary>
    public string DockerComposeExecutableConfiguration => _dockerComposeExecutable;

    /// <summary>Сервисы по имени; сам словарь не заменить снаружи, изменение значений влияет на модель.</summary>
    public IReadOnlyDictionary<string, Service> Services => _services;

    /// <summary>Сети по имени.</summary>
    public IReadOnlyDictionary<string, Network> Networks => _networks;

    /// <summary>Именованные тома верхнего уровня.</summary>
    public IReadOnlyDictionary<string, Volume> Volumes => _volumes;

    /// <summary>Секреты compose.</summary>
    public IReadOnlyDictionary<string, Secret> Secrets => _secrets;

    /// <summary>Configs compose.</summary>
    public IReadOnlyDictionary<string, Config> Configs => _configs;

    /// <summary>Дополнительные корневые поля (<c>x-*</c> и пр.).</summary>
    public IReadOnlyDictionary<string, object> ExtensionFields => _xFields;

    /// <summary>
    /// Независимая копия политики репликации; изменения возвращённого объекта не меняют билдер.
    /// </summary>
    public ReplicaPolicy? ReplicaPolicySnapshot => CloneReplicaPolicy(_replicaPolicy);

    private static ReplicaPolicy? CloneReplicaPolicy(ReplicaPolicy? source)
    {
        if (source is null)
            return null;

        var r = new ReplicaPolicy
        {
            Count = source.Count,
            PortOffsetPerReplica = source.PortOffsetPerReplica
        };
        foreach (var v in source.SharedNamedVolumes)
            r.SharedNamedVolumes.Add(v);
        return r;
    }

    /// <summary>Declare replica intent for Chronos Master (YAML extension <c>x-chronos-replicas</c>).</summary>
    public ComposeBuilder WithReplicaPolicy(ReplicaPolicy policy)
    {
        _replicaPolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    public ComposeBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }

    public ComposeBuilder WithComposeFilePath(string composeFilePath)
    {
        _composeFilePath = composeFilePath ?? throw new ArgumentNullException(nameof(composeFilePath));
        return this;
    }

    public ComposeBuilder WithProjectName(string projectName)
    {
        _projectName = projectName ?? throw new ArgumentNullException(nameof(projectName));
        return this;
    }

    /// <param name="dockerComposeExecutable">Concrete CLI (e.g. <c>docker compose</c>, <c>docker-compose</c>) or <c>auto</c> to probe at runtime.</param>
    public ComposeBuilder WithDockerComposeExecutable(string dockerComposeExecutable)
    {
        _dockerComposeExecutable = dockerComposeExecutable ?? throw new ArgumentNullException(nameof(dockerComposeExecutable));
        return this;
    }

    /// <summary>Forces Docker Compose V2 plugin spelling (<c>docker compose</c>).</summary>
    public ComposeBuilder WithDockerComposeV2()
        => WithDockerComposeExecutable("docker compose");

    private string ResolvedDockerComposeExecutable()
        => DockerComposeExecutableResolver.Resolve(_dockerComposeExecutable);

    public ComposeBuilder AddService(Service service)
    {
        _services[service.Name] = service;
        return this;
    }

    public ComposeBuilder AddService(Action<IServiceBuilder> configure)
    {
        var builder = new ServiceBuilder();
        configure(builder);
        var service = builder.Build();
        _services[service.Name] = service;
        return this;
    }

    public ComposeBuilder AddNetwork(Network network)
    {
        _networks[network.Name] = network;
        return this;
    }

    public ComposeBuilder AddNetwork(string name, string driver = "bridge")
    {
        _networks[name] = new Network { Name = name, Driver = driver };
        return this;
    }

    public ComposeBuilder AddVolume(Volume volume)
    {
        _volumes[volume.Name] = volume;
        return this;
    }

    public ComposeBuilder AddVolume(string name, string driver = "local")
    {
        _volumes[name] = new Volume { Name = name, Driver = driver };
        return this;
    }

    public ComposeBuilder AddSecret(Secret secret)
    {
        _secrets[secret.Name] = secret;
        return this;
    }

    public ComposeBuilder AddConfig(Config config)
    {
        _configs[config.Name] = config;
        return this;
    }

    public ComposeBuilder AddExtension(string key, object value)
    {
        _xFields[key] = value;
        return this;
    }

    /// <summary>Ship a file next to compose on the agent (packed into a tar upload on publish).</summary>
    public ComposeBuilder AddDeployArtifactFromFile(string deployRelativePath, string sourceFilePath, int? unixFileMode = null)
    {
        if (string.IsNullOrWhiteSpace(deployRelativePath)) throw new ArgumentException("Relative path is required.", nameof(deployRelativePath));
        if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentException("Source path is required.", nameof(sourceFilePath));

        _deployArtifacts.Add(new DeployArtifact
        {
            RelativePath = deployRelativePath,
            SourceKind = ArtifactSourceKind.File,
            SourcePathOnDisk = Path.GetFullPath(sourceFilePath),
            UnixFileMode = unixFileMode
        });
        return this;
    }

    /// <summary>Ship a directory tree under <paramref name="deployRelativePath"/>.</summary>
    public ComposeBuilder AddDeployArtifactFromDirectory(string deployRelativePath, string sourceDirectoryPath, int? unixFileMode = null)
    {
        if (string.IsNullOrWhiteSpace(deployRelativePath)) throw new ArgumentException("Relative path is required.", nameof(deployRelativePath));
        if (string.IsNullOrWhiteSpace(sourceDirectoryPath)) throw new ArgumentException("Source path is required.", nameof(sourceDirectoryPath));

        _deployArtifacts.Add(new DeployArtifact
        {
            RelativePath = deployRelativePath,
            SourceKind = ArtifactSourceKind.Directory,
            SourcePathOnDisk = Path.GetFullPath(sourceDirectoryPath),
            UnixFileMode = unixFileMode
        });
        return this;
    }

    public ProjectManifest BuildManifest()
    {
        var manifest = new ProjectManifest();
        foreach (var svc in _services.Values)
        {
            if (svc.Checks.Count == 0 && svc.Jobs.Count == 0 && svc.CodeTests.Count == 0 && svc.CodeJobs.Count == 0)
                continue;

            manifest.Services[svc.Name] = new ManifestServiceSection
            {
                Tests = svc.Checks.ToList(),
                Jobs = svc.Jobs.ToList(),
                CodeTests = svc.CodeTests.ToList(),
                CodeJobs = svc.CodeJobs.ToList()
            };
        }

        return manifest;
    }

    public string SerializeManifestJson()
    {
        var manifest = BuildManifest();
        return JsonSerializer.Serialize(manifest, ManifestJson.Options);
    }

    public bool HasManifestPayload() =>
        _services.Values.Any(s => s.Checks.Count > 0 || s.Jobs.Count > 0 || s.CodeTests.Count > 0 || s.CodeJobs.Count > 0);

    public IReadOnlyList<DeployArtifact> GetDeployArtifacts() => _deployArtifacts;

    /// <inheritdoc cref="IComposeBuilder.Build"/>
    public BuiltCompose Build() => new(new ComposeBuilder(this));

    /// <summary>Граф зависимостей и связей сервис↔сеть↔volume для UI.</summary>
    public ComposeGraphDto DescribeGraph()
    {
        var nodes = new List<ComposeGraphNode>();
        var edges = new List<ComposeGraphEdge>();

        foreach (var (netName, _) in _networks)
            nodes.Add(new ComposeGraphNode
            {
                Id = $"net:{netName}",
                Label = netName,
                Kind = "network",
                Subtitle = "network"
            });

        foreach (var (volName, _) in _volumes)
            nodes.Add(new ComposeGraphNode
            {
                Id = $"vol:{volName}",
                Label = volName,
                Kind = "volume",
                Subtitle = "named volume"
            });

        foreach (var (svcName, svc) in _services)
        {
            nodes.Add(new ComposeGraphNode
            {
                Id = $"svc:{svcName}",
                Label = svcName,
                Kind = "service",
                Subtitle = svc.Image ?? svc.BuildContext ?? "service"
            });

            foreach (var dep in svc.DependsOn)
                edges.Add(new ComposeGraphEdge
                {
                    From = $"svc:{svcName}",
                    To = $"svc:{dep}",
                    Kind = "depends_on"
                });

            foreach (var net in svc.Networks)
                edges.Add(new ComposeGraphEdge
                {
                    From = $"svc:{svcName}",
                    To = $"net:{net}",
                    Kind = "network"
                });

            foreach (var volRaw in svc.Volumes)
            {
                var named = NamedVolumeSource(volRaw);
                if (!string.IsNullOrEmpty(named))
                    edges.Add(new ComposeGraphEdge
                    {
                        From = $"svc:{svcName}",
                        To = $"vol:{named}",
                        Kind = "volume"
                    });
            }
        }

        var idSet = new HashSet<string>(nodes.Select(n => n.Id));
        foreach (var e in edges)
        {
            foreach (var id in new[] { e.From, e.To })
            {
                if (idSet.Add(id))
                {
                    var kind = id.StartsWith("svc:", StringComparison.Ordinal) ? "service"
                        : id.StartsWith("net:", StringComparison.Ordinal) ? "network"
                        : id.StartsWith("vol:", StringComparison.Ordinal) ? "volume"
                        : "other";
                    var label = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
                    nodes.Add(new ComposeGraphNode
                    {
                        Id = id,
                        Label = label,
                        Kind = kind,
                        Subtitle = "referenced"
                    });
                }
            }
        }

        return new ComposeGraphDto { Nodes = nodes, Edges = edges };
    }

    /// <summary>Named volume key from short syntax <c>volname:/path</c>; bind mounts return null.</summary>
    private static string? NamedVolumeSource(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var idx = raw.IndexOf(':');
        if (idx <= 0)
            return null;
        var src = raw[..idx].Trim();
        var dst = raw[(idx + 1)..].Trim();
        if (dst.Length == 0 || src.StartsWith('/') || src.StartsWith('.'))
            return null;
        return src;
    }

    private Dictionary<string, List<DeclarativeCheck>> BuildStartupChecksMap()
    {
        var d = new Dictionary<string, List<DeclarativeCheck>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _services.Values)
        {
            var list = s.Checks.Where(t => t.OnStartup).ToList();
            if (list.Count > 0)
                d[s.Name] = list;
        }

        return d;
    }

    private void ApplyChecksToOptions(TestOptions options)
    {
        var map = BuildStartupChecksMap();
        if (map.Count > 0)
            options.DeclarativeChecksByService = map;

        var codeMap = BuildStartupCodeTestsMap();
        if (codeMap.Count > 0)
            options.CodeTestsByService = codeMap;
    }

    private Dictionary<string, List<CodeTestEntry>> BuildStartupCodeTestsMap()
    {
        var d = new Dictionary<string, List<CodeTestEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _services.Values)
        {
            var list = s.CodeTests.Where(c => c.OnStartup)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count > 0)
                d[s.Name] = list;
        }

        return d;
    }

    private ComposeDefinition BuildDefinition()
    {
        return new ComposeDefinition
        {
            Version = _version,
            Services = new Dictionary<string, Service>(_services),
            Networks = new Dictionary<string, Network>(_networks),
            Volumes = new Dictionary<string, Volume>(_volumes),
            Secrets = new Dictionary<string, Secret>(_secrets),
            Configs = new Dictionary<string, Config>(_configs),
            ExtensionFields = new Dictionary<string, object>(_xFields)
        };
    }

    // --- Генерация docker-compose.yml через YamlDotNet ---

    public string GenerateYaml()
    {
        var def = BuildDefinition();

        var servicesObject = new Dictionary<string, object>();
        foreach (var kv in def.Services)
            servicesObject[kv.Key] = ToComposeServiceObject(kv.Value);

        var root = new Dictionary<string, object?>
        {
            ["version"] = def.Version,
            ["services"] = servicesObject
        };

        if (def.Networks.Count > 0)
            root["networks"] = def.Networks.ToDictionary(n => n.Key, n => new Dictionary<string, object?>
            {
                ["driver"] = n.Value.Driver
            });

        if (def.Volumes.Count > 0)
            root["volumes"] = def.Volumes.ToDictionary(v => v.Key, v => new Dictionary<string, object?>
            {
                ["driver"] = v.Value.Driver
            });

        if (def.Secrets.Count > 0)
            root["secrets"] = def.Secrets.ToDictionary(s => s.Key, s => new Dictionary<string, object?>
            {
                ["file"] = s.Value.File
            });

        if (def.Configs.Count > 0)
            root["configs"] = def.Configs.ToDictionary(c => c.Key, c => new Dictionary<string, object?>
            {
                ["file"] = c.Value.File
            });

        foreach (var ext in def.ExtensionFields)
            root[ext.Key] = ext.Value;

        if (_replicaPolicy is not null && _replicaPolicy.Count > 1)
            root["x-chronos-replicas"] = JsonSerializer.SerializeToElement(_replicaPolicy, ManifestJson.Options);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        var yaml = serializer.Serialize(root);

        // docker-compose validates "version" as a string.
        yaml = Regex.Replace(
            yaml,
            @"^version:\s*([0-9]+(?:\.[0-9]+)?)\s*$",
            "version: \"$1\"",
            RegexOptions.Multiline);

        return yaml;
    }

    public Task SaveToFileAsync(string path, CancellationToken cancellationToken = default)
        => File.WriteAllTextAsync(path, GenerateYaml(), Encoding.UTF8, cancellationToken);

    public Task<ValidationResult> ValidateAsync(ComposeValidatorOptions? options = null, CancellationToken cancellationToken = default)
    {
        var definition = BuildDefinition();
        return ComposeValidator.ValidateAsync(definition, options, cancellationToken);
    }

    // --- Локальный compose up/down и прогон тестов (LocalTester) ---

    public async Task<TestResult> StartAsync(
        string composeFilePath,
        string projectName,
        string dockerComposeExecutable,
        TestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Start] Writing compose to '{composeFilePath}'...");
        var validation = await ValidateAsync(options: null, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(e => $"{e.Service}: {e.Message}").ToList();
            Console.WriteLine("[Start] Validation failed.");
            return new TestResult { Success = false, Errors = errors };
        }

        await SaveToFileAsync(composeFilePath, cancellationToken);

        options ??= new TestOptions();
        options.ProjectName = projectName;
        options.DockerComposeExecutable = DockerComposeExecutableResolver.Resolve(dockerComposeExecutable);
        options.RemoveAfterTest = false;
        ApplyChecksToOptions(options);

        Console.WriteLine($"[Start] docker-compose up -d (project '{projectName}')...");
        var tester = new LocalTester(composeFilePath);
        return await tester.TestAsync(options, cancellationToken);
    }

    public Task<TestResult> StartAsync(TestOptions? options = null, CancellationToken cancellationToken = default)
        => StartAsync(_composeFilePath, _projectName, ResolvedDockerComposeExecutable(), options, cancellationToken);

    public async Task StopAsync(
        string composeFilePath,
        string projectName,
        bool removeVolumes = false,
        string dockerComposeExecutable = "auto",
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Stop] Writing compose to '{composeFilePath}' (for consistency)...");
        await SaveToFileAsync(composeFilePath, cancellationToken);

        var tester = new LocalTester(composeFilePath);
        await tester.StopAsync(projectName, removeVolumes, DockerComposeExecutableResolver.Resolve(dockerComposeExecutable), cancellationToken);
    }

    public Task StopAsync(bool removeVolumes, CancellationToken cancellationToken = default)
        => StopAsync(_composeFilePath, _projectName, removeVolumes, ResolvedDockerComposeExecutable(), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => StopAsync(false, cancellationToken);

    public async Task<TestResult> TestAsync(
        string composeFilePath,
        string projectName,
        string dockerComposeExecutable,
        TestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Test] Validating compose...");
        var validation = await ValidateAsync(options: null, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(e => $"{e.Service}: {e.Message}").ToList();
            Console.WriteLine("[Test] Validation failed.");
            return new TestResult { Success = false, Errors = errors };
        }

        await SaveToFileAsync(composeFilePath, cancellationToken);

        options ??= new TestOptions();
        options.ProjectName = projectName;
        options.DockerComposeExecutable = DockerComposeExecutableResolver.Resolve(dockerComposeExecutable);
        // keep caller's RemoveAfterTest
        ApplyChecksToOptions(options);

        Console.WriteLine($"[Test] Running local test (project '{projectName}')...");
        var tester = new LocalTester(composeFilePath);
        return await tester.TestAsync(options, cancellationToken);
    }

    public Task<TestResult> TestAsync(TestOptions? options = null, CancellationToken cancellationToken = default)
        => TestAsync(_composeFilePath, _projectName, ResolvedDockerComposeExecutable(), options, cancellationToken);

    // ---------- Публикация на агента, вызовы Master (/cluster), манифест и артефакты ----------

    public async Task<DeployResult> PublishAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Publish] Validating compose before publishing...");
        var validation = await ValidateAsync(cancellationToken: cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(e => $"{e.Service}: {e.Message}").ToList();
            Console.WriteLine("[Publish] Validation failed.");
            return new DeployResult { Success = false, Error = string.Join("; ", errors) };
        }

        var composeYaml = GenerateYaml();
        Console.WriteLine($"[Publish] Sending compose YAML to agent project '{_projectName}'...");
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        var result = await agent.StartProjectAsync(_projectName, composeYaml, cancellationToken);
        if (!result.Success)
            return result;

        try
        {
            await PushManifestAndArtifactsAsync(agent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new DeployResult
            {
                Success = false,
                Error = $"Compose started but manifest/artifacts upload failed: {ex.Message}",
                Containers = result.Containers
            };
        }

        return result;
    }

    /// <summary>
    /// Deploy compose through Chronos.Master (smart agent selection).
    /// </summary>
    public async Task<ClusterDeployResult> DeployToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        string? preferredLocation = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Cluster Deploy] Validating compose...");
        var validation = await ValidateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Cluster deploy aborted: {validation.Errors.Count} validation error(s).");

        var client = new ClusterClient(masterUrl, apiKey);
        var request = new ClusterDeployRequest
        {
            ProjectName = _projectName,
            ComposeYaml = GenerateYaml(),
            PreferredLocation = preferredLocation
        };
        return await client.DeployAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publish compose + manifest through Chronos.Master.
    /// </summary>
    public async Task<ClusterDeployResult> PublishToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        string? preferredLocation = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Cluster Publish] Validating compose...");
        var validation = await ValidateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Cluster publish aborted: {validation.Errors.Count} validation error(s).");

        var client = new ClusterClient(masterUrl, apiKey);
        var request = new ClusterDeployRequest
        {
            ProjectName = _projectName,
            ComposeYaml = GenerateYaml(),
            PreferredLocation = preferredLocation,
            ManifestJson = HasManifestPayload() ? SerializeManifestJson() : null
        };
        return await client.PublishAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task PushManifestAndArtifactsAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        await PushManifestAndArtifactsAsync(agent, cancellationToken).ConfigureAwait(false);
    }

    private async Task PushManifestAndArtifactsAsync(HttpDeployAgent agent, CancellationToken cancellationToken)
    {
        if (HasManifestPayload())
        {
            var json = SerializeManifestJson();
            await agent.UploadManifestJsonAsync(_projectName, json, cancellationToken).ConfigureAwait(false);
        }

        var artifacts = EnumerateAllDeployArtifacts().ToList();
        if (artifacts.Count > 0)
            await agent.UploadArtifactsTarAsync(_projectName, artifacts, cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<DeployArtifact> EnumerateAllDeployArtifacts()
    {
        foreach (var a in _deployArtifacts)
            yield return a;

        foreach (var svc in _services.Values)
        {
            foreach (var c in svc.CodeTests)
            {
                if (string.IsNullOrWhiteSpace(c.LocalAssemblyPath))
                    continue;

                yield return new DeployArtifact
                {
                    RelativePath = c.AssemblyRelativePath,
                    SourceKind = ArtifactSourceKind.File,
                    SourcePathOnDisk = Path.GetFullPath(c.LocalAssemblyPath!)
                };
            }

            foreach (var j in svc.CodeJobs)
            {
                if (string.IsNullOrWhiteSpace(j.LocalAssemblyPath))
                    continue;

                yield return new DeployArtifact
                {
                    RelativePath = j.AssemblyRelativePath,
                    SourceKind = ArtifactSourceKind.File,
                    SourcePathOnDisk = Path.GetFullPath(j.LocalAssemblyPath!)
                };
            }
        }
    }

    // --- Операции с томами на агенте (снимок, заливка, восстановление) ---

    /// <summary>Потоковый снимок тома с агента в локальный файл (stdout docker → tar).</summary>
    public Task SnapshotRemoteVolumeToFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string localFilePath,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return agent.SnapshotVolumeToFileAsync(_projectName, dockerVolumeName, localFilePath, compress, cancellationToken);
    }

    /// <summary>
    /// Convenience overload: stores snapshot into "./snapshots" with autogenerated filename.
    /// Returns full path to created file.
    /// </summary>
    public Task<string> SnapshotRemoteVolumeToFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default)
    {
        var localDir = Path.Combine(Environment.CurrentDirectory, "snapshots");
        return SnapshotRemoteVolumeToDirectoryAsync(
            agentUrl: agentUrl,
            dockerVolumeName: dockerVolumeName,
            localDirectoryPath: localDir,
            apiKey: apiKey,
            compress: compress,
            filePrefix: null,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Stream volume snapshot from agent to a local directory and auto-generate file name.
    /// Returns full created file path.
    /// </summary>
    public async Task<string> SnapshotRemoteVolumeToDirectoryAsync(
        string agentUrl,
        string dockerVolumeName,
        string localDirectoryPath,
        string? apiKey = null,
        string compress = "gzip",
        string? filePrefix = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(localDirectoryPath);

        var prefix = string.IsNullOrWhiteSpace(filePrefix) ? $"{_projectName}_{dockerVolumeName}" : filePrefix;
        prefix = SanitizeFileNamePart(prefix);

        var extension = string.Equals(compress, "none", StringComparison.OrdinalIgnoreCase) ? ".tar" : ".tar.gz";
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{prefix}_{timestamp}{extension}";
        var fullPath = Path.Combine(localDirectoryPath, fileName);

        await SnapshotRemoteVolumeToFileAsync(
            agentUrl: agentUrl,
            dockerVolumeName: dockerVolumeName,
            localFilePath: fullPath,
            apiKey: apiKey,
            compress: compress,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return fullPath;
    }

    public Task<VolumeOperationResult> SnapshotRemoteVolumeUploadToUrlAsync(
        string agentUrl,
        string dockerVolumeName,
        VolumeSnapshotUploadRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return agent.SnapshotVolumeUploadToUrlAsync(_projectName, dockerVolumeName, request, cancellationToken);
    }

    public Task<VolumeOperationResult> RestoreRemoteVolumeFromFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string localArchivePath,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return agent.RestoreVolumeFromFileAsync(_projectName, dockerVolumeName, localArchivePath, compress, cancellationToken);
    }

    public Task<VolumeOperationResult> RestoreRemoteVolumeFromUrlAsync(
        string agentUrl,
        string dockerVolumeName,
        VolumeRestoreFromUrlRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return agent.RestoreVolumeFromUrlAsync(_projectName, dockerVolumeName, request, cancellationToken);
    }

    public async Task<DeployResult> StartRemoteAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Remote Start] Sending compose to agent project '{_projectName}' (/start)...");
        var validation = await ValidateAsync(cancellationToken: cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(e => $"{e.Service}: {e.Message}").ToList();
            Console.WriteLine("[Remote Start] Validation failed.");
            return new DeployResult { Success = false, Error = string.Join("; ", errors) };
        }

        var composeYaml = GenerateYaml();
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return await agent.StartProjectAsync(_projectName, composeYaml, cancellationToken);
    }

    public async Task<DeployResult> RestartRemoteAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Remote Restart] Sending compose to agent project '{_projectName}' (/restart)...");
        var validation = await ValidateAsync(cancellationToken: cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(e => $"{e.Service}: {e.Message}").ToList();
            Console.WriteLine("[Remote Restart] Validation failed.");
            return new DeployResult { Success = false, Error = string.Join("; ", errors) };
        }

        var composeYaml = GenerateYaml();
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return await agent.RestartProjectAsync(_projectName, composeYaml, cancellationToken);
    }

    public async Task<DeployResult> StopRemoteAsync(string agentUrl, string? apiKey = null, bool removeVolumes = false, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Remote Stop] Asking agent to stop project '{_projectName}' (/stop)...");
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return await agent.StopProjectAsync(_projectName, removeVolumes, cancellationToken);
    }

    // --- Обратная сторона: список проектов на агенте, импорт YAML, генерация Fluent-кода ---

    public static Task<IReadOnlyList<string>> ListRemoteProjectsAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        return agent.ListProjectsAsync(cancellationToken);
    }

    public static async Task<ComposeBuilder> LoadFromAgentAsync(string agentUrl, string projectName, string? apiKey = null, string? composeFilePath = null, CancellationToken cancellationToken = default)
    {
        var agent = new HttpDeployAgent(agentUrl, apiKey);
        var yaml = await agent.GetComposeAsync(projectName, cancellationToken);
        return FromYaml(yaml, projectName: projectName, composeFilePath: composeFilePath);
    }

    public static ComposeBuilder FromYaml(string yaml, string? projectName = null, string? composeFilePath = null)
    {
        var builder = ComposeYamlParser.Parse(yaml);
        if (!string.IsNullOrWhiteSpace(projectName))
            builder.WithProjectName(projectName);
        if (!string.IsNullOrWhiteSpace(composeFilePath))
            builder.WithComposeFilePath(composeFilePath);
        return builder;
    }

    // Реконструкция Fluent-кода из текущей модели (best-effort, для отладки и round-trip).
    public string ToFluentApiCode(string variableName = "compose")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new ComposeBuilder()");
        sb.AppendLine($"    .WithVersion(\"{_version}\")");

        if (_networks.Count > 0)
        {
            foreach (var n in _networks.Values)
                sb.AppendLine($"    .AddNetwork(\"{n.Name}\")");
        }

        if (_volumes.Count > 0)
        {
            foreach (var v in _volumes.Values)
                sb.AppendLine($"    .AddVolume(\"{v.Name}\")");
        }

        if (_services.Count > 0)
        {
            var first = true;
            foreach (var svc in _services.Values)
            {
                sb.AppendLine(first ? $"    .AddService(s => s" : $"    .AddService(s => s");
                sb.AppendLine($"        .WithName(\"{svc.Name}\")");
                if (!string.IsNullOrWhiteSpace(svc.Image))
                    sb.AppendLine($"        .UseImage(\"{svc.Image}\")");

                foreach (var port in svc.Ports)
                    sb.AppendLine($"        .AddPort({port.HostPort}, {port.ContainerPort})");

                foreach (var env in svc.Environment)
                    sb.AppendLine($"        .AddEnvironment(\"{env.Key}\", \"{env.Value}\")");

                sb.AppendLine(first ? $"    );" : $"    );");
                first = false;
            }
        }

        return sb.ToString();
    }

    // ---------------- YAML serialization ----------------

    private static object ToComposeServiceObject(Service s)
    {
        var obj = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(s.Image))
            obj["image"] = s.Image;

        if (!string.IsNullOrWhiteSpace(s.BuildContext))
        {
            if (!string.IsNullOrWhiteSpace(s.Dockerfile))
            {
                obj["build"] = new Dictionary<string, object?>
                {
                    ["context"] = s.BuildContext,
                    ["dockerfile"] = s.Dockerfile
                };
            }
            else
            {
                obj["build"] = s.BuildContext;
            }
        }

        if (!string.IsNullOrWhiteSpace(s.ContainerName))
            obj["container_name"] = s.ContainerName;

        if (s.Command.Count > 0)
            obj["command"] = string.Join(" ", s.Command);

        if (s.Ports.Count > 0)
            obj["ports"] = s.Ports.Select(p => p.ToComposeString()).ToList();

        if (s.Environment.Count > 0)
            obj["environment"] = s.Environment.ToDictionary(k => k.Key, v => (object?)v.Value);

        if (s.EnvFiles.Count > 0)
            obj["env_file"] = s.EnvFiles.ToList();

        if (s.Labels.Count > 0)
            obj["labels"] = s.Labels.ToDictionary(k => k.Key, v => (object?)v.Value);

        if (s.DependsOn.Count > 0)
            obj["depends_on"] = s.DependsOn.ToList();

        if (s.Networks.Count > 0)
            obj["networks"] = s.Networks.ToList();

        if (s.Volumes.Count > 0)
            obj["volumes"] = s.Volumes.ToList();

        if (s.Secrets.Count > 0)
            obj["secrets"] = s.Secrets.ToList();

        if (!string.IsNullOrWhiteSpace(s.RestartPolicy))
            obj["restart"] = s.RestartPolicy;

        if (s.HealthCheck != null)
        {
            obj["healthcheck"] = new Dictionary<string, object?>
            {
                ["test"] = new object[] { "CMD-SHELL", s.HealthCheck.TestCommand },
                ["interval"] = $"{s.HealthCheck.IntervalSeconds}s",
                ["timeout"] = $"{s.HealthCheck.TimeoutSeconds}s",
                ["retries"] = s.HealthCheck.Retries
            };
        }

        if (s.Deploy != null)
        {
            var deploy = new Dictionary<string, object?>();

            if (s.Deploy.Replicas.HasValue)
                deploy["replicas"] = s.Deploy.Replicas.Value;

            if (s.Deploy.Resources != null &&
                (s.Deploy.Resources.Cpus.HasValue || s.Deploy.Resources.MemoryMb.HasValue))
            {
                var resources = new Dictionary<string, object?>();
                var limits = new Dictionary<string, object?>();

                if (s.Deploy.Resources.Cpus.HasValue)
                    limits["cpus"] = s.Deploy.Resources.Cpus.Value.ToString(CultureInfo.InvariantCulture) + "m";
                if (s.Deploy.Resources.MemoryMb.HasValue)
                    limits["memory"] = $"{s.Deploy.Resources.MemoryMb.Value}M";

                resources["limits"] = limits;
                deploy["resources"] = resources;
            }

            obj["deploy"] = deploy;
        }

        if (s.ExtraHosts.Count > 0)
            obj["extra_hosts"] = s.ExtraHosts.Select(kvp => $"{kvp.Key}:{kvp.Value}").ToList();

        if (s.Capabilities.Count > 0)
            obj["cap_add"] = s.Capabilities.ToList();

        if (s.SecurityOpt.Count > 0)
            obj["security_opt"] = s.SecurityOpt.ToList();

        if (!string.IsNullOrWhiteSpace(s.User))
            obj["user"] = s.User;

        if (!string.IsNullOrWhiteSpace(s.WorkingDir))
            obj["working_dir"] = s.WorkingDir;

        if (!string.IsNullOrWhiteSpace(s.LoggingDriver))
        {
            var logging = new Dictionary<string, object?>
            {
                ["driver"] = s.LoggingDriver
            };
            if (s.LoggingOptions.Count > 0)
                logging["options"] = s.LoggingOptions.ToDictionary(k => k.Key, v => (object?)v.Value);
            obj["logging"] = logging;
        }

        if (s.Init)
            obj["init"] = true;

        if (s.Privileged)
            obj["privileged"] = true;

        return obj;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }

    IComposeBuilder IComposeBuilder.WithVersion(string version) => WithVersion(version);
    IComposeBuilder IComposeBuilder.WithComposeFilePath(string composeFilePath) => WithComposeFilePath(composeFilePath);
    IComposeBuilder IComposeBuilder.WithProjectName(string projectName) => WithProjectName(projectName);
    IComposeBuilder IComposeBuilder.WithDockerComposeExecutable(string dockerComposeExecutable) => WithDockerComposeExecutable(dockerComposeExecutable);
    IComposeBuilder IComposeBuilder.WithDockerComposeV2() => WithDockerComposeV2();
    IComposeBuilder IComposeBuilder.AddService(Service service) => AddService(service);
    IComposeBuilder IComposeBuilder.AddService(Action<IServiceBuilder> configure) => AddService(configure);
    IComposeBuilder IComposeBuilder.AddNetwork(Network network) => AddNetwork(network);
    IComposeBuilder IComposeBuilder.AddNetwork(string name, string driver) => AddNetwork(name, driver);
    IComposeBuilder IComposeBuilder.AddVolume(Volume volume) => AddVolume(volume);
    IComposeBuilder IComposeBuilder.AddVolume(string name, string driver) => AddVolume(name, driver);
    IComposeBuilder IComposeBuilder.AddSecret(Secret secret) => AddSecret(secret);
    IComposeBuilder IComposeBuilder.AddConfig(Config config) => AddConfig(config);
    IComposeBuilder IComposeBuilder.AddExtension(string key, object value) => AddExtension(key, value);
    IComposeBuilder IComposeBuilder.WithReplicaPolicy(ReplicaPolicy policy) => WithReplicaPolicy(policy);
    IComposeBuilder IComposeBuilder.AddDeployArtifactFromFile(string deployRelativePath, string sourceFilePath, int? unixFileMode) =>
        AddDeployArtifactFromFile(deployRelativePath, sourceFilePath, unixFileMode);
    IComposeBuilder IComposeBuilder.AddDeployArtifactFromDirectory(string deployRelativePath, string sourceDirectoryPath, int? unixFileMode) =>
        AddDeployArtifactFromDirectory(deployRelativePath, sourceDirectoryPath, unixFileMode);
    ProjectManifest IComposeBuilder.BuildManifest() => BuildManifest();
    string IComposeBuilder.SerializeManifestJson() => SerializeManifestJson();
    bool IComposeBuilder.HasManifestPayload() => HasManifestPayload();
    IReadOnlyList<DeployArtifact> IComposeBuilder.GetDeployArtifacts() => GetDeployArtifacts();
    IBuiltCompose IComposeBuilder.Build() => Build();
    string IComposeBuilder.GenerateYaml() => GenerateYaml();
    Task IComposeBuilder.SaveToFileAsync(string path, CancellationToken cancellationToken) => SaveToFileAsync(path, cancellationToken);
    Task<ValidationResult> IComposeBuilder.ValidateAsync(ComposeValidatorOptions? options, CancellationToken cancellationToken) =>
        ValidateAsync(options, cancellationToken);
    Task<TestResult> IComposeBuilder.StartAsync(string composeFilePath, string projectName, string dockerComposeExecutable, TestOptions? options, CancellationToken cancellationToken) =>
        StartAsync(composeFilePath, projectName, dockerComposeExecutable, options, cancellationToken);
    Task<TestResult> IComposeBuilder.StartAsync(TestOptions? options, CancellationToken cancellationToken) => StartAsync(options, cancellationToken);
    Task IComposeBuilder.StopAsync(string composeFilePath, string projectName, bool removeVolumes, string dockerComposeExecutable, CancellationToken cancellationToken) =>
        StopAsync(composeFilePath, projectName, removeVolumes, dockerComposeExecutable, cancellationToken);
    Task IComposeBuilder.StopAsync(bool removeVolumes, CancellationToken cancellationToken) => StopAsync(removeVolumes, cancellationToken);
    Task IComposeBuilder.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);
    Task<TestResult> IComposeBuilder.TestAsync(string composeFilePath, string projectName, string dockerComposeExecutable, TestOptions? options, CancellationToken cancellationToken) =>
        TestAsync(composeFilePath, projectName, dockerComposeExecutable, options, cancellationToken);
    Task<TestResult> IComposeBuilder.TestAsync(TestOptions? options, CancellationToken cancellationToken) => TestAsync(options, cancellationToken);
    Task<DeployResult> IComposeBuilder.PublishAsync(string agentUrl, string? apiKey, CancellationToken cancellationToken) =>
        PublishAsync(agentUrl, apiKey, cancellationToken);
    Task<ClusterDeployResult> IComposeBuilder.DeployToClusterAsync(string masterUrl, string? apiKey, string? preferredLocation, CancellationToken cancellationToken) =>
        DeployToClusterAsync(masterUrl, apiKey, preferredLocation, cancellationToken);
    Task<ClusterDeployResult> IComposeBuilder.PublishToClusterAsync(string masterUrl, string? apiKey, string? preferredLocation, CancellationToken cancellationToken) =>
        PublishToClusterAsync(masterUrl, apiKey, preferredLocation, cancellationToken);
    Task IComposeBuilder.PushManifestAndArtifactsAsync(string agentUrl, string? apiKey, CancellationToken cancellationToken) =>
        PushManifestAndArtifactsAsync(agentUrl, apiKey, cancellationToken);
    Task IComposeBuilder.SnapshotRemoteVolumeToFileAsync(string agentUrl, string dockerVolumeName, string localFilePath, string? apiKey, string compress, CancellationToken cancellationToken) =>
        SnapshotRemoteVolumeToFileAsync(agentUrl, dockerVolumeName, localFilePath, apiKey, compress, cancellationToken);
    Task<string> IComposeBuilder.SnapshotRemoteVolumeToFileAsync(string agentUrl, string dockerVolumeName, string? apiKey, string compress, CancellationToken cancellationToken) =>
        SnapshotRemoteVolumeToFileAsync(agentUrl, dockerVolumeName, apiKey, compress, cancellationToken);
    Task<string> IComposeBuilder.SnapshotRemoteVolumeToDirectoryAsync(string agentUrl, string dockerVolumeName, string localDirectoryPath, string? apiKey, string compress, string? filePrefix, CancellationToken cancellationToken) =>
        SnapshotRemoteVolumeToDirectoryAsync(agentUrl, dockerVolumeName, localDirectoryPath, apiKey, compress, filePrefix, cancellationToken);
    Task<VolumeOperationResult> IComposeBuilder.SnapshotRemoteVolumeUploadToUrlAsync(string agentUrl, string dockerVolumeName, VolumeSnapshotUploadRequest request, string? apiKey, CancellationToken cancellationToken) =>
        SnapshotRemoteVolumeUploadToUrlAsync(agentUrl, dockerVolumeName, request, apiKey, cancellationToken);
    Task<VolumeOperationResult> IComposeBuilder.RestoreRemoteVolumeFromFileAsync(string agentUrl, string dockerVolumeName, string localArchivePath, string? apiKey, string compress, CancellationToken cancellationToken) =>
        RestoreRemoteVolumeFromFileAsync(agentUrl, dockerVolumeName, localArchivePath, apiKey, compress, cancellationToken);
    Task<VolumeOperationResult> IComposeBuilder.RestoreRemoteVolumeFromUrlAsync(string agentUrl, string dockerVolumeName, VolumeRestoreFromUrlRequest request, string? apiKey, CancellationToken cancellationToken) =>
        RestoreRemoteVolumeFromUrlAsync(agentUrl, dockerVolumeName, request, apiKey, cancellationToken);
    Task<DeployResult> IComposeBuilder.StartRemoteAsync(string agentUrl, string? apiKey, CancellationToken cancellationToken) =>
        StartRemoteAsync(agentUrl, apiKey, cancellationToken);
    Task<DeployResult> IComposeBuilder.RestartRemoteAsync(string agentUrl, string? apiKey, CancellationToken cancellationToken) =>
        RestartRemoteAsync(agentUrl, apiKey, cancellationToken);
    Task<DeployResult> IComposeBuilder.StopRemoteAsync(string agentUrl, string? apiKey, bool removeVolumes, CancellationToken cancellationToken) =>
        StopRemoteAsync(agentUrl, apiKey, removeVolumes, cancellationToken);
    string IComposeBuilder.ToFluentApiCode(string variableName) => ToFluentApiCode(variableName);
}
