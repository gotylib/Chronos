using System.Diagnostics;

// docker compose exec в конкретный сервис (shell-команда внутри уже запущенного контейнера).
namespace Chronos.Core;

internal static class ComposeExec
{
    internal static string QuoteUnix(string command)
        => "'" + command.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    internal static async Task<(int ExitCode, string Stdout, string Stderr)> RunInServiceAsync(
        string dockerComposeExecutable,
        string composeWorkingDirectory,
        string composeFileName,
        string projectName,
        string serviceName,
        string shellCommand,
        CancellationToken cancellationToken)
    {
        var composeArgs = $"-f \"{composeFileName}\" -p {projectName} exec -T {serviceName} sh -c {QuoteUnix(shellCommand)}";
        var (fileName, args) = ComposeCommandLine.Build(dockerComposeExecutable, composeArgs);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = composeWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }
}
