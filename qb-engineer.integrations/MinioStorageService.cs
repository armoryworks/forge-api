using Minio;
using Minio.DataModel.Args;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Core.Settings;

namespace QBEngineer.Integrations;

/// <summary>
/// Real implementation of <see cref="IStorageService"/> backed by MinIO.
/// Phase 1m: endpoint + credentials read live from
/// <see cref="ISettingsService"/>. <see cref="IMinioClient"/> instances
/// are cached but rebuilt when the underlying (endpoint, access-key,
/// secret-key, ssl) tuple changes — so admin rotations take effect
/// without restart.
///
/// Two separate clients: one for internal-network ops, one for presigned
/// URL generation pointed at the public endpoint (presigned URLs embed
/// the host in their HMAC signature, so the client used to generate
/// must target the host the browser hits).
/// </summary>
public class MinioStorageService(ISettingsService settings) : IStorageService
{
    private readonly object _lock = new();
    private (string Endpoint, string AccessKey, string SecretKey, bool UseSsl, string? PublicEndpoint) _cachedKey;
    private IMinioClient? _client;
    private IMinioClient? _presignClient;

    public async Task UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct)
    {
        var (client, _) = await GetClientsAsync(ct);
        await client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType), ct);
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct)
    {
        var (client, _) = await GetClientsAsync(ct);
        var ms = new MemoryStream();
        await client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithCallbackStream(stream => stream.CopyTo(ms)), ct);
        ms.Position = 0;
        return ms;
    }

    public async Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds, CancellationToken ct)
    {
        var (_, presignClient) = await GetClientsAsync(ct);
        return await presignClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds));
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct)
    {
        var (client, _) = await GetClientsAsync(ct);
        await client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey), ct);
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        var (client, _) = await GetClientsAsync(ct);
        var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), ct);
        if (!exists)
        {
            await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName), ct);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var (client, _) = await GetClientsAsync(ct);
            await client.ListBucketsAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve cached MinIO clients, rebuilding when any underlying
    /// connection setting changed since last use. Lazy + double-checked-
    /// locked so a steady-state hot path takes a single field read.
    /// </summary>
    private async Task<(IMinioClient Client, IMinioClient PresignClient)> GetClientsAsync(CancellationToken ct)
    {
        var endpoint = await settings.GetStringAsync(MinioSettings.KeyEndpoint, ct) ?? "qb-engineer-storage:9000";
        var accessKey = await settings.GetStringAsync(MinioSettings.KeyAccessKey, ct) ?? "minioadmin";
        var secretKey = await settings.GetStringAsync(MinioSettings.KeySecretKey, ct) ?? "minioadmin";
        var useSsl = bool.TryParse(await settings.GetStringAsync(MinioSettings.KeyUseSsl, ct), out var ssl) && ssl;
        // PublicEndpoint isn't admin-managed today (no descriptor); leave
        // null so presignClient falls back to endpoint. Future descriptor
        // expansion can wire this in.
        string? publicEndpoint = null;

        var liveKey = (endpoint, accessKey, secretKey, useSsl, publicEndpoint);

        if (_client is not null && _cachedKey == liveKey)
        {
            return (_client, _presignClient!);
        }

        lock (_lock)
        {
            if (_client is not null && _cachedKey == liveKey)
            {
                return (_client, _presignClient!);
            }

            _client = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(useSsl)
                .Build();

            var presignEndpoint = !string.IsNullOrWhiteSpace(publicEndpoint) ? publicEndpoint : endpoint;
            _presignClient = new MinioClient()
                .WithEndpoint(presignEndpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(useSsl)
                .Build();

            _cachedKey = liveKey;
            return (_client, _presignClient);
        }
    }
}
