using System.Text.RegularExpressions;

namespace Chronos.Core.Safety;

public static class LogRedactor
{
    // Very lightweight redaction for common secrets.
    private static readonly Regex[] Patterns =
    [
        new(@"(?i)(password|passwd|secret|api[_-]?key|token)\s*[:=]\s*([^\s""']+)", RegexOptions.Compiled),
        new(@"(?i)(bearer)\s+([^\s]+)", RegexOptions.Compiled),
        new(@"(?i)(x-api-key)\s*[:=]\s*([^\s""']+)", RegexOptions.Compiled)
    ];

    public static string RedactSecrets(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var s = input;
        foreach (var p in Patterns)
            s = p.Replace(s, m => $"{m.Groups[1].Value}=***REDACTED***");

        return s;
    }
}

