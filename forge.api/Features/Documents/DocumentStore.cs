using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public class DocumentStore(AppDbContext db, IStorageService storage, IClock clock) : IDocumentStore
{
    public const string Bucket = "forge-documents";

    public async Task<DocumentSetVersion> StoreAsync(
        string kind, DocumentLinkTarget primary, IReadOnlyCollection<DocumentLinkTarget> links,
        byte[] bytes, string fileName, string contentType, CancellationToken ct)
    {
        // Find the existing set for this (kind, primary entity) via its primary link.
        var set = await db.DocumentSets
            .Include(s => s.Versions)
            .Include(s => s.Links)
            .FirstOrDefaultAsync(s => s.Kind == kind &&
                s.Links.Any(l => l.EntityType == primary.EntityType && l.EntityId == primary.EntityId), ct);

        var now = clock.UtcNow;
        var userId = db.CurrentUserId;

        if (set is null)
        {
            set = new DocumentSet { Kind = kind, CreatedBy = userId };
            db.DocumentSets.Add(set);
        }
        else
        {
            // End-date + archive the current version before adding the new one.
            foreach (var current in set.Versions.Where(v => v.EffectiveTo == null && !v.IsArchived))
            {
                current.EffectiveTo = now;
                current.IsArchived = true;
            }
        }

        var nextVersion = (set.Versions.Count == 0 ? 0 : set.Versions.Max(v => v.Version)) + 1;
        var objectKey = $"{kind}/{primary.EntityType}-{primary.EntityId}/v{nextVersion}-{fileName}";

        await storage.EnsureBucketExistsAsync(Bucket, ct);
        using (var ms = new MemoryStream(bytes))
            await storage.UploadAsync(Bucket, objectKey, ms, contentType, ct);

        var version = new DocumentSetVersion
        {
            DocumentSet = set,
            Version = nextVersion,
            FileName = fileName,
            ContentType = contentType,
            Size = bytes.LongLength,
            BucketName = Bucket,
            ObjectKey = objectKey,
            EffectiveFrom = now,
            IsArchived = false,
            CreatedBy = userId,
        };
        set.Versions.Add(version);

        // Ensure every link (primary + extras) exists, de-duplicated.
        foreach (var target in links.Prepend(primary)
                     .GroupBy(l => (l.EntityType, l.EntityId)).Select(g => g.First()))
        {
            if (!set.Links.Any(l => l.EntityType == target.EntityType && l.EntityId == target.EntityId))
                set.Links.Add(new DocumentSetLink { DocumentSet = set, EntityType = target.EntityType, EntityId = target.EntityId });
        }

        await db.SaveChangesAsync(ct);
        return version;
    }

    public async Task<byte[]?> ReadCurrentAsync(string kind, DocumentLinkTarget primary, CancellationToken ct)
    {
        var current = await db.DocumentSetVersions
            .Where(v => !v.IsArchived && v.EffectiveTo == null
                && v.DocumentSet.Kind == kind
                && v.DocumentSet.Links.Any(l => l.EntityType == primary.EntityType && l.EntityId == primary.EntityId))
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);
        if (current is null) return null;

        await using var stream = await storage.DownloadAsync(current.BucketName, current.ObjectKey, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
