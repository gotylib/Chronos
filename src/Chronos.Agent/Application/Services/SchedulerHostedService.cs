using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Chronos.Core;

namespace Chronos.Agent;

/// <summary>Периодические проверки и jobs из <c>.chronos/manifest.json</c> по каждому проекту на диске.</summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly AgentPaths _paths;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SchedulerHostedService> _logger;
    private readonly ExecutionPolicyOptions _policy;
    private readonly ExecutionThrottler _throttler;

    private static readonly ConcurrentDictionary<string, DateTimeOffset> NextRunUtc = new();

    public SchedulerHostedService(
        AgentPaths paths,
        IHttpClientFactory httpClientFactory,
        ILogger<SchedulerHostedService> logger,
        ExecutionPolicyOptions policy,
        ExecutionThrottler throttler)
    {
        _paths = paths;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _policy = policy;
        _throttler = throttler;
    }

    private async Task<T> RunWithTimeoutAndThrottleAsync<T>(
        string projectName,
        CancellationToken ct,
        Func<CancellationToken, Task<T>> action)
    {
        return await _throttler.RunThrottledAsync(projectName, ct, async throttledCt =>
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(throttledCt);
            timeoutCts.CancelAfter(_policy.TestExecutionTimeout);
            return await action(timeoutCts.Token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private Task RunWithTimeoutAndThrottleAsync(
        string projectName,
        CancellationToken ct,
        Func<CancellationToken, Task> action)
    {
        return _throttler.RunThrottledAsync(projectName, ct, async throttledCt =>
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(throttledCt);
            timeoutCts.CancelAfter(_policy.TestExecutionTimeout);
            await action(timeoutCts.Token).ConfigureAwait(false);
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manifest scheduler tick failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_paths.AppPath))
            return;

        foreach (var projectDir in Directory.EnumerateDirectories(_paths.AppPath))
        {
            ct.ThrowIfCancellationRequested();
            var projectName = Path.GetFileName(projectDir);
            if (string.IsNullOrWhiteSpace(projectName))
                continue;

            var composePath = Path.Combine(projectDir, _paths.ComposeFileName);
            if (!File.Exists(composePath))
                continue;

            var manifestPath = Path.Combine(projectDir, ".chronos", "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            ProjectManifest? manifest;
            try
            {
                await using var fs = File.OpenRead(manifestPath);
                manifest = await JsonSerializer.DeserializeAsync<ProjectManifest>(fs, ManifestJson.Options, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read manifest for project {Project}.", projectName);
                continue;
            }

            if (manifest?.Services == null || manifest.Services.Count == 0)
                continue;

            var now = DateTimeOffset.UtcNow;
            using var http = _httpClientFactory.CreateClient(nameof(SchedulerHostedService));
            http.Timeout = TimeSpan.FromMinutes(5);

            foreach (var (serviceName, section) in manifest.Services)
            {
                foreach (var test in section.Tests ?? new List<DeclarativeCheck>())
                {
                    if (!test.IntervalMinutes.HasValue || test.IntervalMinutes.Value <= 0)
                        continue;

                    var key = $"{projectName}|{serviceName}|t|{test.Id}";
                    if (!ShouldRun(key, now, TimeSpan.FromMinutes(test.IntervalMinutes.Value)))
                        continue;

                    var rec = await RunWithTimeoutAndThrottleAsync(
                        projectName,
                        ct,
                        token => DeclarativeCheckRunner.RunCheckAsync(
                            test,
                            serviceName,
                            projectDir,
                            _paths.ComposeFileName,
                            projectName,
                            _paths.DockerComposeExecutable,
                            http,
                            token)).ConfigureAwait(false);

                    await AgentPersistence.AppendTestAsync(projectDir, rec, ct).ConfigureAwait(false);

                    if (!rec.Success && test.Criticality == TestCriticality.Critical)
                        await RunWithTimeoutAndThrottleAsync(
                            projectName,
                            ct,
                            token => RestartServiceAsync(projectDir, serviceName, token)).ConfigureAwait(false);
                }

                foreach (var job in section.Jobs ?? new List<JobDefinition>())
                {
                    if (job.IntervalMinutes <= 0)
                        continue;

                    var key = $"{projectName}|{serviceName}|j|{job.Id}";
                    if (!ShouldRun(key, now, TimeSpan.FromMinutes(job.IntervalMinutes)))
                        continue;

                    var rec = await RunWithTimeoutAndThrottleAsync(
                        projectName,
                        ct,
                        token => DeclarativeCheckRunner.RunJobAsync(
                            job,
                            serviceName,
                            projectDir,
                            _paths.ComposeFileName,
                            projectName,
                            _paths.DockerComposeExecutable,
                            projectDir,
                            token)).ConfigureAwait(false);

                    await AgentPersistence.AppendJobAsync(projectDir, rec, ct).ConfigureAwait(false);

                    if (!rec.Success && job.Criticality == TestCriticality.Critical)
                        await RunWithTimeoutAndThrottleAsync(
                            projectName,
                            ct,
                            token => RestartServiceAsync(projectDir, serviceName, token)).ConfigureAwait(false);
                }

                foreach (var code in section.CodeTests ?? new List<CodeTestEntry>())
                {
                    if (!code.IntervalMinutes.HasValue || code.IntervalMinutes.Value <= 0)
                        continue;

                    var key = $"{projectName}|{serviceName}|c|{code.Id}";
                    if (!ShouldRun(key, now, TimeSpan.FromMinutes(code.IntervalMinutes.Value)))
                        continue;

                    var rec = await RunWithTimeoutAndThrottleAsync(
                        projectName,
                        ct,
                        token => CodeTestRunner.RunAsync(
                            code,
                            serviceName,
                            projectDir,
                            _paths.ComposeFileName,
                            projectName,
                            _paths.DockerComposeExecutable,
                            http,
                            token)).ConfigureAwait(false);

                    await AgentPersistence.AppendTestAsync(projectDir, rec, ct).ConfigureAwait(false);

                    if (!rec.Success && code.Criticality == TestCriticality.Critical)
                        await RunWithTimeoutAndThrottleAsync(
                            projectName,
                            ct,
                            token => RestartServiceAsync(projectDir, serviceName, token)).ConfigureAwait(false);
                }

                foreach (var codeJob in section.CodeJobs ?? new List<CodeJobEntry>())
                {
                    if (!codeJob.IntervalMinutes.HasValue || codeJob.IntervalMinutes.Value <= 0)
                        continue;

                    var key = $"{projectName}|{serviceName}|cj|{codeJob.Id}";
                    if (!ShouldRun(key, now, TimeSpan.FromMinutes(codeJob.IntervalMinutes.Value)))
                        continue;

                    var rec = await RunWithTimeoutAndThrottleAsync(
                        projectName,
                        ct,
                        token => CodeJobRunner.RunAsync(
                            codeJob,
                            serviceName,
                            projectDir,
                            _paths.ComposeFileName,
                            projectName,
                            _paths.DockerComposeExecutable,
                            http,
                            token)).ConfigureAwait(false);

                    await AgentPersistence.AppendJobAsync(projectDir, rec, ct).ConfigureAwait(false);

                    if (!rec.Success && codeJob.Criticality == TestCriticality.Critical)
                        await RunWithTimeoutAndThrottleAsync(
                            projectName,
                            ct,
                            token => RestartServiceAsync(projectDir, serviceName, token)).ConfigureAwait(false);
                }
            }
        }
    }

    private static bool ShouldRun(string key, DateTimeOffset now, TimeSpan interval)
    {
        var next = NextRunUtc.GetOrAdd(key, now);
        if (now < next)
            return false;

        NextRunUtc[key] = now.Add(interval);
        return true;
    }

    private async Task RestartServiceAsync(string projectDir, string serviceName, CancellationToken ct)
    {
        try
        {
            _logger.LogWarning("Restarting service {Service} after critical check failure.", serviceName);
            var args = $"-f \"{_paths.ComposeFileName}\" restart {serviceName}";
            await RunProcessAsync(_paths.DockerComposeExecutable, args, projectDir, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart service {Service}.", serviceName);
        }
    }

    private static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct)
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
        var err = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        _ = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Process failed: {resolvedFileName} {resolvedArgs}. {err}");
    }
}
