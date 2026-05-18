using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chronos.Master.Application.Abstractions;
using Chronos.Master.Application.Cluster;
using Chronos.Master.Application.Contracts;

namespace Chronos.Master.Api;

/// <summary>
/// Основной REST Master: реестр агентов и аудит; кластерный деплой с выбором агента и прокси к нему.
/// </summary>
public static class MasterApiExtensions
{
    private static readonly JsonSerializerOptions AgentArchiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WebApplication MapChronosMasterEndpoints(this WebApplication app)
    {
        var expectedApiKey = app.Configuration["CHRONOS_MASTER_API_KEY"];

        // --- Регистрация агентов, heartbeat, список агентов, журнал аудита ---

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

        // --- Деплой и публикация на выбранного агента; затем маршруты /cluster/projects/* (прокси) ---

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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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
            }
            catch (Exception ex)
            {
                await AuditAsync(store, request, "cluster.publish", "exception", ex.Message, ct).ConfigureAwait(false);
                return Results.Json(new ClusterDeployResult
                {
                    Success = false,
                    AgentId = selected.AgentId,
                    AgentUrl = selected.BaseUrl,
                    Error = ex.Message
                });
            }
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

        app.MapGet("/cluster/projects/archived", async (
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var rows = await store.ListArchivedProjectsAsync(ct).ConfigureAwait(false);
            await AuditAsync(store, request, "cluster.archived.list", "success", $"count={rows.Count}", ct).ConfigureAwait(false);
            return Results.Json(rows);
        });

        app.MapPost("/cluster/projects/archived/{archiveId}/restore", async (
            string archiveId,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var row = await store.GetArchivedProjectAsync(archiveId, ct).ConfigureAwait(false);
            if (row == null)
                return Results.NotFound();

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var emptyBody = new MultipartFormDataContent();
            var resp = await http.PostAsync(
                    $"{row.AgentUrl.TrimEnd('/')}/projects/archived/{Uri.EscapeDataString(archiveId)}/restore",
                    emptyBody,
                    ct)
                .ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                await AuditAsync(store, request, "cluster.archived.restore", "agent_error",
                    $"archive={archiveId}; HTTP {(int)resp.StatusCode}; {text}", ct).ConfigureAwait(false);
                return Results.Content(text, resp.Content.Headers.ContentType?.MediaType ?? "application/json",
                    statusCode: (int)resp.StatusCode);
            }

            await store.UpsertProjectPlacementAsync(row.ProjectName, row.AgentId, row.AgentUrl, ct).ConfigureAwait(false);
            await store.DeleteArchivedProjectAsync(archiveId, ct).ConfigureAwait(false);
            await AuditAsync(store, request, "cluster.archived.restore", "success",
                $"project={row.ProjectName}; archive={archiveId}", ct).ConfigureAwait(false);
            return Results.Content(text, "application/json");
        });

        app.MapDelete("/cluster/projects/archived/{archiveId}", async (
            string archiveId,
            IMasterPersistence store,
            IHttpClientFactory httpFactory,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var row = await store.GetArchivedProjectAsync(archiveId, ct).ConfigureAwait(false);
            if (row == null)
                return Results.NotFound();

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var resp = await http
                .DeleteAsync($"{row.AgentUrl.TrimEnd('/')}/projects/archived/{Uri.EscapeDataString(archiveId)}", ct)
                .ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
            {
                await AuditAsync(store, request, "cluster.archived.purge", "agent_error",
                    $"archive={archiveId}; HTTP {(int)resp.StatusCode}; {text}", ct).ConfigureAwait(false);
                return Results.Content(text, resp.Content.Headers.ContentType?.MediaType ?? "application/json",
                    statusCode: (int)resp.StatusCode);
            }

            await store.DeleteArchivedProjectAsync(archiveId, ct).ConfigureAwait(false);
            await AuditAsync(store, request, "cluster.archived.purge", "success", $"archive={archiveId}", ct).ConfigureAwait(false);
            return Results.Json(new { purged = true });
        });

        app.MapPost("/cluster/projects/{projectName}/archive", async (
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var emptyBody = new MultipartFormDataContent();
            var resp = await http.PostAsync(
                    $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/archive",
                    emptyBody,
                    ct)
                .ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                await AuditAsync(store, request, "cluster.project.archive", "agent_error",
                    $"project={projectName}; HTTP {(int)resp.StatusCode}; {text}", ct).ConfigureAwait(false);
                return Results.Content(text, resp.Content.Headers.ContentType?.MediaType ?? "application/json",
                    statusCode: (int)resp.StatusCode);
            }

            ArchiveProjectAgentResponse? agentArchive;
            try
            {
                agentArchive = JsonSerializer.Deserialize<ArchiveProjectAgentResponse>(text, AgentArchiveJsonOptions);
            }
            catch (Exception ex)
            {
                await AuditAsync(store, request, "cluster.project.archive", "bad_agent_json", $"{projectName}: {ex.Message}", ct)
                    .ConfigureAwait(false);
                return Results.Problem(detail: $"Agent returned invalid JSON: {ex.Message}");
            }

            if (agentArchive == null || string.IsNullOrWhiteSpace(agentArchive.ArchiveId))
            {
                await AuditAsync(store, request, "cluster.project.archive", "bad_agent_payload", text, ct).ConfigureAwait(false);
                return Results.Problem(detail: "Agent archive response missing archiveId.");
            }

            await store.DeleteVolumePlacementsForProjectAsync(agentArchive.ProjectName, ct).ConfigureAwait(false);
            await store.DeleteProjectPlacementAsync(agentArchive.ProjectName, ct).ConfigureAwait(false);
            await store.AddArchivedProjectAsync(new ArchivedProjectInfo
            {
                ArchiveId = agentArchive.ArchiveId,
                ProjectName = agentArchive.ProjectName,
                AgentId = placement.AgentId,
                AgentUrl = placement.AgentUrl,
                ArchivedUtc = agentArchive.ArchivedUtc,
                PurgeAfterUtc = agentArchive.PurgeAfterUtc
            }, ct).ConfigureAwait(false);

            await AuditAsync(store, request, "cluster.project.archive", "success",
                $"project={agentArchive.ProjectName}; archive={agentArchive.ArchiveId}", ct).ConfigureAwait(false);
            return Results.Content(text, "application/json");
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var resp = await http.GetAsync($"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/compose", ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Content(text, "text/plain; charset=utf-8", statusCode: (int)resp.StatusCode);
        });

        app.MapPost("/cluster/projects/{projectName}/compose", async (
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var content = await BuildForwardedMultipartAsync(request, ct).ConfigureAwait(false);
            var resp = await http.PostAsync(
                    $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/compose",
                    content,
                    ct)
                .ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Content(text, resp.Content.Headers.ContentType?.MediaType ?? "application/json",
                statusCode: (int)resp.StatusCode);
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var content = await BuildForwardedMultipartAsync(request, ct).ConfigureAwait(false);
            var resp = await http.PostAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/start",
                content,
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Content(text, "application/json", statusCode: (int)resp.StatusCode);
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            using var content = await BuildForwardedMultipartAsync(request, ct).ConfigureAwait(false);
            var resp = await http.PostAsync(
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/restart?removeVolumes={removeVolumes}",
                content,
                ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Results.Content(text, "application/json", statusCode: (int)resp.StatusCode);
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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

            using var http = httpFactory.CreateClient("MasterApiProxy");
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
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var placement = await store.GetProjectPlacementAsync(projectName, ct).ConfigureAwait(false);
            if (placement == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                await httpContext.Response.WriteAsync($"Project '{projectName}' is not known by master.", ct).ConfigureAwait(false);
                return;
            }

            using var http = httpFactory.CreateClient("MasterApiProxy");
            if (!string.IsNullOrWhiteSpace(expectedApiKey))
                http.DefaultRequestHeaders.Add("X-API-Key", expectedApiKey);

            var mode = string.IsNullOrWhiteSpace(compress) ? "gzip" : compress;
            var url =
                $"{placement.AgentUrl.TrimEnd('/')}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/snapshot?compress={Uri.EscapeDataString(mode)}";

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                httpContext.Response.StatusCode = (int)resp.StatusCode;
                httpContext.Response.ContentType = "text/plain; charset=utf-8";
                await resp.Content.CopyToAsync(httpContext.Response.Body, ct).ConfigureAwait(false);
                return;
            }

            var fileName = $"{projectName}_{volumeName}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{(mode == "none" ? "tar" : "tar.gz")}";
            var contentType = mode == "none" ? "application/x-tar" : "application/gzip";
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = contentType;
            var safeName = fileName.Replace("\"", "", StringComparison.Ordinal);
            httpContext.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{safeName}\"");

            await resp.Content.CopyToAsync(httpContext.Response.Body, ct).ConfigureAwait(false);
        });

        app.MapGet("/cluster/volume-backup-policies", async (
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var list = await store.ListVolumeBackupPoliciesAsync(ct).ConfigureAwait(false);
            return Results.Json(list);
        });

        app.MapPost("/cluster/volume-backup-policies", async (
            VolumeBackupPolicyCreateRequest body,
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(body.ProjectName))
                return Results.BadRequest("projectName is required.");
            if (body.MinCopies < 1)
                return Results.BadRequest("minCopies must be >= 1.");
            if (body.MaxCopies < body.MinCopies)
                return Results.BadRequest("maxCopies must be >= minCopies.");
            if (body.MinMinutesBetweenBackups < 1)
                return Results.BadRequest("minMinutesBetweenBackups must be >= 1.");
            if (body.MinutesCooldownPerGb < 0)
                return Results.BadRequest("minutesCooldownPerGb must be >= 0.");
            if (body.MaxCooldownMinutes < body.MinMinutesBetweenBackups)
                return Results.BadRequest("maxCooldownMinutes must be >= minMinutesBetweenBackups.");
            if (body.MinimumFreeDiskMb is < 1)
                return Results.BadRequest("minimumFreeDiskMb must be >= 1 when set.");

            var id = await store.CreateVolumeBackupPolicyAsync(body, ct).ConfigureAwait(false);
            return Results.Created($"/cluster/volume-backup-policies/{id}", new { id });
        });

        app.MapDelete("/cluster/volume-backup-policies/{id:guid}", async (
            Guid id,
            IMasterPersistence store,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var ok = await store.DeleteVolumeBackupPolicyAsync(id, ct).ConfigureAwait(false);
            return ok ? Results.NoContent() : Results.NotFound();
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

    private static async Task<MultipartFormDataContent> BuildForwardedMultipartAsync(HttpRequest request, CancellationToken ct)
    {
        var content = new MultipartFormDataContent();
        if (!request.HasFormContentType)
            return content;

        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        foreach (var kv in form)
        {
            foreach (var value in kv.Value)
                content.Add(new StringContent(value ?? string.Empty), kv.Key);
        }

        foreach (var file in form.Files)
        {
            var streamContent = new StreamContent(file.OpenReadStream());
            if (!string.IsNullOrEmpty(file.ContentType))
                streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            content.Add(streamContent, file.Name, file.FileName ?? file.Name);
        }

        return content;
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
