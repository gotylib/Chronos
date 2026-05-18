using Chronos.Core;
using Chronos.Core.Compose.Implementation;
using Chronos.Master.Application;
using Chronos.Master.Application.Abstractions;

namespace Chronos.Master.Api;

/// <summary>API для React UI под <c>/api/v1</c>: граф compose, статус проекта, HAProxy TCP-маршруты, прокси диагностик.</summary>
public static class UiV1Extensions
{
    public static WebApplication MapChronosUiV1(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");
        var expectedApiKey = app.Configuration["CHRONOS_MASTER_API_KEY"];

        v1.MapPost("/compose/graph", (
            GraphFromYamlRequest body,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ComposeYaml))
                return Results.BadRequest(new { error = "composeYaml is required." });
            try
            {
                var builder = ComposeYamlParser.Parse(body.ComposeYaml);
                return Results.Json(builder.DescribeGraph());
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithTags("ui")
        .WithDescription("Service / network / volume graph for compose YAML.");

        v1.MapGet("/cluster/projects/{projectName}/diagnostics", async (
            string projectName,
            Chronos.Master.Application.Abstractions.IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound(new { error = $"Project '{projectName}' not known." });

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var resp = await http.GetAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/chronos/diagnostics",
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json", statusCode: (int)resp.StatusCode);
        })
        .WithTags("ui");

        v1.MapGet("/cluster/projects/{projectName}/full-status", async (
            string projectName,
            Chronos.Master.Application.Abstractions.IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound(new { error = $"Project '{projectName}' not known." });

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var statusResp = await http.GetAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/status",
                ct).ConfigureAwait(false);
            var statusText = await statusResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var composeResp = await http.GetAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/compose",
                ct).ConfigureAwait(false);
            var composeText = await composeResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var diagResp = await http.GetAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/chronos/diagnostics",
                ct).ConfigureAwait(false);
            var diagText = await diagResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return Results.Json(new ProjectFullStatusDto
            {
                ProjectName = projectName,
                AgentId = placement.AgentId,
                AgentUrl = placement.AgentUrl,
                StatusJson = statusText,
                ComposeYaml = composeText,
                DiagnosticsJson = diagText,
                StatusOk = statusResp.IsSuccessStatusCode,
                ComposeOk = composeResp.IsSuccessStatusCode,
                DiagnosticsOk = diagResp.IsSuccessStatusCode
            });
        })
        .WithTags("ui");

        v1.MapGet("/haproxy/tcp-routes", async (
            HttpRequest request,
            IHaproxyTcpRouteRegistry registry,
            IConfiguration cfg,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var routes = await registry.ListAsync(ct).ConfigureAwait(false);
            var generatedCfg = await registry.ReadGeneratedCfgAsync(ct).ConfigureAwait(false);
            int? suggestedBackendPort = null;
            if (int.TryParse(cfg["CHRONOS_HAPROXY_TCP_SUGGESTED_BACKEND_PORT"], out var sbp) && sbp is >= 1 and <= 65535)
                suggestedBackendPort = sbp;

            int? listenMin = null;
            int? listenMax = null;
            if (int.TryParse(cfg["CHRONOS_HAPROXY_LISTEN_PORT_MIN"], out var lmn) && lmn is >= 1 and <= 65535)
                listenMin = lmn;
            if (int.TryParse(cfg["CHRONOS_HAPROXY_LISTEN_PORT_MAX"], out var lmx) && lmx is >= 1 and <= 65535)
                listenMax = lmx;
            if (listenMin is null || listenMax is null || listenMin > listenMax)
            {
                listenMin = null;
                listenMax = null;
            }

            return Results.Json(new HaproxyTcpRoutesApiDto
            {
                DynamicDirectory = registry.DynamicDirectory,
                MasterPublicHost = cfg["CHRONOS_MASTER_PUBLIC_HOST"]?.Trim(),
                Routes = routes.ToList(),
                GeneratedCfg = generatedCfg,
                SuggestedBackendHost = cfg["CHRONOS_HAPROXY_TCP_SUGGESTED_BACKEND_HOST"]?.Trim(),
                SuggestedBackendPort = suggestedBackendPort,
                ListenPortMin = listenMin,
                ListenPortMax = listenMax
            });
        })
        .WithTags("ui");

        v1.MapPost("/haproxy/tcp-routes", async (
            AddHaproxyTcpRouteRequest body,
            HttpRequest request,
            IHaproxyTcpRouteRegistry registry,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (registry.DynamicDirectory == null)
                return Results.BadRequest(new { error = "CHRONOS_HAPROXY_DYNAMIC_DIR is not set on Master." });

            if (string.IsNullOrWhiteSpace(body.BackendHost) || body.BackendPort is < 1 or > 65535)
                return Results.BadRequest(new { error = "backendHost and backendPort (1–65535) are required." });

            var add = await registry.TryAddAsync(body, ct).ConfigureAwait(false);
            if (!add.Ok)
                return Results.BadRequest(new { error = add.Error ?? "Could not add route." });

            return Results.Json(add.Route);
        })
        .WithTags("ui");

        v1.MapDelete("/haproxy/tcp-routes/{id}", async (
            string id,
            HttpRequest request,
            IHaproxyTcpRouteRegistry registry,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (registry.DynamicDirectory == null)
                return Results.BadRequest(new { error = "CHRONOS_HAPROXY_DYNAMIC_DIR is not set on Master." });

            var ok = await registry.RemoveAsync(id, ct).ConfigureAwait(false);
            return ok ? Results.Ok(new { ok = true }) : Results.NotFound();
        })
        .WithTags("ui");

        v1.MapPost("/sandbox/fluent-preview", async (
            FluentPreviewRequest body,
            HttpRequest request,
            IHostEnvironment env,
            IConfiguration cfg,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (!FluentComposeScriptRunner.IsEnabled(env, cfg))
                return Results.Json(new FluentPreviewResponse
                {
                    Enabled = false,
                    Success = false,
                    Error = "Fluent sandbox disabled. Set CHRONOS_MASTER_FLUENT_SANDBOX_ENABLED=1 or ASPNETCORE_ENVIRONMENT=Development."
                });

            if (string.IsNullOrWhiteSpace(body.Code))
                return Results.BadRequest(new FluentPreviewResponse { Success = false, Error = "code is required." });

            var (compose, err) = await FluentComposeScriptRunner.BuildComposeAsync(body.Code!, env, cfg, ct)
                .ConfigureAwait(false);
            if (compose == null)
                return Results.Json(new FluentPreviewResponse { Success = false, Error = err });

            try
            {
                var validation = await compose.ValidateAsync(cancellationToken: ct).ConfigureAwait(false);
                var yaml = compose.GenerateYaml();
                var preview = yaml.Length > 96_000 ? yaml[..96_000] + "\n… (truncated)" : yaml;
                return Results.Json(new FluentPreviewResponse
                {
                    Enabled = true,
                    Success = validation.IsValid,
                    Validation = validation,
                    GeneratedYamlPreview = preview,
                    Error = validation.IsValid ? null : string.Join("; ", validation.Errors.ConvertAll(e => e.Message))
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new FluentPreviewResponse
                {
                    Enabled = true,
                    Success = false,
                    Error = ex.Message
                });
            }
        })
        .WithTags("sandbox");

        return app;
    }

    private static bool IsUiAuthorized(HttpRequest request, string? expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return true;

        if (request.Query.TryGetValue("apiKey", out var queryApiKey))
            return string.Equals(queryApiKey.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal);

        if (request.Headers.TryGetValue("X-API-Key", out var values))
            return string.Equals(values.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal);

        return false;
    }
}

public sealed class GraphFromYamlRequest
{
    public string ComposeYaml { get; set; } = string.Empty;
}

public sealed class FluentPreviewRequest
{
    public string? Code { get; set; }
}

public sealed class FluentPreviewResponse
{
    public bool Enabled { get; init; } = true;
    public bool Success { get; init; }
    public ValidationResult? Validation { get; init; }
    public string? GeneratedYamlPreview { get; init; }
    public string? Error { get; init; }
}

public sealed class HaproxyTcpRoutesApiDto
{
    public string? DynamicDirectory { get; init; }
    public string? MasterPublicHost { get; init; }
    public List<HaproxyTcpRouteDto> Routes { get; init; } = new();
    public string? GeneratedCfg { get; init; }

    /// <summary>Подсказка для UI: куда HAProxy в Docker должен проксировать (DNS сервиса агента, не 127.0.0.1).</summary>
    public string? SuggestedBackendHost { get; init; }

    public int? SuggestedBackendPort { get; init; }

    /// <summary>Если заданы оба — авто listen только в этом диапазоне (как в docker compose ports у HAProxy).</summary>
    public int? ListenPortMin { get; init; }

    public int? ListenPortMax { get; init; }
}

public sealed class ProjectFullStatusDto
{
    public string ProjectName { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentUrl { get; init; } = string.Empty;
    public string StatusJson { get; init; } = string.Empty;
    public string ComposeYaml { get; init; } = string.Empty;
    public string DiagnosticsJson { get; init; } = string.Empty;
    public bool StatusOk { get; init; }
    public bool ComposeOk { get; init; }
    public bool DiagnosticsOk { get; init; }
}
