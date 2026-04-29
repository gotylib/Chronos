// Chronos.Agent — процесс на хосте с Docker: compose-проекты на диске,
// Minimal API (start/stop/compose/volumes/…), регистрация в Master, EF-метаданные.
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chronos.Agent;
using Chronos.Agent.Api;
using Chronos.Agent.Application;
using Chronos.Agent.Domain;
using Chronos.Agent.Domain.Entities;
using Chronos.Agent.Infrastructure.Persistence;
using Chronos.Core.Safety;
using Chronos.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsLinux())
    builder.Host.UseSystemd();

// Конфигурация через переменные окружения (пути, ключи, связь с Master).
var appPath = builder.Configuration["CHRONOS_AGENT_APP_PATH"] ?? "/app";
var composeFileName = builder.Configuration["CHRONOS_AGENT_COMPOSE_FILE"] ?? "docker-compose.yml";
var dockerComposeExecutable = DockerComposeExecutableResolver.Resolve(builder.Configuration["CHRONOS_AGENT_DOCKER_COMPOSE_EXECUTABLE"]);
Console.WriteLine($"[agent] Docker Compose CLI: {dockerComposeExecutable}");
var dockerExecutable = builder.Configuration["CHRONOS_AGENT_DOCKER_EXECUTABLE"] ?? "docker";
var archiveImage = builder.Configuration["CHRONOS_AGENT_ARCHIVE_IMAGE"] ?? "alpine:latest";
var expectedApiKey = builder.Configuration["CHRONOS_AGENT_API_KEY"];

// Регистрация на Master (если заданы URL): сервис доступен master по agentBaseUrl.
var masterUrl = builder.Configuration["CHRONOS_MASTER_URL"];
var agentBaseUrl = builder.Configuration["CHRONOS_AGENT_BASE_URL"]; // например http://agent:5000 — как обращается Master
var agentLocation = builder.Configuration["CHRONOS_AGENT_LOCATION"];
var configuredAgentId = builder.Configuration["CHRONOS_AGENT_ID"];
var masterApiKey = builder.Configuration["CHRONOS_MASTER_API_KEY"];

var agentPaths = new AgentPaths
{
    AppPath = appPath,
    ComposeFileName = composeFileName,
    DockerComposeExecutable = dockerComposeExecutable,
    DockerExecutable = dockerExecutable,
    ArchiveImage = archiveImage
};

builder.Services.AddSingleton(agentPaths);

var metadataConnectionString = ResolveAgentMetadataConnection(builder.Configuration, appPath);
builder.Services.AddDbContext<ChronosAgentDbContext>((sp, opts) =>
{
    if (metadataConnectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
        opts.UseNpgsql(metadataConnectionString);
    else
        opts.UseSqlite(metadataConnectionString);
});

builder.Services.AddHttpClient();
builder.Services.AddHttpClient(nameof(AgentRoutes));
builder.Services.AddHttpClient(nameof(SchedulerHostedService));
builder.Services.AddHostedService<SchedulerHostedService>();

var deploymentLock = new SemaphoreSlim(1, 1);
var volumeLock = new SemaphoreSlim(2, 2);

var execTimeoutSeconds = int.TryParse(builder.Configuration["CHRONOS_AGENT_TEST_EXECUTION_TIMEOUT_SECONDS"], out var t)
    ? t
    : 30;
var maxParallelPerProject = int.TryParse(builder.Configuration["CHRONOS_AGENT_MAX_PARALLEL_TESTS_PER_PROJECT"], out var pp)
    ? pp
    : 5;
var maxParallelTotal = int.TryParse(builder.Configuration["CHRONOS_AGENT_MAX_PARALLEL_TESTS_TOTAL"], out var mt)
    ? mt
    : 20;

builder.Services.AddSingleton(new ExecutionPolicyOptions
{
    TestExecutionTimeout = TimeSpan.FromSeconds(Math.Max(1, execTimeoutSeconds)),
    MaxParallelTestsPerProject = Math.Max(1, maxParallelPerProject),
    MaxParallelTestsTotal = Math.Max(1, maxParallelTotal)
});
builder.Services.AddSingleton<ExecutionThrottler>(sp =>
{
    var opts = sp.GetRequiredService<ExecutionPolicyOptions>();
    return new ExecutionThrottler(opts.MaxParallelTestsTotal, opts.MaxParallelTestsPerProject);
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbMeta = scope.ServiceProvider.GetRequiredService<ChronosAgentDbContext>();
    if (metadataConnectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
        await dbMeta.Database.MigrateAsync().ConfigureAwait(false);
    else
        await dbMeta.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

var throttler = app.Services.GetRequiredService<ExecutionThrottler>();
var policy = app.Services.GetRequiredService<ExecutionPolicyOptions>();
// Расширенные маршруты: manifest, артефакты, тома, восстановление — см. AgentRoutes.
AgentRoutes.MapAgentRoutes(app, agentPaths, expectedApiKey, deploymentLock, volumeLock, throttler, policy);

if (!string.IsNullOrWhiteSpace(masterUrl) && !string.IsNullOrWhiteSpace(agentBaseUrl))
{
    var agentId = await GetOrCreateAgentIdAsync(appPath, configuredAgentId);

    // Цикл регистрации агента на Master и периодический heartbeat.
    _ = Task.Run(async () =>
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrWhiteSpace(masterApiKey))
            http.DefaultRequestHeaders.Add("X-API-Key", masterApiKey);
        var capabilities = new Dictionary<string, string>
        {
            ["dockerComposeExecutable"] = dockerComposeExecutable,
            ["dockerExecutable"] = dockerExecutable,
        };

        // One best-effort registration.
        while (!app.Lifetime.ApplicationStopped.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"[agent] Register to master: {masterUrl}/agents/register (id={agentId})");
                var payload = new
                {
                    AgentId = agentId,
                    BaseUrl = agentBaseUrl.TrimEnd('/'),
                    Location = string.IsNullOrWhiteSpace(agentLocation) ? null : agentLocation,
                    Capabilities = capabilities
                };

                // Re-register on every loop until success; simple for MVP.
                var reg = await http.PostAsJsonAsync($"{masterUrl.TrimEnd('/')}/agents/register", payload);
                if (reg.IsSuccessStatusCode)
                    break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[agent] Master register failed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        // Heartbeats every ~30 seconds.
        while (!app.Lifetime.ApplicationStopped.IsCancellationRequested)
        {
            try
            {
                var cpu = await HostMetrics.GetCpuPercentAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
                var mem = HostMetrics.GetMemoryPercent();
                var disk = HostMetrics.GetDiskPercent(appPath);
                var hb = new
                {
                    CpuPercent = cpu,
                    MemoryPercent = mem,
                    DiskPercent = disk
                };
                var resp = await http.PostAsJsonAsync(
                    $"{masterUrl.TrimEnd('/')}/agents/{Uri.EscapeDataString(agentId)}/heartbeat",
                    hb);
                if (!resp.IsSuccessStatusCode)
                    Console.WriteLine($"[agent] Master heartbeat failed: {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[agent] Master heartbeat error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(30));
        }
    });
}
else
{
    if (!string.IsNullOrWhiteSpace(masterUrl) && string.IsNullOrWhiteSpace(agentBaseUrl))
        Console.WriteLine("[agent] CHRONOS_MASTER_URL is set but CHRONOS_AGENT_BASE_URL is empty; master registry disabled.");
}

static async Task<string> GetOrCreateAgentIdAsync(string appPath, string? configuredAgentId)
{
    if (!string.IsNullOrWhiteSpace(configuredAgentId))
        return configuredAgentId.Trim();

    var chronosDir = Path.Combine(appPath, ".chronos");
    Directory.CreateDirectory(chronosDir);
    var idPath = Path.Combine(chronosDir, "agent_id.txt");

    if (File.Exists(idPath))
    {
        var existing = (await File.ReadAllTextAsync(idPath).ConfigureAwait(false)).Trim();
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;
    }

    var id = Guid.NewGuid().ToString("N");
    await File.WriteAllTextAsync(idPath, id).ConfigureAwait(false);
    return id;
}
app.MapMainRoutes(
    agentPaths: agentPaths,
    expectedApiKey: expectedApiKey,
    deploymentLock: deploymentLock,
    composeFileName: composeFileName,
    dockerComposeExecutable: dockerComposeExecutable,
    dockerExecutable: dockerExecutable,
    appPath: appPath,
    throttler: throttler,
    policy: policy);

app.Run();

static string ResolveAgentMetadataConnection(IConfiguration configuration, string agentAppPath)
{
    var cs =
        configuration.GetConnectionString("AgentMetadata")
        ?? configuration["ConnectionStrings__AgentMetadata"];

    if (!string.IsNullOrWhiteSpace(cs))
        return cs;

    var path = configuration["CHRONOS_AGENT_METADATA_DB"]
        ?? Path.Combine(agentAppPath, ".chronos", "metadata.db");

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    return $"Data Source={path}";
}
