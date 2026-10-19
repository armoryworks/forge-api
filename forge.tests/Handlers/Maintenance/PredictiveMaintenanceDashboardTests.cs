using FluentAssertions;

using Forge.Api.Features.Maintenance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Maintenance;

public class PredictiveMaintenanceDashboardTests
{
    private static MaintenancePrediction Prediction(int workCenterId, MaintenancePredictionStatus status, MaintenancePredictionSeverity severity, decimal confidence, int days, bool accurate = false) => new()
    {
        WorkCenterId = workCenterId, PredictionType = $"{severity}-{status}", ConfidencePercent = confidence,
        PredictedFailureDate = DateTimeOffset.UtcNow.AddDays(days), ModelId = "m", ModelVersion = "1",
        Status = status, Severity = severity, PredictedAt = DateTimeOffset.UtcNow, WasAccurate = accurate,
    };

    [Fact]
    public async Task Dashboard_counts_come_from_the_prediction_rows()
    {
        using var db = TestDbContextFactory.Create();
        var press = new WorkCenter { Name = "IMM-220", Code = "IMM" };
        var mill = new WorkCenter { Name = "VMC", Code = "VMC" };
        db.WorkCenters.AddRange(press, mill);
        await db.SaveChangesAsync();

        var resolved = Prediction(press.Id, MaintenancePredictionStatus.Resolved, MaintenancePredictionSeverity.High, 80m, 5, accurate: true);
        db.MaintenancePredictions.AddRange(
            Prediction(press.Id, MaintenancePredictionStatus.Predicted, MaintenancePredictionSeverity.Critical, 90m, 3),
            Prediction(press.Id, MaintenancePredictionStatus.MaintenanceScheduled, MaintenancePredictionSeverity.Medium, 70m, 9),
            Prediction(mill.Id, MaintenancePredictionStatus.Acknowledged, MaintenancePredictionSeverity.Low, 60m, 1),
            Prediction(mill.Id, MaintenancePredictionStatus.FalsePositive, MaintenancePredictionSeverity.High, 64m, 2),
            resolved);
        await db.SaveChangesAsync();
        db.PredictionFeedbacks.Add(new PredictionFeedback { PredictionId = resolved.Id, ActualFailureOccurred = true });
        await db.SaveChangesAsync();

        var dash = await new GetPredictiveMaintenanceDashboardHandler(db)
            .Handle(new GetPredictiveMaintenanceDashboardQuery(), CancellationToken.None);

        dash.ActivePredictions.Should().Be(3);
        dash.CriticalPredictions.Should().Be(1);
        dash.PendingAcknowledgment.Should().Be(1);
        dash.MaintenanceScheduled.Should().Be(1);
        dash.OverallModelAccuracy.Should().Be(50m);
        dash.EstimatedDowntimePreventedHours.Should().Be(0m);
        dash.WorkCenterRisks.Select(r => r.WorkCenterName).Should().Equal("IMM-220", "VMC");
        dash.WorkCenterRisks[0].RiskScore.Should().Be(90m);
        dash.UpcomingPredictions.Select(p => p.ConfidencePercent).Should().Equal(60m, 90m, 70m);
    }

    [Fact]
    public async Task Empty_install_shows_zeroes()
    {
        using var db = TestDbContextFactory.Create();

        var dash = await new GetPredictiveMaintenanceDashboardHandler(db)
            .Handle(new GetPredictiveMaintenanceDashboardQuery(), CancellationToken.None);

        dash.ActivePredictions.Should().Be(0);
        dash.OverallModelAccuracy.Should().Be(0m);
        dash.WorkCenterRisks.Should().BeEmpty();
    }
}
