namespace Chronos.Agent.Api;

public sealed class VolumeArchiveRegisterDto
{
    public string VolumeName { get; set; } = string.Empty;
    public string StoredRelativePath { get; set; } = string.Empty;
    public long? BytesApprox { get; set; }
    public string? CompressMode { get; set; }
}
