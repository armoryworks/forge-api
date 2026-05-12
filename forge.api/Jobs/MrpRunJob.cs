using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Jobs;

public class MrpRunJob(IMrpService mrpService, ILogger<MrpRunJob> logger)
{
    public async Task ExecuteNightlyRunAsync(CancellationToken cancellationToken = default)
    {
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
