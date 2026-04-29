// Сборка командной строки: отдельное приложение docker-compose v1 или «docker compose» v2.
namespace Chronos.Core;

/// <summary>Нормализует значение исполняемого файла compose для v1 и v2 CLI.</summary>
public static class ComposeCommandLine
{
    /// <summary>
    /// Supports values like:
    /// - "docker-compose" (v1)
    /// - "docker" with "compose" in arguments (v2)
    /// - "docker compose" (v2 shortcut)
    /// </summary>
    public static (string FileName, string Arguments) Build(string dockerComposeExecutable, string composeArguments)
    {
        var exec = DockerComposeExecutableResolver.Resolve(
            string.IsNullOrWhiteSpace(dockerComposeExecutable) ? null : dockerComposeExecutable.Trim());
        if (exec.Equals("docker compose", StringComparison.OrdinalIgnoreCase))
            return ("docker", $"compose {composeArguments}");

        if (exec.Contains(' ', StringComparison.Ordinal))
        {
            var split = exec.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (split.Length == 2)
                return (split[0], $"{split[1]} {composeArguments}");
        }

        return (exec, composeArguments);
    }
}
