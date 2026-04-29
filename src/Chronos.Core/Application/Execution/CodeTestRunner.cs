using System.Reflection;
using System.Runtime.Loader;
using Chronos.Core.Safety;

// Загрузка пользовательской сборки и вызов метода с [Test] в изолированном контексте.
namespace Chronos.Core;

public static class CodeTestRunner
{
    public static async Task<CheckRunRecord> RunAsync(
        CodeTestEntry entry,
        string serviceName,
        string composeWorkingDirectory,
        string composeFileName,
        string projectName,
        string dockerComposeExecutable,
        HttpClient http,
        CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(entry.Id) ? "code" : entry.Id;
        var utc = DateTimeOffset.UtcNow;

        try
        {
            var asmFull = !string.IsNullOrWhiteSpace(entry.LocalAssemblyPath)
                ? Path.GetFullPath(entry.LocalAssemblyPath)
                : Path.GetFullPath(Path.Combine(
                    composeWorkingDirectory,
                    entry.AssemblyRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!File.Exists(asmFull))
            {
                return Fail(serviceName, id, entry.Criticality,
                    $"Assembly not found: {asmFull}", utc);
            }

            if (!AssemblySafety.ValidateAssemblyPath(asmFull, out var reason))
                return Fail(serviceName, id, entry.Criticality, reason ?? "Assembly rejected by safety policy.", utc);

            Assembly asm;
            try
            {
                asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(asmFull);
            }
            catch (Exception ex)
            {
                return Fail(serviceName, id, entry.Criticality, $"Load failed: {ex.Message}", utc);
            }

            var type = asm.GetType(entry.ClassName, throwOnError: false, ignoreCase: false);
            if (type == null)
                return Fail(serviceName, id, entry.Criticality, $"Type not found: {entry.ClassName}", utc);

            var method = type.GetMethod(entry.MethodName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (method == null)
                return Fail(serviceName, id, entry.Criticality, $"Method not found: {entry.MethodName}", utc);

            var ctx = new ComposeTestContext
            {
                ProjectName = projectName,
                ServiceName = serviceName,
                ComposeWorkingDirectory = composeWorkingDirectory,
                ComposeFileName = composeFileName,
                DockerComposeExecutable = dockerComposeExecutable,
                Http = http,
                CancellationToken = cancellationToken
            };

            object? target = null;
            if (!method.IsStatic)
            {
                target = Activator.CreateInstance(type);
                if (target == null)
                    return Fail(serviceName, id, entry.Criticality, $"Cannot create instance of {type.FullName}.", utc);
            }

            var args = BuildArguments(method, ctx, cancellationToken);
            var resultObj = method.Invoke(target, args);

            if (resultObj is Task task)
            {
                await task.ConfigureAwait(false);
                if (task.GetType().IsGenericType &&
                    task.GetType().GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var resultProp = task.GetType().GetProperty("Result");
                    var outcome = resultProp?.GetValue(task);
                    if (outcome is CodeTestOutcome o && !o.Success)
                        return Fail(serviceName, id, entry.Criticality, o.Message ?? "failed", utc);
                }

                return Ok(serviceName, id, entry.Criticality, utc);
            }

            return Fail(serviceName, id, entry.Criticality, "Test method must return Task.", utc);
        }
        catch (OperationCanceledException)
        {
            return Fail(serviceName, id, entry.Criticality, "Execution cancelled/timeout.", utc);
        }
        catch (Exception ex)
        {
            return Fail(serviceName, id, entry.Criticality, LogRedactor.RedactSecrets(ex.Message), utc);
        }
    }

    private static object?[] BuildArguments(MethodInfo method, ComposeTestContext ctx, CancellationToken ct)
    {
        var ps = method.GetParameters();
        var args = new object?[ps.Length];
        for (var i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType == typeof(ComposeTestContext))
                args[i] = ctx;
            else if (ps[i].ParameterType == typeof(CancellationToken))
                args[i] = ct;
        }

        return args;
    }

    private static CheckRunRecord Ok(string service, string testId, TestCriticality c, DateTimeOffset utc)
        => new()
        {
            Service = service,
            TestId = testId,
            Success = true,
            UtcTime = utc,
            Criticality = c
        };

    private static CheckRunRecord Fail(string service, string testId, TestCriticality c, string msg, DateTimeOffset utc)
        => new()
        {
            Service = service,
            TestId = testId,
            Success = false,
            Message = msg,
            UtcTime = utc,
            Criticality = c
        };
}
