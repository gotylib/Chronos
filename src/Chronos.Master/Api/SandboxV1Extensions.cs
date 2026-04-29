using Chronos.Core;
using Chronos.Core.Compose.Implementation;

namespace Chronos.Master.Api;

/// <summary>Валидация compose YAML в <c>/api/v1/sandbox/validate-compose</c>; превью Fluent-кода см. UiV1 (<c>fluent-preview</c>).</summary>
public static class SandboxV1Extensions
{
    public static WebApplication MapChronosSandboxV1(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");

        v1.MapPost("/sandbox/validate-compose", async (
            ValidateComposeSandboxRequest body,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ComposeYaml))
                return Results.BadRequest("composeYaml is required.");

            try
            {
                var compose = ComposeYamlParser.Parse(body.ComposeYaml);
                var validation = await compose.Build().ValidateAsync(cancellationToken: ct).ConfigureAwait(false);
                return Results.Json(validation);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithTags("sandbox")
        .WithDescription("Dry-run validation of docker-compose YAML (best-effort parser).");

        return app;
    }
}

/// <summary>Тело POST validate-compose: сырое содержимое docker-compose.</summary>
public sealed class ValidateComposeSandboxRequest
{
    public string ComposeYaml { get; set; } = string.Empty;
}
