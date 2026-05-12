using Microsoft.Extensions.Diagnostics.HealthChecks;

using Forge.Core.Interfaces;

namespace Forge.Api.HealthChecks;

public class MinioHealthCheck(IStorageService storageService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // EnsureBucketExistsAsync is idempotent — creates if missing, no-ops if exists
            await storageService.EnsureBucketExistsAsync("forge-job-files", cancellationToken);
            return HealthCheckResult.Healthy("MinIO is accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MinIO is not accessible", ex);
        }
    }
}
