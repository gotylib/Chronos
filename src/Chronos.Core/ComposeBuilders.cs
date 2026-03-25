using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Chronos.Core;

public sealed class ComposeDefinition
{
    public string Version { get; init; } = "3.8";
    public IReadOnlyDictionary<string, Service> Services { get; init; } = new Dictionary<string, Service>();
    public IReadOnlyDictionary<string, Network> Networks { get; init; } = new Dictionary<string, Network>();
    public IReadOnlyDictionary<string, Volume> Volumes { get; init; } = new Dictionary<string, Volume>();
    public IReadOnlyDictionary<string, Secret> Secrets { get; init; } = new Dictionary<string, Secret>();
    public IReadOnlyDictionary<string, Config> Configs { get; init; } = new Dictionary<string, Config>();
    public IReadOnlyDictionary<string, object> ExtensionFields { get; init; } = new Dictionary<string, object>();
}

public sealed class ServiceBuilder
{
    private readonly Service _service = new();

    public ServiceBuilder WithName(string name)
    {
        _service.Name = name;
        return this;
    }

    public ServiceBuilder UseImage(string image)
    {
        _service.Image = image;
        return this;
    }

    public ServiceBuilder BuildFrom(string contextPath, string? dockerfile = null)
    {
        _service.BuildContext = contextPath;
        _service.Dockerfile = dockerfile;
        return this;
    }

    public ServiceBuilder WithContainerName(string name)
    {
        _service.ContainerName = name;
        return this;
    }

    public ServiceBuilder WithCommand(params string[] command)
    {
        _service.Command.AddRange(command.Where(c => !string.IsNullOrWhiteSpace(c)));
        return this;
    }

    public ServiceBuilder AddPort(int hostPort, int containerPort, string protocol = "tcp")
        => AddPort("0.0.0.0", hostPort, containerPort, protocol);

    public ServiceBuilder AddPort(string host, int hostPort, int containerPort, string protocol = "tcp")
    {
        _service.Ports.Add(new PortMapping
        {
            Host = host,
            HostPort = hostPort,
            ContainerPort = containerPort,
            Protocol = protocol
        });
        return this;
    }

    public ServiceBuilder AddEnvironment(string key, string value)
    {
        _service.Environment[key] = value;
        return this;
    }

    public ServiceBuilder AddEnvironment(Dictionary<string, string> env)
    {
        foreach (var kv in env)
            _service.Environment[kv.Key] = kv.Value;
        return this;
    }

    public ServiceBuilder AddEnvironmentFile(string filePath)
    {
        _service.EnvFiles.Add(filePath);
        return this;
    }

    public ServiceBuilder AddLabel(string key, string value)
    {
        _service.Labels[key] = value;
        return this;
    }

    public ServiceBuilder DependsOn(params string[] services)
    {
        _service.DependsOn.AddRange(services.Where(s => !string.IsNullOrWhiteSpace(s)));
        return this;
    }

    public ServiceBuilder ConnectToNetwork(string network)
    {
        _service.Networks.Add(network);
        return this;
    }

    public ServiceBuilder AddVolume(string source, string target, string mode = "rw")
    {
        _service.Volumes.Add($"{source}:{target}:{mode}");
        return this;
    }

    public ServiceBuilder AddSecret(string secretName)
    {
        _service.Secrets.Add(secretName);
        return this;
    }

    public ServiceBuilder WithHealthCheck(string testCommand, int intervalSeconds = 30, int timeoutSeconds = 10, int retries = 3)
    {
        _service.HealthCheck = new HealthCheck
        {
            TestCommand = testCommand,
            IntervalSeconds = intervalSeconds,
            TimeoutSeconds = timeoutSeconds,
            Retries = retries
        };
        return this;
    }

    public ServiceBuilder WithRestartPolicy(string policy)
    {
        _service.RestartPolicy = policy;
        return this;
    }

    public ServiceBuilder WithResources(decimal? cpus = null, int? memoryMb = null)
    {
        _service.Deploy ??= new DeployConfig();
        _service.Deploy.Resources = new ResourceLimits { Cpus = cpus, MemoryMb = memoryMb };
        return this;
    }

    public ServiceBuilder WithReplicas(int count)
    {
        _service.Deploy ??= new DeployConfig();
        _service.Deploy.Replicas = count;
        return this;
    }

    public ServiceBuilder WithUser(string user)
    {
        _service.User = user;
        return this;
    }

    public ServiceBuilder WithWorkingDirectory(string path)
    {
        _service.WorkingDir = path;
        return this;
    }

    public ServiceBuilder AddCapability(params string[] capabilities)
    {
        _service.Capabilities.AddRange(capabilities.Where(c => !string.IsNullOrWhiteSpace(c)));
        return this;
    }

    public ServiceBuilder AddExtraHost(string hostname, string ip)
    {
        _service.ExtraHosts[hostname] = ip;
        return this;
    }

    public ServiceBuilder WithLogging(string driver, Dictionary<string, string>? options = null)
    {
        _service.LoggingDriver = driver;
        if (options != null)
        {
            foreach (var opt in options)
                _service.LoggingOptions[opt.Key] = opt.Value;
        }
        return this;
    }

    public ServiceBuilder WithInit(bool init)
    {
        _service.Init = init;
        return this;
    }

    public ServiceBuilder AsPrivileged(bool privileged = true)
    {
        _service.Privileged = privileged;
        return this;
    }

    public Service Build()
    {
        if (string.IsNullOrWhiteSpace(_service.Name))
            throw new InvalidOperationException("Service name is required.");

        if (string.IsNullOrWhiteSpace(_service.Image) && string.IsNullOrWhiteSpace(_service.BuildContext))
            throw new InvalidOperationException($"Service '{_service.Name}' must have either Image or Build context specified.");

        return _service;
    }
}

public sealed class ComposeBuilder
{
    private string _version = "3.8";
    private readonly Dictionary<string, Service> _services = new();
    private readonly Dictionary<string, Network> _networks = new();
    private readonly Dictionary<string, Volume> _volumes = new();
    private readonly Dictionary<string, Secret> _secrets = new();
    private readonly Dictionary<string, Config> _configs = new();
    private readonly Dictionary<string, object> _xFields = new();

    // local runtime settings (NOT used by agent remote endpoints)
    private string _composeFilePath = "docker-compose.yml";
    private string _projectName = "chronos";
    private string _dockerComposeExecutable = "docker-compose";

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

    public ComposeBuilder WithDockerComposeExecutable(string dockerComposeExecutable)
    {
        _dockerComposeExecutable = dockerComposeExecutable ?? throw new ArgumentNullException(nameof(dockerComposeExecutable));
        return this;
    }

    public ComposeBuilder AddService(Service service)
    {
        _services[service.Name] = service;
        return this;
    }

    public ComposeBuilder AddService(Action<ServiceBuilder> configure)
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
        options.DockerComposeExecutable = dockerComposeExecutable;
        options.RemoveAfterTest = false;

        Console.WriteLine($"[Start] docker-compose up -d (project '{projectName}')...");
        var tester = new LocalTester(composeFilePath);
        return await tester.TestAsync(options, cancellationToken);
    }

    public Task<TestResult> StartAsync(TestOptions? options = null, CancellationToken cancellationToken = default)
        => StartAsync(_composeFilePath, _projectName, _dockerComposeExecutable, options, cancellationToken);

    public async Task StopAsync(
        string composeFilePath,
        string projectName,
        bool removeVolumes = false,
        string dockerComposeExecutable = "docker-compose",
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Stop] Writing compose to '{composeFilePath}' (for consistency)...");
        await SaveToFileAsync(composeFilePath, cancellationToken);

        var tester = new LocalTester(composeFilePath);
        await tester.StopAsync(projectName, removeVolumes, dockerComposeExecutable, cancellationToken);
    }

    public Task StopAsync(bool removeVolumes, CancellationToken cancellationToken = default)
        => StopAsync(_composeFilePath, _projectName, removeVolumes, _dockerComposeExecutable, cancellationToken);

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
        options.DockerComposeExecutable = dockerComposeExecutable;
        // keep caller's RemoveAfterTest

        Console.WriteLine($"[Test] Running local test (project '{projectName}')...");
        var tester = new LocalTester(composeFilePath);
        return await tester.TestAsync(options, cancellationToken);
    }

    public Task<TestResult> TestAsync(TestOptions? options = null, CancellationToken cancellationToken = default)
        => TestAsync(_composeFilePath, _projectName, _dockerComposeExecutable, options, cancellationToken);

    // ---------------- Remote API ----------------

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
        return await agent.StartProjectAsync(_projectName, composeYaml, cancellationToken);
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

    // Best-effort C# fluent reconstruction.
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
}

