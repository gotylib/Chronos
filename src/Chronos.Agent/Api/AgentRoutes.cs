using System.Diagnostics;
using System.Formats.Tar;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Chronos.Agent.Application;
using Chronos.Agent.Application.Configuration;
using Chronos.Agent.Infrastructure.ObjectStorage;
using Chronos.Core;

namespace Chronos.Agent.Api;

/// <summary>
/// Дополнительные маршруты Chronos: манифест, артефакты (tar), diagnostics, снимки/восстановление томов;
/// запуск проверок из манифеста при старте см. <see cref="RunStartupFromManifestAsync"/>.
/// </summary>
public static class AgentRoutes
{
    public static void MapAgentRoutes(
        WebApplication app,
        AgentPaths paths,
        string? expectedApiKey,
        SemaphoreSlim deployLock,
        SemaphoreSlim volumeLock,
        ExecutionThrottler throttler,
        ExecutionPolicyOptions policy,
        VolumeObjectStorageOptions volumeObjectStorage)
    {
        _ = throttler;
        _ = policy;

        app.MapPost("/projects/{projectName}/chronos/manifest", async (
            string projectName,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deployLock.WaitAsync(ct);
            try
            {
                var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
                Directory.CreateDirectory(projectDir);
                var dir = Path.Combine(projectDir, ".chronos");
                Directory.CreateDirectory(dir);

                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return Results.BadRequest("Empty body.");

                var path = Path.Combine(dir, "manifest.json");
                var tmp = path + ".tmp";
                await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
                return Results.Ok();
            }
            finally
            {
                deployLock.Release();
            }
        });

        app.MapPost("/projects/{projectName}/chronos/artifacts", async (
            string projectName,
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            await deployLock.WaitAsync(ct);
            try
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest("Expected multipart form-data.");

                var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
                var file = form.Files.GetFile("archive");
                if (file == null)
                    return Results.BadRequest("Missing form file 'archive'.");

                var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
                Directory.CreateDirectory(projectDir);

                await using var stream = file.OpenReadStream();
                await ExtractTarToProjectAsync(projectDir, stream, ct).ConfigureAwait(false);
                return Results.Ok();
            }
            finally
            {
                deployLock.Release();
            }
        });

        app.MapGet("/chronos/host-disk", (HttpRequest request) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (!HostDiskStats.TryGetDiskSpaceForPath(paths.AppPath, out var freeBytes, out var totalBytes))
                return Results.Json(new { error = "unable to resolve disk for app path." });

            var root = Path.GetPathRoot(Path.GetFullPath(paths.AppPath));
            return Results.Json(new { rootPath = root, appPath = paths.AppPath, freeBytes, totalBytes });
        });

        app.MapGet("/projects/{projectName}/chronos/diagnostics", async (string projectName, CancellationToken ct) =>
        {
            var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.NotFound();

            var snap = await AgentPersistence.LoadAsync(projectDir, ct).ConfigureAwait(false);
            return Results.Json(snap, ManifestJson.Options);
        });

        app.MapGet("/projects/{projectName}/volumes/{volumeName}/snapshot", async (
            string projectName,
            string volumeName,
            string? compress,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(http.Request, expectedApiKey))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
            if (!Directory.Exists(projectDir))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await volumeLock.WaitAsync(ct);
            try
            {
                try
                {
                    ValidateVolumeName(volumeName);
                }
                catch (ArgumentException ex)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await http.Response.WriteAsync(ex.Message, ct).ConfigureAwait(false);
                    return;
                }

                var gzip = !string.Equals(compress, "none", StringComparison.OrdinalIgnoreCase);
                http.Response.ContentType = gzip ? "application/gzip" : "application/x-tar";

                using var proc = StartVolumeBackupProcess(paths, volumeName, gzip);
                proc.Start();
                var drainErr = proc.StandardError.ReadToEndAsync(ct);
                await proc.StandardOutput.BaseStream.CopyToAsync(http.Response.Body, ct).ConfigureAwait(false);
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                _ = await drainErr.ConfigureAwait(false);
                if (proc.ExitCode != 0)
                    Console.WriteLine($"[volume snapshot] docker exited {proc.ExitCode} for volume '{volumeName}'.");
            }
            finally
            {
                volumeLock.Release();
            }
        });

        app.MapPost("/projects/{projectName}/volumes/{volumeName}/snapshot/upload", async (
            string projectName,
            string volumeName,
            HttpRequest request,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.NotFound();

            VolumeSnapshotUploadRequest? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<VolumeSnapshotUploadRequest>(request.Body, ManifestJson.Options, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                return Results.BadRequest("Invalid JSON.");
            }

            if (body == null || string.IsNullOrWhiteSpace(body.UploadUrl))
                return Results.BadRequest("uploadUrl is required.");

            var gzip = !string.Equals(body.Compress, "none", StringComparison.OrdinalIgnoreCase);

            try
            {
                ValidateVolumeName(volumeName);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            await volumeLock.WaitAsync(ct);
            try
            {
                using var proc = StartVolumeBackupProcess(paths, volumeName, gzip);
                proc.Start();
                var drainErr = proc.StandardError.ReadToEndAsync(ct);

                using var http = httpFactory.CreateClient(nameof(AgentRoutes));
                http.Timeout = TimeSpan.FromHours(6);

                var method = new HttpMethod(string.IsNullOrWhiteSpace(body.Method) ? "PUT" : body.Method.ToUpperInvariant());
                using var msg = new HttpRequestMessage(method, body.UploadUrl);
                msg.Content = new StreamContent(proc.StandardOutput.BaseStream);
                msg.Content.Headers.ContentType = new MediaTypeHeaderValue(gzip ? "application/gzip" : "application/x-tar");

                if (body.Headers != null)
                {
                    foreach (var kv in body.Headers)
                        msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }

                using var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                _ = await drainErr.ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    return Results.Json(new VolumeOperationResult
                    {
                        Success = false,
                        Error = $"docker backup exited with code {proc.ExitCode}"
                    });
                }

                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return Results.Json(new VolumeOperationResult
                    {
                        Success = false,
                        Error = $"Upload failed ({(int)resp.StatusCode}): {err}"
                    });
                }

                return Results.Json(new VolumeOperationResult { Success = true });
            }
            finally
            {
                volumeLock.Release();
            }
        });

        app.MapPost("/projects/{projectName}/volumes/{volumeName}/snapshot/to-object-storage", async (
            string projectName,
            string volumeName,
            HttpRequest request,
            CancellationToken ct,
            string? compress = null,
            string? keyPrefixExtra = null) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (!volumeObjectStorage.IsComplete)
            {
                return Results.Json(
                    new VolumeOperationResult
                    {
                        Success = false,
                        Error =
                            "Object storage is not configured. Set CHRONOS_VOLUME_STORAGE_ENABLED=true and SERVICE_URL, ACCESS_KEY, SECRET_KEY, BUCKET."
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.NotFound();

            try
            {
                ValidateVolumeName(volumeName);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            var gzip = !string.Equals(compress, "none", StringComparison.OrdinalIgnoreCase);
            var prefix = ObjectStorageLayout.CombinePrefix(volumeObjectStorage.KeyPrefix, keyPrefixExtra);
            var objectKey = ObjectStorageLayout.BuildBackupObjectKey(prefix, projectName, volumeName, gzip);
            var contentType = gzip ? "application/gzip" : "application/x-tar";

            await volumeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var proc = StartVolumeBackupProcess(paths, volumeName, gzip);
                proc.Start();
                var drainErrTask = proc.StandardError.ReadToEndAsync(ct);

                var cfg = new AmazonS3Config
                {
                    ServiceURL = volumeObjectStorage.ServiceUrl.TrimEnd('/'),
                    ForcePathStyle = volumeObjectStorage.ForcePathStyle,
                    AuthenticationRegion = "us-east-1"
                };

                using var s3 = new AmazonS3Client(volumeObjectStorage.AccessKey, volumeObjectStorage.SecretKey, cfg);

                await using (var stdout = proc.StandardOutput.BaseStream)
                {
                    var (ok, err, bytes) = await VolumeTarS3Uploader.UploadAsync(
                            s3,
                            volumeObjectStorage.BucketName,
                            objectKey,
                            stdout,
                            contentType,
                            ct)
                        .ConfigureAwait(false);

                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    _ = await drainErrTask.ConfigureAwait(false);

                    if (proc.ExitCode != 0)
                    {
                        try
                        {
                            await s3.DeleteObjectAsync(volumeObjectStorage.BucketName, objectKey, ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            // best-effort cleanup of partial upload
                        }

                        return Results.Json(new VolumeOperationResult
                        {
                            Success = false,
                            Error = $"docker backup exited with code {proc.ExitCode}"
                        });
                    }

                    if (!ok)
                    {
                        try
                        {
                            await s3.DeleteObjectAsync(volumeObjectStorage.BucketName, objectKey, ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignore
                        }

                        return Results.Json(new VolumeOperationResult { Success = false, Error = err });
                    }

                    return Results.Json(new VolumeOperationResult
                    {
                        Success = true,
                        ObjectKey = objectKey,
                        BytesTransferred = bytes
                    });
                }
            }
            catch (Exception ex)
            {
                return Results.Json(new VolumeOperationResult { Success = false, Error = ex.Message });
            }
            finally
            {
                volumeLock.Release();
            }
        });

        app.MapPost("/projects/{projectName}/volumes/{volumeName}/restore", async (
            string projectName,
            string volumeName,
            HttpRequest request,
            string? compress,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart form-data with 'archive'.");

            var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.NotFound();

            var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
            var file = form.Files.GetFile("archive");
            if (file == null)
                return Results.BadRequest("Missing form file 'archive'.");

            var gzip = !string.Equals(compress, "none", StringComparison.OrdinalIgnoreCase);

            await volumeLock.WaitAsync(ct);
            try
            {
                try
                {
                    ValidateVolumeName(volumeName);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }

                using var proc = StartVolumeRestoreProcess(paths, volumeName, gzip);
                proc.Start();
                var drainErr = proc.StandardError.ReadToEndAsync(ct);

                await using (var s = file.OpenReadStream())
                await using (var stdin = proc.StandardInput.BaseStream)
                    await s.CopyToAsync(stdin, ct).ConfigureAwait(false);

                proc.StandardInput.Close();
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                var errText = await drainErr.ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    return Results.Json(new VolumeOperationResult
                    {
                        Success = false,
                        Error = string.IsNullOrWhiteSpace(errText) ? $"restore exit {proc.ExitCode}" : errText
                    });
                }

                return Results.Json(new VolumeOperationResult { Success = true });
            }
            finally
            {
                volumeLock.Release();
            }
        });

        app.MapPost("/projects/{projectName}/volumes/{volumeName}/restore-url", async (
            string projectName,
            string volumeName,
            HttpRequest request,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            if (!IsAuthorized(request, expectedApiKey))
                return Results.Unauthorized();

            var projectDir = ProjectPaths.GetProjectDirectory(paths.AppPath, projectName);
            if (!Directory.Exists(projectDir))
                return Results.NotFound();

            VolumeRestoreFromUrlRequest? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<VolumeRestoreFromUrlRequest>(request.Body, ManifestJson.Options, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                return Results.BadRequest("Invalid JSON.");
            }

            if (body == null || string.IsNullOrWhiteSpace(body.DownloadUrl))
                return Results.BadRequest("downloadUrl is required.");

            var gzip = !string.Equals(body.Compress, "none", StringComparison.OrdinalIgnoreCase);

            await volumeLock.WaitAsync(ct);
            try
            {
                try
                {
                    ValidateVolumeName(volumeName);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }

                using var http = httpFactory.CreateClient(nameof(AgentRoutes));
                http.Timeout = TimeSpan.FromHours(6);

                using var get = new HttpRequestMessage(HttpMethod.Get, body.DownloadUrl);
                if (body.Headers != null)
                {
                    foreach (var kv in body.Headers)
                        get.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }

                using var dl = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!dl.IsSuccessStatusCode)
                {
                    var t = await dl.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return Results.Json(new VolumeOperationResult
                    {
                        Success = false,
                        Error = $"Download failed ({(int)dl.StatusCode}): {t}"
                    });
                }

                await using var downStream = await dl.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var proc = StartVolumeRestoreProcess(paths, volumeName, gzip);
                proc.Start();
                var drainErr = proc.StandardError.ReadToEndAsync(ct);

                await using var stdin = proc.StandardInput.BaseStream;
                await downStream.CopyToAsync(stdin, ct).ConfigureAwait(false);
                proc.StandardInput.Close();

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                var errText = await drainErr.ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    return Results.Json(new VolumeOperationResult
                    {
                        Success = false,
                        Error = string.IsNullOrWhiteSpace(errText) ? $"restore exit {proc.ExitCode}" : errText
                    });
                }

                return Results.Json(new VolumeOperationResult { Success = true });
            }
            finally
            {
                volumeLock.Release();
            }
        });
    }

    internal static async Task RunStartupFromManifestAsync(
        string projectName,
        string projectDir,
        AgentPaths paths,
        ExecutionThrottler throttler,
        ExecutionPolicyOptions policy,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(projectDir, ".chronos", "manifest.json");
        if (!File.Exists(manifestPath))
            return;

        ProjectManifest? manifest;
        try
        {
            await using var fs = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ProjectManifest>(fs, ManifestJson.Options, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[startup] Failed to read manifest: {ex.Message}");
            return;
        }

        if (manifest?.Services == null || manifest.Services.Count == 0)
            return;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        async Task<T> RunWithTimeoutAndThrottleAsync<T>(Func<CancellationToken, Task<T>> action)
            => await throttler.RunThrottledAsync(projectName, ct, async throttledCt =>
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(throttledCt);
                timeoutCts.CancelAfter(policy.TestExecutionTimeout);
                return await action(timeoutCts.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        Task RunWithTimeoutAndThrottleNoResultAsync(Func<CancellationToken, Task> action)
            => throttler.RunThrottledAsync(projectName, ct, async throttledCt =>
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(throttledCt);
                timeoutCts.CancelAfter(policy.TestExecutionTimeout);
                await action(timeoutCts.Token).ConfigureAwait(false);
            });

        foreach (var (serviceName, section) in manifest.Services)
        {
            foreach (var test in (section.Tests ?? new List<DeclarativeCheck>()).Where(t => t.OnStartup))
            {
                    var rec = await RunWithTimeoutAndThrottleAsync(token => DeclarativeCheckRunner.RunCheckAsync(
                        test,
                        serviceName,
                        projectDir,
                        paths.ComposeFileName,
                        projectName,
                        paths.DockerComposeExecutable,
                        http,
                        token)).ConfigureAwait(false);

                await AgentPersistence.AppendTestAsync(projectDir, rec, ct).ConfigureAwait(false);

                if (!rec.Success)
                    Console.WriteLine($"[startup] {serviceName}/{rec.TestId}: FAIL ({test.Criticality}) {rec.Message}");

                if (!rec.Success && test.Criticality == TestCriticality.Critical)
                    await RunWithTimeoutAndThrottleNoResultAsync(token => RestartComposeServiceAsync(paths, projectDir, serviceName, token)).ConfigureAwait(false);
            }

            foreach (var code in (section.CodeTests ?? new List<CodeTestEntry>()).Where(c => c.OnStartup)
                         .OrderBy(c => c.Order)
                         .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
            {
                    var rec = await RunWithTimeoutAndThrottleAsync(token => CodeTestRunner.RunAsync(
                        code,
                        serviceName,
                        projectDir,
                        paths.ComposeFileName,
                        projectName,
                        paths.DockerComposeExecutable,
                        http,
                        token)).ConfigureAwait(false);

                await AgentPersistence.AppendTestAsync(projectDir, rec, ct).ConfigureAwait(false);

                if (!rec.Success)
                    Console.WriteLine($"[startup] code {serviceName}/{rec.TestId}: FAIL ({code.Criticality}) {rec.Message}");

                if (!rec.Success && code.Criticality == TestCriticality.Critical)
                    await RunWithTimeoutAndThrottleNoResultAsync(token => RestartComposeServiceAsync(paths, projectDir, serviceName, token)).ConfigureAwait(false);
            }

            foreach (var job in (section.Jobs ?? new List<JobDefinition>()).Where(j => j.OnStartup))
            {
                    var rec = await RunWithTimeoutAndThrottleAsync(token => DeclarativeCheckRunner.RunJobAsync(
                        job,
                        serviceName,
                        projectDir,
                        paths.ComposeFileName,
                        projectName,
                        paths.DockerComposeExecutable,
                        projectDir,
                        token)).ConfigureAwait(false);

                await AgentPersistence.AppendJobAsync(projectDir, rec, ct).ConfigureAwait(false);

                if (!rec.Success)
                    Console.WriteLine($"[startup] job {serviceName}/{rec.JobId}: FAIL ({job.Criticality}) {rec.Message}");

                if (!rec.Success && job.Criticality == TestCriticality.Critical)
                    await RunWithTimeoutAndThrottleNoResultAsync(token => RestartComposeServiceAsync(paths, projectDir, serviceName, token)).ConfigureAwait(false);
            }

            foreach (var job in (section.CodeJobs ?? new List<CodeJobEntry>()).Where(j => j.OnStartup))
            {
                    var rec = await RunWithTimeoutAndThrottleAsync(token => CodeJobRunner.RunAsync(
                        job,
                        serviceName,
                        projectDir,
                        paths.ComposeFileName,
                        projectName,
                        paths.DockerComposeExecutable,
                        http,
                        token)).ConfigureAwait(false);

                await AgentPersistence.AppendJobAsync(projectDir, rec, ct).ConfigureAwait(false);

                if (!rec.Success)
                    Console.WriteLine($"[startup] code-job {serviceName}/{rec.JobId}: FAIL ({job.Criticality}) {rec.Message}");

                if (!rec.Success && job.Criticality == TestCriticality.Critical)
                    await RunWithTimeoutAndThrottleNoResultAsync(token => RestartComposeServiceAsync(paths, projectDir, serviceName, token)).ConfigureAwait(false);
            }
        }
    }

    private static async Task RestartComposeServiceAsync(AgentPaths paths, string projectDir, string serviceName, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[startup] Restarting service '{serviceName}' after critical test failure.");
            var args = $"-f \"{paths.ComposeFileName}\" restart {serviceName}";
            await RunSilentProcessAsync(paths.DockerComposeExecutable, args, projectDir, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[startup] Restart failed: {ex.Message}");
        }
    }

    private static async Task RunSilentProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct)
    {
        var (resolvedFileName, resolvedArgs) = ComposeCommandLine.Build(fileName, arguments);
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

        using var p = new Process { StartInfo = psi };
        p.Start();
        _ = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var err = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException(err);
    }

    private static Process StartVolumeBackupProcess(AgentPaths paths, string volumeName, bool gzip)
    {
        ValidateVolumeName(volumeName);
        var args = gzip
            ? $"run --rm -v {volumeName}:/source:ro {paths.ArchiveImage} tar czf - -C /source ."
            : $"run --rm -v {volumeName}:/source:ro {paths.ArchiveImage} tar cf - -C /source .";

        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = paths.DockerExecutable,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
    }

    private static Process StartVolumeRestoreProcess(AgentPaths paths, string volumeName, bool gzip)
    {
        ValidateVolumeName(volumeName);
        var inner = gzip ? "tar xzf - -C /target" : "tar xf - -C /target";
        var args = $"run --rm -i -v {volumeName}:/target {paths.ArchiveImage} sh -c \"{inner}\"";

        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = paths.DockerExecutable,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
    }

    private static void ValidateVolumeName(string volumeName)
    {
        if (string.IsNullOrWhiteSpace(volumeName) || volumeName.Length > 255)
            throw new ArgumentException("Invalid volume name.");

        foreach (var c in volumeName)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '.' or '-')
                continue;
            throw new ArgumentException($"Invalid character in volume name: '{c}'.");
        }
    }

    private static async Task ExtractTarToProjectAsync(string projectRoot, Stream tarStream, CancellationToken ct)
    {
        using var reader = new TarReader(tarStream, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: true, ct).ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is not TarEntryType.RegularFile and not TarEntryType.Directory)
                continue;

            var name = entry.Name.Replace('\\', '/').TrimStart('/');
            if (name.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsafe tar entry '{entry.Name}'.");

            var dest = Path.GetFullPath(Path.Combine(projectRoot, name));
            var rootFull = Path.GetFullPath(projectRoot);
            var rel = Path.GetRelativePath(rootFull, dest);
            if (rel.StartsWith("..", StringComparison.Ordinal))
                throw new InvalidOperationException($"Tar entry escapes project directory: '{entry.Name}'.");

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 20,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (entry.DataStream != null)
                await entry.DataStream.CopyToAsync(fs, ct).ConfigureAwait(false);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    if (entry.Mode != default)
                        File.SetUnixFileMode(dest, entry.Mode);
                }
                catch
                {
                    // best-effort chmod
                }
            }
        }
    }

    private static bool IsAuthorized(HttpRequest request, string? expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return true;

        if (request.Headers.TryGetValue("X-API-Key", out var values))
            return string.Equals(values.FirstOrDefault(), expectedApiKey, StringComparison.Ordinal);

        return false;
    }

}
