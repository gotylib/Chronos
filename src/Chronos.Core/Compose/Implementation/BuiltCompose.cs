using Chronos.Core;
using Chronos.Core.Compose.Interfaces;

// Обёртка над ComposeBuilder: неизменяемый снимок для валидации и запуска без дальнейшей правки Fluent-цепочки.
namespace Chronos.Core.Compose.Implementation;

/// <inheritdoc cref="IBuiltCompose"/>
public sealed class BuiltCompose : IBuiltCompose
{
    private readonly ComposeBuilder _inner;

    internal BuiltCompose(ComposeBuilder inner)
    {
        _inner = inner;
    }

    public string ComposeSpecificationVersion => _inner.ComposeSpecificationVersion;

    public string ComposeFileRelativePath => _inner.ComposeFileRelativePath;

    public string ProjectName => _inner.ProjectName;

    public string DockerComposeExecutableConfiguration => _inner.DockerComposeExecutableConfiguration;

    public IReadOnlyDictionary<string, Service> Services => _inner.Services;

    public IReadOnlyDictionary<string, Network> Networks => _inner.Networks;

    public IReadOnlyDictionary<string, Volume> Volumes => _inner.Volumes;

    public IReadOnlyDictionary<string, Secret> Secrets => _inner.Secrets;

    public IReadOnlyDictionary<string, Config> Configs => _inner.Configs;

    public IReadOnlyDictionary<string, object> ExtensionFields => _inner.ExtensionFields;

    public ReplicaPolicy? ReplicaPolicySnapshot => _inner.ReplicaPolicySnapshot;

    public Task<ValidationResult> ValidateAsync(ComposeValidatorOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.ValidateAsync(options, cancellationToken);

    public string GenerateYaml() => _inner.GenerateYaml();

    public Task SaveToFileAsync(string path, CancellationToken cancellationToken = default) =>
        _inner.SaveToFileAsync(path, cancellationToken);

    public Task<TestResult> StartAsync(
        string composeFilePath,
        string projectName,
        string dockerComposeExecutable,
        TestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.StartAsync(composeFilePath, projectName, dockerComposeExecutable, options, cancellationToken);

    public Task<TestResult> StartAsync(TestOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.StartAsync(options, cancellationToken);

    public Task StopAsync(
        string composeFilePath,
        string projectName,
        bool removeVolumes = false,
        string dockerComposeExecutable = "auto",
        CancellationToken cancellationToken = default) =>
        _inner.StopAsync(composeFilePath, projectName, removeVolumes, dockerComposeExecutable, cancellationToken);

    public Task StopAsync(bool removeVolumes, CancellationToken cancellationToken = default) =>
        _inner.StopAsync(removeVolumes, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _inner.StopAsync(cancellationToken);

    public Task<TestResult> TestAsync(
        string composeFilePath,
        string projectName,
        string dockerComposeExecutable,
        TestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _inner.TestAsync(composeFilePath, projectName, dockerComposeExecutable, options, cancellationToken);

    public Task<TestResult> TestAsync(TestOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.TestAsync(options, cancellationToken);

    public Task<DeployResult> PublishAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default) =>
        _inner.PublishAsync(agentUrl, apiKey, cancellationToken);

    public Task<ClusterDeployResult> DeployToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        string? preferredLocation = null,
        CancellationToken cancellationToken = default) =>
        _inner.DeployToClusterAsync(masterUrl, apiKey, preferredLocation, cancellationToken);

    public Task<ClusterDeployResult> PublishToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        string? preferredLocation = null,
        CancellationToken cancellationToken = default) =>
        _inner.PublishToClusterAsync(masterUrl, apiKey, preferredLocation, cancellationToken);

    public Task PushManifestAndArtifactsAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default) =>
        _inner.PushManifestAndArtifactsAsync(agentUrl, apiKey, cancellationToken);

    public Task SnapshotRemoteVolumeToFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string localFilePath,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default) =>
        _inner.SnapshotRemoteVolumeToFileAsync(agentUrl, dockerVolumeName, localFilePath, apiKey, compress, cancellationToken);

    public Task<string> SnapshotRemoteVolumeToFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default) =>
        _inner.SnapshotRemoteVolumeToFileAsync(agentUrl, dockerVolumeName, apiKey, compress, cancellationToken);

    public Task<string> SnapshotRemoteVolumeToDirectoryAsync(
        string agentUrl,
        string dockerVolumeName,
        string localDirectoryPath,
        string? apiKey = null,
        string compress = "gzip",
        string? filePrefix = null,
        CancellationToken cancellationToken = default) =>
        _inner.SnapshotRemoteVolumeToDirectoryAsync(
            agentUrl, dockerVolumeName, localDirectoryPath, apiKey, compress, filePrefix, cancellationToken);

    public Task<VolumeOperationResult> SnapshotRemoteVolumeUploadToUrlAsync(
        string agentUrl,
        string dockerVolumeName,
        VolumeSnapshotUploadRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default) =>
        _inner.SnapshotRemoteVolumeUploadToUrlAsync(agentUrl, dockerVolumeName, request, apiKey, cancellationToken);

    public Task<VolumeOperationResult> RestoreRemoteVolumeFromFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string localArchivePath,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default) =>
        _inner.RestoreRemoteVolumeFromFileAsync(agentUrl, dockerVolumeName, localArchivePath, apiKey, compress, cancellationToken);

    public Task<VolumeOperationResult> RestoreRemoteVolumeFromUrlAsync(
        string agentUrl,
        string dockerVolumeName,
        VolumeRestoreFromUrlRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default) =>
        _inner.RestoreRemoteVolumeFromUrlAsync(agentUrl, dockerVolumeName, request, apiKey, cancellationToken);

    public Task<DeployResult> StartRemoteAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default) =>
        _inner.StartRemoteAsync(agentUrl, apiKey, cancellationToken);

    public Task<DeployResult> RestartRemoteAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default) =>
        _inner.RestartRemoteAsync(agentUrl, apiKey, cancellationToken);

    public Task<DeployResult> StopRemoteAsync(string agentUrl, string? apiKey = null, bool removeVolumes = false, CancellationToken cancellationToken = default) =>
        _inner.StopRemoteAsync(agentUrl, apiKey, removeVolumes, cancellationToken);

    public string ToFluentApiCode(string variableName = "compose") => _inner.ToFluentApiCode(variableName);
}
