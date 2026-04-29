using System.Diagnostics;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Polly;
using Chronos.Core.Safety;

// Локальный прогон стека: polling health через Docker API, затем декларативные и кодовые проверки из манифеста.
namespace Chronos.Core;

/// <summary>Параметры <see cref="LocalTester"/> (таймауты, compose CLI, запуск startup-проверок).</summary>
public sealed class TestOptions
{
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public bool RemoveAfterTest { get; set; } = true;
    public bool ShowLogsOnFailure { get; set; } = true;
    public TimeSpan TestExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary><c>auto</c> = detect compose v2 vs v1 on this machine.</summary>
    public string DockerComposeExecutable { get; set; } = "auto";
    public string ProjectName { get; set; } = "chronos";
    public bool RequireHealthChecksIfDefined { get; set; } = true;
    public bool Verbose { get; set; } = true;

    /// <summary>После health: декларативные проверки (<see cref="DeclarativeCheck.OnStartup"/>).</summary>
    public bool RunStartupChecks { get; set; } = true;

    public Dictionary<string, List<DeclarativeCheck>>? DeclarativeChecksByService { get; set; }
    public Dictionary<string, List<CodeTestEntry>>? CodeTestsByService { get; set; }
}

/// <summary>Сводка состояния одного контейнера после опроса Docker.</summary>
public sealed class ContainerHealth
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? HealthStatus { get; set; }
    public bool IsHealthy { get; set; }
    public string? Error { get; set; }
}

/// <summary>Итог локального теста: успех/ошибки, health по контейнерам, логи, записи проверок.</summary>
public sealed class TestResult
{
    public bool Success { get; set; }
    public List<ContainerHealth> ContainerHealths { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public TimeSpan Duration { get; set; }
    public string? Logs { get; set; }
    public List<CheckRunRecord> CheckRuns { get; set; } = [];
}

/// <summary>Поднимает compose локально и проверяет health и тесты из <see cref="TestOptions"/>.</summary>
public sealed class LocalTester
{
    private readonly DockerClient _docker;
    private readonly string _composeFilePath;
    private readonly string _composeWorkingDirectory;

    public LocalTester(string composeFilePath)
    {
        _composeFilePath = composeFilePath;
        _composeWorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(composeFilePath)) ?? Directory.GetCurrentDirectory();

        _docker = new DockerClientConfiguration().CreateClient();
    }

    public async Task<TestResult> TestAsync(TestOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TestOptions();
        options.DockerComposeExecutable = DockerComposeExecutableResolver.Resolve(options.DockerComposeExecutable);
        var result = new TestResult();
        var sw = Stopwatch.StartNew();

        try
        {
            if (options.Verbose)
                Console.WriteLine("🧪 docker-compose up -d (local test)...");

            await RunComposeUpAsync(options, cancellationToken);

            if (options.Verbose)
                Console.WriteLine($"🕒 Waiting for compose containers (timeout: {options.StartupTimeout})...");

            var ok = await WaitForContainersAsync(options, cancellationToken);

            result.ContainerHealths = await GetContainerHealthsAsync(options, cancellationToken);
            result.Success = ok && result.ContainerHealths.All(c => c.IsHealthy);

            if (result.Success &&
                options.RunStartupChecks &&
                options.DeclarativeChecksByService is { Count: > 0 })
            {
                await RunStartupDeclarativeChecksAsync(result, options, cancellationToken).ConfigureAwait(false);
            }

            if (result.Success &&
                options.RunStartupChecks &&
                options.CodeTestsByService is { Count: > 0 })
            {
                await RunStartupCodeTestsAsync(result, options, cancellationToken).ConfigureAwait(false);
            }

            if (!result.Success && options.ShowLogsOnFailure)
                result.Logs = LogRedactor.RedactSecrets(await GetComposeLogsAsync(options, cancellationToken));

            if (options.Verbose)
            {
                Console.WriteLine();
                foreach (var health in result.ContainerHealths)
                {
                    var icon = health.IsHealthy ? "✅" : "❌";
                    var extra = health.HealthStatus != null ? $" (health: {health.HealthStatus})" : "";
                    Console.WriteLine($"{icon} {health.Name}: {health.Status}{extra}");
                    if (!string.IsNullOrWhiteSpace(health.Error))
                        Console.WriteLine($"   Error: {health.Error}");
                }

                Console.WriteLine(result.Success ? "✅ Test passed." : "❌ Test failed.");

                if (!result.Success && !string.IsNullOrWhiteSpace(result.Logs))
                {
                    Console.WriteLine();
                    Console.WriteLine("---- compose logs (tail) ----");
                    Console.WriteLine(result.Logs);
                    Console.WriteLine("------------------------------");
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.ToString());

            if (options.ShowLogsOnFailure)
            {
                try
                {
                    result.Logs = LogRedactor.RedactSecrets(await GetComposeLogsAsync(options, cancellationToken));
                }
                catch
                {
                    // best-effort
                }
            }

            if (options.Verbose)
            {
                Console.WriteLine();
                Console.WriteLine("❌ Local test threw an exception.");
                Console.WriteLine(ex.ToString());

                if (!string.IsNullOrWhiteSpace(result.Logs))
                {
                    Console.WriteLine();
                    Console.WriteLine("---- compose logs (tail) ----");
                    Console.WriteLine(result.Logs);
                    Console.WriteLine("------------------------------");
                }
            }
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.Elapsed;

            if (options.RemoveAfterTest)
            {
                try { await RunComposeDownAsync(options, cancellationToken); }
                catch { /* best-effort cleanup */ }
            }
        }

        return result;
    }

    public async Task StopAsync(string projectName, bool removeVolumes = false, string? dockerComposeExecutable = null, CancellationToken cancellationToken = default)
    {
        var exec = DockerComposeExecutableResolver.Resolve(dockerComposeExecutable);
        var args = $"-f \"{_composeFilePath}\" -p {projectName} down";
        if (removeVolumes)
            args += " -v";

        Console.WriteLine($"🛑 Stopping compose project '{projectName}'...");
        await RunProcessAsync(exec, args, cancellationToken);
        Console.WriteLine("🛑 Stop finished.");
    }

    private Task RunComposeUpAsync(TestOptions options, CancellationToken ct)
    {
        // --progress plain: глобальный флаг `docker compose` (до подкоманды up), иначе CLI пишет «unknown flag: --progress».
        var progress = options.DockerComposeExecutable.Trim().Equals("docker compose", StringComparison.OrdinalIgnoreCase)
            ? "--progress plain "
            : "";
        var args = $"{progress}-f \"{_composeFilePath}\" -p {options.ProjectName} up -d";
        return RunProcessAsync(options.DockerComposeExecutable, args, ct);
    }

    private Task RunComposeDownAsync(TestOptions options, CancellationToken ct)
    {
        var args = $"-f \"{_composeFilePath}\" -p {options.ProjectName} down -v";
        return RunProcessAsync(options.DockerComposeExecutable, args, ct);
    }

    private async Task<bool> WaitForContainersAsync(TestOptions options, CancellationToken ct)
    {
        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: (int)Math.Max(1, Math.Ceiling(options.StartupTimeout.TotalSeconds / 2)),
                sleepDurationProvider: _ => TimeSpan.FromSeconds(2));

        return await retryPolicy.ExecuteAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();

            var containers = await GetComposeContainersAsync(options, ct);
            if (containers.Count == 0)
                return false;

            var allOk = true;
            foreach (var container in containers)
            {
                var inspect = await _docker.Containers.InspectContainerAsync(container.ID, ct);

                var running = string.Equals(inspect.State?.Status, "running", StringComparison.OrdinalIgnoreCase);
                if (!running)
                {
                    allOk = false;
                    break;
                }

                var health = inspect.State?.Health;
                if (health != null && options.RequireHealthChecksIfDefined)
                {
                    if (!string.Equals(health.Status, "healthy", StringComparison.OrdinalIgnoreCase))
                    {
                        allOk = false;
                        break;
                    }
                }
            }

            return allOk;
        });
    }

    private async Task<List<ContainerHealth>> GetContainerHealthsAsync(TestOptions options, CancellationToken ct)
    {
        var result = new List<ContainerHealth>();
        var containers = await GetComposeContainersAsync(options, ct);

        foreach (var container in containers)
        {
            var inspect = await _docker.Containers.InspectContainerAsync(container.ID, ct);
            var health = inspect.State?.Health;

            var name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID;

            var isRunning = string.Equals(inspect.State?.Status, "running", StringComparison.OrdinalIgnoreCase);
            var isHealthy = isRunning;

            string? error = null;
            string? healthStatus = null;
            if (health != null)
            {
                healthStatus = health.Status;
                // If caller doesn't require healthchecks, treat "running" as OK even when health is "starting".
                if (options.RequireHealthChecksIfDefined)
                    isHealthy = string.Equals(health.Status, "healthy", StringComparison.OrdinalIgnoreCase);

                error = health.Log?.LastOrDefault()?.Output;
            }

            result.Add(new ContainerHealth
            {
                Name = name,
                Status = inspect.State?.Status ?? "unknown",
                HealthStatus = healthStatus,
                IsHealthy = isHealthy,
                Error = string.IsNullOrWhiteSpace(error) ? null : error
            });
        }

        return result;
    }

    private async Task<List<ContainerListResponse>> GetComposeContainersAsync(TestOptions options, CancellationToken ct)
    {
        var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);

        return containers
            .Where(c =>
                c.Labels != null &&
                c.Labels.TryGetValue("com.docker.compose.project", out var project) &&
                string.Equals(project, options.ProjectName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private Task<string> GetComposeLogsAsync(TestOptions options, CancellationToken ct)
    {
        var args = $"-f \"{_composeFilePath}\" -p {options.ProjectName} logs --tail=100";
        return RunProcessCaptureOutputAsync(options.DockerComposeExecutable, args, ct);
    }

    private async Task RunProcessAsync(string fileName, string args, CancellationToken ct)
    {
        var (resolvedFileName, resolvedArgs) = ComposeCommandLine.Build(fileName, args);
        Console.WriteLine($"[docker-compose cmd] {resolvedFileName} {resolvedArgs}");

        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            Arguments = resolvedArgs,
            WorkingDirectory = _composeWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                Console.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                Console.WriteLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Command '{resolvedFileName} {resolvedArgs}' failed (exit code {process.ExitCode}). See compose output above.");
    }

    private async Task<string> RunProcessCaptureOutputAsync(string fileName, string args, CancellationToken ct)
    {
        var (resolvedFileName, resolvedArgs) = ComposeCommandLine.Build(fileName, args);
        Console.WriteLine($"[docker-compose cmd] {resolvedFileName} {resolvedArgs}");

        var psi = new ProcessStartInfo
        {
            FileName = resolvedFileName,
            Arguments = resolvedArgs,
            WorkingDirectory = _composeWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;
            Console.WriteLine(e.Data);
            stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;
            Console.WriteLine(e.Data);
            stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Command '{resolvedFileName} {resolvedArgs}' failed (exit code {process.ExitCode}). {stderr}");

        return stdout.ToString();
    }

    private async Task RunStartupDeclarativeChecksAsync(TestResult result, TestOptions options, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var composeFileName = Path.GetFileName(_composeFilePath);

        foreach (var kv in options.DeclarativeChecksByService!)
        {
            var serviceName = kv.Key;
            foreach (var test in kv.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(options.TestExecutionTimeout);

                var rec = await DeclarativeCheckRunner.RunCheckAsync(
                    test,
                    serviceName,
                    _composeWorkingDirectory,
                    composeFileName,
                    options.ProjectName,
                    options.DockerComposeExecutable,
                    http,
                    timeoutCts.Token).ConfigureAwait(false);

                result.CheckRuns.Add(rec);

                if (!rec.Success && test.Criticality == TestCriticality.Critical)
                    result.Success = false;

                if (options.Verbose)
                {
                    var sev = rec.Success ? "ok" : test.Criticality.ToString();
                    Console.WriteLine(rec.Success
                        ? $"[check] {serviceName}/{rec.TestId}: {sev}"
                        : $"[check] {serviceName}/{rec.TestId}: FAIL ({test.Criticality}) — {rec.Message}");
                }
            }
        }
    }

    private async Task RunStartupCodeTestsAsync(TestResult result, TestOptions options, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var composeFileName = Path.GetFileName(_composeFilePath);

        foreach (var kv in options.CodeTestsByService!)
        {
            var serviceName = kv.Key;
            foreach (var def in kv.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(options.TestExecutionTimeout);

                var rec = await CodeTestRunner.RunAsync(
                    def,
                    serviceName,
                    _composeWorkingDirectory,
                    composeFileName,
                    options.ProjectName,
                    options.DockerComposeExecutable,
                    http,
                    timeoutCts.Token).ConfigureAwait(false);

                result.CheckRuns.Add(rec);

                if (!rec.Success && def.Criticality == TestCriticality.Critical)
                    result.Success = false;

                if (options.Verbose)
                {
                    Console.WriteLine(rec.Success
                        ? $"[test] {serviceName}/{rec.TestId}: ok"
                        : $"[test] {serviceName}/{rec.TestId}: FAIL ({def.Criticality}) — {rec.Message}");
                }
            }
        }
    }
}
