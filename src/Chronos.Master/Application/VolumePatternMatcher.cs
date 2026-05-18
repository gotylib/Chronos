using System.Text.RegularExpressions;

namespace Chronos.Master.Application;

internal static class VolumePatternMatcher
{
    internal static bool Matches(string volumeName, string pattern)
    {
        pattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim();
        if (pattern == "*")
            return true;

        if (!pattern.Contains('*'))
            return string.Equals(volumeName, pattern, StringComparison.OrdinalIgnoreCase);

        var escaped = Regex.Escape(pattern);
        var regexBody = escaped.Replace("\\*", ".*", StringComparison.Ordinal);
        var regex = "^" + regexBody + "$";
        return Regex.IsMatch(volumeName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
