using Chronos.Core.Compose.Interfaces;
using Chronos.Core.Compose.Implementation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Chronos.Master.Application;

/// <summary>Выполнение пользовательского C# с Fluent ComposeBuilder через Roslyn Scripting (песочница UI).</summary>
public static class FluentComposeScriptRunner
{
    private const int DefaultTimeoutSeconds = 45;

    public static async Task<(IBuiltCompose? Compose, string? Error)> BuildComposeAsync(
        string userBody,
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(environment, configuration))
            throw new InvalidOperationException(
                "Fluent sandbox is disabled. Set CHRONOS_MASTER_FLUENT_SANDBOX_ENABLED=1 or run in Development.");

        var coreAssembly = typeof(ComposeBuilder).Assembly;
        var coreDir = Path.GetDirectoryName(coreAssembly.Location);
        if (string.IsNullOrEmpty(coreDir))
            throw new InvalidOperationException("Cannot resolve Chronos.Core assembly directory for Roslyn references.");

        var references = new List<Microsoft.CodeAnalysis.MetadataReference>();
        foreach (var path in Directory.GetFiles(coreDir, "*.dll"))
            references.Add(MetadataReference.CreateFromFile(path));

        var options = ScriptOptions.Default
            .WithAllowUnsafe(false)
            .AddReferences(references)
            .AddImports(
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Threading",
                "System.Threading.Tasks",
                "Chronos.Core",
                "Chronos.Core.Compose.Interfaces",
                "Chronos.Core.Compose.Implementation");

        var scriptText = $$"""
IBuiltCompose __run()
{
{{userBody}}
}

return __run();
""";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(
            configuration.GetValue("CHRONOS_MASTER_FLUENT_SANDBOX_TIMEOUT_SECONDS", DefaultTimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var script = CSharpScript.Create<object>(scriptText, options);
            var state = await script.RunAsync(null, linked.Token).ConfigureAwait(false);
            if (state.ReturnValue is not IBuiltCompose compose)
                return (null, "Script did not return IBuiltCompose. End with `return ...Build();`.");

            return (compose, null);
        }
        catch (CompilationErrorException ex)
        {
            return (null, string.Join(Environment.NewLine, ex.Diagnostics.Select(d => d.ToString())));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return (null, "Fluent script timed out.");
        }
    }

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        configuration.GetValue("CHRONOS_MASTER_FLUENT_SANDBOX_ENABLED", environment.IsDevelopment());
}
