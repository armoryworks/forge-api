using Forge.Core.Interfaces;
using Forge.Core.Models;

using Forge.Api.Capabilities;

namespace Forge.Api.Jobs;

public class MrpRunJob(IMrpService mrpService, ILogger<MrpRunJob> logger, ICapabilitySnapshotProvider capabilities)
{
    public async Task ExecuteNightlyRunAsync(CancellationToken cancellationToken = default)
    {
        // ── Capability gate (self-gating job — the VarianceWatchdogJob pattern):
        // MRP is capability-owned; when the capability is off (services /
        // construction installs) the schedule still ticks but the job is a no-op,
        // so toggling the capability takes effect without a restart.
        if (!capabilities.IsEnabled("CAP-PLAN-MRP"))
            return;

        logger.LogInformation("Starting nightly MRP run");

        try
        {
            var options = new MrpRunOptions();
            var result = await mrpService.ExecuteRunAsync(options, cancellationToken);

            logger.LogInformation("Nightly MRP run {RunNumber} completed: {PlannedOrders} planned orders, {Exceptions} exceptions",
                result.RunNumber, result.PlannedOrderCount, result.ExceptionCount);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
        {
            logger.LogWarning("Nightly MRP run skipped — another run is already in progress");
        }
    }
}
