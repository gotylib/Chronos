namespace Chronos.Core.Compose.Interfaces;

/// <summary>Fluent builder for a single Docker Compose service.</summary>
public interface IServiceBuilder
{
    IServiceBuilder WithName(string name);
    IServiceBuilder UseImage(string image);
    IServiceBuilder BuildFrom(string contextPath, string? dockerfile = null);
    IServiceBuilder WithContainerName(string name);
    IServiceBuilder WithCommand(params string[] command);
    IServiceBuilder AddPort(int hostPort, int containerPort, string protocol = "tcp");
    IServiceBuilder AddPort(string host, int hostPort, int containerPort, string protocol = "tcp");
    IServiceBuilder AddEnvironment(string key, string value);
    IServiceBuilder AddEnvironment(Dictionary<string, string> env);
    IServiceBuilder AddEnvironmentFile(string filePath);
    IServiceBuilder AddLabel(string key, string value);
    IServiceBuilder DependsOn(params string[] services);
    IServiceBuilder ConnectToNetwork(string network);
    IServiceBuilder AddVolume(string source, string target, string mode = "rw");
    IServiceBuilder AddSecret(string secretName);
    IServiceBuilder WithHealthCheck(string testCommand, int intervalSeconds = 30, int timeoutSeconds = 10, int retries = 3);
    IServiceBuilder WithRestartPolicy(string policy);
    IServiceBuilder WithResources(decimal? cpus = null, int? memoryMb = null);
    IServiceBuilder WithReplicas(int count);
    IServiceBuilder WithUser(string user);
    IServiceBuilder WithWorkingDirectory(string path);
    IServiceBuilder AddCapability(params string[] capabilities);
    IServiceBuilder AddExtraHost(string hostname, string ip);
    IServiceBuilder WithLogging(string driver, Dictionary<string, string>? options = null);
    IServiceBuilder WithInit(bool init);
    IServiceBuilder AsPrivileged(bool privileged = true);
    IServiceBuilder AddCheck(DeclarativeCheck check);
    IServiceBuilder UseChecks(params DeclarativeCheck[] checks);
    IServiceBuilder AddJob(JobDefinition job);
    IServiceBuilder UseJobs(params JobDefinition[] jobs);
    /// <summary>Класс с методами, помеченными <see cref="JobAttribute"/> (программируемые jobs).</summary>
    IServiceBuilder UseJobs(params Type[] jobClasses);
    IServiceBuilder UseJobs<TJobs>();
    /// <summary>Класс с методами, помеченными <see cref="TestAttribute"/> (ваша логика в коде).</summary>
    IServiceBuilder UseTests(params Type[] testClasses);
    IServiceBuilder UseTests<TTests>();

    Service Build();
}
