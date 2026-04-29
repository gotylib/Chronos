using Chronos.Core.Compose.Interfaces;

// Fluent-настройка одного сервиса; наполняет модель Service для ComposeBuilder.
namespace Chronos.Core.Compose.Implementation;

public sealed class ServiceBuilder : IServiceBuilder
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

    public ServiceBuilder AddSecurityOption(string option)
    {
        if (!string.IsNullOrWhiteSpace(option))
            _service.SecurityOpt.Add(option.Trim());
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

    public ServiceBuilder AddCheck(DeclarativeCheck check)
    {
        if (check == null) throw new ArgumentNullException(nameof(check));
        _service.Checks.Add(check);
        return this;
    }

    public ServiceBuilder UseChecks(params DeclarativeCheck[] checks)
    {
        foreach (var t in checks)
            _service.Checks.Add(t);
        return this;
    }

    public ServiceBuilder AddJob(JobDefinition job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        _service.Jobs.Add(job);
        return this;
    }

    public ServiceBuilder UseJobs(params JobDefinition[] jobs)
    {
        foreach (var j in jobs)
            _service.Jobs.Add(j);
        return this;
    }

    /// <summary>Класс с методами, помеченными <see cref="JobAttribute"/> (программируемые jobs).</summary>
    public ServiceBuilder UseJobs(params Type[] jobClasses)
    {
        ArgumentNullException.ThrowIfNull(jobClasses);
        foreach (var t in jobClasses)
            CodeJobRegistration.AddJobsFromType(_service, t);
        return this;
    }

    public ServiceBuilder UseJobs<TJobs>() => UseJobs(typeof(TJobs));

    /// <summary>Класс с методами, помеченными <see cref="TestAttribute"/> (ваша логика в коде).</summary>
    public ServiceBuilder UseTests(params Type[] testClasses)
    {
        ArgumentNullException.ThrowIfNull(testClasses);
        foreach (var t in testClasses)
            CodeTestRegistration.AddTestsFromType(_service, t);
        return this;
    }

    public ServiceBuilder UseTests<TTests>() => UseTests(typeof(TTests));

    public Service Build()
    {
        if (string.IsNullOrWhiteSpace(_service.Name))
            throw new InvalidOperationException("Service name is required.");

        if (string.IsNullOrWhiteSpace(_service.Image) && string.IsNullOrWhiteSpace(_service.BuildContext))
            throw new InvalidOperationException($"Service '{_service.Name}' must have either Image or Build context specified.");

        return _service;
    }

    IServiceBuilder IServiceBuilder.WithName(string name) => WithName(name);
    IServiceBuilder IServiceBuilder.UseImage(string image) => UseImage(image);
    IServiceBuilder IServiceBuilder.BuildFrom(string contextPath, string? dockerfile) => BuildFrom(contextPath, dockerfile);
    IServiceBuilder IServiceBuilder.WithContainerName(string name) => WithContainerName(name);
    IServiceBuilder IServiceBuilder.WithCommand(params string[] command) => WithCommand(command);
    IServiceBuilder IServiceBuilder.AddPort(int hostPort, int containerPort, string protocol) => AddPort(hostPort, containerPort, protocol);
    IServiceBuilder IServiceBuilder.AddPort(string host, int hostPort, int containerPort, string protocol) => AddPort(host, hostPort, containerPort, protocol);
    IServiceBuilder IServiceBuilder.AddEnvironment(string key, string value) => AddEnvironment(key, value);
    IServiceBuilder IServiceBuilder.AddEnvironment(Dictionary<string, string> env) => AddEnvironment(env);
    IServiceBuilder IServiceBuilder.AddEnvironmentFile(string filePath) => AddEnvironmentFile(filePath);
    IServiceBuilder IServiceBuilder.AddLabel(string key, string value) => AddLabel(key, value);
    IServiceBuilder IServiceBuilder.DependsOn(params string[] services) => DependsOn(services);
    IServiceBuilder IServiceBuilder.ConnectToNetwork(string network) => ConnectToNetwork(network);
    IServiceBuilder IServiceBuilder.AddVolume(string source, string target, string mode) => AddVolume(source, target, mode);
    IServiceBuilder IServiceBuilder.AddSecret(string secretName) => AddSecret(secretName);
    IServiceBuilder IServiceBuilder.WithHealthCheck(string testCommand, int intervalSeconds, int timeoutSeconds, int retries) =>
        WithHealthCheck(testCommand, intervalSeconds, timeoutSeconds, retries);
    IServiceBuilder IServiceBuilder.WithRestartPolicy(string policy) => WithRestartPolicy(policy);
    IServiceBuilder IServiceBuilder.WithResources(decimal? cpus, int? memoryMb) => WithResources(cpus, memoryMb);
    IServiceBuilder IServiceBuilder.WithReplicas(int count) => WithReplicas(count);
    IServiceBuilder IServiceBuilder.WithUser(string user) => WithUser(user);
    IServiceBuilder IServiceBuilder.WithWorkingDirectory(string path) => WithWorkingDirectory(path);
    IServiceBuilder IServiceBuilder.AddCapability(params string[] capabilities) => AddCapability(capabilities);
    IServiceBuilder IServiceBuilder.AddSecurityOption(string option) => AddSecurityOption(option);
    IServiceBuilder IServiceBuilder.AddExtraHost(string hostname, string ip) => AddExtraHost(hostname, ip);
    IServiceBuilder IServiceBuilder.WithLogging(string driver, Dictionary<string, string>? options) => WithLogging(driver, options);
    IServiceBuilder IServiceBuilder.WithInit(bool init) => WithInit(init);
    IServiceBuilder IServiceBuilder.AsPrivileged(bool privileged) => AsPrivileged(privileged);
    IServiceBuilder IServiceBuilder.AddCheck(DeclarativeCheck check) => AddCheck(check);
    IServiceBuilder IServiceBuilder.UseChecks(params DeclarativeCheck[] checks) => UseChecks(checks);
    IServiceBuilder IServiceBuilder.AddJob(JobDefinition job) => AddJob(job);
    IServiceBuilder IServiceBuilder.UseJobs(params JobDefinition[] jobs) => UseJobs(jobs);
    IServiceBuilder IServiceBuilder.UseJobs(params Type[] jobClasses) => UseJobs(jobClasses);
    IServiceBuilder IServiceBuilder.UseJobs<TJobs>() => UseJobs<TJobs>();
    IServiceBuilder IServiceBuilder.UseTests(params Type[] testClasses) => UseTests(testClasses);
    IServiceBuilder IServiceBuilder.UseTests<TTests>() => UseTests<TTests>();
    Service IServiceBuilder.Build() => Build();
}
