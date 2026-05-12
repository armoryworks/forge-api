using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record RecordPredictionFeedbackCommand(int Id, RecordPredictionFeedbackRequestModel Request) : IRequest;

public class RecordPredictionFeedbackValidator : AbstractValidator<RecordPredictionFeedbackCommand>
{
    public RecordPredictionFeedbackValidator()
    {
        RuleFor(x => x.Request.ActualFailureDate)
            .NotNull()
            .When(x => x.Request.ActualFailureOccurred)
            .WithMessage("Actual failure date required when failure occurred");
    }
}

public class RecordPredictionFeedbackHandler(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<RecordPredictionFeedbackCommand>
{
    public async Task Handle(RecordPredictionFeedbackCommand command, CancellationToken cancellationToken)
    {
        var prediction = await db.MaintenancePredictions
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Prediction {command.Id} not found");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var req = command.Request;

        decimal? errorHours = null;
        if (req.ActualFailureOccurred && req.ActualFailureDate.HasValue)
        {
            errorHours = (decimal)Math.Abs((prediction.PredictedFailureDate - req.ActualFailureDate.Value).TotalHours);
        }

        var feedback = new PredictionFeedback
        {
            PredictionId = command.Id,
            ActualFailureOccurred = req.ActualFailureOccurred,
            ActualFailureDate = req.ActualFailureDate,
            PredictionErrorHours = errorHours,
            Notes = req.Notes,
            RecordedByUserId = userId,
        };

        prediction.WasAccurate = req.ActualFailureOccurred;

        db.PredictionFeedbacks.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);
    }
}
