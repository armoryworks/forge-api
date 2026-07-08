using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — "Activate staged schedule": seed the editable stage layer from the
/// advisory derived backward-scheduling timeline.
///
/// <para><strong>Seeding decision.</strong> The derived model is <em>per SO line</em>
/// (<see cref="BackwardSchedulingService"/> computes production-complete / ship
/// dates for each line independently); it is NOT a sequence of order-level
/// production milestones. There is therefore no natural "one stage per derived
/// milestone" mapping. We seed a single order-level <c>"Production → Ship"</c>
/// stage that covers the whole order, with:
/// <list type="bullet">
/// <item>PlannedProductionComplete = the latest ProductionCompleteBy across all lines</item>
/// <item>PlannedShipDate = the latest ShipBy across all lines</item>
/// </list>
/// (the latest date = when the entire order is ready to ship together), and stage
/// lines allocating each SO line at its full ordered quantity. This is a sensible
/// editable starting point: the user then splits it into multiple stages and
/// re-allocates quantities. Seeding is one-shot — re-activating an order that
/// already has stages is rejected.</para>
/// </summary>
public record ActivateStagedScheduleCommand(int SalesOrderId) : IRequest<SalesOrderStagesResponseModel>;

public class ActivateStagedScheduleHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<ActivateStagedScheduleCommand, SalesOrderStagesResponseModel>
{
    public async Task<SalesOrderStagesResponseModel> Handle(ActivateStagedScheduleCommand request, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        var alreadyActivated = await db.SalesOrderStages
            .AnyAsync(s => s.SalesOrderId == request.SalesOrderId, cancellationToken);
        if (alreadyActivated)
            throw new InvalidOperationException(
                "The staged schedule is already activated for this order. Edit the existing stages instead.");

        if (order.Lines.Count == 0)
            throw new InvalidOperationException("Cannot activate a staged schedule for an order with no lines.");

        // Derive the advisory timeline (per line) and roll it up to the order level.
        var derived = await mediator.Send(new GetSalesOrderScheduleQuery(request.SalesOrderId), cancellationToken);

        // Roll the per-line derived dates up to the order level: the latest date =
        // when the whole order is ready to ship together.
        var productionCompleteDates = derived
            .Where(m => m.ProductionCompleteBy.HasValue)
            .Select(m => m.ProductionCompleteBy!.Value)
            .ToList();
        DateTimeOffset? plannedProductionComplete =
            productionCompleteDates.Count > 0 ? productionCompleteDates.Max() : null;

        var shipDates = derived
            .Where(m => m.ShipBy.HasValue)
            .Select(m => m.ShipBy!.Value)
            .ToList();
        DateTimeOffset? plannedShipDate = shipDates.Count > 0 ? shipDates.Max() : null;

        var stage = new SalesOrderStage
        {
            SalesOrderId = request.SalesOrderId,
            Sequence = 1,
            Name = "Production → Ship",
            Status = Core.Enums.SalesOrderStageStatus.Planned,
            PlannedProductionComplete = plannedProductionComplete,
            PlannedShipDate = plannedShipDate,
        };
        foreach (var line in order.Lines.OrderBy(l => l.LineNumber))
        {
            stage.Lines.Add(new SalesOrderStageLine
            {
                SalesOrderLineId = line.Id,
                Quantity = line.Quantity,
            });
        }
        db.SalesOrderStages.Add(stage);

        db.LogActivityAt(
            "staged-schedule-activated",
            $"Staged schedule activated: seeded 1 stage '{stage.Name}' covering {order.Lines.Count} line(s)",
            ("SalesOrder", request.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetSalesOrderStagesQuery(request.SalesOrderId), cancellationToken);
    }
}
