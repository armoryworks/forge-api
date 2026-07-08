using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — set the exact set of lots attached to a stage (PUT semantics). Attaches
/// the requested lots (<c>LotRecord.SalesOrderStageId = stageId</c>) and detaches
/// any lot currently on the stage but absent from the request.
///
/// <para><strong>Ownership check level.</strong> A lot belongs to the order when
/// its production job traces back to it: <c>lot.Job.SalesOrderLine.SalesOrderId
/// == thisOrder</c>. We validate that chain when it exists; a lot whose Job/line
/// chain resolves to a DIFFERENT order is rejected. Lots without a job→line chain
/// (e.g. a finished-goods or receipt lot with no production job) are accepted on
/// existence — they legitimately may be allocated to a shipment stage without a
/// job — so the guard rejects wrong-order lots without blocking chain-less ones.</para>
/// </summary>
public record AssignLotsToStageCommand(int StageId, List<int> LotIds)
    : IRequest<SalesOrderStageResponseModel>;

public class AssignLotsToStageValidator : AbstractValidator<AssignLotsToStageCommand>
{
    public AssignLotsToStageValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleForEach(x => x.LotIds).GreaterThan(0);
    }
}

public class AssignLotsToStageHandler(AppDbContext db)
    : IRequestHandler<AssignLotsToStageCommand, SalesOrderStageResponseModel>
{
    public async Task<SalesOrderStageResponseModel> Handle(AssignLotsToStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await db.SalesOrderStages
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order stage {request.StageId} not found");

        var requestedIds = request.LotIds.Distinct().ToList();

        var requestedLots = await db.LotRecords
            .Include(l => l.Job)
                .ThenInclude(j => j!.SalesOrderLine)
            .Where(l => requestedIds.Contains(l.Id))
            .ToListAsync(cancellationToken);

        // Every requested lot must exist.
        var missing = requestedIds.Except(requestedLots.Select(l => l.Id)).ToList();
        if (missing.Count > 0)
            throw new KeyNotFoundException($"Lot(s) not found: {string.Join(", ", missing)}");

        // Reject lots whose production job traces to a different order.
        foreach (var lot in requestedLots)
        {
            var lotOrderId = lot.Job?.SalesOrderLine?.SalesOrderId;
            if (lotOrderId is int otherOrder && otherOrder != stage.SalesOrderId)
                throw new InvalidOperationException(
                    $"Lot {lot.Id} ({lot.LotNumber}) belongs to sales order {otherOrder}, not {stage.SalesOrderId}.");
        }

        // Detach lots currently on this stage that are no longer requested.
        var currentStageLots = await db.LotRecords
            .Where(l => l.SalesOrderStageId == request.StageId)
            .ToListAsync(cancellationToken);
        foreach (var lot in currentStageLots.Where(l => !requestedIds.Contains(l.Id)))
            lot.SalesOrderStageId = null;

        // Attach the requested set.
        foreach (var lot in requestedLots)
            lot.SalesOrderStageId = request.StageId;

        db.LogActivityAt(
            "stage-lots-assigned",
            $"Set {requestedIds.Count} lot(s) on stage {stage.Sequence} '{stage.Name}'",
            ("SalesOrder", stage.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        var loaded = await GetSalesOrderStagesHandler.LoadStageWithDetailsAsync(db, stage.Id, cancellationToken);
        return GetSalesOrderStagesHandler.BuildStageResponse(loaded);
    }
}
