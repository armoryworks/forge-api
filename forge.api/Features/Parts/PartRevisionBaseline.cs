using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// The revision-history row a new part starts with, written in the same shape as
/// <see cref="CreatePartRevisionHandler"/> writes a later revision, so a part's history begins
/// at its first revision instead of at its first ECO.
/// </summary>
public static class PartRevisionBaseline
{
    /// <summary>
    /// Queues the current baseline row for <paramref name="part"/> at its present revision code.
    /// The caller saves it with the part.
    /// </summary>
    public static PartRevision Add(AppDbContext db, Part part, DateTimeOffset effectiveDate, string changeDescription)
    {
        var row = new PartRevision
        {
            Part = part,
            Revision = part.Revision.Trim(),
            ChangeDescription = changeDescription,
            EffectiveDate = effectiveDate,
            IsCurrent = true,
            CreatedBy = db.CurrentUserId,
        };
        db.PartRevisions.Add(row);
        return row;
    }

    /// <summary>
    /// Keeps the current history row in step when the revision field itself is corrected, so the
    /// history does not keep showing the old code as current. Nothing changes when another row
    /// already holds the new code.
    /// </summary>
    public static async Task RenameCurrentAsync(
        AppDbContext db, int partId, string previous, string next, CancellationToken ct)
    {
        if (string.Equals(previous, next, StringComparison.Ordinal))
            return;

        var rows = await db.PartRevisions.Where(r => r.PartId == partId).ToListAsync(ct);
        if (rows.Any(r => !string.Equals(r.Revision, previous, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Revision, next, StringComparison.OrdinalIgnoreCase)))
            return;

        var current = rows.FirstOrDefault(r => r.IsCurrent
            && string.Equals(r.Revision, previous, StringComparison.OrdinalIgnoreCase));
        if (current is not null)
            current.Revision = next;
    }
}
