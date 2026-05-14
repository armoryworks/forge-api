using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record GetPredictionsQuery(int? WorkCenterId, MaintenancePredictionSeverity? Severity, MaintenancePredictionStatus? Status)
    : IRequest<List<MaintenancePredictionResponseModel>>;

public class GetPredictionsHandler(AppDbContext db)
    : IRequestHandler<GetPredictionsQuery, List<MaintenancePredictionResponseModel>>
{
    public async Task<List<MaintenancePredictionResponseModel>> Handle(
        GetPredictionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.MaintenancePredictions
            .AsNoTracking()
            .Include(p => p.WorkCenter)
            .AsQueryable();

        if (request.WorkCenterId.HasValue)
            query = query.Where(p => p.WorkCenterId == request.WorkCenterId.Value);
        if (request.Severity.HasValue)
            query = query.Where(p => p.Severity == request.Severity.Value);
        if (request.Status.HasValue)
            query = query.Where(p => p.Status == request.Status.Value);

        var predictions = await query
            .OrderByDescending(p => p.PredictedAt)
            .ToListAsync(cancellationToken);

        var ackUserIds = predictions
            .Where(p => p.AcknowledgedByUserId.HasValue)
            .Select(p => p.AcknowledgedByUserId!.Value)
            .Distinct()
            .ToList();

        var userNames = ackUserIds.Count > 0
            ? await db.Users
                .Where(u => ackUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken)
            : new Dictionary<int, string>();

        return predictions.Select(p => new MaintenancePredictionResponseModel
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
            AcknowledgedByName = p.AcknowledgedByUserId.HasValue && userNames.TryGetValue(p.AcknowledgedByUserId.Value, out var n) ? n : null,
            PreventiveMaintenanceJobId = p.PreventiveMaintenanceJobId,
            ResolutionNotes = p.ResolutionNotes,
            WasAccurate = p.WasAccurate,
            InputFeaturesJson = p.InputFeaturesJson,
        }).ToList();
    }
}
