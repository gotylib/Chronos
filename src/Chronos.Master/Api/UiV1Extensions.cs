using Chronos.Core;
using Chronos.Core.Compose.Implementation;
using Chronos.Master.Application;
using Chronos.Master.Application.Abstractions;

namespace Chronos.Master.Api;

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

        v1.MapGet("/traefik/routes", (
            HttpRequest request,
            IHostEnvironment env,
            IConfiguration cfg) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var dir = cfg["CHRONOS_TRAEFIK_DYNAMIC_DIR"];
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return Results.Json(new TraefikRoutesListDto { Directory = dir, Routes = new List<TraefikRouteFileDto>() });

            var routes = Directory.GetFiles(dir, "chronos-route-*.yml")
                .Select(p => new TraefikRouteFileDto
                {
                    FileName = Path.GetFileName(p),
                    Yaml = File.ReadAllText(p)
                })
                .ToList();

            return Results.Json(new TraefikRoutesListDto { Directory = dir, Routes = routes });
        })
        .WithTags("ui");

        v1.MapPost("/traefik/routes", async (
            UpsertTraefikRouteRequest body,
            HttpRequest request,
            IReverseProxyConfigurator proxy,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(body.RouteName) || string.IsNullOrWhiteSpace(body.Rule) ||
                body.BackendUrls == null || body.BackendUrls.Count == 0)
                return Results.BadRequest(new { error = "routeName, rule and backendUrls are required." });

            await proxy.UpsertHttpRouteAsync(body.RouteName.Trim(), body.Rule.Trim(), body.BackendUrls, ct)
                .ConfigureAwait(false);
            return Results.Ok(new { ok = true });
        })
        .WithTags("ui");

        v1.MapDelete("/traefik/routes/{routeName}", async (
            string routeName,
            HttpRequest request,
            IReverseProxyConfigurator proxy,
            CancellationToken ct) =>
        {
            if (!IsUiAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await proxy.RemoveRouteAsync(routeName, ct).ConfigureAwait(false);
            return Results.Ok(new { ok = true });
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

public sealed class UpsertTraefikRouteRequest
{
    public string RouteName { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public List<string> BackendUrls { get; set; } = new();
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

public sealed class TraefikRoutesListDto
{
    public string? Directory { get; init; }
    public List<TraefikRouteFileDto> Routes { get; init; } = new();
}

public sealed class TraefikRouteFileDto
{
    public string FileName { get; init; } = string.Empty;
    public string Yaml { get; init; } = string.Empty;
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
