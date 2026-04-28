using System.Text.Json.Nodes;
using Chronos.Master.Application.Abstractions;
using Chronos.Master.Application.Cluster;

namespace Chronos.Master.Api;

public static class MasterApiExtensions
{
    public static WebApplication MapChronosMasterEndpoints(this WebApplication app)
    {
        var expectedApiKey = app.Configuration["CHRONOS_MASTER_API_KEY"];

        app.MapPost("/agents/register", async (
            AgentRegistrationRequest body,
            HttpRequest request,
            IMasterPersistence store,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
            {
                await AuditAsync(store, request, "agent.register", "unauthorized", null, ct).ConfigureAwait(false);
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(body.AgentId) || string.IsNullOrWhiteSpace(body.BaseUrl))
                return Results.BadRequest("agentId and baseUrl are required.");

            await store.UpsertAgentAsync(body, ct).ConfigureAwait(false);
            await AuditAsync(store, request, "agent.register", "success", $"agent={body.AgentId}", ct).ConfigureAwait(false);
            return Results.Ok();
        });

        app.MapPost("/agents/{agentId}/heartbeat", async (
            string agentId,
            AgentHeartbeatRequest body,
            HttpRequest request,
            IMasterPersistence store,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
            {
                await AuditAsync(store, request, "agent.heartbeat", "unauthorized", $"agent={agentId}", ct).ConfigureAwait(false);
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(agentId))
                return Results.BadRequest("agentId is required.");

            await store.UpdateHeartbeatAsync(agentId, body, ct).ConfigureAwait(false);
            await AuditAsync(store, request, "agent.heartbeat", "success", $"agent={agentId}", ct).ConfigureAwait(false);
            return Results.Ok();
        });

        app.MapGet("/agents", async (
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
            {
                await AuditAsync(store, request, "agents.list", "unauthorized", null, ct).ConfigureAwait(false);
                return Results.Unauthorized();
            }

            var agents = await store.ListAgentsAsync(ct).ConfigureAwait(false);
            await AuditAsync(store, request, "agents.list", "success", $"count={agents.Count}", ct).ConfigureAwait(false);
            return Results.Json(agents);
        });

        app.MapGet("/audit", async (
            int? limit,
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
            {
                await AuditAsync(store, request, "audit.list", "unauthorized", null, ct).ConfigureAwait(false);
                return Results.Unauthorized();
            }

            var records = await store.ListAuditAsync(limit ?? 200, ct).ConfigureAwait(false);
            await AuditAsync(store, request, "audit.list", "success", $"count={records.Count}", ct).ConfigureAwait(false);
            return Results.Json(records);
        });

        app.MapPost("/cluster/deploy", async (
            ClusterDeployRequest body,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
            {
                await store.AppendAuditAsync(new AuditLogEntry
                {
                    UtcTime = DateTimeOffset.UtcNow,
                    Action = "cluster.deploy",
                    Result = "unauthorized",
                    Actor = ActorFromRequest(request),
                    ClientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString()
                }, ct).ConfigureAwait(false);
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(body.ProjectName) || string.IsNullOrWhiteSpace(body.ComposeYaml))
                return Results.BadRequest("projectName and composeYaml are required.");

            var agents = await store.ListAgentsAsync(ct).ConfigureAwait(false);
            if (agents.Count == 0)
                return Results.Json(new ClusterDeployResult { Success = false, Error = "No active agents registered." });

            var selected = AgentSelector.SelectBestAgent(agents, body.PreferredLocation);
            if (selected == null)
                return Results.Json(new ClusterDeployResult { Success = false, Error = "No suitable agent found." });

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            try
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(body.ComposeYaml), "compose");
                var startResp = await http.PostAsync(
                    $"{selected.BaseUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(body.ProjectName)}/start",
                    form,
                    ct).ConfigureAwait(false);
                var startText = await startResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!startResp.IsSuccessStatusCode)
                {
                    await store.AppendAuditAsync(new AuditLogEntry
                    {
                        UtcTime = DateTimeOffset.UtcNow,
                        Action = "cluster.deploy",
                        Result = "failed",
                        Actor = ActorFromRequest(request),
                        ClientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                        Details = $"agent={selected.AgentId}; status={(int)startResp.StatusCode}"
                    }, ct).ConfigureAwait(false);

                    return Results.Json(new ClusterDeployResult
                    {
                        Success = false,
                        AgentId = selected.AgentId,
                        AgentUrl = selected.BaseUrl,
                        Error = $"Agent returned {(int)startResp.StatusCode}: {startText}"
                    });
                }

                if (!string.IsNullOrWhiteSpace(body.ManifestJson))
                {
                    using var content = new StringContent(body.ManifestJson, System.Text.Encoding.UTF8, "application/json");
                    var manifestResp = await http.PostAsync(
                        $"{selected.BaseUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(body.ProjectName)}/chronos/manifest",
                        content,
                        ct).ConfigureAwait(false);
                    _ = await manifestResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }

                await store.AppendAuditAsync(new AuditLogEntry
                {
                    UtcTime = DateTimeOffset.UtcNow,
                    Action = "cluster.deploy",
                    Result = "success",
                    Actor = ActorFromRequest(request),
                    ClientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Details = $"project={body.ProjectName}; agent={selected.AgentId}"
                }, ct).ConfigureAwait(false);

                await store.UpsertProjectPlacementAsync(body.ProjectName, selected.AgentId, selected.BaseUrl, ct).ConfigureAwait(false);
                return Results.Json(new ClusterDeployResult
                {
                    Success = true,
                    AgentId = selected.AgentId,
                    AgentUrl = selected.BaseUrl,
                    AgentResponse = JsonNode.Parse(startText)
                });
            }
            catch (Exception ex)
            {
                await store.AppendAuditAsync(new AuditLogEntry
                {
                    UtcTime = DateTimeOffset.UtcNow,
                    Action = "cluster.deploy",
                    Result = "exception",
                    Actor = ActorFromRequest(request),
                    ClientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Details = ex.Message
                }, ct).ConfigureAwait(false);

                return Results.Json(new ClusterDeployResult
                {
                    Success = false,
                    AgentId = selected.AgentId,
                    AgentUrl = selected.BaseUrl,
                    Error = ex.Message
                });
            }
        });

        app.MapPost("/cluster/publish", async (
            ClusterDeployRequest body,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
            {
                await AuditAsync(store, request, "cluster.publish", "unauthorized", null, ct).ConfigureAwait(false);
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(body.ProjectName) || string.IsNullOrWhiteSpace(body.ComposeYaml))
                return Results.BadRequest("projectName and composeYaml are required.");

            var agents = await store.ListAgentsAsync(ct).ConfigureAwait(false);
            if (agents.Count == 0)
            {
                await AuditAsync(store, request, "cluster.publish", "failed", "no-agents", ct).ConfigureAwait(false);
                return Results.Json(new ClusterDeployResult { Success = false, Error = "No active agents registered." });
            }

            var selected = AgentSelector.SelectBestAgent(agents, body.PreferredLocation);
            if (selected == null)
            {
                await AuditAsync(store, request, "cluster.publish", "failed", "no-suitable-agent", ct).ConfigureAwait(false);
                return Results.Json(new ClusterDeployResult { Success = false, Error = "No suitable agent found." });
            }

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(body.ComposeYaml), "compose");

            var startResp = await http.PostAsync(
                $"{selected.BaseUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(body.ProjectName)}/start",
                form,
                ct).ConfigureAwait(false);
            var startText = await startResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!startResp.IsSuccessStatusCode)
            {
                await AuditAsync(store, request, "cluster.publish", "failed", $"agent={selected.AgentId}; status={(int)startResp.StatusCode}", ct).ConfigureAwait(false);
                return Results.Json(new ClusterDeployResult
                {
                    Success = false,
                    AgentId = selected.AgentId,
                    AgentUrl = selected.BaseUrl,
                    Error = $"Agent returned {(int)startResp.StatusCode}: {startText}"
                });
            }

            if (!string.IsNullOrWhiteSpace(body.ManifestJson))
            {
                using var content = new StringContent(body.ManifestJson, System.Text.Encoding.UTF8, "application/json");
                _ = await http.PostAsync(
                    $"{selected.BaseUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(body.ProjectName)}/chronos/manifest",
                    content,
                    ct).ConfigureAwait(false);
            }

            await store.UpsertProjectPlacementAsync(body.ProjectName, selected.AgentId, selected.BaseUrl, ct).ConfigureAwait(false);
            await AuditAsync(store, request, "cluster.publish", "success", $"project={body.ProjectName}; agent={selected.AgentId}", ct).ConfigureAwait(false);

            return Results.Json(new ClusterDeployResult
            {
                Success = true,
                AgentId = selected.AgentId,
                AgentUrl = selected.BaseUrl,
                AgentResponse = JsonNode.Parse(startText)
            });
        });

        app.MapGet("/cluster/projects", async (
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var list = await store.ListProjectPlacementsAsync(ct).ConfigureAwait(false);
            return Results.Json(list);
        });

        app.MapGet("/cluster/projects/{projectName}/status", async (
            string projectName,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var resp = await http.GetAsync($"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/status", ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json");
        });

        app.MapGet("/cluster/projects/{projectName}/compose", async (
            string projectName,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var resp = await http.GetAsync($"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/compose", ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "text/plain");
        });

        app.MapPost("/cluster/projects/{projectName}/stop", async (
            string projectName,
            bool removeVolumes,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);
            using var content = new MultipartFormDataContent();
            var resp = await http.PostAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/stop?removeVolumes={removeVolumes}",
                content,
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json");
        });

        app.MapPost("/cluster/projects/{projectName}/start", async (
            string projectName,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);
            using var content = new MultipartFormDataContent();
            var resp = await http.PostAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/start",
                content,
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json");
        });

        app.MapPost("/cluster/projects/{projectName}/restart", async (
            string projectName,
            bool removeVolumes,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);
            using var content = new MultipartFormDataContent();
            var resp = await http.PostAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/restart?removeVolumes={removeVolumes}",
                content,
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json");
        });

        app.MapGet("/cluster/projects/{projectName}/volumes", async (
            string projectName,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);
            var resp = await http.GetAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volumes",
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json");
        });

        app.MapGet("/cluster/projects/{projectName}/volume-archive-index", async (
            string projectName,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var resp = await http.GetAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volume-archive-index",
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json", statusCode: (int)resp.StatusCode);
        });

        app.MapPost("/cluster/projects/{projectName}/volume-archives/register", async (
            string projectName,
            VolumeArchiveRegisterRequest body,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var resp = await http.PostAsJsonAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volume-archives/register",
                body,
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Text(text, "application/json", statusCode: (int)resp.StatusCode);
        });

        app.MapGet("/cluster/projects/{projectName}/volumes/{volumeName}/snapshot", async (
            string projectName,
            string volumeName,
            string? compress,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
                return Results.NotFound($"Project '{projectName}' is not known by master.");

            using var http = httpFactory.CreateClient();
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var mode = string.IsNullOrWhiteSpace(compress) ? "gzip" : compress;
            var url =
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/snapshot?compress={Uri.EscapeDataString(mode)}";

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Results.Problem(err, statusCode: (int)resp.StatusCode);
            }

            var fileName = $"{projectName}_{volumeName}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{(mode == "none" ? "tar" : "tar.gz")}";
            var contentType = mode == "none" ? "application/x-tar" : "application/gzip";
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return Results.Stream(stream, contentType, fileName);
        });

        return app;
    }

    public sealed class VolumeArchiveRegisterRequest
    {
        public string VolumeName { get; set; } = string.Empty;
        public string StoredRelativePath { get; set; } = string.Empty;
        public long? BytesApprox { get; set; }
        public string? CompressMode { get; set; }
    }

    private static bool IsAuthorized(HttpRequest request, string? expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return true;

        if (request.Query.TryGetValue("apiKey", out var queryApiKey))
            return string.Equals(queryApiKey.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal);

        if (request.Headers.TryGetValue("X-API-Key", out var values))
            return string.Equals(values.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal);

        return false;
    }

    private static string? ActorFromRequest(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-API-Key", out var values))
        {
            var key = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(key))
            {
                var shortKey = key.Length <= 6 ? key : key[..6];
                return $"apiKey:{shortKey}";
            }
        }

        return "anonymous";
    }

    private static async Task AuditAsync(IMasterPersistence store, HttpRequest request, string action, string result, string? details, CancellationToken ct)
    {
        await store.AppendAuditAsync(new AuditLogEntry
        {
            UtcTime = DateTimeOffset.UtcNow,
            Action = action,
            Result = result,
            Actor = ActorFromRequest(request),
            ClientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            Details = details
        }, ct).ConfigureAwait(false);
    }
}
