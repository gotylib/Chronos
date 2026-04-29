namespace Chronos.Agent;

/// <summary>Таймауты и лимиты параллелизма для тестов/jobs на агенте.</summary>
public sealed class ExecutionPolicyOptions
{
    public TimeSpan TestExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxParallelTestsPerProject { get; set; } = 5;
    public int MaxParallelTestsTotal { get; set; } = 20;
}

