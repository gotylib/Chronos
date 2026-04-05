using System.Text.RegularExpressions;

namespace Chronos.Core.Safety;

public static class CommandSafety
{
    private static readonly Regex DangerousRmRf = new(@"rm\s+-rf", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] DefaultDangerousPatterns =
    [
        "rm -rf",
        "shutdown",
        "poweroff",
        "reboot",
        "drop database",
        "drop table",
        "truncate table",
        "mkfs.",
        "dd if=",
        "docker system prune",
        "docker rm -",
    ];

    /// <summary>
    /// If command is blocked, returns false and a human-readable reason.
    /// </summary>
    public static bool ValidateShellCommand(
        string projectName,
        string command,
        out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(command))
            return true;

        var cmd = command.Trim();
        var lower = cmd.ToLowerInvariant();

        // Sensitive mode is optional: only enforced when whitelist is explicitly provided.
        var sensitiveProjects = ReadSensitiveProjects();
        var isSensitive = sensitiveProjects.Contains(projectName, StringComparer.OrdinalIgnoreCase);

        // Blocklist
        foreach (var p in DefaultDangerousPatterns)
        {
            if (lower.Contains(p, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Command contains dangerous pattern '{p}'.";
                return false;
            }
        }

        if (DangerousRmRf.IsMatch(cmd))
        {
            reason = "Command contains 'rm -rf'.";
            return false;
        }

        // Optional whitelist for sensitive projects
        if (isSensitive)
        {
            var whitelistRaw = Environment.GetEnvironmentVariable("CHRONOS_AGENT_SENSITIVE_COMMAND_WHITELIST");
            if (!string.IsNullOrWhiteSpace(whitelistRaw))
            {
                var whitelist = whitelistRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();

                if (whitelist.Length > 0 && !whitelist.Any(w => lower.Contains(w.ToLowerInvariant())))
                {
                    reason = $"Sensitive project '{projectName}': command is not in allowed whitelist.";
                    return false;
                }
            }
        }

        return true;
    }

    public static bool ValidateScriptFile(
        string projectName,
        string scriptPath,
        out string? reason)
    {
        reason = null;
        if (!File.Exists(scriptPath))
        {
            reason = $"Script not found: {scriptPath}";
            return false;
        }

        // Read up to 1MB to avoid huge file scans.
        var info = new FileInfo(scriptPath);
        if (info.Length > 1024 * 1024)
        {
            reason = "Script too large for safety scan (limit: 1MB).";
            return false;
        }

        var text = File.ReadAllText(scriptPath);
        return ValidateShellCommand(projectName, text, out reason);
    }

    private static HashSet<string> ReadSensitiveProjects()
    {
        var raw = Environment.GetEnvironmentVariable("CHRONOS_AGENT_SENSITIVE_PROJECTS");
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

