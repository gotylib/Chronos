using Amazon.S3;
using Amazon.S3.Model;

namespace Chronos.Agent.Infrastructure.ObjectStorage;

internal static class VolumeTarS3Uploader
{
    /// <summary>Потоковая загрузка tar/gzip в S3 (multipart при размере ≥ одного полного чанка).</summary>
    internal static async Task<(bool Success, string? Error, long BytesUploaded)> UploadAsync(
        IAmazonS3 client,
        string bucket,
        string objectKey,
        Stream sourceStream,
        string contentType,
        CancellationToken ct)
    {
        const int partSize = 6 * 1024 * 1024;
        var bufferA = new byte[partSize];
        var bufferB = new byte[partSize];

        var firstLen = await ReadFullChunkAsync(sourceStream, bufferA, ct).ConfigureAwait(false);
        if (firstLen == 0)
            return (false, "empty snapshot stream", 0);

        if (firstLen < partSize)
        {
            await client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = bucket,
                        Key = objectKey,
                        InputStream = new MemoryStream(bufferA, 0, firstLen, writable: false),
                        ContentType = contentType
                    },
                    ct)
                .ConfigureAwait(false);
            return (true, null, firstLen);
        }

        var init = await client.InitiateMultipartUploadAsync(
                new InitiateMultipartUploadRequest
                {
                    BucketName = bucket,
                    Key = objectKey,
                    ContentType = contentType
                },
                ct)
            .ConfigureAwait(false);

        long uploaded = 0;
        var parts = new List<PartETag>();
        try
        {
            var curBuf = bufferA;
            var curLen = firstLen;
            var altBuf = bufferB;
            var partNumber = 1;

            while (true)
            {
                var nextLen = await ReadFullChunkAsync(sourceStream, altBuf, ct).ConfigureAwait(false);
                var isLast = nextLen == 0;

                var up = await client.UploadPartAsync(
                        new UploadPartRequest
                        {
                            BucketName = bucket,
                            Key = objectKey,
                            UploadId = init.UploadId,
                            PartNumber = partNumber++,
                            PartSize = curLen,
                            IsLastPart = isLast,
                            InputStream = new MemoryStream(curBuf, 0, curLen, writable: false)
                        },
                        ct)
                    .ConfigureAwait(false);

                parts.Add(new PartETag(up.PartNumber, up.ETag));
                uploaded += curLen;

                if (isLast)
                    break;

                (curBuf, altBuf) = (altBuf, curBuf);
                curLen = nextLen;
            }

            await client.CompleteMultipartUploadAsync(
                    new CompleteMultipartUploadRequest
                    {
                        BucketName = bucket,
                        Key = objectKey,
                        UploadId = init.UploadId,
                        PartETags = parts
                    },
                    ct)
                .ConfigureAwait(false);

            return (true, null, uploaded);
        }
        catch
        {
            try
            {
                await client.AbortMultipartUploadAsync(
                        new AbortMultipartUploadRequest
                        {
                            BucketName = bucket,
                            Key = objectKey,
                            UploadId = init.UploadId
                        },
                        ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }

            throw;
        }
    }

    private static async Task<int> ReadFullChunkAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }
}
