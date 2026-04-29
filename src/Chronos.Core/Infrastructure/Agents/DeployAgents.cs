using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// Абстракция и реализация HTTP-клиента к Chronos.Agent: деплой YAML, проекты, логи, compose по имени.
namespace Chronos.Core;

public interface IDeployAgent
{
    Task<DeployResult> DeployAsync(string composeYaml, CancellationToken cancellationToken = default);
    Task<DeployResult> StartAsync(string? composeYaml = null, CancellationToken cancellationToken = default);
    Task<DeployResult> StopAsync(bool removeVolumes = false, CancellationToken cancellationToken = default);
    Task<DeployResult> RestartAsync(string? composeYaml = null, CancellationToken cancellationToken = default);
    Task<DeployResult> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<string> GetLogsAsync(string? service = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken cancellationToken = default);
    Task<string> GetComposeAsync(string projectName, CancellationToken cancellationToken = default);
    Task UploadComposeAsync(string projectName, string composeYaml, CancellationToken cancellationToken = default);

    Task<DeployResult> StartProjectAsync(string projectName, string? composeYaml = null, CancellationToken cancellationToken = default);
    Task<DeployResult> StopProjectAsync(string projectName, bool removeVolumes = false, CancellationToken cancellationToken = default);
    Task<DeployResult> RestartProjectAsync(string projectName, string? composeYaml = null, CancellationToken cancellationToken = default);
    Task<DeployResult> GetProjectStatusAsync(string projectName, CancellationToken cancellationToken = default);
    Task<string> GetProjectLogsAsync(string projectName, string? service = null, CancellationToken cancellationToken = default);
}

/// <summary>REST-вызовы к базовому URL агента с опциональным заголовком <c>X-API-Key</c>.</summary>
public sealed class HttpDeployAgent : IDeployAgent
{
    private static readonly JsonSerializerOptions DeployJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _agentUrl;

    public HttpDeployAgent(string agentUrl, string? apiKey = null, HttpClient? httpClient = null)
    {
        _agentUrl = agentUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    /// <summary>
    /// When the agent returns <see cref="DeployResult.OperationPending"/>, polls GET status until <see cref="DeployResult.DeploymentInProgress"/> is false.
    /// </summary>
    private async Task<DeployResult> AwaitBackgroundDeploymentAsync(
        Func<Task<DeployResult>> pollStatus,
        CancellationToken cancellationToken)
    {
        var delayMs = 1000;
        while (!cancellationToken.IsCancellationRequested)
        {
            var s = await pollStatus().ConfigureAwait(false);
            if (!s.DeploymentInProgress)
                return s;
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            delayMs = Math.Min(delayMs + 500, 5000);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async Task<DeployResult> DeployAsync(string composeYaml, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync($"{_agentUrl}/deploy", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent deploy failed ({(int)response.StatusCode}). {responseText}");

        var result = JsonSerializer.Deserialize<DeployResult>(responseText, DeployJsonOptions);

        result ??= new DeployResult { Success = false, Error = "Empty agent response" };
        if (result.OperationPending)
            return await AwaitBackgroundDeploymentAsync(() => GetStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<DeployResult> StartAsync(string? composeYaml = null, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(composeYaml))
            content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync($"{_agentUrl}/start", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent start failed ({(int)response.StatusCode}). {responseText}");

        var result = JsonSerializer.Deserialize<DeployResult>(responseText, DeployJsonOptions)
                     ?? new DeployResult { Success = false, Error = "Empty agent response" };
        if (result.OperationPending)
            return await AwaitBackgroundDeploymentAsync(() => GetStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<DeployResult> StopAsync(bool removeVolumes = false, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var response = await _http.PostAsync($"{_agentUrl}/stop?removeVolumes={removeVolumes}", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent stop failed ({(int)response.StatusCode}). {responseText}");

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
    }

    public async Task<DeployResult> RestartAsync(string? composeYaml = null, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(composeYaml))
            content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync($"{_agentUrl}/restart", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent restart failed ({(int)response.StatusCode}). {responseText}");

        var result = JsonSerializer.Deserialize<DeployResult>(responseText, DeployJsonOptions)
                     ?? new DeployResult { Success = false, Error = "Empty agent response" };
        if (result.OperationPending)
            return await AwaitBackgroundDeploymentAsync(() => GetStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<DeployResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"{_agentUrl}/status", cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent status failed ({(int)response.StatusCode}). {responseText}");

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
    }

    public async Task<string> GetLogsAsync(string? service = null, CancellationToken cancellationToken = default)
    {
        var url = $"{_agentUrl}/logs";
        if (!string.IsNullOrWhiteSpace(service))
            url += $"?service={Uri.EscapeDataString(service)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent logs failed ({(int)response.StatusCode}). {text}");

        return text;
    }

    public async Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"{_agentUrl}/projects", cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent projects failed ({(int)response.StatusCode}). {responseText}");

        var projects = JsonSerializer.Deserialize<List<string>>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<string>();

        return projects;
    }

    public async Task<string> GetComposeAsync(string projectName, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/compose", cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent get compose failed ({(int)response.StatusCode}). {text}");

        return text;
    }

    public async Task UploadComposeAsync(string projectName, string composeYaml, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/compose",
            content,
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent upload compose failed ({(int)response.StatusCode}). {responseText}");
    }

    public async Task<DeployResult> StartProjectAsync(string projectName, string? composeYaml = null, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(composeYaml))
            content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/start",
            content,
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent start project failed ({(int)response.StatusCode}). {responseText}");

        var result = JsonSerializer.Deserialize<DeployResult>(responseText, DeployJsonOptions)
                     ?? new DeployResult { Success = false, Error = "Empty agent response" };
        if (result.OperationPending)
        {
            return await AwaitBackgroundDeploymentAsync(
                () => GetProjectStatusAsync(projectName, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<DeployResult> StopProjectAsync(string projectName, bool removeVolumes = false, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/stop?removeVolumes={removeVolumes}",
            content,
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent stop project failed ({(int)response.StatusCode}). {responseText}");

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
    }

    public async Task<DeployResult> RestartProjectAsync(string projectName, string? composeYaml = null, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(composeYaml))
            content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/restart",
            content,
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent restart project failed ({(int)response.StatusCode}). {responseText}");

        var result = JsonSerializer.Deserialize<DeployResult>(responseText, DeployJsonOptions)
                     ?? new DeployResult { Success = false, Error = "Empty agent response" };
        if (result.OperationPending)
        {
            return await AwaitBackgroundDeploymentAsync(
                () => GetProjectStatusAsync(projectName, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<DeployResult> GetProjectStatusAsync(string projectName, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/status", cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent status project failed ({(int)response.StatusCode}). {responseText}");

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
    }

    public async Task<string> GetProjectLogsAsync(string projectName, string? service = null, CancellationToken cancellationToken = default)
    {
        var url = $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/logs";
        if (!string.IsNullOrWhiteSpace(service))
            url += $"?service={Uri.EscapeDataString(service)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent logs project failed ({(int)response.StatusCode}). {text}");

        return text;
    }

    public async Task UploadManifestJsonAsync(string projectName, string manifestJson, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(manifestJson, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/chronos/manifest",
            content,
            cancellationToken).ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent chronos manifest failed ({(int)response.StatusCode}). {responseText}");
    }

    public async Task UploadArtifactsTarAsync(
        string projectName,
        IReadOnlyList<DeployArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        if (artifacts.Count == 0)
            return;

        var temp = Path.Combine(Path.GetTempPath(), $"chronos-artifacts-{Guid.NewGuid():N}.tar");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 20,
                         options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await DeployArtifactTarWriter.WriteArtifactsAsync(artifacts, fs, cancellationToken).ConfigureAwait(false);
            }

            await using var upload = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(upload), "archive", "artifacts.tar");

            using var response = await _http.PostAsync(
                $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/chronos/artifacts",
                form,
                cancellationToken).ConfigureAwait(false);

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Agent chronos artifacts failed ({(int)response.StatusCode}). {responseText}");
        }
        finally
        {
            try { File.Delete(temp); }
            catch { /* best-effort */ }
        }
    }

    public async Task<DiagnosticsSnapshot> GetDiagnosticsAsync(string projectName, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/chronos/diagnostics",
            cancellationToken).ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent chronos diagnostics failed ({(int)response.StatusCode}). {responseText}");

        return JsonSerializer.Deserialize<DiagnosticsSnapshot>(responseText, ManifestJson.Options)
               ?? new DiagnosticsSnapshot();
    }

    public async Task SnapshotVolumeToFileAsync(
        string projectName,
        string volumeName,
        string localFilePath,
        string compress = "gzip",
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/snapshot?compress={Uri.EscapeDataString(compress)}";

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Agent volume snapshot failed ({(int)response.StatusCode}). {err}");
        }

        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 20,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await httpStream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VolumeOperationResult> SnapshotVolumeUploadToUrlAsync(
        string projectName,
        string volumeName,
        VolumeSnapshotUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request, ManifestJson.Options), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/snapshot/upload",
            content,
            cancellationToken).ConfigureAwait(false);

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent volume snapshot upload failed ({(int)response.StatusCode}). {text}");

        return JsonSerializer.Deserialize<VolumeOperationResult>(text, ManifestJson.Options)
               ?? new VolumeOperationResult { Success = false, Error = "Empty response" };
    }

    public async Task<VolumeOperationResult> RestoreVolumeFromFileAsync(
        string projectName,
        string volumeName,
        string localArchivePath,
        string compress = "gzip",
        CancellationToken cancellationToken = default)
    {
        await using var fs = new FileStream(localArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(fs), "archive", Path.GetFileName(localArchivePath));

        var url =
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/restore?compress={Uri.EscapeDataString(compress)}";

        using var response = await _http.PostAsync(url, form, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent volume restore failed ({(int)response.StatusCode}). {text}");

        return JsonSerializer.Deserialize<VolumeOperationResult>(text, ManifestJson.Options)
               ?? new VolumeOperationResult { Success = false, Error = "Empty response" };
    }

    public async Task<VolumeOperationResult> RestoreVolumeFromUrlAsync(
        string projectName,
        string volumeName,
        VolumeRestoreFromUrlRequest request,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request, ManifestJson.Options), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(
            $"{_agentUrl}/projects/{Uri.EscapeDataString(projectName)}/volumes/{Uri.EscapeDataString(volumeName)}/restore-url",
            content,
            cancellationToken).ConfigureAwait(false);

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent volume restore-url failed ({(int)response.StatusCode}). {text}");

        return JsonSerializer.Deserialize<VolumeOperationResult>(text, ManifestJson.Options)
               ?? new VolumeOperationResult { Success = false, Error = "Empty response" };
    }
}

