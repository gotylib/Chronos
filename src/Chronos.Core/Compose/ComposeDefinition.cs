namespace Chronos.Core.Compose;

/// <summary>
/// Immutable snapshot of a compose document (services, networks, volumes, etc.).
/// </summary>
public sealed class ComposeDefinition
{
    public string Version { get; init; } = "3.8";
    public IReadOnlyDictionary<string, Service> Services { get; init; } = new Dictionary<string, Service>();
    public IReadOnlyDictionary<string, Network> Networks { get; init; } = new Dictionary<string, Network>();
    public IReadOnlyDictionary<string, Volume> Volumes { get; init; } = new Dictionary<string, Volume>();
    public IReadOnlyDictionary<string, Secret> Secrets { get; init; } = new Dictionary<string, Secret>();
    public IReadOnlyDictionary<string, Config> Configs { get; init; } = new Dictionary<string, Config>();
    public IReadOnlyDictionary<string, object> ExtensionFields { get; init; } = new Dictionary<string, object>();
}
