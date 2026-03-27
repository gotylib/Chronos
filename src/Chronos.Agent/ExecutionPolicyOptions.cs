namespace Chronos.Agent;

public sealed class ExecutionPolicyOptions
{
    public TimeSpan TestExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxParallelTestsPerProject { get; set; } = 5;
    public int MaxParallelTestsTotal { get; set; } = 20;
}

