using System.Text.Json;
using Chronos.Core.Compose.Implementation;

namespace Chronos.Core;

public sealed class ServerProjectSpec
{
    public string ProjectName { get; set; } = string.Empty;
    public string ComposeRelativePath { get; set; } = "docker-compose.yml";
    public string? ManifestRelativePath { get; set; }
    public string? PreferredLocation { get; set; }
}

public sealed class ServerBlueprintFile
{
    public List<ServerProjectSpec> Projects { get; set; } = [];
}

public sealed class ServerProjectDeploymentResult
{
    public string ProjectName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string AgentUrl { get; set; } = string.Empty;
}

public sealed class ServerProjectDefinition
{
    public required ComposeBuilder Compose { get; init; }
    public string? PreferredLocation { get; init; }
    public string? ManifestJsonOverride { get; init; }
}

/// <summary>
/// Server-level deployment blueprint that can include many compose projects.
/// </summary>
public sealed class ServerBlueprint
{
    private readonly Dictionary<string, ServerProjectDefinition> _projects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ServerProjectDefinition> Projects => _projects;

    public ServerBlueprint AddProject(
        string projectName,
        ComposeBuilder compose,
        string? preferredLocation = null,
        string? manifestJsonOverride = null)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("projectName is required.", nameof(projectName));
        ArgumentNullException.ThrowIfNull(compose);

        compose.WithProjectName(projectName);
        _projects[projectName] = new ServerProjectDefinition
        {
            Compose = compose,
            PreferredLocation = preferredLocation,
            ManifestJsonOverride = manifestJsonOverride
        };
        return this;
    }

    public async Task<IReadOnlyList<ServerProjectDeploymentResult>> PublishAllToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var client = new ClusterClient(masterUrl, apiKey);
        var results = new List<ServerProjectDeploymentResult>();

        foreach (var (projectName, def) in _projects)
        {
            try
            {
                var validation = await def.Compose.ValidateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    results.Add(new ServerProjectDeploymentResult
                    {
                        ProjectName = projectName,
                        Success = false,
                        Error = $"Validation failed: {validation.Errors.Count} error(s)."
                    });
                    continue;
                }

                var req = new ClusterDeployRequest
                {
                    ProjectName = projectName,
                    ComposeYaml = def.Compose.GenerateYaml(),
                    PreferredLocation = def.PreferredLocation,
                    ManifestJson = def.ManifestJsonOverride
                                   ?? (def.Compose.HasManifestPayload() ? def.Compose.SerializeManifestJson() : null)
                };

                var resp = await client.PublishAsync(req, cancellationToken).ConfigureAwait(false);
                results.Add(new ServerProjectDeploymentResult
                {
                    ProjectName = projectName,
                    Success = resp.Success,
                    Error = resp.Error,
                    AgentId = resp.AgentId,
                    AgentUrl = resp.AgentUrl
                });
            }
            catch (Exception ex)
            {
                results.Add(new ServerProjectDeploymentResult
                {
                    ProjectName = projectName,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    public static ServerBlueprint LoadFromRepositoryDirectory(string repoDir)
    {
        var manifestPath = Path.Combine(repoDir, "chronos.server.json");
        if (File.Exists(manifestPath))
            return LoadFromManifestFile(manifestPath);

        return DiscoverFromRepository(repoDir);
    }

    public static ServerBlueprint LoadFromManifestFile(string manifestFilePath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestFilePath)) ?? Directory.GetCurrentDirectory();
        var json = File.ReadAllText(manifestFilePath);
        var file = JsonSerializer.Deserialize<ServerBlueprintFile>(json, ManifestJson.Options)
                   ?? new ServerBlueprintFile();

        var blueprint = new ServerBlueprint();
        foreach (var p in file.Projects)
        {
            var composePath = Path.GetFullPath(Path.Combine(baseDir, p.ComposeRelativePath));
            if (!File.Exists(composePath))
                throw new FileNotFoundException($"Compose file not found: {composePath}");

            var yaml = File.ReadAllText(composePath);
            var compose = ComposeBuilder.FromYaml(yaml, projectName: p.ProjectName, composeFilePath: composePath);

            string? manifestJson = null;
            if (!string.IsNullOrWhiteSpace(p.ManifestRelativePath))
            {
                var manifestPath = Path.GetFullPath(Path.Combine(baseDir, p.ManifestRelativePath));
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException($"Manifest file not found: {manifestPath}");
                manifestJson = File.ReadAllText(manifestPath);
            }

            blueprint.AddProject(
                projectName: p.ProjectName,
                compose: compose,
                preferredLocation: p.PreferredLocation,
                manifestJsonOverride: manifestJson);
        }

        return blueprint;
    }

    public static ServerBlueprint DiscoverFromRepository(string repoDir)
    {
        var blueprint = new ServerBlueprint();
        var files = Directory.EnumerateFiles(repoDir, "*compose*.yml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repoDir, "*compose*.yaml", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path).ToLowerInvariant();
            if (fileName.Contains("override", StringComparison.OrdinalIgnoreCase))
                continue;

            var projectName = Path.GetFileName(Path.GetDirectoryName(path) ?? "project");
            var yaml = File.ReadAllText(path);
            var compose = ComposeBuilder.FromYaml(yaml, projectName: projectName, composeFilePath: path);
            blueprint.AddProject(projectName, compose);
        }

        return blueprint;
    }
}

