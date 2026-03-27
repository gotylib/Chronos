using Chronos.Core;
using SampleTests;

var config = new ComposeBuilder()
    .WithVersion("3.8")
    .AddNetwork("app-net")
    .AddVolume("pgdata")
    .AddService(s => s
        .WithName("postgres")
        .UseImage("postgres:16-alpine")
        .WithRestartPolicy("unless-stopped")
        .AddEnvironment("POSTGRES_USER", "app")
        .AddEnvironment("POSTGRES_PASSWORD", "app_password")
        .AddEnvironment("POSTGRES_DB", "appdb")
        .AddPort(5432, 5432)
        .ConnectToNetwork("app-net")
        .AddVolume("pgdata", "/var/lib/postgresql/data"))
    .AddService(s => s
        .WithName("nginx")
        .UseImage("nginx:alpine")
        .WithRestartPolicy("unless-stopped")
        .AddPort(8080, 80)
        .ConnectToNetwork("app-net")
        .DependsOn("postgres")
        .UseTests<NginxTest>());

await config.StartAsync();