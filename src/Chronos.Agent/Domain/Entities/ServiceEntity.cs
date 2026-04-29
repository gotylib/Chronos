namespace Chronos.Agent.Domain.Entities;

public class ServiceEntity(
    string serviceName,
    string dockerComposeFile,
    string dockerComposeFilePath,
    List<string> imageNames,
    List<string> volumeNames)
{
    public long Id { get; set; }
    public string ServiceName { get; set; } = serviceName;
    public string DockerComposeFile { get; set; } = dockerComposeFile;
    public string DockerComposeFilePath { get; set; } = dockerComposeFilePath;
    public List<string> ImageNames { get; set; } = imageNames;
    public List<string> VolumeNames {get; set; } = volumeNames;
}