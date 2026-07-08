using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — ship a stage: sets Status to <see cref="SalesOrderStageStatus.Shipped"/>
/// and ActualShipDate to now (via <see cref="IClock"/>). Requires the stage to be
/// ReadyToShip first. If the stage already has a linked shipment the linkage is
/// left as-is; otherwise an optional <c>shipmentId</c> in the request may link one.
///
/// <para><strong>Payment-milestone interplay (documented).</strong> When the stage
/// has a linked <c>PaymentMilestoneId</c>, this handler does NOT mutate the
/// milestone's stored Status. Milestone due-ness is computed on read by
/// <see cref="Forge.Api.Features.PaymentSchedules.PaymentMilestoneEvaluator"/>
/// from the order/shipment state (an <c>OnShipment</c> milestone becomes Due once
/// the order reaches a shipped status). Shipping the stage only records the
/// linkage + ActualShipDate so the evaluator observes the shipment; forcing the
/// milestone status here would double-book the S2 evaluator's single source of
/// truth.</para>
/// </summary>
public record ShipStageCommand(int StageId, int? ShipmentId) : IRequest<SalesOrderStageResponseModel>;

public class ShipStageValidator : AbstractValidator<ShipStageCommand>
{
    public ShipStageValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
    }
}

public class ShipStageHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ShipStageCommand, SalesOrderStageResponseModel>
{
    public async Task<SalesOrderStageResponseModel> Handle(ShipStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await db.SalesOrderStages
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order stage {request.StageId} not found");

        if (stage.Status is SalesOrderStageStatus.Shipped or SalesOrderStageStatus.Closed)
            throw new InvalidOperationException($"Stage '{stage.Name}' has already been shipped.");
        if (stage.Status != SalesOrderStageStatus.ReadyToShip)
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' must be marked ready to ship before it can be shipped.");

        // Link a shipment only when the stage doesn't already have one.
        if (stage.ShipmentId is null && request.ShipmentId is int shipmentId)
        {
            var shipmentExists = await db.Shipments.AnyAsync(s => s.Id == shipmentId, cancellationToken);
            if (!shipmentExists)
                throw new KeyNotFoundException($"Shipment {shipmentId} not found");
            stage.ShipmentId = shipmentId;
        }

        stage.Status = SalesOrderStageStatus.Shipped;
        stage.ActualShipDate = clock.UtcNow;
        // NOTE: the linked PaymentMilestone's Status is intentionally NOT mutated —
        // see the class summary. S2's evaluator derives its Due-ness on read.

        db.LogActivityAt(
            "stage-shipped",
            $"Stage {stage.Sequence} '{stage.Name}' shipped"
            + (stage.ShipmentId is int sid ? $" (shipment #{sid})" : string.Empty),
            ("SalesOrder", stage.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        var loaded = await GetSalesOrderStagesHandler.LoadStageWithDetailsAsync(db, stage.Id, cancellationToken);
        return GetSalesOrderStagesHandler.BuildStageResponse(loaded);
    }
}
