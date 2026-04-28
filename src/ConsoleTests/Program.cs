using Chronos.Core;
using Chronos.Core.Compose.Implementation;
using SampleTests;

// Именованные volumes; при chgrp/ulimit на Podman нужен privileged (ниже по умолчанию включён, отключить: GITLAB_USE_PRIVILEGED=0).
// Старые тома: compose down -v или podman volume rm …
var useBindMounts = string.Equals(Environment.GetEnvironmentVariable("GITLAB_USE_BIND_MOUNTS"), "1", StringComparison.OrdinalIgnoreCase);
var dataRoot = Environment.GetEnvironmentVariable("GITLAB_DATA_ROOT");
var usePrivileged = !string.Equals(Environment.GetEnvironmentVariable("GITLAB_USE_PRIVILEGED"), "0", StringComparison.OrdinalIgnoreCase);

// Тестовые учётные данные; при необходимости переопредели GITLAB_ROOT_EMAIL / GITLAB_ROOT_PASSWORD.
var rootEmail = Environment.GetEnvironmentVariable("GITLAB_ROOT_EMAIL") ?? "mixalev702@mail.ru";
var rootPassword = Environment.GetEnvironmentVariable("GITLAB_ROOT_PASSWORD") ?? "G@tlib100404.";

var omnibus = """
              external_url 'https://gitlab.nvpn12345.ru'
              nginx['enable'] = false
              puma['enable'] = true
              puma['listen'] = '0.0.0.0'
              puma['port'] = 8080
              gitlab_workhorse['enable'] = true
              gitlab_workhorse['listen_network'] = "tcp"
              gitlab_workhorse['listen_addr'] = "0.0.0.0:8181"
              gitlab_workhorse['auth_backend'] = "http://localhost:8080"
              gitlab_rails['trusted_proxies'] = [ '0.0.0.0/0' ]
              """;

var cb = new ComposeBuilder()
    .WithProjectName("gitlab")
    .AddNetwork("gitlab_net");

if (!useBindMounts)
{
    // Отдельный том под git-data: поверх gitlab_data маскирует только эту ветку (чистые права для chgrp).
    cb = cb
        .AddVolume("gitlab_etc")
        .AddVolume("gitlab_logs")
        .AddVolume("gitlab_data")
        .AddVolume("gitlab_git_data");
}

var compose = cb.AddService(s =>
{
    var sb = s
        .WithName("gitlab")
        .UseImage("gitlab/gitlab-ce:18.5.0-ce.0")
        .WithRestartPolicy("always")
        .WithContainerName("gitlab-server")
        .AddEnvironment("GITLAB_ROOT_EMAIL", rootEmail)
        .AddEnvironment("GITLAB_ROOT_PASSWORD", rootPassword)
        .AddEnvironment("GITLAB_OMNIBUS_CONFIG", omnibus);

    if (useBindMounts)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new InvalidOperationException("GITLAB_USE_BIND_MOUNTS=1 requires GITLAB_DATA_ROOT (e.g. /opt/gitlab or D:\\gitlab-data).");

        sb = sb
            .AddVolume(Path.Combine(dataRoot, "config"), "/etc/gitlab")
            .AddVolume(Path.Combine(dataRoot, "logs"), "/var/log/gitlab")
            .AddVolume(Path.Combine(dataRoot, "data"), "/var/opt/gitlab");
    }
    else
    {
        sb = sb
            .AddVolume("gitlab_etc", "/etc/gitlab")
            .AddVolume("gitlab_logs", "/var/log/gitlab")
            .AddVolume("gitlab_data", "/var/opt/gitlab")
            .AddVolume("gitlab_git_data", "/var/opt/gitlab/git-data");
    }

    if (usePrivileged)
    {
        sb = sb
            .AsPrivileged(true)
            .WithUser("0:0")
            .AddCapability("CHOWN", "DAC_OVERRIDE", "FOWNER")
            .AddSecurityOption("label=disable")
            .AddSecurityOption("seccomp=unconfined");
    }

    sb.AddPort(8282, 8181)
        .AddPort(9393, 8080)
        .AddPort(8822, 22)
        .ConnectToNetwork("gitlab_net")
        .UseTests<GitLabServiceTests>();
});

// Локально: TestAsync + кодовые тесты. Удалённо — PublishAsync с манифестом.
// Первый pull образа GitLab + старт Omnibus занимают заметно больше минуты — StartupTimeout не оставляй дефолтным.
// await compose.StartAsync(new TestOptions
// {
//     StartupTimeout = TimeSpan.FromMinutes(45),
//     TestExecutionTimeout = TimeSpan.FromMinutes(25),
//     RequireHealthChecksIfDefined = false
// });

// await compose.StartRemoteAsync("http://localhost:5008");
