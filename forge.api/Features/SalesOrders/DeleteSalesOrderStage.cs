using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — soft-delete a stage. The schema's FK cascades (stage_lines CASCADE, lot
/// FK SET NULL) only fire on a hard DELETE, so a soft delete must reproduce them
/// in code: soft-delete the stage, soft-delete its <c>SalesOrderStageLine</c>
/// rows, and null out <c>SalesOrderStageId</c> on the lots that were attached.
/// </summary>
public record DeleteSalesOrderStageCommand(int StageId) : IRequest;

public class DeleteSalesOrderStageValidator : AbstractValidator<DeleteSalesOrderStageCommand>
{
    public DeleteSalesOrderStageValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
    }
}

public class DeleteSalesOrderStageHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteSalesOrderStageCommand>
{
    public async Task Handle(DeleteSalesOrderStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await db.SalesOrderStages
            .Include(s => s.Lines)
            .Include(s => s.Lots)
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order stage {request.StageId} not found");

        var now = clock.UtcNow;

        // Soft-delete the stage and its line allocations (DeletedBy auto-stamped).
        stage.DeletedAt = now;
        foreach (var line in stage.Lines)
            line.DeletedAt = now;

        // Detach lots (mirror the schema's ON DELETE SET NULL, which a soft delete
        // won't trigger).
        var detachedLots = stage.Lots.Count;
        foreach (var lot in stage.Lots)
            lot.SalesOrderStageId = null;

        db.LogActivityAt(
            "stage-deleted",
            $"Deleted stage {stage.Sequence} '{stage.Name}' "
            + $"({stage.Lines.Count} line(s), {detachedLots} lot(s) detached)",
            ("SalesOrder", stage.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
