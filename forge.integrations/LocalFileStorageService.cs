using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// IStorageService backed by the local filesystem.
/// Files are stored at {RootPath}/{bucketName}/{objectKey}.
/// The host directory is expected to be mounted into the container via a Docker volume.
/// Presigned URLs are time-limited tokens signed with ASP.NET Data Protection.
/// </summary>
public class LocalFileStorageService : IStorageService
{
    private const string ProtectorPurpose = "LocalStorage.PresignedUrl";

    private readonly LocalStorageOptions _opts;
    private readonly IDataProtector _protector;

    public LocalFileStorageService(IOptions<LocalStorageOptions> options, IDataProtectionProvider dataProtection)
    {
        _opts = options.Value;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
    }

    public async Task UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct)
    {
        var path = GetFilePath(bucketName, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(file, ct);
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct)
    {
        var path = GetFilePath(bucketName, objectKey);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Storage object not found: {bucketName}/{objectKey}");

        var ms = new MemoryStream();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds, CancellationToken ct)
    {
        var expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds).ToUnixTimeSeconds();
        var payload = $"{bucketName}:{objectKey}:{expiry}";
        var token = _protector.Protect(payload);

        var baseUrl = _opts.PublicBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/v1/storage/{Uri.EscapeDataString(bucketName)}/{Uri.EscapeDataString(objectKey)}?token={Uri.EscapeDataString(token)}";
        return Task.FromResult(url);
    }

    public Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct)
    {
        var path = GetFilePath(bucketName, objectKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(_opts.RootPath, bucketName));
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_opts.RootPath);
            var probe = Path.Combine(_opts.RootPath, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Validates a presigned token and returns (bucketName, objectKey) if valid.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    public (string Bucket, string Key)? ValidateToken(string token)
    {
        try
        {
            var payload = _protector.Unprotect(token);
            var parts = payload.Split(':', 3);
            if (parts.Length != 3) return null;

            var expiry = long.Parse(parts[2]);
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry) return null;

            return (parts[0], parts[1]);
        }
        catch
        {
            return null;
        }
    }

    private string GetFilePath(string bucketName, string objectKey)
    {
        // Normalise the object key: forward slashes become directory separators
        var safeBucket = Path.GetFileName(bucketName); // strip any path traversal
        var safeKey = objectKey.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_opts.RootPath, safeBucket, safeKey);
    }
}
