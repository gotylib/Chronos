using Chronos.Core;
using Chronos.Core.Compose.Implementation;

var compose = new ComposeBuilder()
    .WithProjectName("chronos-mvp-test")
    .AddNetwork("backend")
    .AddVolume("pg_data")
    .AddVolume("redis_data")

    .AddService(s => s
        .WithName("postgres")
        .UseImage("postgres:16-alpine")
        .AddEnvironment("POSTGRES_USER", "chronos")
        .AddEnvironment("POSTGRES_PASSWORD", "chronos")
        .AddEnvironment("POSTGRES_DB", "chronos_app")
        .AddPort(55432, 5432)
        .AddVolume("pg_data", "/var/lib/postgresql/data")
        .DependsOn())

    .AddService(s => s
        .WithName("redis")
        .UseImage("redis:7-alpine")
        .AddPort(56379, 6379)
        .AddVolume("redis_data", "/data"))

    .AddService(s => s
        .WithName("adminer")
        .UseImage("adminer:4")
        .AddPort(58080, 8080)
        .DependsOn("postgres"))

    .AddService(s => s
        .WithName("nginx")
        .UseImage("nginx:alpine")
        .AddPort(58081, 80)
        .DependsOn("postgres", "redis"));

var composePublisher = compose.Build();

try
{
    var result = await composePublisher.PublishToClusterAsync("http://localhost:5000");
    Console.WriteLine(
        $"Cluster publish result: success={result.Success}, agent={result.AgentId}, url={result.AgentUrl}, error={result.Error ?? "<none>"}");
}
catch (Exception ex)
{
    Console.WriteLine($"Cluster publish failed: {ex.GetType().Name}: {ex.Message}");
    throw;
}

// Локально: TestAsync + кодовые тесты. Удалённо — PublishAsync с манифестом.
// Первый pull образа GitLab + старт Omnibus занимают заметно больше минуты — StartupTimeout не оставляй дефолтным.
// await compose.StartAsync(new TestOptions
// {
//     StartupTimeout = TimeSpan.FromMinutes(45),
//     TestExecutionTimeout = TimeSpan.FromMinutes(25),
//     RequireHealthChecksIfDefined = false
// });

// await compose.StartRemoteAsync("http://localhost:5008");
