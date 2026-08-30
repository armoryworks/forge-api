using Forge.Core.Interfaces;

namespace Forge.Api.Jobs;

/// <summary>
/// Drives the opt-in health report to Armory Works: enroll, collect the decision,
/// then heartbeat.
///
/// The job is always scheduled and does nothing at all unless the operator has
/// accepted the agreement — the consent gate lives inside
/// <see cref="ITelemetryReporter"/> so there is one place it can be got wrong rather
/// than several. Scheduling it unconditionally means switching monitoring on takes
/// effect on the next tick without a restart.
///
/// Never throws. A vendor's monitoring endpoint being unreachable is not a reason for
/// a Hangfire job on a customer's ERP to start failing and retrying.
/// </summary>
public class TelemetryHeartbeatJob(ITelemetryReporter reporter, ILogger<TelemetryHeartbeatJob> logger)
{
    public async Task ReportAsync(CancellationToken ct = default)
    {
        try
        {
            await reporter.RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Telemetry report failed; will retry on the next schedule");
        }
    }
}
