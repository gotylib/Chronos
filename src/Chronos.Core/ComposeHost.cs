using System.Diagnostics;
using System.Text;

namespace Chronos.Core;

/// <summary>Запуск compose с хоста (без <c>exec</c> в сервис): restart, logs, и т.д.</summary>
internal static class ComposeHost
{
    internal static async Task<(int ExitCode, string Stdout, string Stderr)> RunComposeAsync(
        string dockerComposeExecutable,
        string composeWorkingDirectory,
        string composeFileName,
        string projectName,
        string composeArguments,
        CancellationToken cancellationToken)
    {
        var composeArgs = $"-f \"{composeFileName}\" -p {projectName} {composeArguments}";
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

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
