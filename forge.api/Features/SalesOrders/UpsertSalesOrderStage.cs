using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// S4c — create or update a single sales-order stage. On create
/// (<see cref="StageId"/> null) the stage is appended to the order identified by
/// <see cref="SalesOrderId"/>; on update the addressed stage's editable fields are
/// replaced. Stages are the user-owned editable layer over the advisory derived
/// timeline.
/// </summary>
public record UpsertSalesOrderStageCommand(
    int? StageId,
    int? SalesOrderId,
    string Name,
    int Sequence,
    DateTimeOffset? PlannedProductionComplete,
    DateTimeOffset? PlannedShipDate,
    string? Notes,
    int? PaymentMilestoneId) : IRequest<SalesOrderStageResponseModel>;

public class UpsertSalesOrderStageValidator : AbstractValidator<UpsertSalesOrderStageCommand>
{
    public UpsertSalesOrderStageValidator()
    {
        RuleFor(x => x)
            .Must(x => x.StageId is > 0 || x.SalesOrderId is > 0)
            .WithMessage("Either a stage id (update) or a sales order id (create) is required");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sequence).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class UpsertSalesOrderStageHandler(AppDbContext db)
    : IRequestHandler<UpsertSalesOrderStageCommand, SalesOrderStageResponseModel>
{
    public async Task<SalesOrderStageResponseModel> Handle(UpsertSalesOrderStageCommand request, CancellationToken cancellationToken)
    {
        // Light referential check: a linked milestone must exist so the stage
        // can never point at a dangling id.
        if (request.PaymentMilestoneId is int milestoneId)
        {
            var milestoneExists = await db.PaymentMilestones
                .AnyAsync(m => m.Id == milestoneId, cancellationToken);
            if (!milestoneExists)
                throw new KeyNotFoundException($"Payment milestone {milestoneId} not found");
        }

        SalesOrderStage stage;
        string action;
        string description;

        if (request.StageId is int stageId)
        {
            stage = await db.SalesOrderStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken)
                ?? throw new KeyNotFoundException($"Sales order stage {stageId} not found");

            var changed = new List<string>();
            if (stage.Name != request.Name) changed.Add(nameof(stage.Name));
            if (stage.Sequence != request.Sequence) changed.Add(nameof(stage.Sequence));
            if (stage.PlannedProductionComplete != request.PlannedProductionComplete) changed.Add(nameof(stage.PlannedProductionComplete));
            if (stage.PlannedShipDate != request.PlannedShipDate) changed.Add(nameof(stage.PlannedShipDate));
            if (stage.Notes != request.Notes) changed.Add(nameof(stage.Notes));
            if (stage.PaymentMilestoneId != request.PaymentMilestoneId) changed.Add(nameof(stage.PaymentMilestoneId));

            stage.Name = request.Name;
            stage.Sequence = request.Sequence;
            stage.PlannedProductionComplete = request.PlannedProductionComplete;
            stage.PlannedShipDate = request.PlannedShipDate;
            stage.Notes = request.Notes;
            stage.PaymentMilestoneId = request.PaymentMilestoneId;

            action = "stage-updated";
            description = changed.Count == 0
                ? $"Stage '{stage.Name}' saved with no field changes"
                : $"Updated stage '{stage.Name}': {string.Join(", ", changed)}";
        }
        else
        {
            var salesOrderId = request.SalesOrderId!.Value;
            var orderExists = await db.SalesOrders.AnyAsync(s => s.Id == salesOrderId, cancellationToken);
            if (!orderExists)
                throw new KeyNotFoundException($"Sales order {salesOrderId} not found");

            stage = new SalesOrderStage
            {
                SalesOrderId = salesOrderId,
                Name = request.Name,
                Sequence = request.Sequence,
                PlannedProductionComplete = request.PlannedProductionComplete,
                PlannedShipDate = request.PlannedShipDate,
                Notes = request.Notes,
                PaymentMilestoneId = request.PaymentMilestoneId,
            };
            db.SalesOrderStages.Add(stage);

            action = "stage-created";
            description = $"Added stage {request.Sequence} '{request.Name}'";
        }

        db.LogActivityAt(action, description, ("SalesOrder", stage.SalesOrderId));

        await db.SaveChangesAsync(cancellationToken);

        var loaded = await GetSalesOrderStagesHandler.LoadStageWithDetailsAsync(db, stage.Id, cancellationToken);
        return GetSalesOrderStagesHandler.BuildStageResponse(loaded);
    }
}
