using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorReturns;

public record GetVendorReturnByIdQuery(int Id) : IRequest<VendorReturnDetailResponseModel>;

public class GetVendorReturnByIdHandler(AppDbContext db) : IRequestHandler<GetVendorReturnByIdQuery, VendorReturnDetailResponseModel>
{
    public async Task<VendorReturnDetailResponseModel> Handle(GetVendorReturnByIdQuery request, CancellationToken ct)
    {
        var rtv = await db.VendorReturns.AsNoTracking()
            .Include(r => r.Vendor)
            .Include(r => r.NonConformance)
            .Include(r => r.PurchaseOrder)
            .Include(r => r.Currency)
            .Include(r => r.Lines).ThenInclude(l => l.Part)
            .Include(r => r.Lines).ThenInclude(l => l.Location)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor return {request.Id} not found");
        var memo = (await VendorReturnMemos.LatestAsync(db, [rtv.Id], ct)).GetValueOrDefault(rtv.Id);

        return new VendorReturnDetailResponseModel(
            rtv.Id, rtv.ReturnNumber, rtv.VendorId, rtv.Vendor.CompanyName, rtv.NonConformanceId, rtv.NonConformance?.NcrNumber,
            rtv.PurchaseOrderId, rtv.PurchaseOrder?.PONumber, rtv.Status.ToString(), rtv.ExpectedCredit,
            rtv.CurrencyId, rtv.Currency?.Code, rtv.FxRate, rtv.ShipDate, rtv.Carrier, rtv.TrackingNumber, rtv.RmaNumber, rtv.Notes,
            rtv.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new VendorReturnLineModel(
                    l.Id, l.LineNumber, l.PartId, l.Part?.PartNumber, l.Part?.Description, l.LotNumber, l.LocationId,
                    l.Location?.Name, l.Quantity, l.UnitPrice, l.UnitCost, l.ShippedCost))
                .ToList(),
            memo?.Id, memo?.MemoNumber, rtv.CreatedAt, rtv.Version);
    }
}
