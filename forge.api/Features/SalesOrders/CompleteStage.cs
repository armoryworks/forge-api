using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — mark a stage's production complete: advances Status to
/// <see cref="SalesOrderStageStatus.ReadyToShip"/> (from Planned or InProduction).
/// An already-shipped or closed stage cannot be re-completed.
/// </summary>
public record CompleteStageCommand(int StageId) : IRequest<SalesOrderStageResponseModel>;

public class CompleteStageValidator : AbstractValidator<CompleteStageCommand>
{
    public CompleteStageValidator()
    {
        RuleFor(x => x.StageId).GreaterThan(0);
    }
}

public class CompleteStageHandler(AppDbContext db)
    : IRequestHandler<CompleteStageCommand, SalesOrderStageResponseModel>
{
    public async Task<SalesOrderStageResponseModel> Handle(CompleteStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await db.SalesOrderStages
            .FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order stage {request.StageId} not found");

        if (stage.Status is SalesOrderStageStatus.Shipped or SalesOrderStageStatus.Closed)
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' is {stage.Status} and cannot be marked ready to ship.");

        stage.Status = SalesOrderStageStatus.ReadyToShip;

        db.LogActivityAt(
            "stage-completed",
            $"Stage {stage.Sequence} '{stage.Name}' marked ready to ship",
            ("SalesOrder", stage.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        var loaded = await GetSalesOrderStagesHandler.LoadStageWithDetailsAsync(db, stage.Id, cancellationToken);
        return GetSalesOrderStagesHandler.BuildStageResponse(loaded);
    }
}
