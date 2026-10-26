using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>The book's opening stock loads, newest first (first active book when none is given).</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record GetStockLoadsQuery(int? BookId = null) : IRequest<IReadOnlyList<StockLoadSummaryModel>>;

public class GetStockLoadsHandler(AppDbContext db) : IRequestHandler<GetStockLoadsQuery, IReadOnlyList<StockLoadSummaryModel>>
{
    public async Task<IReadOnlyList<StockLoadSummaryModel>> Handle(GetStockLoadsQuery request, CancellationToken ct)
    {
        var loads = db.InventoryStockLoads.AsNoTracking();
        if (request.BookId is int bookId)
            loads = loads.Where(l => l.BookId == bookId);
        return await loads
            .OrderByDescending(l => l.Id)
            .Select(l => new StockLoadSummaryModel(
                l.Id, l.LoadNumber, l.AsOfDate, l.Status, l.GlMode, l.Lines.Count,
                l.Lines.Sum(x => x.ExtendedValue), l.JournalEntryId, l.PostedAt, l.CreatedAt))
            .ToListAsync(ct);
    }
}
