// DTO графа compose для UI: узлы (сервис / сеть / volume) и рёбра (depends_on, сеть, …).
namespace Chronos.Core;

/// <summary>Узлы и рёбра для визуализации compose (сервисы, сети, зависимости).</summary>
public sealed class ComposeGraphDto
{
    public List<ComposeGraphNode> Nodes { get; init; } = new();
    public List<ComposeGraphEdge> Edges { get; init; } = new();
}

public sealed class ComposeGraphNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    /// <summary><c>service</c>, <c>network</c>, <c>volume</c>.</summary>
    public required string Kind { get; init; }
    public string? Subtitle { get; init; }
}

public sealed class ComposeGraphEdge
{
    public required string From { get; init; }
    public required string To { get; init; }
    /// <summary><c>depends_on</c>, <c>network</c>, …</summary>
    public required string Kind { get; init; }
}
