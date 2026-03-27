namespace Chronos.Agent;

/// <summary>Resolved paths and executables for Chronos agent features.</summary>
public sealed class AgentPaths
{
    public required string AppPath { get; init; }
    public required string ComposeFileName { get; init; }
    public required string DockerComposeExecutable { get; init; }
    public required string DockerExecutable { get; init; }
    public required string ArchiveImage { get; init; }
}
