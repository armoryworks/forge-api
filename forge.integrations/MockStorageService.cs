using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockStorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();
    private readonly ILogger<MockStorageService> _logger;

    public MockStorageService(ILogger<MockStorageService> logger)
    {
        _logger = logger;
    }

    public Task UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var key = $"{bucketName}/{objectKey}";
        _store[key] = ms.ToArray();
        _logger.LogInformation("[MockStorage] Uploaded {Key} ({Size} bytes, {ContentType})", key, ms.Length, contentType);
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct)
    {
        var key = $"{bucketName}/{objectKey}";
        if (!_store.TryGetValue(key, out var data))
            throw new KeyNotFoundException($"Object not found: {key}");

        _logger.LogInformation("[MockStorage] Downloaded {Key} ({Size} bytes)", key, data.Length);
        Stream result = new MemoryStream(data);
        return Task.FromResult(result);
    }

    public Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds, CancellationToken ct)
    {
        var url = $"mock:///{bucketName}/{objectKey}?expires={expirySeconds}";
        _logger.LogInformation("[MockStorage] Generated presigned URL: {Url}", url);
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct)
    {
        var key = $"{bucketName}/{objectKey}";
        _store.TryRemove(key, out _);
        _logger.LogInformation("[MockStorage] Deleted {Key}", key);
        return Task.CompletedTask;
    }

    public Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        _logger.LogInformation("[MockStorage] Bucket ensured: {Bucket}", bucketName);
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockStorage] Connection test — always succeeds");
        return Task.FromResult(true);
    }
}
