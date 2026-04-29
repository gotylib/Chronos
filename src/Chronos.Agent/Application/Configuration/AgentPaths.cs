namespace Chronos.Agent;

/// <summary>Корень данных агента, имя compose-файла, пути к docker/docker-compose и образ для архивов.</summary>
public sealed class AgentPaths
{
    public required string AppPath { get; init; }
    public required string ComposeFileName { get; init; }
    public required string DockerComposeExecutable { get; init; }
    public required string DockerExecutable { get; init; }
    public required string ArchiveImage { get; init; }
}
