using System.Text;

namespace Chronos.Core.Safety;

public static class AssemblySafety
{
    // Crude byte-string scanning as a lightweight MVP.
    // Real implementation should use signature verification + IL analysis.
    private static readonly string[] ForbiddenAsciiStrings =
    [
        "System.Diagnostics.Process",
        "ProcessStartInfo",
        "System.IO.File",
        "System.IO.Directory",
        "System.Net.Sockets",
        "System.Runtime.InteropServices",
        "DllImport",
    ];

    public static bool ValidateAssemblyPath(string assemblyPath, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            reason = "Assembly path is required.";
            return false;
        }

        if (!File.Exists(assemblyPath))
        {
            reason = $"Assembly not found: {assemblyPath}";
            return false;
        }

        // Read full file bytes (assemblies are typically a few MB).
        var bytes = File.ReadAllBytes(assemblyPath);
        var ascii = Encoding.ASCII.GetString(bytes);
        var lower = ascii.ToLowerInvariant();

        foreach (var forbidden in ForbiddenAsciiStrings)
        {
            if (lower.Contains(forbidden.ToLowerInvariant()))
            {
                reason = $"Assembly contains forbidden reference/pattern '{forbidden}'.";
                return false;
            }
        }

        return true;
    }
}

