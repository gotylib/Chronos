using Chronos.Master.Api;
using Chronos.Master.Application.Abstractions;
using Chronos.Master.Application.Services;
using Chronos.Master.Infrastructure.Persistence;
using Chronos.Master.Infrastructure.Proxy;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = ResolveConnectionString(builder.Configuration);
builder.Services.AddDbContextFactory<ChronosMasterDbContext>(opts =>
    opts.UseNpgsql(connectionString));
builder.Services.AddScoped<ChronosMasterDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ChronosMasterDbContext>>().CreateDbContext());

builder.Services.AddScoped<IMasterPersistence, MasterPersistenceService>();
builder.Services.AddSingleton<ILeaderElectionService, LeaderElectionService>();
builder.Services.AddHostedService<LeaderElectionHostedService>();
builder.Services.AddHostedService<VolumeReplicationHostedService>();
builder.Services.AddSingleton<IReverseProxyConfigurator>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var dir = cfg["CHRONOS_TRAEFIK_DYNAMIC_DIR"];
    if (string.IsNullOrWhiteSpace(dir))
        return new NoOpReverseProxyConfigurator();
    return new TraefikFileReverseProxyConfigurator(
        dir,
        sp.GetRequiredService<ILogger<TraefikFileReverseProxyConfigurator>>());
});
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Chronos Master API",
        Version = "v1"
    });
});
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChronosMasterDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Chronos Master v1"));
}

app.MapChronosMasterEndpoints();
app.MapChronosSandboxV1();
app.MapChronosUiV1();

var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var uiIndexPath = Path.Combine(webRoot, "ui", "index.html");
if (File.Exists(uiIndexPath))
{
    app.MapFallbackToFile("/ui/{*path:nonfile}", "ui/index.html");
    app.MapGet("/", () => Results.Redirect("/ui"));
}
else
{
    app.MapGet("/", () => "Chronos.Master is running.");
}

var agentTtlSeconds = int.TryParse(app.Configuration["CHRONOS_MASTER_AGENT_TTL_SECONDS"], out var ttl)
    ? ttl
    : 120;
var auditRetentionDays = int.TryParse(app.Configuration["CHRONOS_MASTER_AUDIT_RETENTION_DAYS"], out var rd)
    ? rd
    : 90;

_ = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopped.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            using var scope = app.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IMasterPersistence>();
            await store.DeleteStaleAgentsAsync(TimeSpan.FromSeconds(agentTtlSeconds), CancellationToken.None)
                .ConfigureAwait(false);
            await store.DeleteOldAuditAsync(TimeSpan.FromDays(auditRetentionDays), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }
});

app.Run();

static string ResolveConnectionString(IConfiguration configuration)
{
    var cs =
        configuration.GetConnectionString("Chronos")
        ?? configuration["ConnectionStrings__Chronos"];

    if (!string.IsNullOrWhiteSpace(cs))
        return cs;

    throw new InvalidOperationException(
        "Configure PostgreSQL with ConnectionStrings:Chronos or environment ConnectionStrings__Chronos.");
}
