using System.Net.Http.Headers;
using System.Text.Json;

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

public sealed class HttpDeployAgent : IDeployAgent
{
    private readonly HttpClient _http;
    private readonly string _agentUrl;

    public HttpDeployAgent(string agentUrl, string? apiKey = null, HttpClient? httpClient = null)
    {
        _agentUrl = agentUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public async Task<DeployResult> DeployAsync(string composeYaml, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(composeYaml), "compose");

        using var response = await _http.PostAsync($"{_agentUrl}/deploy", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Agent deploy failed ({(int)response.StatusCode}). {responseText}");

        var result = JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result ?? new DeployResult { Success = false, Error = "Empty agent response" };
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

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
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

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
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

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
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

        return JsonSerializer.Deserialize<DeployResult>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new DeployResult { Success = false, Error = "Empty agent response" };
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
}

