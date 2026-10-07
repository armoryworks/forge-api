using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record ExecuteScanReceiveCommand(ScanReceiveRequestModel Data) : IRequest<int>;

public class ExecuteScanReceiveCommandValidator : AbstractValidator<ExecuteScanReceiveCommand>
{
    public ExecuteScanReceiveCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.PurchaseOrderLineId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.ToLocationId).GreaterThan(0);
    }
}

public class ExecuteScanReceiveHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext,
    ICapabilitySnapshotProvider? capabilities = null)
    : IRequestHandler<ExecuteScanReceiveCommand, int>
{
    public async Task<int> Handle(ExecuteScanReceiveCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userName = httpContext.HttpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
        var now = clock.UtcNow;

        // Validate PO line exists and has unreceived qty
        var poLine = await db.PurchaseOrderLines
            .Include(pol => pol.PurchaseOrder)
            .Include(pol => pol.PurchaseUnit)
            .Where(pol => pol.Id == data.PurchaseOrderLineId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order line {data.PurchaseOrderLineId} not found");

        if (poLine.PartId != data.PartId)
            throw new InvalidOperationException("Part does not match purchase order line");

        if (poLine.PurchaseOrder.Status == PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot receive against a cancelled purchase order");

        var remaining = poLine.OrderedQuantity - poLine.ReceivedQuantity;
        if (data.Quantity > remaining)
            throw new InvalidOperationException(
                $"Cannot receive {data.Quantity} — only {remaining} remaining on PO line");

        // Validate destination location
        var destExists = await db.StorageLocations.AsNoTracking()
            .AnyAsync(sl => sl.Id == data.ToLocationId, cancellationToken);
        if (!destExists)
            throw new KeyNotFoundException($"Destination location {data.ToLocationId} not found");

        // Get part info
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == data.PartId)
            .Select(p => new { p.PartNumber, p.RequiresReceivingInspection })
            .FirstAsync(cancellationToken);

        // Create receiving record
        var receivingRecord = new ReceivingRecord
        {
            PurchaseOrderLineId = data.PurchaseOrderLineId,
            // Phase 3 / WU-23 (F8-broad): ReceivingRecord.QuantityReceived is now
            // decimal; the (int) cast is gone, scan-receive preserves precision.
            QuantityReceived = data.Quantity,
            ReceivedBy = userName,
            StorageLocationId = data.ToLocationId,
            InspectionStatus = ReceivingInspectionPolicy.InitialStatus(part.RequiresReceivingInspection, capabilities),
        };
        db.ReceivingRecords.Add(receivingRecord);

        // Update PO line received quantity (decimal-precision safe — Phase 3 / WU-10)
        poLine.ReceivedQuantity += data.Quantity;

        var contentPerUnit = poLine.PurchaseUnit?.ContentQuantity;
        var baseQuantity = contentPerUnit is > 0
            ? data.Quantity * contentPerUnit.Value
            : data.Quantity;

        var destContent = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == data.PartId
                && bc.LocationId == data.ToLocationId
                && bc.LotNumber == null
                && bc.RemovedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (destContent != null)
        {
            destContent.Quantity += baseQuantity;
        }
        else
        {
            destContent = new BinContent
            {
                LocationId = data.ToLocationId,
                EntityType = "part",
                EntityId = data.PartId,
                Quantity = baseQuantity,
                PlacedBy = userId,
                PlacedAt = now,
            };
            db.BinContents.Add(destContent);
        }

        // Create scan action log
        var scanLog = new ScanActionLog
        {
            UserId = userId,
            ActionType = ScanActionType.Receive,
            PartId = data.PartId,
            PartNumber = part.PartNumber,
            ToLocationId = data.ToLocationId,
            Quantity = baseQuantity,
            RelatedEntityId = poLine.PurchaseOrderId,
            RelatedEntityType = "PurchaseOrder",
        };
        db.ScanActionLogs.Add(scanLog);
        await db.SaveChangesAsync(cancellationToken);

        // Create bin movement with scan log reference
        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = baseQuantity,
            ToLocationId = data.ToLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.ScanReceive,
            ScanActionLogId = scanLog.Id,
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
