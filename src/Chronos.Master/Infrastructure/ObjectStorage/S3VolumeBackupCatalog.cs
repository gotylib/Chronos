using Amazon.S3;
using Amazon.S3.Model;

namespace Chronos.Master.Infrastructure.ObjectStorage;

internal static class S3VolumeBackupCatalog
{
    internal static async Task<List<S3Object>> ListVolumeBackupsDescendingAsync(
        IAmazonS3 s3,
        string bucket,
        string keyPrefix,
        CancellationToken ct)
    {
        var acc = new List<S3Object>();
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = keyPrefix };
        ListObjectsV2Response resp;
        do
        {
            resp = await s3.ListObjectsV2Async(request, ct).ConfigureAwait(false);
            acc.AddRange(resp.S3Objects);
            request.ContinuationToken = resp.NextContinuationToken;
        } while (resp.IsTruncated == true);

        return acc.OrderByDescending(o => o.LastModified).ToList();
    }

    internal static async Task DeleteObjectsAsync(IAmazonS3 s3, string bucket, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
            return;

        foreach (var chunk in keys.Chunk(900))
        {
            await s3.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = bucket,
                        Quiet = true,
                        Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList()
                    },
                    ct)
                .ConfigureAwait(false);
        }
    }
}
