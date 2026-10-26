using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorReturns;

/// <summary>Returns to vendor, newest first, optionally for one vendor, NCR or status.</summary>
public record GetVendorReturnsQuery(
    int? VendorId = null,
    int? NonConformanceId = null,
    VendorReturnStatus? Status = null) : IRequest<List<VendorReturnListItemModel>>;

public class GetVendorReturnsHandler(AppDbContext db) : IRequestHandler<GetVendorReturnsQuery, List<VendorReturnListItemModel>>
{
    private const int MaxRows = 500;

    public async Task<List<VendorReturnListItemModel>> Handle(GetVendorReturnsQuery request, CancellationToken ct)
    {
        var query = db.VendorReturns.AsNoTracking()
            .Include(r => r.Vendor)
            .Include(r => r.NonConformance)
            .Include(r => r.PurchaseOrder)
            .Include(r => r.Currency)
            .Include(r => r.Lines)
            .AsSplitQuery()
            .AsQueryable();
        if (request.VendorId is int vendorId)
            query = query.Where(r => r.VendorId == vendorId);
        if (request.NonConformanceId is int ncrId)
            query = query.Where(r => r.NonConformanceId == ncrId);
        if (request.Status is VendorReturnStatus status)
            query = query.Where(r => r.Status == status);

        var returns = await query.OrderByDescending(r => r.Id).Take(MaxRows).ToListAsync(ct);
        var memos = await VendorReturnMemos.LatestAsync(db, returns.Select(r => r.Id).ToList(), ct);

        return returns.Select(r => new VendorReturnListItemModel(
                r.Id, r.ReturnNumber, r.VendorId, r.Vendor.CompanyName, r.NonConformanceId, r.NonConformance?.NcrNumber,
                r.PurchaseOrderId, r.PurchaseOrder?.PONumber, r.Status.ToString(), r.Lines.Sum(l => l.Quantity),
                r.ExpectedCredit, r.CurrencyId, r.Currency?.Code, r.ShipDate, r.Carrier, r.TrackingNumber,
                memos.GetValueOrDefault(r.Id)?.Id, memos.GetValueOrDefault(r.Id)?.MemoNumber, r.CreatedAt))
            .ToList();
    }
}

/// <summary>The live (not voided) vendor credit memo of each return to vendor.</summary>
internal static class VendorReturnMemos
{
    public static async Task<Dictionary<int, VendorCreditMemo>> LatestAsync(AppDbContext db, List<int> returnIds, CancellationToken ct)
    {
        if (returnIds.Count == 0)
            return [];
        var memos = await db.VendorCreditMemos.AsNoTracking()
            .Where(m => m.VendorReturnId != null && returnIds.Contains(m.VendorReturnId.Value) && m.Status != VendorCreditMemoStatus.Voided)
            .OrderByDescending(m => m.Id)
            .ToListAsync(ct);
        return memos.GroupBy(m => m.VendorReturnId!.Value).ToDictionary(g => g.Key, g => g.First());
    }
}
