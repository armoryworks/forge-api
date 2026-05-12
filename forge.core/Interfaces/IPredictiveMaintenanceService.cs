using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPredictiveMaintenanceService
{
    Task<IReadOnlyList<MaintenancePrediction>> GetActivePredictionsAsync(int? workCenterId, CancellationToken ct);
    Task<MaintenancePrediction> GetPredictionAsync(int predictionId, CancellationToken ct);
    Task AcknowledgePredictionAsync(int predictionId, int userId, CancellationToken ct);
    Task ScheduleMaintenanceAsync(int predictionId, CancellationToken ct);
    Task ResolvePredictionAsync(int predictionId, string notes, CancellationToken ct);
    Task MarkFalsePositiveAsync(int predictionId, string notes, CancellationToken ct);
    Task RecordFeedbackAsync(int predictionId, RecordPredictionFeedbackRequestModel feedback, CancellationToken ct);
    Task<IReadOnlyList<MlModel>> GetModelsAsync(CancellationToken ct);
    Task<MlModelPerformanceResponseModel> GetModelPerformanceAsync(string modelId, CancellationToken ct);
    Task TriggerPredictionRunAsync(int workCenterId, CancellationToken ct);
    Task<PredictiveMaintenanceDashboardResponseModel> GetDashboardAsync(CancellationToken ct);
}
