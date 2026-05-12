namespace Forge.Core.Interfaces;

public interface IStorageService
{
    Task UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct);
    Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds, CancellationToken ct);
    Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct);
    Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct);
    Task<bool> TestConnectionAsync(CancellationToken ct);
}
