using System.Reflection;
using System.Text.Json.Serialization;

namespace Chronos.Core;

[AttributeUsage(AttributeTargets.Method)]
public sealed class JobAttribute : Attribute
{
    public string? Id { get; set; }
    public TestCriticality Criticality { get; set; } = TestCriticality.Warning;
    public bool OnStartup { get; set; } = true;
    public int? IntervalMinutes { get; set; } = 60;
}

public sealed class ComposeJobContext
{
    public required string ProjectName { get; init; }
    public required string ServiceName { get; init; }
    public required string ComposeWorkingDirectory { get; init; }
    public required string ComposeFileName { get; init; }
    public required string DockerComposeExecutable { get; init; }
    public required HttpClient Http { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public Task<(int ExitCode, string Stdout, string Stderr)> ExecInServiceAsync(
        string shellCommand,
        CancellationToken cancellationToken = default)
        => ComposeExec.RunInServiceAsync(
            DockerComposeExecutable,
            ComposeWorkingDirectory,
            ComposeFileName,
            ProjectName,
            ServiceName,
            shellCommand,
            cancellationToken);
}

public sealed class CodeJobOutcome
{
    public bool Success { get; init; }
    public string? Message { get; init; }

    public static CodeJobOutcome Ok(string? message = null) => new() { Success = true, Message = message };
    public static CodeJobOutcome Fail(string message) => new() { Success = false, Message = message };
}

public sealed class CodeJobEntry
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("className")]
    public string ClassName { get; set; } = string.Empty;

    [JsonPropertyName("methodName")]
    public string MethodName { get; set; } = string.Empty;

    public string AssemblyRelativePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string? LocalAssemblyPath { get; set; }

    public bool OnStartup { get; set; } = true;
    public int? IntervalMinutes { get; set; } = 60;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestCriticality Criticality { get; set; } = TestCriticality.Warning;
}

public static class CodeJobRegistration
{
    internal static void AddJobsFromType(Service service, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsClass || type.IsAbstract)
            throw new ArgumentException("Укажите конкретный класс.", nameof(type));

        var asmPath = type.Assembly.Location;
        if (string.IsNullOrEmpty(asmPath))
            throw new InvalidOperationException(
                $"Тип {type.FullName} не загружен с диска (нужна собранная DLL, не dynamic).");

        var asmFile = Path.GetFileName(asmPath);
        var deployRel = $"tests/{asmFile}".Replace('\\', '/');

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var any = false;
        foreach (var method in type.GetMethods(flags))
        {
            var attr = method.GetCustomAttribute<JobAttribute>();
            if (attr == null)
                continue;

            ValidateSignature(method, type);
            any = true;
            var id = string.IsNullOrWhiteSpace(attr.Id) ? method.Name : attr.Id!;
            service.CodeJobs.Add(new CodeJobEntry
            {
                Id = id,
                ClassName = type.FullName ?? type.Name,
                MethodName = method.Name,
                AssemblyRelativePath = deployRel,
                LocalAssemblyPath = asmPath,
                OnStartup = attr.OnStartup,
                IntervalMinutes = attr.IntervalMinutes,
                Criticality = attr.Criticality
            });
        }

        if (!any)
            throw new InvalidOperationException($"Нет методов с [Job] на типе {type.FullName}.");
    }

    private static void ValidateSignature(MethodInfo method, Type declaringType)
    {
        var ret = method.ReturnType;
        var okReturn = ret == typeof(Task)
            || (ret.IsGenericType && ret.GetGenericTypeDefinition() == typeof(Task<>)
                && ret.GetGenericArguments()[0] == typeof(CodeJobOutcome));

        if (!okReturn)
        {
            throw new InvalidOperationException(
                $"{declaringType.Name}.{method.Name}: возвращайте Task или Task<CodeJobOutcome>.");
        }

        foreach (var p in method.GetParameters())
        {
            if (p.ParameterType != typeof(ComposeJobContext) && p.ParameterType != typeof(CancellationToken))
            {
                throw new InvalidOperationException(
                    $"{declaringType.Name}.{method.Name}: допустимы только параметры ComposeJobContext и CancellationToken.");
            }
        }

        if (!method.IsStatic && declaringType.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new InvalidOperationException(
                $"{declaringType.Name}: для нестатических [Job] нужен публичный конструктор без параметров.");
        }
    }
}
