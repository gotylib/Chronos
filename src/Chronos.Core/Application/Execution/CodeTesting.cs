using System.Reflection;
using System.Text.Json.Serialization;

// Кодовые интеграционные тесты: [Test], ComposeTestContext, регистрация тестовых сборок в сервисе.
namespace Chronos.Core;

/// <summary>Помечает метод теста. Сигнатура: <c>Task</c> или <c>Task&lt;CodeTestOutcome&gt;</c>; параметры только <see cref="ComposeTestContext"/> и/или <see cref="CancellationToken"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
    public string? Id { get; set; }
    public TestCriticality Criticality { get; set; } = TestCriticality.Warning;
    public bool OnStartup { get; set; } = true;
    public int? IntervalMinutes { get; set; }

    /// <summary>Меньше — раньше (например 0 = до остальных проверок).</summary>
    public int Order { get; set; } = 100;
}

/// <summary>Контекст для методов с <see cref="TestAttribute"/>.</summary>
public sealed class ComposeTestContext
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

    /// <summary>
    /// Выполняет compose с хоста (как <c>docker compose -f … -p … &lt;args&gt;</c>): restart, pull, и т.д.
    /// Без <c>-it</c> — для сценариев вроде «docker compose restart» после <c>docker exec … update-permissions</c>.
    /// </summary>
    public Task<(int ExitCode, string Stdout, string Stderr)> RunComposeAsync(
        string composeArguments,
        CancellationToken cancellationToken = default)
        => ComposeHost.RunComposeAsync(
            DockerComposeExecutable,
            ComposeWorkingDirectory,
            ComposeFileName,
            ProjectName,
            composeArguments,
            cancellationToken);
}

public sealed class CodeTestOutcome
{
    public bool Success { get; init; }
    public string? Message { get; init; }

    public static CodeTestOutcome Ok(string? message = null) => new() { Success = true, Message = message };
    public static CodeTestOutcome Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>Один метод-тест в манифесте (агент / локальный прогон).</summary>
public sealed class CodeTestEntry
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
    public int? IntervalMinutes { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TestCriticality Criticality { get; set; } = TestCriticality.Warning;

    /// <summary>Порядок запуска (меньше — раньше). По умолчанию 100.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; } = 100;
}

/// <summary>Регистрация класса с методами <see cref="TestAttribute"/> на сервисе.</summary>
public static class CodeTestRegistration
{
    internal static void AddTestsFromType(Service service, Type type)
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
        var methods = new List<(MethodInfo Method, TestAttribute Attr)>();
        foreach (var method in type.GetMethods(flags))
        {
            var attr = method.GetCustomAttribute<TestAttribute>();
            if (attr == null)
                continue;

            ValidateSignature(method, type);
            methods.Add((method, attr));
        }

        methods.Sort((a, b) =>
        {
            var c = a.Attr.Order.CompareTo(b.Attr.Order);
            return c != 0 ? c : string.Compare(a.Method.Name, b.Method.Name, StringComparison.OrdinalIgnoreCase);
        });

        var any = false;
        foreach (var (method, attr) in methods)
        {
            any = true;
            var id = string.IsNullOrWhiteSpace(attr.Id) ? method.Name : attr.Id!;
            service.CodeTests.Add(new CodeTestEntry
            {
                Id = id,
                ClassName = type.FullName ?? type.Name,
                MethodName = method.Name,
                AssemblyRelativePath = deployRel,
                LocalAssemblyPath = asmPath,
                OnStartup = attr.OnStartup,
                IntervalMinutes = attr.IntervalMinutes,
                Criticality = attr.Criticality,
                Order = attr.Order
            });
        }

        if (!any)
            throw new InvalidOperationException($"Нет методов с [Test] на типе {type.FullName}.");
    }

    private static void ValidateSignature(MethodInfo method, Type declaringType)
    {
        var ret = method.ReturnType;
        var okReturn = ret == typeof(Task)
            || (ret.IsGenericType && ret.GetGenericTypeDefinition() == typeof(Task<>)
                && ret.GetGenericArguments()[0] == typeof(CodeTestOutcome));

        if (!okReturn)
        {
            throw new InvalidOperationException(
                $"{declaringType.Name}.{method.Name}: возвращайте Task или Task<CodeTestOutcome>.");
        }

        foreach (var p in method.GetParameters())
        {
            if (p.ParameterType != typeof(ComposeTestContext) && p.ParameterType != typeof(CancellationToken))
            {
                throw new InvalidOperationException(
                    $"{declaringType.Name}.{method.Name}: допустимы только параметры ComposeTestContext и CancellationToken.");
            }
        }

        if (!method.IsStatic && declaringType.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new InvalidOperationException(
                $"{declaringType.Name}: для нестатических [Test] нужен публичный конструктор без параметров.");
        }
    }
}
