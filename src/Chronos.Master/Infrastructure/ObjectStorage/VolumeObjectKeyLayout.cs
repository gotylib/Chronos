using System.Text;

namespace Chronos.Master.Infrastructure.ObjectStorage;

internal static class VolumeObjectKeyLayout
{
    internal static string SanitizeSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "_";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                sb.Append(c);
            else
                sb.Append('_');
        }

        return sb.ToString();
    }

    internal static string CombinePrefix(string rootPrefix, string? extra)
    {
        rootPrefix = rootPrefix.Trim().Trim('/');
        extra = string.IsNullOrWhiteSpace(extra) ? null : extra.Trim().Trim('/');
        if (string.IsNullOrEmpty(rootPrefix))
            return extra ?? "";
        if (extra == null)
            return rootPrefix;
        return $"{rootPrefix}/{extra}";
    }

    internal static string ListingPrefix(string combinedRoot, string projectName, string volumeName)
    {
        var p = SanitizeSegment(projectName);
        var v = SanitizeSegment(volumeName);
        var root = string.IsNullOrEmpty(combinedRoot) ? "" : combinedRoot.TrimEnd('/') + "/";
        return $"{root}{p}/{v}/";
    }
}
