using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>
/// View the most-recent purchase orders that included this part. Buyer-side
/// "what have we been paying / from whom / how much" — one row per (PO,
/// line). Capped at 50 rows to keep the request small for parts that have
/// been ordered hundreds of times; the optional <paramref name="Search"/>
/// term filters on PO number + vendor name + line description (case-
/// insensitive substring match) so the user can find a specific PO without
/// paginating.
/// </summary>
public record GetPartPurchaseHistoryQuery(int PartId, string? Search)
    : IRequest<List<PartPurchaseHistoryItemResponseModel>>;

public class GetPartPurchaseHistoryHandler(AppDbContext db)
    : IRequestHandler<GetPartPurchaseHistoryQuery, List<PartPurchaseHistoryItemResponseModel>>
{
    private const int MaxRows = 50;

    public async Task<List<PartPurchaseHistoryItemResponseModel>> Handle(
        GetPartPurchaseHistoryQuery request, CancellationToken ct)
    {
        // Confirm the part exists; gives a clean 404 instead of an empty
        // list for callers passing a stale id.
        var partExists = await db.Parts.AsNoTracking()
            .AnyAsync(p => p.Id == request.PartId, ct);
        if (!partExists)
            throw new KeyNotFoundException($"Part {request.PartId} not found");

        var search = request.Search?.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);
        // EF translates ToLower() + Contains() to ILIKE on Postgres, so this
        // stays case-insensitive without forcing client-side evaluation.
        var loweredSearch = hasSearch ? search!.ToLower() : null;

        var query = db.PurchaseOrderLines
            .AsNoTracking()
            .Where(l => l.PartId == request.PartId)
            .Select(l => new
            {
                Line = l,
                l.PurchaseOrder,
                l.PurchaseOrder.Vendor,
            });

        if (hasSearch)
        {
            query = query.Where(x =>
                x.PurchaseOrder.PONumber.ToLower().Contains(loweredSearch!) ||
                x.Vendor.CompanyName.ToLower().Contains(loweredSearch!) ||
                x.Line.Description.ToLower().Contains(loweredSearch!));
        }

        // Order by the buyer-meaningful date: SubmittedDate when the PO has
        // been sent to the vendor, falling back to CreatedAt for drafts and
        // older rows where SubmittedDate was never set. Pull more than we
        // need so the in-memory ordering can dedupe-tie-breaker by line id.
        var rows = await query
            .OrderByDescending(x => x.PurchaseOrder.SubmittedDate ?? x.PurchaseOrder.CreatedAt)
            .ThenByDescending(x => x.Line.Id)
            .Take(MaxRows)
            .ToListAsync(ct);

        return rows
            .Select(x => new PartPurchaseHistoryItemResponseModel(
                PurchaseOrderId: x.PurchaseOrder.Id,
                PurchaseOrderLineId: x.Line.Id,
                PoNumber: x.PurchaseOrder.PONumber,
                VendorId: x.Vendor.Id,
                VendorName: x.Vendor.CompanyName,
                Status: x.PurchaseOrder.Status,
                OrderedQuantity: x.Line.OrderedQuantity,
                ReceivedQuantity: x.Line.ReceivedQuantity,
                UnitPrice: x.Line.UnitPrice,
                LineTotal: x.Line.OrderedQuantity * x.Line.UnitPrice,
                OrderedDate: x.PurchaseOrder.SubmittedDate ?? x.PurchaseOrder.CreatedAt,
                ExpectedDeliveryDate: x.PurchaseOrder.ExpectedDeliveryDate))
            .ToList();
    }
}
