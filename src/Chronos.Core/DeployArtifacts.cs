using System.Formats.Tar;

namespace Chronos.Core;

public enum ArtifactSourceKind
{
    File,
    Directory
}

public sealed class DeployArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public ArtifactSourceKind SourceKind { get; set; }
    public string SourcePathOnDisk { get; set; } = string.Empty;
    public int? UnixFileMode { get; set; }
}

public static class DeployArtifactTarWriter
{
    public static async Task WriteArtifactsAsync(
        IEnumerable<DeployArtifact> artifacts,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new TarWriter(destination, leaveOpen: true);

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(artifact.RelativePath))
                throw new InvalidOperationException("Artifact RelativePath is required.");

            var rel = artifact.RelativePath.Replace('\\', '/').TrimStart('/');
            if (rel.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid artifact path '{artifact.RelativePath}'.");

            if (artifact.SourceKind == ArtifactSourceKind.File)
            {
                if (!File.Exists(artifact.SourcePathOnDisk))
                    throw new FileNotFoundException("Artifact file not found.", artifact.SourcePathOnDisk);

                await WriteFileEntryAsync(writer, rel, artifact.SourcePathOnDisk, artifact.UnixFileMode, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                if (!Directory.Exists(artifact.SourcePathOnDisk))
                    throw new DirectoryNotFoundException($"Artifact directory not found: {artifact.SourcePathOnDisk}");

                foreach (var file in Directory.EnumerateFiles(artifact.SourcePathOnDisk, "*", SearchOption.AllDirectories))
                {
                    var sub = Path.GetRelativePath(artifact.SourcePathOnDisk, file).Replace('\\', '/');
                    var entryPath = string.IsNullOrEmpty(sub) ? rel : $"{rel}/{sub}";
                    await WriteFileEntryAsync(writer, entryPath, file, artifact.UnixFileMode, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task WriteFileEntryAsync(
        TarWriter writer,
        string entryName,
        string filePath,
        int? unixMode,
        CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 20,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var mode = unixMode ?? 420;
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = fs,
            Mode = UnixFileModeExtensions.ToUnixFileMode(mode)
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }
}

internal static class UnixFileModeExtensions
{
    public static UnixFileMode ToUnixFileMode(int octalMode)
    {
        var m = octalMode & 0x1FF;
        UnixFileMode u = 0;
        if ((m & 256) != 0) u |= UnixFileMode.UserRead;
        if ((m & 128) != 0) u |= UnixFileMode.UserWrite;
        if ((m & 64) != 0) u |= UnixFileMode.UserExecute;
        if ((m & 32) != 0) u |= UnixFileMode.GroupRead;
        if ((m & 16) != 0) u |= UnixFileMode.GroupWrite;
        if ((m & 8) != 0) u |= UnixFileMode.GroupExecute;
        if ((m & 4) != 0) u |= UnixFileMode.OtherRead;
        if ((m & 2) != 0) u |= UnixFileMode.OtherWrite;
        if ((m & 1) != 0) u |= UnixFileMode.OtherExecute;
        return u;
    }
}
