namespace Chronos.Agent;

/// <summary>Свободное место на томе, где лежит <paramref name="path"/>.</summary>
internal static class HostDiskStats
{
    internal static bool TryGetDiskSpaceForPath(string path, out long freeBytes, out long totalBytes)
    {
        freeBytes = 0;
        totalBytes = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var drive = new DriveInfo(root);
            freeBytes = drive.AvailableFreeSpace;
            totalBytes = drive.TotalSize;
            return totalBytes > 0;
        }
        catch
        {
            return false;
        }
    }
}
