using System.Diagnostics;

namespace Chronos.Core;

/// <summary>
/// Picks <c>docker compose</c> (plugin v2) or <c>docker-compose</c> (v1) by probing the local machine.
/// Use configured value <c>auto</c> / empty to enable detection; any other string is used as-is.
/// </summary>
public static class DockerComposeExecutableResolver
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static string? _cached;
    private static readonly object Gate = new();

    /// <summary>
    /// Returns <paramref name="configured"/> unchanged unless it is null, whitespace, <c>auto</c>, or <c>detect</c>.
    /// </summary>
    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var t = configured.Trim();
            if (!t.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                !t.Equals("detect", StringComparison.OrdinalIgnoreCase))
                return t;
        }

        lock (Gate)
            return _cached ??= Detect();
    }

    /// <summary>Clears cached detection (e.g. in tests).</summary>
    public static void ClearCache()
    {
        lock (Gate)
            _cached = null;
    }

    private static string Detect()
    {
        if (TryExitZero("docker", "compose version", ProbeTimeout))
            return "docker compose";
        if (TryExitZero("docker-compose", "version", ProbeTimeout))
            return "docker-compose";
        // Prefer v2 spelling; docker CLI is usually present even if compose subcommand fails elsewhere.
        return "docker compose";
    }

    private static bool TryExitZero(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            p.Start();
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return false;
            }

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
