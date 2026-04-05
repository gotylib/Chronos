using System.Diagnostics;
using System.Globalization;
using Chronos.Core;
using Chronos.Core.Compose.Implementation;
using SampleTests;
using Spectre.Console;

static string? GetArgValue(string[] argv, string name)
{
    for (var i = 0; i < argv.Length; i++)
    {
        if (string.Equals(argv[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
            return argv[i + 1];
    }

    return null;
}

static bool HasFlag(string[] argv, string name)
    => argv.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
var command = argv.FirstOrDefault()?.Trim().ToLowerInvariant();
var bootstrapMode = command == "bootstrap" || GetArgValue(argv, "--repo") != null;

if (argv.Length == 0 || HasFlag(argv, "--help"))
{
    AnsiConsole.WriteLine("[bold]Chronos.Cli[/] - prototype");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine("Usage:");
    AnsiConsole.WriteLine("  Chronos.Cli --sample nginx|pg-nginx --project-name <name> --compose-out <path> [--local-test]");
    AnsiConsole.WriteLine("  Optional: --startup-timeout-seconds <N> --remove-after-test");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine("Bootstrap:");
    AnsiConsole.WriteLine("  Chronos.Cli bootstrap --repo <git-url> --branch <name> --master-url <url> [--api-key <key>] [--keep-repo]");
    return;
}

if (bootstrapMode)
{
    await RunBootstrapAsync(argv);
    return;
}

var sample = GetArgValue(argv, "--sample") ?? "nginx";
var projectName = GetArgValue(argv, "--project-name") ?? "chronos";
var composeOut = GetArgValue(argv, "--compose-out") ?? "docker-compose.yml";

var startupTimeoutSecondsRaw = GetArgValue(argv, "--startup-timeout-seconds");
var startupTimeoutSeconds = 60;
if (!string.IsNullOrWhiteSpace(startupTimeoutSecondsRaw) &&
    int.TryParse(startupTimeoutSecondsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
{
    startupTimeoutSeconds = parsed;
}

var localTest = HasFlag(argv, "--local-test");
var removeAfterTest = !HasFlag(argv, "--no-remove-after-test");

var compose = CreateSample(sample)
    .WithProjectName(projectName)
    .WithComposeFilePath(composeOut);

var validation = await compose.ValidateAsync();
if (validation.Errors.Count > 0)
{
    AnsiConsole.WriteLine("[red]Validation failed[/]");
    foreach (var err in validation.Errors)
        AnsiConsole.WriteLine($"  ❌ {err.Service}: {err.Message}");
    Environment.ExitCode = 2;
    return;
}

if (validation.Warnings.Count > 0)
{
    AnsiConsole.WriteLine("[yellow]Validation warnings[/]");
    foreach (var warn in validation.Warnings)
        AnsiConsole.WriteLine($"  ⚠️ {warn.Service}: {warn.Message}");
}

AnsiConsole.WriteLine("[green]Validation OK[/]");

if (localTest)
{
    var test = await compose.TestAsync(new TestOptions
    {
        StartupTimeout = TimeSpan.FromSeconds(startupTimeoutSeconds),
        RemoveAfterTest = removeAfterTest,
        ShowLogsOnFailure = true,
        Verbose = true
    });

    if (!test.Success)
        Environment.ExitCode = 1;
}

static ComposeBuilder CreateSample(string sample)
{
    sample = sample.Trim().ToLowerInvariant();

    return sample switch
    {
        "nginx" => new ComposeBuilder()
            .WithVersion("3.8")
            .AddNetwork("web-net")
            .AddService(s => s
                .WithName("web")
                .UseImage("nginx:alpine")
                .AddPort(8080, 80)
                .ConnectToNetwork("web-net")
                .WithRestartPolicy("unless-stopped")
                .UseTests<NginxTest>()),

        "pg-nginx" => new ComposeBuilder()
            .WithVersion("3.8")
            .AddNetwork("app-net")
            .AddVolume("pgdata")
            .AddService(s => s
                .WithName("postgres")
                .UseImage("postgres:16-alpine")
                .WithRestartPolicy("unless-stopped")
                .AddEnvironment("POSTGRES_USER", "app")
                .AddEnvironment("POSTGRES_PASSWORD", "app_password")
                .AddEnvironment("POSTGRES_DB", "appdb")
                .AddPort(5432, 5432)
                .ConnectToNetwork("app-net")
                .AddVolume("pgdata", "/var/lib/postgresql/data")
                .WithHealthCheck(
                    testCommand: "pg_isready -U app -d appdb",
                    intervalSeconds: 5,
                    timeoutSeconds: 5,
                    retries: 20))
            .AddService(s => s
                .WithName("nginx")
                .UseImage("nginx:alpine")
                .WithRestartPolicy("unless-stopped")
                .AddPort(8080, 80)
                .ConnectToNetwork("app-net")
                .DependsOn("postgres")
                .UseTests<NginxTest>()),

        _ => throw new InvalidOperationException($"Unknown sample '{sample}'. Supported: nginx, pg-nginx")
    };
}

static async Task RunBootstrapAsync(string[] argv)
{
    var repo = GetArgValue(argv, "--repo");
    if (string.IsNullOrWhiteSpace(repo))
        throw new InvalidOperationException("Bootstrap requires --repo <git-url>.");

    var branch = GetArgValue(argv, "--branch") ?? "main";
    var masterUrl = GetArgValue(argv, "--master-url") ?? "http://localhost:5000";
    var apiKey = GetArgValue(argv, "--api-key");
    var keepRepo = HasFlag(argv, "--keep-repo");

    var tempDir = Path.Combine(Path.GetTempPath(), $"chronos-bootstrap-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    AnsiConsole.MarkupLine($"[blue]Bootstrap[/] Cloning [grey]{repo}[/] (branch: [grey]{branch}[/])...");
    var clone = await RunProcessCaptureAsync(
        fileName: "git",
        arguments: $"clone --depth 1 --branch \"{branch}\" \"{repo}\" \"{tempDir}\"");
    if (clone.ExitCode != 0)
        throw new InvalidOperationException($"git clone failed: {clone.Stderr}");

    var blueprint = ServerBlueprint.LoadFromRepositoryDirectory(tempDir);
    if (blueprint.Projects.Count == 0)
        throw new InvalidOperationException("No compose projects found (chronos.server.json missing and auto-discovery found nothing).");

    AnsiConsole.MarkupLine($"[blue]Bootstrap[/] Found projects: [green]{blueprint.Projects.Count}[/]");
    var results = await blueprint.PublishAllToClusterAsync(masterUrl, apiKey);

    var table = new Table().RoundedBorder();
    table.AddColumn("Project");
    table.AddColumn("Result");
    table.AddColumn("Agent");
    table.AddColumn("Error");
    foreach (var r in results)
    {
        table.AddRow(
            r.ProjectName,
            r.Success ? "[green]OK[/]" : "[red]FAIL[/]",
            string.IsNullOrWhiteSpace(r.AgentId) ? "-" : r.AgentId,
            r.Error ?? "-");
    }
    AnsiConsole.Write(table);

    if (!keepRepo)
    {
        try { Directory.Delete(tempDir, recursive: true); }
        catch { /* best-effort */ }
    }
    else
    {
        AnsiConsole.MarkupLine($"[yellow]Repo kept:[/] {tempDir}");
    }

    if (results.Any(r => !r.Success))
        Environment.ExitCode = 1;
}

static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessCaptureAsync(string fileName, string arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var process = new Process { StartInfo = psi };
    process.Start();

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    return (process.ExitCode, await stdoutTask, await stderrTask);
}
