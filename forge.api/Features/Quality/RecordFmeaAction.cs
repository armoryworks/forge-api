using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record RecordFmeaActionCommand(int FmeaId, int ItemId, RecordFmeaActionRequestModel Request) : IRequest<FmeaItemResponseModel>;

public class RecordFmeaActionValidator : AbstractValidator<RecordFmeaActionCommand>
{
    public RecordFmeaActionValidator()
    {
        RuleFor(x => x.Request.ActionTaken).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Request.RevisedSeverity).InclusiveBetween(1, 10).When(x => x.Request.RevisedSeverity.HasValue);
        RuleFor(x => x.Request.RevisedOccurrence).InclusiveBetween(1, 10).When(x => x.Request.RevisedOccurrence.HasValue);
        RuleFor(x => x.Request.RevisedDetection).InclusiveBetween(1, 10).When(x => x.Request.RevisedDetection.HasValue);
    }
}

public class RecordFmeaActionHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RecordFmeaActionCommand, FmeaItemResponseModel>
{
    public async Task<FmeaItemResponseModel> Handle(
        RecordFmeaActionCommand command, CancellationToken cancellationToken)
    {
        var item = await db.Set<FmeaItem>()
            .FirstOrDefaultAsync(i => i.Id == command.ItemId && i.FmeaId == command.FmeaId, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA item {command.ItemId} not found");

        var req = command.Request;
        item.ActionTaken = req.ActionTaken;
        item.ActionCompletedAt = clock.UtcNow;
        item.RevisedSeverity = req.RevisedSeverity;
        item.RevisedOccurrence = req.RevisedOccurrence;
        item.RevisedDetection = req.RevisedDetection;

        await db.SaveChangesAsync(cancellationToken);

        return new FmeaItemResponseModel
        {
            Id = item.Id,
            ItemNumber = item.ItemNumber,
            FailureMode = item.FailureMode,
            PotentialEffect = item.PotentialEffect,
            Severity = item.Severity,
            Occurrence = item.Occurrence,
            Detection = item.Detection,
            Rpn = item.Severity * item.Occurrence * item.Detection,
            ActionTaken = item.ActionTaken,
            ActionCompletedAt = item.ActionCompletedAt,
            RevisedSeverity = item.RevisedSeverity,
            RevisedOccurrence = item.RevisedOccurrence,
            RevisedDetection = item.RevisedDetection,
            RevisedRpn = item.RevisedSeverity.HasValue && item.RevisedOccurrence.HasValue && item.RevisedDetection.HasValue
                ? item.RevisedSeverity.Value * item.RevisedOccurrence.Value * item.RevisedDetection.Value
                : null,
            CapaId = item.CapaId,
        };
    }
}
