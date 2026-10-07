using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record GetPurchaseOrderByIdQuery(int Id) : IRequest<PurchaseOrderDetailResponseModel>;

public class GetPurchaseOrderByIdHandler(IPurchaseOrderRepository repo, AppDbContext db)
    : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDetailResponseModel>
{
    public async Task<PurchaseOrderDetailResponseModel> Handle(GetPurchaseOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // S4b provenance — resolve the origin user's display name ("Last,
        // First") for Manual POs so the detail chip can show who raised it.
        string? originUserName = null;
        if (po.OriginUserId.HasValue)
        {
            originUserName = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == po.OriginUserId.Value)
                .Select(u => u.LastName + ", " + u.FirstName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Bought-parts effort PR2 — surface the vendor-minimum warning so
        // the UI can render a non-blocking banner. Pre-compute here rather
        // than in the UI so the rule lives in one place.
        var lineTotal = po.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice);
        var poTotal = lineTotal + (po.EstimatedFreight ?? 0m);
        var belowMin = po.Vendor.MinOrderAmount.HasValue
            && po.Vendor.MinOrderAmount.Value > 0
            && poTotal < po.Vendor.MinOrderAmount.Value;

        var partDefaultBinIds = po.Lines
            .Where(l => l.Part?.DefaultBinId is not null)
            .Select(l => l.Part!.DefaultBinId!.Value)
            .Distinct()
            .ToList();
        var receivableDefaultBinIds = partDefaultBinIds.Count == 0
            ? new HashSet<int>()
            : (await db.StorageLocations
                .AsNoTracking()
                .Where(s => partDefaultBinIds.Contains(s.Id) && s.IsActive && s.LocationType == LocationType.Bin)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();

        return new PurchaseOrderDetailResponseModel(
            po.Id,
            po.PONumber,
            po.VendorId,
            po.Vendor.CompanyName,
            po.JobId,
            po.Job?.JobNumber,
            po.Status.ToString(),
            po.SubmittedDate,
            po.AcknowledgedDate,
            po.ExpectedDeliveryDate,
            po.ReceivedDate,
            po.Notes,
            po.IsBlanket,
            po.BlanketTotalQuantity,
            po.BlanketReleasedQuantity,
            po.BlanketRemainingQuantity,
            po.BlanketExpirationDate,
            po.AgreedUnitPrice,
            po.Lines.Select(l => new PurchaseOrderLineResponseModel(
                l.Id,
                l.PartId,
                l.Part != null ? l.Part.PartNumber : null,
                l.Description,
                l.OrderedQuantity,
                l.ReceivedQuantity,
                l.RemainingQuantity,
                l.CancelledShortCloseQuantity,
                l.UnbilledReceivedQuantity,
                l.UnitPrice,
                l.OrderedQuantity * l.UnitPrice,
                l.Notes,
                l.PurchaseUnitId,
                l.PurchaseUnit != null ? l.PurchaseUnit.Label : null,
                l.ManualOverrideReason,
                l.Part?.DefaultBinId is int defaultBinId && receivableDefaultBinIds.Contains(defaultBinId)
                    ? defaultBinId
                    : null)).ToList(),
            po.CreatedAt,
            po.UpdatedAt,
            po.ShortCloseReason,
            po.ShortClosedAt,
            po.Incoterm.ToString(),
            po.EstimatedFreight,
            po.QuoteCurrency,
            po.FxRate,
            po.FxRateSource,
            BelowVendorMinimum: belowMin,
            VendorMinimumOrderAmount: po.Vendor.MinOrderAmount,
            OriginSource: po.OriginSource.ToString(),
            OriginUserName: originUserName,
            OriginReference: po.OriginReference);
    }
}
