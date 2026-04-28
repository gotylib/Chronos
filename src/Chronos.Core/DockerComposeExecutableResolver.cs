using System.Diagnostics;

namespace Chronos.Core;

/// <summary>
/// Picks a working Compose CLI by probing the local machine: prefers <c>docker compose</c> only when
/// a Docker daemon answers <c>docker info</c>; otherwise tries <c>podman compose</c> (Podman Desktop / Linux
/// without DOCKER_HOST), then <c>docker-compose</c> (v1). Use configured value <c>auto</c> / empty to enable
/// detection; any other string is used as-is.
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
        // `docker compose version` succeeds without a reachable engine (client-only). Require `docker info`.
        if (TryExitZero("docker", "info", ProbeTimeout) &&
            TryExitZero("docker", "compose version", ProbeTimeout))
            return "docker compose";

        if (TryExitZero("podman", "compose version", ProbeTimeout))
            return "podman compose";

        if (TryExitZero("docker-compose", "version", ProbeTimeout))
            return "docker-compose";

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
            // Drain pipes in parallel with WaitForExit; otherwise a verbose `docker info` can fill buffers and deadlock.
            var drainOut = Task.Run(() => { try { p.StandardOutput.ReadToEnd(); } catch { /* ignore */ } });
            var drainErr = Task.Run(() => { try { p.StandardError.ReadToEnd(); } catch { /* ignore */ } });
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return false;
            }

            Task.WaitAll(drainOut, drainErr);

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
