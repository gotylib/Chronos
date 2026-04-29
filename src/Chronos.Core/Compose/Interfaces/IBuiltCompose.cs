using Chronos.Core;

namespace Chronos.Core.Compose.Interfaces;

/// <summary>
/// Неизменяемый снимок после <see cref="IComposeBuilder.Build"/> — валидация, локальный запуск и деплой.
/// </summary>
public interface IBuiltCompose
{
    /// <inheritdoc cref="IComposeBuilder.ComposeSpecificationVersion"/>
    string ComposeSpecificationVersion { get; }

    /// <inheritdoc cref="IComposeBuilder.ComposeFileRelativePath"/>
    string ComposeFileRelativePath { get; }

    /// <inheritdoc cref="IComposeBuilder.ProjectName"/>
    string ProjectName { get; }

    /// <inheritdoc cref="IComposeBuilder.DockerComposeExecutableConfiguration"/>
    string DockerComposeExecutableConfiguration { get; }

    /// <inheritdoc cref="IComposeBuilder.Services"/>
    IReadOnlyDictionary<string, Service> Services { get; }

    /// <inheritdoc cref="IComposeBuilder.Networks"/>
    IReadOnlyDictionary<string, Network> Networks { get; }

    /// <inheritdoc cref="IComposeBuilder.Volumes"/>
    IReadOnlyDictionary<string, Volume> Volumes { get; }

    /// <inheritdoc cref="IComposeBuilder.Secrets"/>
    IReadOnlyDictionary<string, Secret> Secrets { get; }

    /// <inheritdoc cref="IComposeBuilder.Configs"/>
    IReadOnlyDictionary<string, Config> Configs { get; }

    /// <inheritdoc cref="IComposeBuilder.ExtensionFields"/>
    IReadOnlyDictionary<string, object> ExtensionFields { get; }

    /// <inheritdoc cref="IComposeBuilder.ReplicaPolicySnapshot"/>
    ReplicaPolicy? ReplicaPolicySnapshot { get; }

    Task<ValidationResult> ValidateAsync(ComposeValidatorOptions? options = null, CancellationToken cancellationToken = default);

    string GenerateYaml();
    Task SaveToFileAsync(string path, CancellationToken cancellationToken = default);

    Task<TestResult> StartAsync(
        string composeFilePath,
        string projectName,
        string dockerComposeExecutable,
        TestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<TestResult> StartAsync(TestOptions? options = null, CancellationToken cancellationToken = default);

    Task StopAsync(
        string composeFilePath,
        string projectName,
        bool removeVolumes = false,
        string dockerComposeExecutable = "auto",
        CancellationToken cancellationToken = default);

    Task StopAsync(bool removeVolumes, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task<TestResult> TestAsync(
        string composeFilePath,
        string projectName,
        string dockerComposeExecutable,
        TestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<TestResult> TestAsync(TestOptions? options = null, CancellationToken cancellationToken = default);

    Task<DeployResult> PublishAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default);

    Task<ClusterDeployResult> DeployToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        string? preferredLocation = null,
        CancellationToken cancellationToken = default);

    Task<ClusterDeployResult> PublishToClusterAsync(
        string masterUrl,
        string? apiKey = null,
        string? preferredLocation = null,
        CancellationToken cancellationToken = default);

    Task PushManifestAndArtifactsAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default);

    Task SnapshotRemoteVolumeToFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string localFilePath,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default);

    Task<string> SnapshotRemoteVolumeToFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default);

    Task<string> SnapshotRemoteVolumeToDirectoryAsync(
        string agentUrl,
        string dockerVolumeName,
        string localDirectoryPath,
        string? apiKey = null,
        string compress = "gzip",
        string? filePrefix = null,
        CancellationToken cancellationToken = default);

    Task<VolumeOperationResult> SnapshotRemoteVolumeUploadToUrlAsync(
        string agentUrl,
        string dockerVolumeName,
        VolumeSnapshotUploadRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    Task<VolumeOperationResult> RestoreRemoteVolumeFromFileAsync(
        string agentUrl,
        string dockerVolumeName,
        string localArchivePath,
        string? apiKey = null,
        string compress = "gzip",
        CancellationToken cancellationToken = default);

    Task<VolumeOperationResult> RestoreRemoteVolumeFromUrlAsync(
        string agentUrl,
        string dockerVolumeName,
        VolumeRestoreFromUrlRequest request,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    Task<DeployResult> StartRemoteAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default);
    Task<DeployResult> RestartRemoteAsync(string agentUrl, string? apiKey = null, CancellationToken cancellationToken = default);
    Task<DeployResult> StopRemoteAsync(string agentUrl, string? apiKey = null, bool removeVolumes = false, CancellationToken cancellationToken = default);

    string ToFluentApiCode(string variableName = "compose");
}
