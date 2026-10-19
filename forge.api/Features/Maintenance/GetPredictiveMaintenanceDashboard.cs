using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record GetPredictiveMaintenanceDashboardQuery : IRequest<PredictiveMaintenanceDashboardResponseModel>;

public class GetPredictiveMaintenanceDashboardHandler(AppDbContext db)
    : IRequestHandler<GetPredictiveMaintenanceDashboardQuery, PredictiveMaintenanceDashboardResponseModel>
{
    private const int UpcomingLimit = 10;

    private static readonly MaintenancePredictionStatus[] ActiveStatuses =
    [
        MaintenancePredictionStatus.Predicted,
        MaintenancePredictionStatus.Acknowledged,
        MaintenancePredictionStatus.MaintenanceScheduled,
    ];

    public async Task<PredictiveMaintenanceDashboardResponseModel> Handle(
        GetPredictiveMaintenanceDashboardQuery request, CancellationToken cancellationToken)
    {
        var active = await db.MaintenancePredictions
            .AsNoTracking()
            .Where(p => ActiveStatuses.Contains(p.Status))
            .Select(p => new
            {
                p.Id,
                p.WorkCenterId,
                WorkCenterName = p.WorkCenter.Name,
                p.PredictionType,
                p.ConfidencePercent,
                p.PredictedFailureDate,
                p.Severity,
                p.Status,
            })
            .ToListAsync(cancellationToken);

        var feedbackIds = db.PredictionFeedbacks.Select(f => f.PredictionId);
        var judged = await db.MaintenancePredictions
            .AsNoTracking()
            .Where(p => p.Status == MaintenancePredictionStatus.FalsePositive || feedbackIds.Contains(p.Id))
            .Select(p => p.WasAccurate)
            .ToListAsync(cancellationToken);

        var accuracy = judged.Count == 0
            ? 0m
            : Math.Round(judged.Count(accurate => accurate) * 100m / judged.Count, 1);

        var risks = active
            .GroupBy(p => new { p.WorkCenterId, p.WorkCenterName })
            .Select(g =>
            {
                var worst = g.OrderByDescending(p => p.Severity).ThenByDescending(p => p.ConfidencePercent).First();
                return new WorkCenterRiskScoreResponseModel
                {
                    WorkCenterId = g.Key.WorkCenterId,
                    WorkCenterName = g.Key.WorkCenterName,
                    RiskScore = Math.Round(g.Max(p => p.ConfidencePercent * SeverityWeight(p.Severity)), 1),
                    HighestSeverityPrediction = worst.PredictionType,
                    NextPredictedFailure = g.Min(p => p.PredictedFailureDate),
                };
            })
            .OrderByDescending(r => r.RiskScore)
            .ToList();

        var upcoming = active
            .OrderBy(p => p.PredictedFailureDate)
            .Take(UpcomingLimit)
            .Select(p => new UpcomingPredictionResponseModel
            {
                Id = p.Id,
                WorkCenterName = p.WorkCenterName,
                PredictionType = p.PredictionType,
                ConfidencePercent = p.ConfidencePercent,
                PredictedFailureDate = p.PredictedFailureDate,
                Severity = p.Severity.ToString(),
            })
            .ToList();

        return new PredictiveMaintenanceDashboardResponseModel
        {
            ActivePredictions = active.Count,
            CriticalPredictions = active.Count(p => p.Severity == MaintenancePredictionSeverity.Critical),
            PendingAcknowledgment = active.Count(p => p.Status == MaintenancePredictionStatus.Predicted),
            MaintenanceScheduled = active.Count(p => p.Status == MaintenancePredictionStatus.MaintenanceScheduled),
            OverallModelAccuracy = accuracy,
            EstimatedDowntimePreventedHours = 0m,
            WorkCenterRisks = risks,
            UpcomingPredictions = upcoming,
        };
    }

    private static decimal SeverityWeight(MaintenancePredictionSeverity severity) => severity switch
    {
        MaintenancePredictionSeverity.Critical => 1m,
        MaintenancePredictionSeverity.High => 0.75m,
        MaintenancePredictionSeverity.Medium => 0.5m,
        _ => 0.25m,
    };
}
