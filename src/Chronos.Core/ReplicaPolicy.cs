using System.Text.Json.Serialization;

namespace Chronos.Core;

/// <summary>
/// Describes how Chronos Master should duplicate compose stacks across agents (serialized as compose root <c>x-chronos-replicas</c>).
/// </summary>
public sealed class ReplicaPolicy
{
    /// <summary>Desired stack instances (&gt;1 activates replica placement on cluster).</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    /// <summary>Optional host port increment per replica index.</summary>
    [JsonPropertyName("portOffsetPerReplica")]
    public int PortOffsetPerReplica { get; set; } = 100;

    /// <summary>Named volumes that must stay shared across replicas (e.g. Postgres data).</summary>
    [JsonPropertyName("sharedNamedVolumes")]
    public List<string> SharedNamedVolumes { get; } = new();
}
