using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
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

        // Update line received quantity
        line.ReceivedQuantity += data.QuantityReceived;

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
                Quantity = data.QuantityReceived,
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
                Quantity = data.QuantityReceived,
                LotNumber = data.LotNumber,
                ToLocationId = data.LocationId.Value,
                MovedBy = userId,
                MovedAt = DateTimeOffset.UtcNow,
                Reason = BinMovementReason.Receive,
            };

            await inventoryRepo.AddMovementAsync(movement, cancellationToken);
        }

        await poRepo.SaveChangesAsync(cancellationToken);

        // Load PO info for response
        var po = await poRepo.FindWithDetailsAsync(
            line.PurchaseOrderId, cancellationToken);

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
