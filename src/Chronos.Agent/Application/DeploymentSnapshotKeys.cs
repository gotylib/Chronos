namespace Chronos.Agent.Application;

/// <summary>Ключ строки в таблице <c>services</c> для снимка compose (не путать с именем проекта Docker Compose <c>-p</c>).</summary>
public static class DeploymentSnapshotKeys
{
    public const string GlobalCompose = "__chronos_global_compose__";
}
