using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Chronos.Agent;

internal static class HostMetrics
{
    public static async Task<double> GetCpuPercentAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux())
        {
            var (idle1, total1) = await ReadLinuxCpuAsync(ct).ConfigureAwait(false);
            await Task.Delay(350, ct).ConfigureAwait(false);
            var (idle2, total2) = await ReadLinuxCpuAsync(ct).ConfigureAwait(false);
            var total = Math.Max(1, total2 - total1);
            var idle = Math.Max(0, idle2 - idle1);
            var usage = 100.0 * (1.0 - (double)idle / total);
            return Clamp(usage);
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c wmic cpu get loadpercentage /value",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = new Process { StartInfo = psi };
                p.Start();
                var output = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
                var match = Regex.Match(output, @"LoadPercentage=(\d+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, out var cpu))
                    return Clamp(cpu);
            }
            catch
            {
                // fallback below
            }
        }

        return 0;
    }

    public static double GetMemoryPercent()
    {
        try
        {
            if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                long totalKb = 0;
                long availKb = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        totalKb = ParseKb(line);
                    else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                        availKb = ParseKb(line);
                }

                if (totalKb > 0)
                    return Clamp(((double)(totalKb - availKb) / totalKb) * 100.0);
            }

            if (OperatingSystem.IsWindows())
            {
                if (TryGetMemoryStatus(out var status))
                    return Clamp(status.dwMemoryLoad);
            }
        }
        catch
        {
            // ignored
        }

        return 0;
    }

    public static double GetDiskPercent(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
                return 0;

            var drive = new DriveInfo(root);
            if (drive.TotalSize <= 0)
                return 0;

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return Clamp(100.0 * used / drive.TotalSize);
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<(long Idle, long Total)> ReadLinuxCpuAsync(CancellationToken ct)
    {
        var line = (await File.ReadAllLinesAsync("/proc/stat", ct).ConfigureAwait(false)).FirstOrDefault();
        if (line == null || !line.StartsWith("cpu "))
            return (0, 1);

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        var values = parts.Select(p => long.TryParse(p, out var n) ? n : 0).ToArray();
        long idle = values.Length > 3 ? values[3] : 0;
        long total = values.Sum();
        return (idle, total);
    }

    private static long ParseKb(string s)
    {
        var m = Regex.Match(s, @"(\d+)");
        return m.Success && long.TryParse(m.Groups[1].Value, out var value) ? value : 0;
    }

    private static double Clamp(double v) => Math.Max(0, Math.Min(100, v));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static bool TryGetMemoryStatus(out MEMORYSTATUSEX status)
    {
        status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status);
    }
}

