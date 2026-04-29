using System.Text.Json;
using System.Text.Json.Serialization;
using Chronos.Core;

namespace Chronos.Agent;

/// <summary>
/// Файл deployment.state в .chronos: фоновый deploy/start не завершён — см. <see cref="DeployResult.DeploymentInProgress"/>.
/// </summary>
public static class DeploymentStateHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetStatePath(string projectRoot) =>
        Path.Combine(projectRoot, ".chronos", "deployment.state");

    public static async Task WriteInProgressAsync(string projectRoot, string deploymentId, CancellationToken ct)
    {
        var dir = Path.Combine(projectRoot, ".chronos");
        Directory.CreateDirectory(dir);
        var path = GetStatePath(projectRoot);
        var dto = new DeploymentStateDto
        {
            InProgress = true,
            DeploymentId = deploymentId
        };
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(dto, JsonOptions), ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task CompleteAsync(string projectRoot, string deploymentId, bool success, string? error, CancellationToken ct)
    {
        var path = GetStatePath(projectRoot);
        var dto = new DeploymentStateDto
        {
            InProgress = false,
            DeploymentId = deploymentId,
            LastSuccess = success,
            LastError = error
        };
        var dir = Path.Combine(projectRoot, ".chronos");
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(dto, JsonOptions), ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task<DeployResult> AttachAsync(DeployResult status, string projectRoot, CancellationToken ct)
    {
        var path = GetStatePath(projectRoot);
        if (!File.Exists(path))
            return status;

        DeploymentStateDto? dto;
        try
        {
            await using var fs = File.OpenRead(path);
            dto = await JsonSerializer.DeserializeAsync<DeploymentStateDto>(fs, JsonOptions, ct).ConfigureAwait(false);
        }
        catch
        {
            return status;
        }

        if (dto == null)
            return status;

        status.DeploymentInProgress = dto.InProgress;
        if (!string.IsNullOrWhiteSpace(dto.DeploymentId))
            status.DeploymentId = dto.DeploymentId;

        if (dto is { InProgress: false, LastSuccess: false } && !string.IsNullOrWhiteSpace(dto.LastError))
        {
            status.Success = false;
            status.Error = string.IsNullOrWhiteSpace(status.Error) ? dto.LastError : $"{status.Error} {dto.LastError}";
        }

        return status;
    }

    /// <summary>
    /// Gives <c>chronos publish</c> time to upload manifest after async POST /start returns.
    /// </summary>
    public static async Task WaitForManifestUploadWindowAsync(string projectDir, TimeSpan maxWait, CancellationToken ct)
    {
        var manifestPath = Path.Combine(projectDir, ".chronos", "manifest.json");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(manifestPath) && sw.Elapsed < maxWait)
            await Task.Delay(250, ct).ConfigureAwait(false);
    }

    private sealed class DeploymentStateDto
    {
        public bool InProgress { get; set; }
        public string? DeploymentId { get; set; }
        public bool? LastSuccess { get; set; }
        public string? LastError { get; set; }
    }
}
