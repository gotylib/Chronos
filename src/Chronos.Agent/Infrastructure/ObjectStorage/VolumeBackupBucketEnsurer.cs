using Amazon.S3;
using Amazon.S3.Model;
using Chronos.Agent.Application.Configuration;

namespace Chronos.Agent.Infrastructure.ObjectStorage;

internal static class VolumeBackupBucketEnsurer
{
    internal static async Task EnsureAsync(VolumeObjectStorageOptions storage, CancellationToken ct)
    {
        if (!storage.IsComplete)
            return;

        var cfg = new AmazonS3Config
        {
            ServiceURL = storage.ServiceUrl.TrimEnd('/'),
            ForcePathStyle = storage.ForcePathStyle,
            AuthenticationRegion = "us-east-1"
        };

        using var s3 = new AmazonS3Client(storage.AccessKey, storage.SecretKey, cfg);

        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = storage.BucketName }, ct).ConfigureAwait(false);
            Console.WriteLine($"[agent] Created bucket '{storage.BucketName}'.");
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists" ||
            ex.Message.Contains("BucketAlreadyOwnedByYou", StringComparison.OrdinalIgnoreCase))
        {
            // exists
        }
    }
}
