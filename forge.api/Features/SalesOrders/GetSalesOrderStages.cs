using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — the staged-schedule view for a sales order. Returns the editable stage
/// layer (each stage with its line allocations and attached lots) alongside the
/// advisory derived backward-scheduling timeline (the same per-line milestones
/// <see cref="GetSalesOrderScheduleHandler"/> produces, sent via MediatR) so the
/// UI can show planned-vs-derived drift. <c>IsActivated</c> is true once any
/// stage exists.
/// </summary>
public record GetSalesOrderStagesQuery(int SalesOrderId) : IRequest<SalesOrderStagesResponseModel>;

public class GetSalesOrderStagesHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<GetSalesOrderStagesQuery, SalesOrderStagesResponseModel>
{
    public async Task<SalesOrderStagesResponseModel> Handle(GetSalesOrderStagesQuery request, CancellationToken cancellationToken)
    {
        var exists = await db.SalesOrders
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SalesOrderId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        var stages = await QueryStagesWithDetails(db)
            .Where(s => s.SalesOrderId == request.SalesOrderId)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        // The advisory derived timeline — reuse the existing schedule query so the
        // planned (stage) vs derived (backward-scheduling) comparison uses one source.
        var derivedTimeline = await mediator.Send(
            new GetSalesOrderScheduleQuery(request.SalesOrderId), cancellationToken);

        return new SalesOrderStagesResponseModel(
            request.SalesOrderId,
            IsActivated: stages.Count > 0,
            Stages: stages.Select(BuildStageResponse).ToList(),
            DerivedTimeline: derivedTimeline);
    }

    /// <summary>
    /// The standard include chain for reading a stage with everything the read
    /// model needs: line allocations (+ underlying SO line's part), attached lots,
    /// the linked shipment (number) and payment milestone (name). Shared by the
    /// single-stage-returning handlers so the response is always fully populated.
    /// </summary>
    public static IQueryable<SalesOrderStage> QueryStagesWithDetails(AppDbContext db)
        => db.SalesOrderStages
            .AsNoTracking()
            .Include(s => s.Lines)
                .ThenInclude(l => l.SalesOrderLine)
                    .ThenInclude(sol => sol!.Part)
            .Include(s => s.Lots)
            .Include(s => s.Shipment)
            .Include(s => s.PaymentMilestone);

    /// <summary>
    /// Loads a single stage with the full read-model include chain, or throws 404.
    /// </summary>
    public static async Task<SalesOrderStage> LoadStageWithDetailsAsync(
        AppDbContext db, int stageId, CancellationToken cancellationToken)
        => await QueryStagesWithDetails(db).FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order stage {stageId} not found");

    /// <summary>Projects a loaded stage entity into its read model.</summary>
    public static SalesOrderStageResponseModel BuildStageResponse(SalesOrderStage stage)
        => new(
            stage.Id,
            stage.Sequence,
            stage.Name,
            stage.Status.ToString(),
            stage.PlannedProductionComplete,
            stage.PlannedShipDate,
            stage.ActualShipDate,
            stage.ShipmentId,
            stage.Shipment?.ShipmentNumber,
            stage.PaymentMilestoneId,
            stage.PaymentMilestone?.Name,
            stage.Notes,
            stage.Lines
                .OrderBy(l => l.SalesOrderLineId)
                .Select(l => new SalesOrderStageLineResponseModel(
                    l.Id,
                    l.SalesOrderLineId,
                    l.SalesOrderLine?.Part?.PartNumber,
                    l.SalesOrderLine?.Description ?? string.Empty,
                    l.Quantity))
                .ToList(),
            stage.Lots
                .OrderBy(x => x.Id)
                .Select(x => new SalesOrderStageLotResponseModel(x.Id, x.LotNumber, x.Quantity))
                .ToList());
}
