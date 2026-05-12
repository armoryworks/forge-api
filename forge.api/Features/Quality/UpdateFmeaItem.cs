using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateFmeaItemCommand(int FmeaId, int ItemId, UpdateFmeaItemRequestModel Request) : IRequest<FmeaItemResponseModel>;

public class UpdateFmeaItemHandler(AppDbContext db)
    : IRequestHandler<UpdateFmeaItemCommand, FmeaItemResponseModel>
{
    public async Task<FmeaItemResponseModel> Handle(
        UpdateFmeaItemCommand command, CancellationToken cancellationToken)
    {
        var item = await db.Set<FmeaItem>()
            .FirstOrDefaultAsync(i => i.Id == command.ItemId && i.FmeaId == command.FmeaId, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA item {command.ItemId} not found");

        var req = command.Request;

        if (req.ProcessStep != null) item.ProcessStep = req.ProcessStep;
        if (req.Function != null) item.Function = req.Function;
        if (req.FailureMode != null) item.FailureMode = req.FailureMode;
        if (req.PotentialEffect != null) item.PotentialEffect = req.PotentialEffect;
        if (req.Severity.HasValue) item.Severity = req.Severity.Value;
        if (req.Classification != null) item.Classification = req.Classification;
        if (req.PotentialCause != null) item.PotentialCause = req.PotentialCause;
        if (req.Occurrence.HasValue) item.Occurrence = req.Occurrence.Value;
        if (req.CurrentPreventionControls != null) item.CurrentPreventionControls = req.CurrentPreventionControls;
        if (req.CurrentDetectionControls != null) item.CurrentDetectionControls = req.CurrentDetectionControls;
        if (req.Detection.HasValue) item.Detection = req.Detection.Value;
        if (req.RecommendedAction != null) item.RecommendedAction = req.RecommendedAction;
        if (req.ResponsibleUserId.HasValue) item.ResponsibleUserId = req.ResponsibleUserId;
        if (req.TargetCompletionDate.HasValue) item.TargetCompletionDate = req.TargetCompletionDate;

        await db.SaveChangesAsync(cancellationToken);

        string? responsibleName = null;
        if (item.ResponsibleUserId.HasValue)
        {
            responsibleName = await db.Users
                .Where(u => u.Id == item.ResponsibleUserId.Value)
                .Select(u => $"{u.LastName}, {u.FirstName}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new FmeaItemResponseModel
        {
            Id = item.Id,
            ItemNumber = item.ItemNumber,
            ProcessStep = item.ProcessStep,
            Function = item.Function,
            FailureMode = item.FailureMode,
            PotentialEffect = item.PotentialEffect,
            Severity = item.Severity,
            Classification = item.Classification,
            PotentialCause = item.PotentialCause,
            Occurrence = item.Occurrence,
            CurrentPreventionControls = item.CurrentPreventionControls,
            CurrentDetectionControls = item.CurrentDetectionControls,
            Detection = item.Detection,
            Rpn = item.Severity * item.Occurrence * item.Detection,
            RecommendedAction = item.RecommendedAction,
            ResponsibleUserName = responsibleName,
            TargetCompletionDate = item.TargetCompletionDate,
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
