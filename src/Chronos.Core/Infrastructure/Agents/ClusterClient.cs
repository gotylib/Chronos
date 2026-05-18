using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;

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
        _http = httpClient ?? CreateDefaultClient();
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        _http.DefaultRequestHeaders.ExpectContinue = false;
    }

    public Task<ClusterDeployResult> DeployAsync(ClusterDeployRequest request, CancellationToken cancellationToken = default)
        => PostAsync("/cluster/deploy", request, cancellationToken);

    public Task<ClusterDeployResult> PublishAsync(ClusterDeployRequest request, CancellationToken cancellationToken = default)
        => PostAsync("/cluster/publish", request, cancellationToken);

    private async Task<ClusterDeployResult> PostAsync(string path, ClusterDeployRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, ManifestJson.Options);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_masterUrl}{path}", content, ct).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Master API failed ({(int)response.StatusCode}): {text}");

                return JsonSerializer.Deserialize<ClusterDeployResult>(text, ManifestJson.Options)
                       ?? new ClusterDeployResult { Success = false, Error = "Empty master response." };
            }
            catch (HttpRequestException) when (attempt == 1)
            {
                // Иногда первый коннект рвётся (RST) на локальном Windows-стеке/IDE runner — делаем короткий retry.
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
    }

    private static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }
}

