using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications;

/// <inheritdoc cref="IArtifactStore"/>
public class ArtifactStore(
    AppDbContext db,
    IStorageService storage,
    IOptions<MinioOptions> minioOptions,
    IClock clock,
    ILogger<ArtifactStore> logger) : IArtifactStore
{
    /// <summary>
    /// Communications get their own bucket rather than sharing job files.
    /// Retention, access policy and lifecycle all differ: these are evidence
    /// with a legal retention period, not working documents.
    /// </summary>
    private string Bucket => string.IsNullOrWhiteSpace(minioOptions.Value.JobFilesBucket)
        ? "forge-communications"
        : $"{minioOptions.Value.JobFilesBucket}-communications";

    public async Task<CommunicationArtifact> StoreAsync(
        int communicationId,
        CommunicationArtifactKind kind,
        Stream content,
        string contentType,
        string? originalFilename,
        CancellationToken ct)
    {
        await storage.EnsureBucketExistsAsync(Bucket, ct);

        // Hash and buffer in one pass. CryptoStream over a MemoryStream is the
        // simple correct shape: the digest is finalized from exactly the bytes
        // that go to storage, so there is no window in which the two disagree.
        //
        // Buffering in memory bounds this to the mail server's attachment limit
        // (tens of MB). If that ever stops holding, this becomes a temp file
        // with the same one-pass structure — not a second read of the source,
        // which would reintroduce the gap.
        using var buffer = new MemoryStream();
        string digest;
        using (var sha = SHA256.Create())
        await using (var crypto = new CryptoStream(buffer, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(crypto, ct);
            await crypto.FlushFinalBlockAsync(ct);
            digest = Convert.ToHexStringLower(sha.Hash!);
        }

        var byteSize = buffer.Length;
        buffer.Position = 0;

        // The storage key is built from ids and the digest only. The sender's
        // filename never reaches it — an attachment called "../../../etc/passwd"
        // is a display string, not a path component.
        var extension = SafeExtension(originalFilename, kind);
        var objectKey = $"communications/{communicationId}/{digest}{extension}";

        await storage.UploadAsync(Bucket, objectKey, buffer, contentType, ct);

        var artifact = new CommunicationArtifact
        {
            CommunicationId = communicationId,
            Kind = kind,
            Sha256 = digest,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            ByteSize = byteSize,
            OriginalFilename = Truncate(originalFilename, 500),
            BucketName = Bucket,
            ObjectKey = objectKey,
            IngestedAt = clock.UtcNow,
        };

        db.CommunicationArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[ARTIFACT] Stored {Kind} for communication {CommunicationId}: {Bytes} bytes, sha256 {Digest}",
            kind, communicationId, byteSize, digest);

        return artifact;
    }

    public Task<Stream> OpenAsync(CommunicationArtifact artifact, CancellationToken ct) =>
        storage.DownloadAsync(artifact.BucketName, artifact.ObjectKey, ct);

    public async Task<bool> VerifyAsync(CommunicationArtifact artifact, CancellationToken ct)
    {
        await using var stream = await storage.DownloadAsync(artifact.BucketName, artifact.ObjectKey, ct);
        using var sha = SHA256.Create();
        var actual = Convert.ToHexStringLower(await sha.ComputeHashAsync(stream, ct));

        var matches = string.Equals(actual, artifact.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!matches)
        {
            // Loud, because the only ways here are storage corruption or
            // tampering, and both invalidate every claim made on this artifact.
            logger.LogError(
                "[ARTIFACT] Hash mismatch on artifact {Id}: recorded {Recorded}, stored bytes hash to {Actual}",
                artifact.Id, artifact.Sha256, actual);
        }

        return matches;
    }

    /// <summary>
    /// A conservative extension for the storage key — letters and digits only,
    /// capped, and never taken from a filename with path separators in it. Used
    /// purely so an object is recognisable when browsing the bucket.
    /// </summary>
    private static string SafeExtension(string? filename, CommunicationArtifactKind kind)
    {
        if (kind == CommunicationArtifactKind.Message) return ".eml";
        if (string.IsNullOrWhiteSpace(filename)) return string.Empty;

        var dot = filename.LastIndexOf('.');
        if (dot < 0 || dot == filename.Length - 1) return string.Empty;

        var ext = filename[(dot + 1)..];
        if (ext.Length > 10 || !ext.All(char.IsLetterOrDigit)) return string.Empty;

        return $".{ext.ToLowerInvariant()}";
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
