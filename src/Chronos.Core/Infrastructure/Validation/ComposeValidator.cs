using Chronos.Core.Compose;

// Статическая валидация ComposeDefinition: обязательные поля, образы (опционально через daemon).
namespace Chronos.Core;

public sealed class ComposeValidatorOptions
{
    // Image presence checks can be slow / require daemon access.
    // Keep off for default "offline" validation.
    public bool CheckImagesExist { get; set; } = false;
}

/// <summary>Внутренняя реализация валидации; потребитель — метод ValidateAsync у Fluent ComposeBuilder.</summary>
internal static class ComposeValidator
{
    internal static Task<ValidationResult> ValidateAsync(ComposeDefinition definition, ComposeValidatorOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ComposeValidatorOptions();

        var result = new ValidationResult { IsValid = true };

        if (definition.Services.Count == 0)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError { Service = "", Message = "Compose must contain at least one service." });
            return Task.FromResult(result);
        }

        // 1) Required fields (image or build)
        foreach (var svc in definition.Services)
        {
            if (string.IsNullOrWhiteSpace(svc.Value.Image) && string.IsNullOrWhiteSpace(svc.Value.BuildContext))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Service = svc.Key,
                    Message = $"Service '{svc.Key}' must have either 'image' or 'build' defined."
                });
            }
        }

        // 2) depends_on references
        foreach (var svc in definition.Services)
        {
            foreach (var dep in svc.Value.DependsOn)
            {
                if (!definition.Services.ContainsKey(dep))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Service = svc.Key,
                        Message = $"Service '{svc.Key}' depends on '{dep}', but it doesn't exist."
                    });
                }
            }
        }

        // 3) networks warnings
        foreach (var svc in definition.Services)
        {
            foreach (var networkName in svc.Value.Networks)
            {
                if (!definition.Networks.ContainsKey(networkName))
                {
                    result.Warnings.Add(new ValidationWarning
                    {
                        Service = svc.Key,
                        Message = $"Network '{networkName}' referenced by '{svc.Key}' is not defined. Compose will create it implicitly."
                    });
                }
            }
        }

        // 4) port conflicts (host port only)
        var hostPorts = new Dictionary<int, string>(capacity: 16);
        foreach (var svc in definition.Services)
        {
            foreach (var port in svc.Value.Ports)
            {
                var key = port.HostPort;
                if (hostPorts.TryGetValue(key, out var otherSvc))
                {
                    result.Warnings.Add(new ValidationWarning
                    {
                        Service = svc.Key,
                        Message = $"Host port {key} is also used by '{otherSvc}'. This may conflict on the host."
                    });
                }
                else
                {
                    hostPorts[key] = svc.Key;
                }
            }
        }

        // 5) healthcheck test existence
        foreach (var svc in definition.Services)
        {
            if (svc.Value.HealthCheck != null && string.IsNullOrWhiteSpace(svc.Value.HealthCheck.TestCommand))
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Service = svc.Key,
                    Message = $"Healthcheck is configured but test command is empty for '{svc.Key}'."
                });
            }
        }

        // 6) resources sanity
        foreach (var svc in definition.Services)
        {
            var memoryMb = svc.Value.Deploy?.Resources?.MemoryMb;
            if (memoryMb.HasValue && memoryMb.Value > 0 && memoryMb.Value < 256)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Service = svc.Key,
                    Message = $"Memory limit {memoryMb.Value}MB for '{svc.Key}' looks very low."
                });
            }
        }

        // 7) optional image checks (not implemented yet)
        _ = options.CheckImagesExist;

        return Task.FromResult(result);
    }
}

