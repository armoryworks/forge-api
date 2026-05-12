using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockPredictiveMaintenanceService(ILogger<MockPredictiveMaintenanceService> logger) : IPredictiveMaintenanceService
{
    public Task<IReadOnlyList<MaintenancePrediction>> GetActivePredictionsAsync(int? workCenterId, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] GetActivePredictions WorkCenterId={WorkCenterId}", workCenterId);
        return Task.FromResult<IReadOnlyList<MaintenancePrediction>>([]);
    }

    public Task<MaintenancePrediction> GetPredictionAsync(int predictionId, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] GetPrediction {Id}", predictionId);
        return Task.FromResult(new MaintenancePrediction
        {
            Id = predictionId,
            PredictionType = "BearingFailure",
            ConfidencePercent = 87.5m,
            PredictedFailureDate = DateTimeOffset.UtcNow.AddDays(7),
            ModelId = "bearing_failure_rf_v1",
            ModelVersion = "1.0",
            Status = MaintenancePredictionStatus.Predicted,
            Severity = MaintenancePredictionSeverity.High,
            PredictedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
    }

    public Task AcknowledgePredictionAsync(int predictionId, int userId, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] Acknowledge {Id} by User {UserId}", predictionId, userId);
        return Task.CompletedTask;
    }

    public Task ScheduleMaintenanceAsync(int predictionId, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] ScheduleMaintenance for prediction {Id}", predictionId);
        return Task.CompletedTask;
    }

    public Task ResolvePredictionAsync(int predictionId, string notes, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] Resolve {Id}", predictionId);
        return Task.CompletedTask;
    }

    public Task MarkFalsePositiveAsync(int predictionId, string notes, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] MarkFalsePositive {Id}", predictionId);
        return Task.CompletedTask;
    }

    public Task RecordFeedbackAsync(int predictionId, RecordPredictionFeedbackRequestModel feedback, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] RecordFeedback for {Id}, FailureOccurred={Occurred}", predictionId, feedback.ActualFailureOccurred);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MlModel>> GetModelsAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] GetModels");
        return Task.FromResult<IReadOnlyList<MlModel>>([]);
    }

    public Task<MlModelPerformanceResponseModel> GetModelPerformanceAsync(string modelId, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] GetModelPerformance {ModelId}", modelId);
        return Task.FromResult(new MlModelPerformanceResponseModel
        {
            ModelId = modelId,
            Accuracy = 0.89m,
            Precision = 0.85m,
            Recall = 0.92m,
            F1Score = 0.88m,
            TotalPredictions = 150,
            TruePredictions = 134,
            FalsePredictions = 16,
            AverageLeadTimeHours = 168m,
            AccuracyTrend = [],
        });
    }

    public Task TriggerPredictionRunAsync(int workCenterId, CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] TriggerPredictionRun for WorkCenter {Id}", workCenterId);
        return Task.CompletedTask;
    }

    public Task<PredictiveMaintenanceDashboardResponseModel> GetDashboardAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockPredMaint] GetDashboard");
        return Task.FromResult(new PredictiveMaintenanceDashboardResponseModel
        {
            ActivePredictions = 5,
            CriticalPredictions = 1,
            PendingAcknowledgment = 3,
            MaintenanceScheduled = 2,
            OverallModelAccuracy = 0.89m,
            EstimatedDowntimePreventedHours = 48m,
            WorkCenterRisks = [],
            UpcomingPredictions = [],
        });
    }
}
