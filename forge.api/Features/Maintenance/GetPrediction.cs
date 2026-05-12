using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record GetPredictionQuery(int Id) : IRequest<MaintenancePredictionResponseModel>;

public class GetPredictionHandler(AppDbContext db)
    : IRequestHandler<GetPredictionQuery, MaintenancePredictionResponseModel>
{
    public async Task<MaintenancePredictionResponseModel> Handle(
        GetPredictionQuery request, CancellationToken cancellationToken)
    {
        var p = await db.MaintenancePredictions
            .AsNoTracking()
            .Include(x => x.WorkCenter)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Prediction {request.Id} not found");

        string? ackName = null;
        if (p.AcknowledgedByUserId.HasValue)
        {
            ackName = await db.Users
                .Where(u => u.Id == p.AcknowledgedByUserId.Value)
                .Select(u => $"{u.LastName}, {u.FirstName}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new MaintenancePredictionResponseModel
        {
            Id = p.Id,
            WorkCenterId = p.WorkCenterId,
            WorkCenterName = p.WorkCenter.Name,
            PredictionType = p.PredictionType,
            ConfidencePercent = p.ConfidencePercent,
            PredictedFailureDate = p.PredictedFailureDate,
            RemainingUsefulLifeHours = p.RemainingUsefulLifeHours,
            ModelId = p.ModelId,
            ModelVersion = p.ModelVersion,
            Severity = p.Severity,
            Status = p.Status,
            PredictedAt = p.PredictedAt,
            AcknowledgedAt = p.AcknowledgedAt,
            AcknowledgedByName = ackName,
            PreventiveMaintenanceJobId = p.PreventiveMaintenanceJobId,
            ResolutionNotes = p.ResolutionNotes,
            WasAccurate = p.WasAccurate,
            InputFeaturesJson = p.InputFeaturesJson,
        };
    }
}
