using System.Collections.Concurrent;

namespace Chronos.Agent;

/// <summary>Ограничение параллельных тестов: глобально и на один projectName.</summary>
public sealed class ExecutionThrottler : IDisposable
{
    private readonly SemaphoreSlim _global;
    private readonly int _perProjectLimit;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perProject = new(StringComparer.OrdinalIgnoreCase);

    public ExecutionThrottler(int maxParallelTotal, int maxParallelPerProject)
    {
        maxParallelTotal = Math.Max(1, maxParallelTotal);
        maxParallelPerProject = Math.Max(1, maxParallelPerProject);

        _global = new SemaphoreSlim(maxParallelTotal, maxParallelTotal);
        _perProjectLimit = maxParallelPerProject;
    }

    private SemaphoreSlim GetProjectSemaphore(string projectName)
        => _perProject.GetOrAdd(projectName, _ => new SemaphoreSlim(_perProjectLimit, _perProjectLimit));

    public async Task RunThrottledAsync(string projectName, CancellationToken ct, Func<CancellationToken, Task> action)
    {
        await _global.WaitAsync(ct).ConfigureAwait(false);
        var sem = GetProjectSemaphore(projectName);
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(ct).ConfigureAwait(false);
        }
        finally
        {
            sem.Release();
            _global.Release();
        }
    }

    public async Task<T> RunThrottledAsync<T>(
        string projectName,
        CancellationToken ct,
        Func<CancellationToken, Task<T>> action)
    {
        await _global.WaitAsync(ct).ConfigureAwait(false);
        var sem = GetProjectSemaphore(projectName);
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(ct).ConfigureAwait(false);
        }
        finally
        {
            sem.Release();
            _global.Release();
        }
    }

    public void Dispose()
    {
        _global.Dispose();
        foreach (var kv in _perProject)
            kv.Value.Dispose();
    }
}

