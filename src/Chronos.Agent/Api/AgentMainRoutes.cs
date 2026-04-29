using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Chronos.Agent.Application;
using Chronos.Agent.Domain;
using Chronos.Agent.Domain.Entities;
using Chronos.Agent.Infrastructure.Persistence;
using Chronos.Core;
using Chronos.Core.Compose.Implementation;
using Chronos.Core.Safety;
using Microsoft.EntityFrameworkCore;

namespace Chronos.Agent.Api;

/// <summary>
/// Primary Agent HTTP endpoints for global and project-scoped compose lifecycle operations.
/// </summary>
public static class AgentMainRoutes
{
    public static void MapMainRoutes(
        this WebApplication app,
        AgentPaths agentPaths,
        string? expectedApiKey,
        SemaphoreSlim deploymentLock,
        string composeFileName,
        string dockerComposeExecutable,
        string dockerExecutable,
        string appPath,
        ExecutionThrottler throttler,
        ExecutionPolicyOptions policy)
    {
        async Task GlobalComposeUpBackgroundAsync(string deploymentId, string workingDirectory, CancellationToken ct)
        {
            await deploymentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var up = await RunProcessAsync(dockerComposeExecutable,
                    $"-f \"{composeFileName}\" up -d",
                    workingDirectory: workingDirectory,
                    ct);

                if (up.ExitCode != 0)
                {
                    await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, false, up.Stderr, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, true, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[agent] Background compose up failed: {ex}");
                try
                {
                    await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, false, ex.Message, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }
            }
            finally
            {
                deploymentLock.Release();
            }
        }

        async Task GlobalRestartBackgroundAsync(string deploymentId, string workingDirectory, bool removeVolumes, CancellationToken ct)
        {
            await deploymentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var argsDown = $"-f \"{composeFileName}\" down";
                if (removeVolumes)
                    argsDown += " -v";

                var down = await RunProcessAsync(dockerComposeExecutable, argsDown, workingDirectory: workingDirectory, ct);
                if (down.ExitCode != 0)
                {
                    await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, false, down.Stderr, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                var up = await RunProcessAsync(dockerComposeExecutable,
                    $"-f \"{composeFileName}\" up -d",
                    workingDirectory: workingDirectory,
                    ct);

                if (up.ExitCode != 0)
                {
                    await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, false, up.Stderr, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, true, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[agent] Background restart failed: {ex}");
                try
                {
                    await DeploymentStateHelper.CompleteAsync(workingDirectory, deploymentId, false, ex.Message, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }
            }
            finally
            {
                deploymentLock.Release();
            }
        }

        async Task ProjectComposeUpBackgroundAsync(string projectName, string projectDir, string deploymentId, CancellationToken ct)
        {
            await deploymentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var up = await RunProcessAsync(dockerComposeExecutable,
                    $"-f \"{composeFileName}\" up -d",
                    workingDirectory: projectDir,
                    ct);

                if (up.ExitCode != 0)
                {
                    await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, false, up.Stderr, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                // Release clients polling DeploymentInProgress before manifest upload + startup checks (Publish pushes manifest after compose is up).
                await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, true, null, CancellationToken.None).ConfigureAwait(false);
                await DeploymentStateHelper.WaitForManifestUploadWindowAsync(projectDir, TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
                await AgentRoutes.RunStartupFromManifestAsync(projectName, projectDir, agentPaths, throttler, policy, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[agent] Background project start failed: {ex}");
                try
                {
                    await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, false, ex.Message, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }
            }
            finally
            {
                deploymentLock.Release();
            }
        }

        async Task ProjectRestartBackgroundAsync(string projectName, string projectDir, string deploymentId, bool removeVolumes, CancellationToken ct)
        {
            await deploymentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var argsDown = $"-f \"{composeFileName}\" down";
                if (removeVolumes)
                    argsDown += " -v";

                var down = await RunProcessAsync(dockerComposeExecutable, argsDown, workingDirectory: projectDir, ct);
                if (down.ExitCode != 0)
                {
                    await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, false, down.Stderr, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                var up = await RunProcessAsync(dockerComposeExecutable,
                    $"-f \"{composeFileName}\" up -d",
                    workingDirectory: projectDir,
                    ct);

                if (up.ExitCode != 0)
                {
                    await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, false, up.Stderr, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, true, null, CancellationToken.None).ConfigureAwait(false);
                await DeploymentStateHelper.WaitForManifestUploadWindowAsync(projectDir, TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
                await AgentRoutes.RunStartupFromManifestAsync(projectName, projectDir, agentPaths, throttler, policy, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[agent] Background project restart failed: {ex}");
                try
                {
                    await DeploymentStateHelper.CompleteAsync(projectDir, deploymentId, false, ex.Message, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }
            }
            finally
            {
                deploymentLock.Release();
            }
        }

        app.MapGet("/", () => "Chronos agent is running.");

        app.MapPost("/deploy", async (HttpRequest request, ChronosAgentDbContext db, CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                var form = await request.ReadFormAsync(ct);
                var composeYaml = form["compose"].ToString();
                
                if (string.IsNullOrWhiteSpace(composeYaml))
                    return Results.BadRequest("Missing form field 'compose'.");

                var composeBuilder = ComposeYamlParser.Parse(composeYaml);

                var serviceEntity = new ServiceEntity
                {
                    ServiceName = composeBuilder.ProjectName,
                    DockerComposeFile = composeFileName,
                    DockerComposeFilePath = Path.Combine(appPath, composeFileName),
                    ImageNames = composeBuilder.Services.Select(s => s.Value.Image).ToList(),
                    VolumeNames = composeBuilder.Volumes.Select(v => v.Value.Name).ToList(),
                };

                Directory.CreateDirectory(appPath);
                var composePath = Path.Combine(appPath, composeFileName);
                await File.WriteAllTextAsync(composePath, composeYaml, Encoding.UTF8, ct);
            }
            finally
            {
                deploymentLock.Release();
            }

            var deployId = Guid.NewGuid().ToString("N");
            await DeploymentStateHelper.WriteInProgressAsync(appPath, deployId, ct).ConfigureAwait(false);
            _ = Task.Run(() => GlobalComposeUpBackgroundAsync(deployId, appPath, CancellationToken.None), CancellationToken.None);

            return Results.Json(new DeployResult
            {
                Success = true,
                DeploymentId = deployId,
                OperationPending = true,
                Message = "Compose deploy started. Poll GET /status for containers and diagnostics."
            });
        });

        app.MapPost("/start", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                var form = await request.ReadFormAsync(ct);
                var composeYaml = form["compose"].ToString();
                if (!string.IsNullOrWhiteSpace(composeYaml))
                {
                    Directory.CreateDirectory(appPath);
                    var composePath = Path.Combine(appPath, composeFileName);
                    await File.WriteAllTextAsync(composePath, composeYaml, Encoding.UTF8, ct);
                }

                if (!Directory.Exists(appPath) || !File.Exists(Path.Combine(appPath, composeFileName)))
                    return Results.Json(new DeployResult { Success = false, Error = $"Compose file '{composeFileName}' not found under app path." });
            }
            finally
            {
                deploymentLock.Release();
            }

            Console.WriteLine("[agent] Queuing docker-compose up -d (async)");
            var deployId = Guid.NewGuid().ToString("N");
            await DeploymentStateHelper.WriteInProgressAsync(appPath, deployId, ct).ConfigureAwait(false);
            _ = Task.Run(() => GlobalComposeUpBackgroundAsync(deployId, appPath, CancellationToken.None), CancellationToken.None);

            return Results.Json(new DeployResult
            {
                Success = true,
                DeploymentId = deployId,
                OperationPending = true,
                Message = "Compose start queued. Poll GET /status for containers and diagnostics."
            });
        });

        app.MapPost("/stop", async (HttpRequest request, CancellationToken ct, bool removeVolumes = false) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                if (!Directory.Exists(appPath))
                    return Results.Json(new DeployResult { Success = false, Error = $"App path '{appPath}' doesn't exist." });

                Console.WriteLine($"[agent] Running docker-compose down{(removeVolumes ? " -v" : "")}");
                var args = $"-f \"{composeFileName}\" down";
                if (removeVolumes)
                    args += " -v";

                var down = await RunProcessAsync(dockerComposeExecutable, args, workingDirectory: appPath, ct);
                if (down.ExitCode != 0)
                {
                    return Results.Json(new DeployResult
                    {
                        Success = false,
                        Error = down.Stderr
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                var status = await GetStatusAsync(dockerComposeExecutable, composeFileName, appPath, ct);
                status = await AttachDiagnosticsAsync(status, appPath, ct);
                return Results.Json(status);
            }
            finally
            {
                deploymentLock.Release();
            }
        });

        app.MapPost("/restart", async (HttpRequest request, CancellationToken ct, bool removeVolumes = false) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                var form = await request.ReadFormAsync(ct);
                var composeYaml = form["compose"].ToString();
                if (!string.IsNullOrWhiteSpace(composeYaml))
                {
                    Directory.CreateDirectory(appPath);
                    var composePath = Path.Combine(appPath, composeFileName);
                    await File.WriteAllTextAsync(composePath, composeYaml, Encoding.UTF8, ct);
                }

                if (!Directory.Exists(appPath) || !File.Exists(Path.Combine(appPath, composeFileName)))
                    return Results.Json(new DeployResult { Success = false, Error = $"Compose file '{composeFileName}' not found under app path." });
            }
            finally
            {
                deploymentLock.Release();
            }

            Console.WriteLine($"[agent] Queuing compose restart (down{(removeVolumes ? " -v" : "")} + up -d)");
            var deployId = Guid.NewGuid().ToString("N");
            await DeploymentStateHelper.WriteInProgressAsync(appPath, deployId, ct).ConfigureAwait(false);
            _ = Task.Run(() => GlobalRestartBackgroundAsync(deployId, appPath, removeVolumes, CancellationToken.None), CancellationToken.None);

            return Results.Json(new DeployResult
            {
                Success = true,
                DeploymentId = deployId,
                OperationPending = true,
                Message = "Compose restart queued. Poll GET /status for containers and diagnostics."
            });
        });

        app.MapGet("/status", async (CancellationToken ct) =>
        {
            if (!Directory.Exists(appPath))
                return Results.Json(new DeployResult { Success = false, Error = $"App path '{appPath}' doesn't exist." });

            var status = await GetStatusAsync(dockerComposeExecutable, composeFileName, appPath, ct);
            status = await AttachDiagnosticsAsync(status, appPath, ct);
            return Results.Json(status);
        });

        app.MapGet("/logs", async (string? service, CancellationToken ct) =>
        {
            var args = new StringBuilder();
            args.Append($"-f \"{composeFileName}\" logs --tail=200");
            if (!string.IsNullOrWhiteSpace(service))
                args.Append($" {service}");

            var logs = await RunProcessAsync(dockerComposeExecutable, args.ToString(), workingDirectory: appPath, ct);
            if (logs.ExitCode != 0)
                return Results.Problem(logs.Stderr, statusCode: 500);

            return Results.Text(LogRedactor.RedactSecrets(logs.Stdout), contentType: "text/plain; charset=utf-8");
        });

        app.MapGet("/projects", () =>
        {
            if (!Directory.Exists(appPath))
                return Results.Json(new List<string>());

            var projects = Directory
                .EnumerateDirectories(appPath)
                .Select(d => Path.GetFileName(d))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => File.Exists(Path.Combine(appPath, name!, composeFileName)))
                .OrderBy(n => n)
                .ToList()!;

            return Results.Json(projects);
        });

        app.MapGet("/projects/{projectName}/compose", async (string projectName) =>
        {
            var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
            var composePath = Path.Combine(projectDir, composeFileName);
            if (!File.Exists(composePath))
                return Results.NotFound();

            var text = await File.ReadAllTextAsync(composePath);
            return Results.Text(text, contentType: "text/plain; charset=utf-8");
        });

        app.MapPost("/projects/{projectName}/compose", async (string projectName, HttpRequest request, CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var form = await request.ReadFormAsync(ct);
            var composeYaml = form["compose"].ToString();
            if (string.IsNullOrWhiteSpace(composeYaml))
                return Results.BadRequest("Missing form field 'compose'.");

            var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
            Directory.CreateDirectory(projectDir);
            var composePath = Path.Combine(projectDir, composeFileName);

            await File.WriteAllTextAsync(composePath, composeYaml, Encoding.UTF8, ct);
            return Results.Ok();
        });

        app.MapPost("/projects/{projectName}/start", async (string projectName, HttpRequest request, CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
                var form = await request.ReadFormAsync(ct);
                var composeYaml = form["compose"].ToString();
                if (!string.IsNullOrWhiteSpace(composeYaml))
                {
                    Directory.CreateDirectory(projectDir);
                    var composePath = Path.Combine(projectDir, composeFileName);
                    await File.WriteAllTextAsync(composePath, composeYaml, Encoding.UTF8, ct);
                }

                if (!Directory.Exists(projectDir) || !File.Exists(Path.Combine(projectDir, composeFileName)))
                    return Results.Json(new DeployResult { Success = false, Error = $"Compose file '{composeFileName}' not found for project '{projectName}'." });
            }
            finally
            {
                deploymentLock.Release();
            }

            var projectDirResolved = ProjectPaths.GetProjectDirectory(appPath, projectName);
            Console.WriteLine($"[agent] ({projectName}) Queuing docker-compose up -d (async)");
            var deployId = Guid.NewGuid().ToString("N");
            await DeploymentStateHelper.WriteInProgressAsync(projectDirResolved, deployId, ct).ConfigureAwait(false);
            _ = Task.Run(() => ProjectComposeUpBackgroundAsync(projectName, projectDirResolved, deployId, CancellationToken.None), CancellationToken.None);

            return Results.Json(new DeployResult
            {
                Success = true,
                DeploymentId = deployId,
                OperationPending = true,
                Message = $"Project start queued. Poll GET /projects/{projectName}/status for containers, jobs, and diagnostics."
            });
        });

        app.MapPost("/projects/{projectName}/stop", async (string projectName, HttpRequest request, CancellationToken ct, bool removeVolumes = false) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
                if (!Directory.Exists(projectDir))
                    return Results.Json(new DeployResult { Success = false, Error = $"Project '{projectName}' not found." });

                Console.WriteLine($"[agent] ({projectName}) docker-compose down{(removeVolumes ? " -v" : "")}");
                var args = $"-f \"{composeFileName}\" down";
                if (removeVolumes)
                    args += " -v";

                var down = await RunProcessAsync(dockerComposeExecutable, args, workingDirectory: projectDir, ct);
                if (down.ExitCode != 0)
                {
                    return Results.Json(new DeployResult
                    {
                        Success = false,
                        Error = down.Stderr
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                var status = await GetStatusAsync(dockerComposeExecutable, composeFileName, projectDir, ct);
                status = await AttachDiagnosticsAsync(status, projectDir, ct);
                return Results.Json(status);
            }
            finally
            {
                deploymentLock.Release();
            }
        });

        app.MapPost("/projects/{projectName}/restart", async (string projectName, HttpRequest request, CancellationToken ct, bool removeVolumes = false) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deploymentLock.WaitAsync(ct);
            try
            {
                var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
                var form = await request.ReadFormAsync(ct);
                var composeYaml = form["compose"].ToString();
                if (!string.IsNullOrWhiteSpace(composeYaml))
                {
                    Directory.CreateDirectory(projectDir);
                    var composePath = Path.Combine(projectDir, composeFileName);
                    await File.WriteAllTextAsync(composePath, composeYaml, Encoding.UTF8, ct);
                }

                if (!Directory.Exists(projectDir) || !File.Exists(Path.Combine(projectDir, composeFileName)))
                    return Results.Json(new DeployResult { Success = false, Error = $"Compose file '{composeFileName}' not found for project '{projectName}'." });
            }
            finally
            {
                deploymentLock.Release();
            }

            var projectDirResolved = ProjectPaths.GetProjectDirectory(appPath, projectName);
            Console.WriteLine($"[agent] ({projectName}) Queuing docker-compose restart (async)");
            var deployId = Guid.NewGuid().ToString("N");
            await DeploymentStateHelper.WriteInProgressAsync(projectDirResolved, deployId, ct).ConfigureAwait(false);
            _ = Task.Run(() => ProjectRestartBackgroundAsync(projectName, projectDirResolved, deployId, removeVolumes, CancellationToken.None), CancellationToken.None);

            return Results.Json(new DeployResult
            {
                Success = true,
                DeploymentId = deployId,
                OperationPending = true,
                Message = $"Project restart queued. Poll GET /projects/{projectName}/status for containers, jobs, and diagnostics."
            });
        });

        app.MapGet("/projects/{projectName}/status", async (string projectName, CancellationToken ct) =>
        {
            var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.Json(new DeployResult { Success = false, Error = $"Project '{projectName}' not found." });

            var status = await GetStatusAsync(dockerComposeExecutable, composeFileName, projectDir, ct);
            status = await AttachDiagnosticsAsync(status, projectDir, ct);
            return Results.Json(status);
        });

        app.MapGet("/projects/{projectName}/logs", async (string projectName, string? service, CancellationToken ct) =>
        {
            var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.Problem($"Project '{projectName}' not found.", statusCode: 404);

            var args = new StringBuilder();
            args.Append($"-f \"{composeFileName}\" logs --tail=200");
            if (!string.IsNullOrWhiteSpace(service))
                args.Append($" {service}");

            var logs = await RunProcessAsync(dockerComposeExecutable, args.ToString(), workingDirectory: projectDir, ct);
            if (logs.ExitCode != 0)
                return Results.Problem(logs.Stderr, statusCode: 500);

            return Results.Text(LogRedactor.RedactSecrets(logs.Stdout), contentType: "text/plain; charset=utf-8");
        });

        app.MapGet("/projects/{projectName}/volumes", async (string projectName, HttpRequest request, CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var projectDir = ProjectPaths.GetProjectDirectory(appPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.Json(new List<string>());

            var list = await RunProcessAsync(dockerExecutable, "volume ls --format \"{{.Name}}\"", projectDir, ct);
            if (list.ExitCode != 0)
                return Results.Problem(list.Stderr, statusCode: 500);

            var prefix = projectName + "_";
            var names = list.Stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v)
                .ToList();
            return Results.Json(names);
        });

        app.MapGet("/projects/{projectName}/volume-archive-index", async (
            string projectName,
            ChronosAgentDbContext db,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var rows = await db.VolumeArchives.AsNoTracking()
                .Where(v => v.ProjectName == projectName)
                .OrderByDescending(v => v.CreatedUtc)
                .ToListAsync(ct).ConfigureAwait(false);
            return Results.Json(rows);
        });

        app.MapPost("/projects/{projectName}/volume-archives/register", async (
            string projectName,
            VolumeArchiveRegisterDto body,
            ChronosAgentDbContext db,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(body.VolumeName) || string.IsNullOrWhiteSpace(body.StoredRelativePath))
                return Results.BadRequest("volumeName and storedRelativePath are required.");

            db.VolumeArchives.Add(new VolumeArchiveEntity
            {
                Id = Guid.NewGuid(),
                ProjectName = projectName,
                VolumeName = body.VolumeName.Trim(),
                StoredRelativePath = body.StoredRelativePath.Trim(),
                BytesApprox = body.BytesApprox,
                CompressMode = string.IsNullOrWhiteSpace(body.CompressMode) ? "gzip" : body.CompressMode.Trim(),
                CreatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    private static bool IsAuthorized(HttpRequest request, string? expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return true;

        if (request.Headers.TryGetValue("X-API-Key", out var values))
            return string.Equals(values.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal);

        return false;
    }

    private static List<ContainerStatus> ParseContainerStatuses(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ContainerStatus>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<ContainerStatus>();

            var result = new List<ContainerStatus>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = ReadString(item, "Name", "name", "Service", "service") ?? string.Empty;
                var state = ReadString(item, "State", "state", "Status", "status") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(state))
                    result.Add(new ContainerStatus { Name = name, State = state });
            }

            return result;
        }
        catch
        {
            return new List<ContainerStatus>();
        }

        static string? ReadString(JsonElement element, params string[] propertyNames)
        {
            foreach (var prop in propertyNames)
            {
                if (element.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            }
            return null;
        }
    }

    private static async Task<DeployResult> GetStatusAsync(string dockerComposeExecutable, string composeFileName, string appPath, CancellationToken ct)
    {
        var ps = await RunProcessAsync(dockerComposeExecutable,
            $"-f \"{composeFileName}\" ps --format json",
            workingDirectory: appPath,
            ct);

        if (ps.ExitCode != 0)
        {
            return new DeployResult
            {
                Success = false,
                Error = ps.Stderr
            };
        }

        var containers = ParseContainerStatuses(ps.Stdout);
        var success = containers.All(c =>
            c.State.Contains("running", StringComparison.OrdinalIgnoreCase) ||
            c.State.Contains("Up", StringComparison.OrdinalIgnoreCase) ||
            c.State.Contains("healthy", StringComparison.OrdinalIgnoreCase));

        return new DeployResult
        {
            Success = success,
            Error = success ? null : "One or more containers are not running/healthy.",
            Containers = containers
        };
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct)
    {
        var (resolvedFileName, resolvedArgs) = ComposeCommandLine.Build(fileName, arguments);
        Console.WriteLine($"[agent cmd] {resolvedFileName} {resolvedArgs}");
        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            Arguments = resolvedArgs,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<DeployResult> AttachDiagnosticsAsync(DeployResult status, string projectDir, CancellationToken ct)
    {
        status.Diagnostics = await AgentPersistence.LoadAsync(projectDir, ct).ConfigureAwait(false);
        return await DeploymentStateHelper.AttachAsync(status, projectDir, ct).ConfigureAwait(false);
    }
}
