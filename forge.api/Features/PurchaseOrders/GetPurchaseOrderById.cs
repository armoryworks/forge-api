using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PurchaseOrders;

public record GetPurchaseOrderByIdQuery(int Id) : IRequest<PurchaseOrderDetailResponseModel>;

public class GetPurchaseOrderByIdHandler(IPurchaseOrderRepository repo)
    : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDetailResponseModel>
{
    public async Task<PurchaseOrderDetailResponseModel> Handle(GetPurchaseOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var po = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        // Bought-parts effort PR2 — surface the vendor-minimum warning so
        // the UI can render a non-blocking banner. Pre-compute here rather
        // than in the UI so the rule lives in one place.
        var lineTotal = po.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice);
        var poTotal = lineTotal + (po.EstimatedFreight ?? 0m);
        var belowMin = po.Vendor.MinOrderAmount.HasValue
            && po.Vendor.MinOrderAmount.Value > 0
            && poTotal < po.Vendor.MinOrderAmount.Value;

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
                l.Part.PartNumber,
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
                l.ManualOverrideReason)).ToList(),
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
            VendorMinimumOrderAmount: po.Vendor.MinOrderAmount);
    }
}
