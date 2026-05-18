using Chronos.Core.Compose.Implementation;

namespace Chronos.Core.Application.Compose;

/// <summary>Снимок отличий между предыдущим и новым compose для предупреждения при передеплое.</summary>
public sealed record ComposeRedeployRiskAssessment(
    IReadOnlyList<string> RemovedComposeServices,
    IReadOnlyList<string> RemovedNamedVolumes,
    IReadOnlyList<ComposeServiceImageChange> ImageChanges)
{
    public bool RequiresConfirmation =>
        RemovedComposeServices.Count > 0
        || RemovedNamedVolumes.Count > 0
        || ImageChanges.Count > 0;
}

public sealed record ComposeServiceImageChange(string ServiceName, string? PreviousImage, string? NewImage);

/// <summary>Сравнивает YAML на диске (если был) с уже распарсенным новым compose.</summary>
public static class ComposeRedeployRiskEvaluator
{
    public static ComposeRedeployRiskAssessment Assess(string? previousComposeYaml, ComposeBuilder newCompose)
    {
        ArgumentNullException.ThrowIfNull(newCompose);

        var oldBuilder = TryParsePrevious(previousComposeYaml);

        var removedServices = oldBuilder.Services.Keys
            .Where(k => !newCompose.Services.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var removedVolumes = oldBuilder.Volumes.Keys
            .Where(k => !newCompose.Volumes.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var imageChanges = new List<ComposeServiceImageChange>();
        foreach (var kv in newCompose.Services)
        {
            if (!oldBuilder.Services.TryGetValue(kv.Key, out var oldSvc))
                continue;

            var oldN = NormalizeImage(oldSvc.Image);
            var newN = NormalizeImage(kv.Value.Image);
            if (!string.Equals(oldN, newN, StringComparison.Ordinal))
                imageChanges.Add(new ComposeServiceImageChange(kv.Key, oldSvc.Image, kv.Value.Image));
        }

        imageChanges.Sort((a, b) => string.CompareOrdinal(a.ServiceName, b.ServiceName));

        return new ComposeRedeployRiskAssessment(removedServices, removedVolumes, imageChanges);
    }

    private static ComposeBuilder TryParsePrevious(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return new ComposeBuilder();

        try
        {
            return ComposeYamlParser.Parse(yaml);
        }
        catch
        {
            return new ComposeBuilder();
        }
    }

    private static string? NormalizeImage(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;
        return image.Trim();
    }
}
