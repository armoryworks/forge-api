using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
        RuleFor(x => x.Data.LotNumber)
            .Must(lot => lot is null || lot.Trim().Length <= 100)
            .WithMessage("Lot / heat number must be 100 characters or fewer.").WithErrorCode("lot-number-too-long");
    }
}

public class ExecuteScanReceiveHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IMediator mediator)
    : IRequestHandler<ExecuteScanReceiveCommand, int>
{
    public async Task<int> Handle(ExecuteScanReceiveCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var poLine = await db.PurchaseOrderLines.AsNoTracking()
            .Include(pol => pol.Part)
            .Include(pol => pol.PurchaseUnit)
            .Where(pol => pol.Id == data.PurchaseOrderLineId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order line {data.PurchaseOrderLineId} not found");

        if (poLine.PartId != data.PartId)
            throw new InvalidOperationException("Part does not match purchase order line");

        var lot = string.IsNullOrWhiteSpace(data.LotNumber) ? null : data.LotNumber.Trim();
        var receipt = await mediator.Send(new ReceiveItemsCommand(
            poLine.PurchaseOrderId,
            [new ReceiveLineModel(poLine.Id, data.Quantity, data.ToLocationId, null, LotNumber: lot)],
            ActualFreight: 0m), cancellationToken);

        var contentPerUnit = poLine.PurchaseUnit?.ContentQuantity;
        var baseQuantity = contentPerUnit is > 0
            ? data.Quantity * contentPerUnit.Value
            : data.Quantity;

        var scanLog = new ScanActionLog
        {
            UserId = userId,
            ActionType = ScanActionType.Receive,
            PartId = data.PartId,
            PartNumber = poLine.Part?.PartNumber,
            ToLocationId = data.ToLocationId,
            Quantity = baseQuantity,
            RelatedEntityId = poLine.PurchaseOrderId,
            RelatedEntityType = "PurchaseOrder",
        };
        db.ScanActionLogs.Add(scanLog);
        await db.SaveChangesAsync(cancellationToken);

        var movements = await db.BinMovements
            .Where(bm => receipt.MovementIds.Contains(bm.Id))
            .ToListAsync(cancellationToken);
        foreach (var movement in movements)
        {
            movement.Reason = BinMovementReason.ScanReceive;
            movement.ScanActionLogId = scanLog.Id;
        }
        var records = await db.ReceivingRecords
            .Where(r => r.ReceiptNumber == receipt.ReceiptNumber)
            .ToListAsync(cancellationToken);
        foreach (var record in records)
            record.Notes = ScanReceiveNote.For(scanLog.Id);
        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
