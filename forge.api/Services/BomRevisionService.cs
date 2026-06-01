using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Phase 3 H4 / WU-20 — auto-revisions a part's BOM whenever its component
/// list changes. Handlers call <see cref="CaptureCurrentStateAsync"/> after
/// any add/update/delete on a BOMLine; the service snapshots the part's
/// current BOMLine rows into a fresh <see cref="BomRevision"/> and points
/// <see cref="Part.CurrentBomRevisionId"/> at the new revision.
///
/// Scope discipline (per WU-20 spec):
///   - Component change ⇒ new revision (handled here).
///   - Metadata edit ⇒ in-place on the parent Part row, not here.
///   - Job-side BOM-revision pinning ⇒ <see cref="PinJobToCurrentRevisionAsync"/>.
/// </summary>
public interface IBomRevisionService
{
    /// <summary>
    /// Take a snapshot of the part's current BOMLine rows as a new
    /// <see cref="BomRevision"/>. Caller is responsible for having
    /// already persisted the BOMLine mutation (Add/Update/Delete) — this
    /// reads from the DB to get the post-mutation state.
    /// </summary>
    Task<BomRevision> CaptureCurrentStateAsync(int partId, int? createdByUserId, string? notes, CancellationToken ct);

    /// <summary>
    /// Pin a job to the part's current BOM revision (called at job
    /// release / creation when the job has a part). No-op if the part
    /// has no current BOM revision yet.
    /// </summary>
    Task PinJobToCurrentRevisionAsync(int jobId, int partId, CancellationToken ct);
}

public class BomRevisionService(AppDbContext db, IClock clock) : IBomRevisionService
{
    public async Task<BomRevision> CaptureCurrentStateAsync(
        int partId, int? createdByUserId, string? notes, CancellationToken ct)
    {
        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, ct)
            ?? throw new KeyNotFoundException($"Part {partId} not found");

        // Determine next revision number: max+1 over existing revisions for
        // this part. Includes soft-deleted to avoid number reuse.
        var nextRevNumber = await db.BomRevisions
            .IgnoreQueryFilters()
            .Where(r => r.PartId == partId)
            .Select(r => (int?)r.RevisionNumber)
            .MaxAsync(ct) ?? 0;
        nextRevNumber += 1;

        // Read the current BOMLine rows for the parent.
        var entries = await db.BOMLines
            .Where(b => b.ParentPartId == partId)
            .Include(b => b.Uom)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var revision = new BomRevision
        {
            PartId = partId,
            RevisionNumber = nextRevNumber,
            EffectiveDate = now,
            Notes = notes,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var e in entries)
        {
            revision.Entries.Add(new BomRevisionLine
            {
                PartId = e.ChildPartId,
                Quantity = e.Quantity,
                UnitOfMeasure = e.Uom?.Name ?? string.Empty,
                ReferenceDesignator = e.ReferenceDesignator,
                SourceType = e.SourceType,
                LeadTimeDays = e.LeadTimeDays,
                Notes = e.Notes,
                SortOrder = e.SortOrder,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.BomRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        // Now point the part at this revision.
        part.CurrentBomRevisionId = revision.Id;
        await db.SaveChangesAsync(ct);

        return revision;
    }

    public async Task PinJobToCurrentRevisionAsync(int jobId, int partId, CancellationToken ct)
    {
        var part = await db.Parts
            .Where(p => p.Id == partId)
            .Select(p => new { p.CurrentBomRevisionId })
            .FirstOrDefaultAsync(ct);
        if (part?.CurrentBomRevisionId is null)
            return;

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return;

        // Only pin if not already pinned (idempotent — release should be a
        // one-time event but multiple calls should not overwrite the
        // historical capture).
        if (job.BomRevisionIdAtRelease is null)
        {
            job.BomRevisionIdAtRelease = part.CurrentBomRevisionId;
            await db.SaveChangesAsync(ct);
        }
    }
}
