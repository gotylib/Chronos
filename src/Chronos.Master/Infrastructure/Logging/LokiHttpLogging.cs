using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chronos.Master.Infrastructure.Logging;

internal sealed class MasterLokiLogQueue
{
    internal ConcurrentQueue<MasterLokiLogEntry> Pending { get; } = new();
}

internal readonly record struct MasterLokiLogEntry(DateTimeOffset Ts, string Level, string Category, string Message);

internal static class MasterLokiSerialization
{
    internal static readonly JsonSerializerOptions PushJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

internal sealed class MasterLokiLoggerProvider(MasterLokiLogQueue queue) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new MasterLokiLogger(categoryName, queue);

    public void Dispose()
    {
    }
}

internal sealed class MasterLokiLogger(string category, MasterLokiLogQueue queue) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Information ||
        category.StartsWith("Chronos.", StringComparison.Ordinal) ||
        category.StartsWith("Microsoft.Hosting.Lifetime", StringComparison.Ordinal);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        if (logLevel < LogLevel.Warning &&
            !category.StartsWith("Chronos.", StringComparison.Ordinal) &&
            !category.StartsWith("Microsoft.Hosting.Lifetime", StringComparison.Ordinal))
            return;

        var msg = formatter(state, exception);
        if (exception != null)
            msg += Environment.NewLine + exception;

        queue.Pending.Enqueue(new MasterLokiLogEntry(DateTimeOffset.UtcNow, logLevel.ToString(), category, msg));
    }
}

internal sealed class MasterLokiPushHostedService(
    MasterLokiLogQueue queue,
    string pushUrl,
    Func<string> instanceLabel,
    ILogger<MasterLokiPushHostedService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

                var batch = new List<MasterLokiLogEntry>(128);
                while (batch.Count < 256 && queue.Pending.TryDequeue(out var e))
                    batch.Add(e);

                if (batch.Count == 0)
                    continue;

                var lines = batch.Select(e =>
                        new[]
                        {
                            (e.Ts.ToUnixTimeMilliseconds() * 1_000_000).ToString(),
                            $"{e.Level} {e.Category}: {e.Message}"
                        })
                    .ToArray();

                var payload = new
                {
                    streams = new[]
                    {
                        new
                        {
                            stream = new Dictionary<string, string>
                            {
                                ["job"] = "chronos-master",
                                ["instance"] = instanceLabel()
                            },
                            values = lines
                        }
                    }
                };

                using var resp = await http.PostAsJsonAsync(pushUrl.TrimEnd('/'), payload, MasterLokiSerialization.PushJson, stoppingToken)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var t = await resp.Content.ReadAsStringAsync(stoppingToken).ConfigureAwait(false);
                    log.LogWarning("Loki push failed {Status}: {Body}", (int)resp.StatusCode, t);
                    foreach (var e in batch)
                        queue.Pending.Enqueue(e);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Loki batch failed.");
            }
        }
    }
}

internal static class MasterLokiLoggingRegistration
{
    internal static void TryAddMasterLoki(WebApplicationBuilder builder)
    {
        var url = builder.Configuration["CHRONOS_MASTER_LOKI_PUSH_URL"]?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return;

        var queue = new MasterLokiLogQueue();
        builder.Services.AddSingleton(queue);

        string InstanceLabel()
        {
            var id = builder.Configuration["CHRONOS_MASTER_INSTANCE_ID"]?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
                return id;
            return Environment.MachineName;
        }

        builder.Services.AddHostedService(sp =>
            new MasterLokiPushHostedService(queue, url, InstanceLabel, sp.GetRequiredService<ILogger<MasterLokiPushHostedService>>()));

        builder.Logging.AddProvider(new MasterLokiLoggerProvider(queue));
        Console.WriteLine($"[master] Loki push enabled → {url}");
    }
}
