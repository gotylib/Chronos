namespace Chronos.Core;

/// <summary>Normalizes docker-compose executable input for v1/v2 CLIs.</summary>
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
