using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace Chronos.Agent.Infrastructure.EmbeddedMinio;

/// <summary>
/// Поднимает локальный MinIO через Docker CLI (только если включено конфигом и не задано внешнее CHRONOS_VOLUME_STORAGE_*).
/// </summary>
public sealed class EmbeddedMinioOutcome
{
    public bool StartedOrVerified { get; init; }
    public string? ServiceUrl { get; init; }
    public string? AccessKey { get; init; }
    public string? SecretKey { get; init; }
    public string? Error { get; init; }
}

public static class EmbeddedMinioProvisioner
{
    private const string ContainerName = "chronos-embedded-minio";

    public static async Task<EmbeddedMinioOutcome> TryEnsureAsync(
        string appPath,
        string dockerExecutable,
        IConfiguration configuration,
        CancellationToken ct)
    {
        static bool Truthy(string? v) =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";

        if (!Truthy(configuration["CHRONOS_AGENT_EMBEDDED_MINIO_ENABLED"]))
            return new EmbeddedMinioOutcome { StartedOrVerified = false };

        if (Truthy(configuration["CHRONOS_VOLUME_STORAGE_ENABLED"]) &&
            !string.IsNullOrWhiteSpace(configuration["CHRONOS_VOLUME_STORAGE_SERVICE_URL"]))
        {
            Console.WriteLine("[agent] Embedded MinIO skipped: CHRONOS_VOLUME_STORAGE_* is explicitly configured.");
            return new EmbeddedMinioOutcome { StartedOrVerified = false };
        }

        var dataDir = Path.Combine(appPath, ".chronos", "embedded-minio-data");
        Directory.CreateDirectory(dataDir);

        var credPath = Path.Combine(appPath, ".chronos", "embedded-minio-credentials.env");
        var configuredAccess = configuration["CHRONOS_AGENT_EMBEDDED_MINIO_ROOT_USER"]?.Trim();
        var configuredSecret = configuration["CHRONOS_AGENT_EMBEDDED_MINIO_ROOT_PASSWORD"]?.Trim();

        string accessKey;
        string secretKey;
        if (File.Exists(credPath))
        {
            var kv = await ParseEnvFileAsync(credPath, ct).ConfigureAwait(false);
            accessKey = kv.GetValueOrDefault("MINIO_ROOT_USER", "chronosminio");
            secretKey = kv.GetValueOrDefault("MINIO_ROOT_PASSWORD", "");
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                secretKey = !string.IsNullOrWhiteSpace(configuredSecret) ? configuredSecret : GenerateSecret();
                await WriteCredentialsAsync(credPath, accessKey, secretKey, ct).ConfigureAwait(false);
            }
        }
        else
        {
            accessKey = !string.IsNullOrWhiteSpace(configuredAccess) ? configuredAccess : "chronosminio";
            secretKey = !string.IsNullOrWhiteSpace(configuredSecret) ? configuredSecret : GenerateSecret();
            await WriteCredentialsAsync(credPath, accessKey, secretKey, ct).ConfigureAwait(false);
        }

        var apiPort = int.TryParse(configuration["CHRONOS_AGENT_EMBEDDED_MINIO_API_PORT"], out var ap) ? ap : 9019;
        var consolePort = int.TryParse(configuration["CHRONOS_AGENT_EMBEDDED_MINIO_CONSOLE_PORT"], out var cp) ? cp : 9029;

        var running = await ContainerRunningAsync(dockerExecutable, ct).ConfigureAwait(false);
        if (!running)
        {
            var args =
                $"run -d --restart unless-stopped --name {ContainerName} " +
                $"-p {apiPort}:9000 -p {consolePort}:9001 " +
                $"-v \"{dataDir}:/data\" " +
                $"--env-file \"{credPath}\" " +
                $"minio/minio:latest server /data --console-address \":9001\"";

            var psi = new ProcessStartInfo
            {
                FileName = dockerExecutable,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return new EmbeddedMinioOutcome { StartedOrVerified = false, Error = "docker run failed to start." };

            var err = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                return new EmbeddedMinioOutcome
                {
                    StartedOrVerified = false,
                    Error = $"docker run exited {proc.ExitCode}: {err}"
                };
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        var publicUrl = configuration["CHRONOS_AGENT_EMBEDDED_MINIO_SERVICE_URL"]?.Trim();
        if (string.IsNullOrWhiteSpace(publicUrl))
            publicUrl = GuessReachableUrl(apiPort, configuration);

        Console.WriteLine($"[agent] Embedded MinIO API URL (for S3 clients): {publicUrl}");

        return new EmbeddedMinioOutcome
        {
            StartedOrVerified = true,
            ServiceUrl = publicUrl.TrimEnd('/'),
            AccessKey = accessKey,
            SecretKey = secretKey
        };
    }

    private static string GuessReachableUrl(int hostPort, IConfiguration configuration)
    {
        var hint = configuration["CHRONOS_AGENT_EMBEDDED_MINIO_PUBLIC_HOST"]?.Trim();
        if (!string.IsNullOrWhiteSpace(hint))
            return $"http://{hint}:{hostPort}";

        // Внутри контейнера агента localhost указывает на сам контейнер — типичный шлюз Docker bridge.
        if (File.Exists("/.dockerenv"))
            return $"http://172.17.0.1:{hostPort}";

        return $"http://127.0.0.1:{hostPort}";
    }

    private static async Task<bool> ContainerRunningAsync(string dockerExecutable, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = dockerExecutable,
                Arguments = $"inspect -f \"{{{{.State.Running}}}}\" {ContainerName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return false;

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode == 0 && stdout.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task WriteCredentialsAsync(string path, string user, string pass, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MINIO_ROOT_USER={user}");
        sb.AppendLine($"MINIO_ROOT_PASSWORD={pass}");
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> ParseEnvFileAsync(string path, CancellationToken ct)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0)
                continue;
            dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }

        return dict;
    }
}
