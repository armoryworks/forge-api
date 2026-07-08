using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — replace the SO-line quantity allocations on a stage (PUT semantics).
///
/// <para><strong>Over-allocation guard (critical).</strong> For every SO line, the
/// sum of that line's allocated quantity across ALL stages of the order must not
/// exceed the line's ordered quantity. The check sums the OTHER stages' live
/// allocations (soft-deleted stage lines and stages are excluded by the global
/// query filter) and adds this request's quantities before comparing.</para>
/// </summary>
public record AssignStageLinesCommand(int StageId, List<StageLineAllocationModel> Lines)
    : IRequest<SalesOrderStageResponseModel>;

public class AssignStageLinesValidator : AbstractValidator<AssignStageLinesCommand>
{
    public AssignStageLinesValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.SalesOrderLineId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
        });
    }
}

public class AssignStageLinesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<AssignStageLinesCommand, SalesOrderStageResponseModel>
{
    public async Task<SalesOrderStageResponseModel> Handle(AssignStageLinesCommand request, CancellationToken cancellationToken)
    {
        var stage = await db.SalesOrderStages
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order stage {request.StageId} not found");

        var orderLines = await db.SalesOrderLines
            .AsNoTracking()
            .Where(l => l.SalesOrderId == stage.SalesOrderId)
            .Select(l => new { l.Id, l.Quantity })
            .ToListAsync(cancellationToken);
        var orderLineQty = orderLines.ToDictionary(l => l.Id, l => l.Quantity);

        // Collapse duplicate line ids in the request into one requested quantity.
        var requested = request.Lines
            .GroupBy(l => l.SalesOrderLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var lineId in requested.Keys)
        {
            if (!orderLineQty.ContainsKey(lineId))
                throw new InvalidOperationException(
                    $"Sales order line {lineId} does not belong to this order.");
        }

        // Live allocations on every OTHER stage of this order, per SO line.
        var otherAllocations = await db.SalesOrderStageLines
            .Where(sl => sl.SalesOrderStageId != stage.Id
                && sl.SalesOrderStage.SalesOrderId == stage.SalesOrderId)
            .GroupBy(sl => sl.SalesOrderLineId)
            .Select(g => new { LineId = g.Key, Sum = g.Sum(x => x.Quantity) })
            .ToListAsync(cancellationToken);
        var otherByLine = otherAllocations.ToDictionary(x => x.LineId, x => x.Sum);

        foreach (var (lineId, requestedQty) in requested)
        {
            var otherSum = otherByLine.TryGetValue(lineId, out var s) ? s : 0m;
            var ordered = orderLineQty[lineId];
            if (otherSum + requestedQty > ordered)
                throw new InvalidOperationException(
                    $"Allocating {requestedQty} of sales order line {lineId} would exceed its ordered "
                    + $"quantity {ordered} (already {otherSum} allocated on other stages).");
        }

        // Replace: soft-delete the current stage's lines, then add the new set.
        var now = clock.UtcNow;
        foreach (var existing in stage.Lines)
            existing.DeletedAt = now;

        foreach (var (lineId, qty) in requested)
        {
            db.SalesOrderStageLines.Add(new SalesOrderStageLine
            {
                SalesOrderStageId = stage.Id,
                SalesOrderLineId = lineId,
                Quantity = qty,
            });
        }

        db.LogActivityAt(
            "stage-lines-assigned",
            $"Assigned {requested.Count} line allocation(s) to stage {stage.Sequence} '{stage.Name}'",
            ("SalesOrder", stage.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        var loaded = await GetSalesOrderStagesHandler.LoadStageWithDetailsAsync(db, stage.Id, cancellationToken);
        return GetSalesOrderStagesHandler.BuildStageResponse(loaded);
    }
}
