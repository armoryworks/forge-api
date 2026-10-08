using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Data.Extensions;

/// <summary>
/// Queries behind the lot quality hold: which NCRs still hold a part's lot, and whether an active recall
/// covers a lot. An NCR holds its lot until it is closed or dispositioned Use As Is or Rework.
/// </summary>
public static class QualityHoldExtensions
{
    /// <summary>NCRs on the part and lot whose hold has not been released, newest first.</summary>
    public static IQueryable<NonConformance> NcrsHoldingLot(this AppDbContext db, int partId, string lotNumber)
        => db.NonConformances
            .Where(n => n.PartId == partId
                && n.LotNumber == lotNumber
                && n.Status != NcrStatus.Closed
                && !(n.Status == NcrStatus.Dispositioned
                    && (n.DispositionCode == NcrDispositionCode.UseAsIs
                        || n.DispositionCode == NcrDispositionCode.Rework)))
            .OrderByDescending(n => n.Id);

    /// <summary>True when an active recall's affected lots include the lot.</summary>
    public static Task<bool> IsLotUnderActiveRecallAsync(this AppDbContext db, string lotNumber, CancellationToken ct)
        => db.RecallAffectedLots
            .AnyAsync(a => a.Recall.Status == RecallStatus.Active && a.Lot.LotNumber == lotNumber, ct);

    /// <summary>The number of the newest NCR still holding the part's lot, or null when none does.</summary>
    public static Task<string?> FindQualityHoldNcrNumberAsync(
        this AppDbContext db, int partId, string? lotNumber, CancellationToken ct)
        => lotNumber is null
            ? Task.FromResult<string?>(null)
            : db.NcrsHoldingLot(partId, lotNumber).Select(n => (string?)n.NcrNumber).FirstOrDefaultAsync(ct);
}
