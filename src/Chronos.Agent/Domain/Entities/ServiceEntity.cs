namespace Chronos.Agent.Domain.Entities;

/// <summary>Снимок последнего успешно записанного compose для ключа деплоя (<see cref="ServiceName"/>).</summary>
public sealed class ServiceEntity
{
    public long Id { get; set; }

    /// <summary>Логический ключ: глобальный маркер Chronos или имя проекта на диске.</summary>
    public string ServiceName { get; set; } = "";

    public string DockerComposeFile { get; set; } = "";
    public string DockerComposeFilePath { get; set; } = "";
    public List<string> ImageNames { get; set; } = new();
    public List<string> VolumeNames { get; set; } = new();
}