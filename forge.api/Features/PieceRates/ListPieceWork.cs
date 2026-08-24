using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.PieceRates;
using Forge.Data.Context;

namespace Forge.Api.Features.PieceRates;

/// <summary>Piece-work entries in a date range, optionally for one worker (newest first).</summary>
[RequiresCapability("CAP-HR-PIECE-RATES")]
public record ListPieceWorkQuery(DateOnly From, DateOnly To, int? UserId)
    : IRequest<IReadOnlyList<PieceWorkEntryModel>>;

public class ListPieceWorkHandler(AppDbContext db)
    : IRequestHandler<ListPieceWorkQuery, IReadOnlyList<PieceWorkEntryModel>>
{
    public async Task<IReadOnlyList<PieceWorkEntryModel>> Handle(ListPieceWorkQuery request, CancellationToken ct)
    {
        var entries = await db.PieceWorkEntries.AsNoTracking()
            .Include(e => e.Part)
            .Where(e => e.WorkDate >= request.From && e.WorkDate <= request.To
                     && (request.UserId == null || e.UserId == request.UserId))
            .OrderByDescending(e => e.WorkDate).ThenByDescending(e => e.Id)
            .ToListAsync(ct);

        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}".Trim(' ', ','), ct);

        return entries.Select(e => new PieceWorkEntryModel(
            e.Id, e.UserId, names.GetValueOrDefault(e.UserId, "Unknown"),
            e.PartId, e.Part!.PartNumber, e.OperationId, e.WorkDate,
            e.Quantity, e.RateSnapshot, e.Earnings, e.Notes)).ToList();
    }
}
