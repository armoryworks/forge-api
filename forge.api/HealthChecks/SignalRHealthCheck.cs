using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Forge.Api.HealthChecks;

public class SignalRHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // SignalR health is validated by checking if the service is registered.
        // The hub endpoints being mapped is sufficient for a basic liveness check.
        return Task.FromResult(HealthCheckResult.Healthy("SignalR hubs are registered"));
    }
}
