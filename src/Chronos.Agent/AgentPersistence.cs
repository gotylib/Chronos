using System.Text.Json;
using Chronos.Core;

namespace Chronos.Agent;

internal static class AgentPersistence
{
    private const int MaxRecords = 100;

    public static async Task AppendTestAsync(string projectDir, CheckRunRecord record, CancellationToken ct)
    {
        var snap = await LoadAsync(projectDir, ct).ConfigureAwait(false);
        snap.RecentTests.Insert(0, record);
        if (snap.RecentTests.Count > MaxRecords)
            snap.RecentTests.RemoveRange(MaxRecords, snap.RecentTests.Count - MaxRecords);
        await SaveAsync(projectDir, snap, ct).ConfigureAwait(false);
    }

    public static async Task AppendJobAsync(string projectDir, JobRunRecord record, CancellationToken ct)
    {
        var snap = await LoadAsync(projectDir, ct).ConfigureAwait(false);
        snap.RecentJobs.Insert(0, record);
        if (snap.RecentJobs.Count > MaxRecords)
            snap.RecentJobs.RemoveRange(MaxRecords, snap.RecentJobs.Count - MaxRecords);
        await SaveAsync(projectDir, snap, ct).ConfigureAwait(false);
    }

    public static async Task<DiagnosticsSnapshot> LoadAsync(string projectDir, CancellationToken ct)
    {
        var path = DiagnosticsPath(projectDir);
        if (!File.Exists(path))
            return new DiagnosticsSnapshot();

        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DiagnosticsSnapshot>(fs, ManifestJson.Options, ct)
                   .ConfigureAwait(false) ?? new DiagnosticsSnapshot();
        }
        catch
        {
            return new DiagnosticsSnapshot();
        }
    }

    private static async Task SaveAsync(string projectDir, DiagnosticsSnapshot snap, CancellationToken ct)
    {
        var dir = Path.Combine(projectDir, ".chronos");
        Directory.CreateDirectory(dir);
        var path = DiagnosticsPath(projectDir);
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(fs, snap, ManifestJson.Options, ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    private static string DiagnosticsPath(string projectDir)
        => Path.Combine(projectDir, ".chronos", "diagnostics.json");
}
