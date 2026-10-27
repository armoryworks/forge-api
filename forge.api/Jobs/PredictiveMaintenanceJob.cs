using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Features.Maintenance;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily predictive maintenance run over every active work center. Self-gating: a no-op while
/// CAP-MAINT-PREDICTIVE is off, so toggling the capability takes effect without a restart.
/// </summary>
public class PredictiveMaintenanceJob(
    ISender mediator,
    ICapabilitySnapshotProvider capabilities,
    ILogger<PredictiveMaintenanceJob> logger)
{
    public async Task RunPredictionsAsync(CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled("CAP-MAINT-PREDICTIVE"))
            return;

        var workCenters = await mediator.Send(new TriggerAllPredictionRunsCommand(), ct);
        logger.LogInformation("Predictive maintenance checked {Count} work centers", workCenters);
    }
}
