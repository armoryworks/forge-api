using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record ReceivePurchaseOrderCommand(ReceivePurchaseOrderRequestModel Data) : IRequest<ReceivingRecordResponseModel>;

public class ReceivePurchaseOrderCommandValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.Data.PurchaseOrderLineId).GreaterThan(0);
        // Phase 3 / WU-23 (F8-broad): decimal quantity supports fractional UoM.
        RuleFor(x => x.Data.QuantityReceived).GreaterThan(0m);
        // PRI-1/2/3: a location is required when receiving stock, so the receive always stocks
        // (no notify-without-stock) and on-hand actually rises.
        RuleFor(x => x.Data.LocationId)
            .NotNull().WithMessage("A storage location is required when receiving stock into inventory.");
    }
}

public class ReceivePurchaseOrderHandler(
    IPurchaseOrderRepository poRepo,
    IInventoryRepository inventoryRepo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ReceivePurchaseOrderCommand, ReceivingRecordResponseModel>
{
    public async Task<ReceivingRecordResponseModel> Handle(
        ReceivePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var line = await poRepo.FindLineAsync(data.PurchaseOrderLineId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order line {data.PurchaseOrderLineId} not found");

        if (data.QuantityReceived > line.RemainingQuantity)
            throw new InvalidOperationException(
                $"Cannot receive {data.QuantityReceived} — only {line.RemainingQuantity} remaining");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userName = httpContext.HttpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

        // Create receiving record
        var record = new ReceivingRecord
        {
            PurchaseOrderLineId = data.PurchaseOrderLineId,
            QuantityReceived = data.QuantityReceived,
            ReceivedBy = userName,
            StorageLocationId = data.LocationId,
            Notes = data.Notes,
        };

        await poRepo.AddReceivingRecordAsync(record, cancellationToken);

        // Update line received quantity (stays in the line's order unit — options when the line
        // is option-priced, base units otherwise — to match OrderedQuantity / RemainingQuantity).
        line.ReceivedQuantity += data.QuantityReceived;

        // UoM purchase-units effort — inventory is always tracked in the part's base/stock UoM.
        // When the line was ordered in purchase units (e.g. 2 "4×8 sheets"), convert the received
        // option count to base units (2 × 32 = 64 sqft) before it lands in a bin. Null option (or
        // content ≤ 0) → already base units.
        var contentPerOption = line.PurchaseUnit?.ContentQuantity;
        var baseQuantityReceived = contentPerOption is > 0
            ? data.QuantityReceived * contentPerOption.Value
            : data.QuantityReceived;

        // If location provided, create bin content
        if (data.LocationId.HasValue)
        {
            var location = await inventoryRepo.FindLocationAsync(data.LocationId.Value, cancellationToken)
                ?? throw new KeyNotFoundException($"Location {data.LocationId} not found");

            var content = new BinContent
            {
                LocationId = data.LocationId.Value,
                EntityType = "part",
                EntityId = line.PartId,
                Quantity = baseQuantityReceived,
                LotNumber = data.LotNumber,
                PlacedBy = userId,
                PlacedAt = DateTimeOffset.UtcNow,
                Notes = data.Notes,
            };

            await inventoryRepo.AddBinContentAsync(content, cancellationToken);

            // Create movement record
            var movement = new BinMovement
            {
                EntityType = "part",
                EntityId = line.PartId,
                Quantity = baseQuantityReceived,
                LotNumber = data.LotNumber,
                ToLocationId = data.LocationId.Value,
                MovedBy = userId,
                MovedAt = DateTimeOffset.UtcNow,
                Reason = BinMovementReason.Receive,
            };

            await inventoryRepo.AddMovementAsync(movement, cancellationToken);
        }

        // PRI-1/2/3: advance the PO status — the inv-tab receive previously stocked but never advanced
        // status (notify-XOR-stock), so a fully-received PO stayed open. The PO + its lines are tracked
        // and `line` above is the same tracked instance, so the updated ReceivedQuantity is reflected.
        var po = await poRepo.FindWithDetailsAsync(line.PurchaseOrderId, cancellationToken);
        if (po is not null)
        {
            if (po.Lines.All(l => l.RemainingQuantity <= 0))
            {
                po.Status = PurchaseOrderStatus.Received;
                po.ReceivedDate = DateTimeOffset.UtcNow;
            }
            else if (po.Lines.Any(l => l.ReceivedQuantity > 0))
            {
                po.Status = PurchaseOrderStatus.PartiallyReceived;
            }
        }

        await poRepo.SaveChangesAsync(cancellationToken);

        return new ReceivingRecordResponseModel(
            record.Id,
            record.PurchaseOrderLineId,
            po?.PONumber,
            line.PartId,
            line.Part?.PartNumber,
            record.QuantityReceived,
            record.ReceivedBy,
            record.StorageLocationId,
            null,
            data.LotNumber,
            record.Notes,
            record.CreatedAt);
    }
}
