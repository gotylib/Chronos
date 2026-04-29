using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Тонкий HTTP-клиент к API Chronos.Master для публикации стека в кластер (выбор агента на стороне Master).
namespace Chronos.Core;

public sealed class ClusterDeployRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string ComposeYaml { get; set; } = string.Empty;
    public string? PreferredLocation { get; set; }
    public string? ManifestJson { get; set; }
}

public sealed class ClusterDeployResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
    public JsonNode? AgentResponse { get; set; }
}

/// <summary>Вызовы <c>/cluster/deploy</c> и <c>/cluster/publish</c> на Master.</summary>
public sealed class ClusterClient
{
    private readonly HttpClient _http;
    private readonly string _masterUrl;

    public ClusterClient(string masterUrl, string? apiKey = null, HttpClient? httpClient = null)
    {
        _masterUrl = masterUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient();
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public Task<ClusterDeployResult> DeployAsync(ClusterDeployRequest request, CancellationToken cancellationToken = default)
        => PostAsync("/cluster/deploy", request, cancellationToken);

    public Task<ClusterDeployResult> PublishAsync(ClusterDeployRequest request, CancellationToken cancellationToken = default)
        => PostAsync("/cluster/publish", request, cancellationToken);

    private async Task<ClusterDeployResult> PostAsync(string path, ClusterDeployRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, ManifestJson.Options);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_masterUrl}{path}", content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Master API failed ({(int)response.StatusCode}): {text}");

        return JsonSerializer.Deserialize<ClusterDeployResult>(text, ManifestJson.Options)
               ?? new ClusterDeployResult { Success = false, Error = "Empty master response." };
    }
}

