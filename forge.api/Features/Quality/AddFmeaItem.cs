using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record AddFmeaItemCommand(int FmeaId, CreateFmeaItemRequestModel Request) : IRequest<FmeaItemResponseModel>;

public class AddFmeaItemValidator : AbstractValidator<AddFmeaItemCommand>
{
    public AddFmeaItemValidator()
    {
        RuleFor(x => x.Request.FailureMode).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Request.PotentialEffect).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Request.Severity).InclusiveBetween(1, 10);
        RuleFor(x => x.Request.Occurrence).InclusiveBetween(1, 10);
        RuleFor(x => x.Request.Detection).InclusiveBetween(1, 10);
    }
}

public class AddFmeaItemHandler(AppDbContext db)
    : IRequestHandler<AddFmeaItemCommand, FmeaItemResponseModel>
{
    public async Task<FmeaItemResponseModel> Handle(
        AddFmeaItemCommand command, CancellationToken cancellationToken)
    {
        var fmea = await db.FmeaAnalyses
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.Id == command.FmeaId, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA {command.FmeaId} not found");

        var req = command.Request;
        var nextItemNumber = fmea.Items.Count > 0 ? fmea.Items.Max(i => i.ItemNumber) + 1 : 1;

        var item = new FmeaItem
        {
            FmeaId = command.FmeaId,
            ItemNumber = nextItemNumber,
            ProcessStep = req.ProcessStep,
            Function = req.Function,
            FailureMode = req.FailureMode,
            PotentialEffect = req.PotentialEffect,
            Severity = req.Severity,
            Classification = req.Classification,
            PotentialCause = req.PotentialCause,
            Occurrence = req.Occurrence,
            CurrentPreventionControls = req.CurrentPreventionControls,
            CurrentDetectionControls = req.CurrentDetectionControls,
            Detection = req.Detection,
            RecommendedAction = req.RecommendedAction,
            ResponsibleUserId = req.ResponsibleUserId,
            TargetCompletionDate = req.TargetCompletionDate,
        };

        db.Set<FmeaItem>().Add(item);
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
        };
    }
}
