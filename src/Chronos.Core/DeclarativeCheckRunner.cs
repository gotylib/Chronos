using System.Diagnostics;

namespace Chronos.Core;

public static class DeclarativeCheckRunner
{
    public static async Task<CheckRunRecord> RunCheckAsync(
        DeclarativeCheck test,
        string serviceName,
        string composeWorkingDirectory,
        string composeFileName,
        string projectName,
        string dockerComposeExecutable,
        HttpClient http,
        CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(test.Id) ? "test" : test.Id;
        var utc = DateTimeOffset.UtcNow;

        try
        {
            if (string.Equals(test.Type, "http", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(test.Url))
                    return Fail(serviceName, id, test.Criticality, "HTTP test requires Url.", utc);

                using var response = await http.GetAsync(test.Url, cancellationToken).ConfigureAwait(false);
                var expected = test.ExpectedStatus ?? 200;
                if ((int)response.StatusCode != expected)
                {
                    return Fail(serviceName, id, test.Criticality,
                        $"Expected status {expected}, got {(int)response.StatusCode}.", utc);
                }

                return Ok(serviceName, id, test.Criticality, utc);
            }

            if (string.Equals(test.Type, "exec", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(test.ExecCommand))
                    return Fail(serviceName, id, test.Criticality, "Exec test requires ExecCommand.", utc);

                var (code, stdout, stderr) = await ComposeExec.RunInServiceAsync(
                    dockerComposeExecutable,
                    composeWorkingDirectory,
                    composeFileName,
                    projectName,
                    serviceName,
                    test.ExecCommand,
                    cancellationToken).ConfigureAwait(false);

                if (code != 0)
                {
                    var tail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return Fail(serviceName, id, test.Criticality,
                        $"exec failed (exit {code}): {Trim(tail, 2000)}", utc);
                }

                return Ok(serviceName, id, test.Criticality, utc);
            }

            return Fail(serviceName, id, test.Criticality, $"Unknown test type '{test.Type}'.", utc);
        }
        catch (Exception ex)
        {
            return Fail(serviceName, id, test.Criticality, ex.Message, utc);
        }
    }

    public static async Task<JobRunRecord> RunJobAsync(
        JobDefinition job,
        string serviceName,
        string composeWorkingDirectory,
        string composeFileName,
        string projectName,
        string dockerComposeExecutable,
        string projectDirectoryOnAgent,
        CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(job.Id) ? "job" : job.Id;
        var utc = DateTimeOffset.UtcNow;

        try
        {
            if (string.Equals(job.Type, "exec", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(job.ExecCommand))
                    return JobFail(serviceName, id, job.Criticality, "Exec job requires ExecCommand.", utc);

                var (code, stdout, stderr) = await ComposeExec.RunInServiceAsync(
                    dockerComposeExecutable,
                    composeWorkingDirectory,
                    composeFileName,
                    projectName,
                    serviceName,
                    job.ExecCommand,
                    cancellationToken).ConfigureAwait(false);

                if (code != 0)
                {
                    var tail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return JobFail(serviceName, id, job.Criticality,
                        $"exec failed (exit {code}): {Trim(tail, 2000)}", utc);
                }

                return JobOk(serviceName, id, job.Criticality, utc);
            }

            if (string.Equals(job.Type, "script", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(job.ScriptRelativePath))
                    return JobFail(serviceName, id, job.Criticality, "Script job requires ScriptRelativePath.", utc);

                var scriptPath = Path.GetFullPath(Path.Combine(projectDirectoryOnAgent, job.ScriptRelativePath));
                if (!scriptPath.StartsWith(Path.GetFullPath(projectDirectoryOnAgent), StringComparison.OrdinalIgnoreCase))
                    return JobFail(serviceName, id, job.Criticality, "Script path escapes project directory.", utc);

                if (!File.Exists(scriptPath))
                    return JobFail(serviceName, id, job.Criticality, $"Script not found: {scriptPath}", utc);

                string fileName;
                string arguments;
                if (OperatingSystem.IsWindows())
                {
                    fileName = "cmd.exe";
                    arguments = $"/c \"\"{scriptPath}\"\"";
                }
                else
                {
                    fileName = "/bin/sh";
                    arguments = $"\"{scriptPath}\"";
                }

                var (code, stdout, stderr) = await RunProcessCaptureAsync(
                    fileName,
                    arguments,
                    projectDirectoryOnAgent,
                    cancellationToken).ConfigureAwait(false);

                if (code != 0)
                {
                    var tail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return JobFail(serviceName, id, job.Criticality,
                        $"script failed (exit {code}): {Trim(tail, 2000)}", utc);
                }

                return JobOk(serviceName, id, job.Criticality, utc);
            }

            return JobFail(serviceName, id, job.Criticality, $"Unknown job type '{job.Type}'.", utc);
        }
        catch (Exception ex)
        {
            return JobFail(serviceName, id, job.Criticality, ex.Message, utc);
        }
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static CheckRunRecord Ok(string service, string testId, TestCriticality c, DateTimeOffset utc)
        => new()
        {
            Service = service,
            TestId = testId,
            Success = true,
            UtcTime = utc,
            Criticality = c
        };

    private static CheckRunRecord Fail(string service, string testId, TestCriticality c, string msg, DateTimeOffset utc)
        => new()
        {
            Service = service,
            TestId = testId,
            Success = false,
            Message = msg,
            UtcTime = utc,
            Criticality = c
        };

    private static JobRunRecord JobOk(string service, string jobId, TestCriticality c, DateTimeOffset utc)
        => new()
        {
            Service = service,
            JobId = jobId,
            Success = true,
            UtcTime = utc,
            Criticality = c
        };

    private static JobRunRecord JobFail(string service, string jobId, TestCriticality c, string msg, DateTimeOffset utc)
        => new()
        {
            Service = service,
            JobId = jobId,
            Success = false,
            Message = msg,
            UtcTime = utc,
            Criticality = c
        };

    private static async Task<(int Code, string Stdout, string Stderr)> RunProcessCaptureAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
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

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }
}
