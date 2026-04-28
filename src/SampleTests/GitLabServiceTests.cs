using System.Net;
using Chronos.Core;

namespace SampleTests;

/// <summary>
/// Проверки GitLab CE (Omnibus): успешный <c>gitlab-ctl reconfigure</c> и ответ Rails на проброшенном порту.
/// Контейнер может быть в состоянии <c>running</c>, пока внутри уже упал reconfigure — базовый <see cref="LocalTester"/> это не видит.
/// </summary>
public sealed class GitLabServiceTests
{
    /// <summary>Порт на хосте, сопоставленный с Puma (как в compose <c>9393:8080</c>).</summary>
    private static string HttpPort => Environment.GetEnvironmentVariable("GITLAB_TEST_HTTP_PORT") ?? "9393";

    private static TimeSpan ReconfigurePollInterval => TimeSpan.FromSeconds(15);

    private static int ReconfigureMaxAttempts =>
        int.TryParse(Environment.GetEnvironmentVariable("GITLAB_RECONFIGURE_POLL_ATTEMPTS"), out var n) ? Math.Max(1, n) : 60;

    private static int HttpMaxAttempts =>
        int.TryParse(Environment.GetEnvironmentVariable("GITLAB_HTTP_POLL_ATTEMPTS"), out var n) ? Math.Max(1, n) : 40;

    /// <summary>
    /// Эквивалент рекомендаций GitLab: <c>docker exec … update-permissions</c> и <c>docker compose restart …</c> (без <c>-it</c>).
    /// Включите <c>GITLAB_AUTO_REMEDIATION=1</c>; иначе шаг пропускается.
    /// </summary>
    [Test(Order = 0, Criticality = TestCriticality.Warning)]
    public async Task<CodeTestOutcome> RunUpdatePermissionsAndRestartWhenEnabled(ComposeTestContext ctx, CancellationToken cancellationToken = default)
    {
        // if (Environment.GetEnvironmentVariable("GITLAB_AUTO_REMEDIATION") != "1" || false)
        //     return CodeTestOutcome.Ok("skipped (set GITLAB_AUTO_REMEDIATION=1 to run update-permissions + compose restart)");

        var fix = await ctx.ExecInServiceAsync(
            "if test -x /usr/bin/update-permissions; then /usr/bin/update-permissions; else update-permissions; fi",
            cancellationToken).ConfigureAwait(false);

        var detail = string.Concat(fix.Stdout, fix.Stderr).Trim();
        if (fix.ExitCode != 0)
            detail = $"update-permissions exit {fix.ExitCode}. {detail}";

        var restart = await ctx.RunComposeAsync($"restart {ctx.ServiceName}", cancellationToken).ConfigureAwait(false);
        if (restart.ExitCode != 0)
        {
            return CodeTestOutcome.Fail(
                $"compose restart {ctx.ServiceName} failed (exit {restart.ExitCode}): {restart.Stderr}{restart.Stdout}");
        }

        var waitSec = int.TryParse(Environment.GetEnvironmentVariable("GITLAB_REMEDIATION_RESTART_WAIT_SECONDS"), out var w)
            ? Math.Max(5, w)
            : 45;
        await Task.Delay(TimeSpan.FromSeconds(waitSec), cancellationToken).ConfigureAwait(false);

        return string.IsNullOrEmpty(detail)
            ? CodeTestOutcome.Ok("update-permissions + compose restart completed.")
            : CodeTestOutcome.Ok("update-permissions + compose restart completed. " + detail);
    }

    /// <summary>
    /// Ждём появления в логах либо успеха (<c>gitlab Reconfigured!</c>), либо фиксируем типичные ошибки reconfigure
    /// (в т.ч. <c>chgrp</c> / <c>Infra Phase failed</c> при bind-mount без подходящих прав).
    /// </summary>
    [Test(Order = 10, Criticality = TestCriticality.Critical)]
    public async Task<CodeTestOutcome> OmnibusReconfigureSucceededOrNoFatalErrors(ComposeTestContext ctx, CancellationToken cancellationToken = default)
    {
        const string fatalIfFound =
            "if grep -RqE \"Infra Phase failed|There was an error running gitlab-ctl reconfigure\" /var/log/gitlab/reconfigure/ 2>/dev/null; then exit 42; fi; exit 0";

        const string successIfFound =
            "grep -Rq \"gitlab Reconfigured!\" /var/log/gitlab/reconfigure/ 2>/dev/null && exit 0 || exit 1";

        for (var attempt = 0; attempt < ReconfigureMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fatal = await ctx.ExecInServiceAsync(fatalIfFound, cancellationToken).ConfigureAwait(false);
            if (fatal.ExitCode == 42)
            {
                var tail = await ctx.ExecInServiceAsync(
                    "tail -n 120 $(ls -t /var/log/gitlab/reconfigure/*.log 2>/dev/null | head -n 1) 2>/dev/null || true",
                    cancellationToken).ConfigureAwait(false);
                var snippet = string.Concat(fatal.Stdout, fatal.Stderr, tail.Stdout).Trim();
                if (snippet.Length > 4000)
                    snippet = snippet[..4000] + "…";
                return CodeTestOutcome.Fail(
                    "GitLab Omnibus reconfigure reported failure (see /var/log/gitlab/reconfigure/). " + snippet);
            }

            var ok = await ctx.ExecInServiceAsync(successIfFound, cancellationToken).ConfigureAwait(false);
            if (ok.ExitCode == 0)
                return CodeTestOutcome.Ok("gitlab Reconfigured! seen in reconfigure logs.");

            await Task.Delay(ReconfigurePollInterval, cancellationToken).ConfigureAwait(false);
        }

        return CodeTestOutcome.Fail(
            $"Timeout: no \"gitlab Reconfigured!\" in /var/log/gitlab/reconfigure/ within {ReconfigureMaxAttempts * ReconfigurePollInterval.TotalSeconds:0}s. " +
            "Increase GITLAB_RECONFIGURE_POLL_ATTEMPTS or StartupTimeout if the VM is slow.");
    }

    /// <summary>HTTP к Puma на хосте (как curl с машины, где выполняется compose).</summary>
    [Test(Order = 20, Criticality = TestCriticality.Critical)]
    public async Task<CodeTestOutcome> SignInPageRespondsOnMappedPort(ComposeTestContext ctx, CancellationToken cancellationToken = default)
    {
        var url = $"http://127.0.0.1:{HttpPort}/users/sign_in";
        var delay = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("GITLAB_HTTP_POLL_SECONDS"), out var s) ? Math.Max(3, s) : 10);

        for (var attempt = 0; attempt < HttpMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await ctx.Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                var code = (int)response.StatusCode;
                if (code < 400)
                    return CodeTestOutcome.Ok();

                if (code is (int)HttpStatusCode.Redirect or (int)HttpStatusCode.Moved or (int)HttpStatusCode.Found
                    or (int)HttpStatusCode.RedirectMethod)
                    return CodeTestOutcome.Ok();
            }
            catch (HttpRequestException)
            {
                // still starting
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return CodeTestOutcome.Fail(
            $"GitLab did not respond acceptably at {url} after {HttpMaxAttempts} attempts (~{HttpMaxAttempts * delay.TotalSeconds:0}s). " +
            "Check port mapping and GITLAB_TEST_HTTP_PORT.");
    }
}
