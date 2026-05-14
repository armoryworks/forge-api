using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record TrainingScanLogRequestModel(
    ScanActionType ActionType,
    int? PartId,
    int? FromLocationId,
    int? ToLocationId,
    int Quantity,
    int? JobId,
    int? PurchaseOrderId,
    int? ShipmentId,
    bool WasSuccessful,
    string? ErrorMessage);

public record LogTrainingActionCommand(int UserId, TrainingScanLogRequestModel Data) : IRequest;

public class LogTrainingActionValidator : AbstractValidator<LogTrainingActionCommand>
{
    public LogTrainingActionValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.Data).NotNull();
        RuleFor(x => x.Data.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Data.ErrorMessage).MaximumLength(500);
    }
}

public class LogTrainingActionHandler(AppDbContext db, IClock clock)
    : IRequestHandler<LogTrainingActionCommand>
{
    public async Task Handle(LogTrainingActionCommand request, CancellationToken ct)
    {
        db.TrainingScanLogs.Add(new TrainingScanLog
        {
            UserId = request.UserId,
            ActionType = request.Data.ActionType,
            PartId = request.Data.PartId,
            FromLocationId = request.Data.FromLocationId,
            ToLocationId = request.Data.ToLocationId,
            Quantity = request.Data.Quantity,
            JobId = request.Data.JobId,
            PurchaseOrderId = request.Data.PurchaseOrderId,
            ShipmentId = request.Data.ShipmentId,
            ScannedAt = clock.UtcNow,
            WasSuccessful = request.Data.WasSuccessful,
            ErrorMessage = request.Data.ErrorMessage,
        });

        await db.SaveChangesAsync(ct);
    }
}
