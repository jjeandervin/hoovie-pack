using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using HooviePack.Files.Api.Configuration;

namespace HooviePack.FileMigration;

public sealed class S3LegacyObjectStorage(
    IAmazonS3 s3,
    FileStorageOptions options) : ILegacyObjectStorage
{
    public async Task<LegacyObjectMetadata?> GetMetadataAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = options.BucketName,
                Key = storageKey
            }, cancellationToken);
            return new LegacyObjectMetadata(response.ContentLength, response.Headers.ContentType ?? string.Empty);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task PutAsync(
        string storageKey,
        string contentType,
        Stream input,
        CancellationToken cancellationToken = default) =>
        s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = storageKey,
            InputStream = input,
            ContentType = contentType,
            AutoCloseStream = false
        }, cancellationToken);
}
