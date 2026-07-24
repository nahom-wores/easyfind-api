using Amazon.S3;
using Amazon.S3.Model;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

public class S3StorageService(IAmazonS3 s3,
    IConfiguration config, ILogger<S3StorageService> logger) : IStorageService
{
    private readonly string _bucket =
        config["AWS:S3:BucketName"] ?? throw new InvalidOperationException("S3 bucket not configured");
    public async Task<StoredFile> UploadAsync(Stream fileStream, string fileName,
        string contentType, string folder, CancellationToken ct = default)
    {
        // Never trust the uploaded filename — generate our own key
        var ext = Path.GetExtension(fileName);
        var key = $"{folder}/{Guid.NewGuid():N}{ext}";

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            // No public ACL — bucket stays private, access is via presigned URLs
        };

        await s3.PutObjectAsync(request, ct);
        logger.LogInformation("Uploaded {Key} to S3", key);

        // We store the key, not a URL — URLs are generated on demand and expire
        return new StoredFile(key, key);
    }

    public Task<string> GetAccessUrlAsync(string key, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Verb = HttpVerb.GET
        };

        // Synchronous by design in the SDK — no network call, it's signed locally
        return Task.FromResult(s3.GetPreSignedURL(request));
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = key
        }, ct);
    }
}