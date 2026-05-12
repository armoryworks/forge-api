using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class OrphanDetectionJob(
    IAccountingProviderFactory providerFactory,
    AppDbContext db,
    ILogger<OrphanDetectionJob> logger)
{
    public async Task DetectOrphansAsync(CancellationToken ct = default)
    {
        var accountingService = await providerFactory.GetActiveProviderAsync(ct);
        if (accountingService is null)
        {
            logger.LogInformation("No accounting provider configured — skipping orphan detection");
            return;
        }

        logger.LogInformation("Starting orphan detection for {Provider}-linked customers", accountingService.ProviderName);

        var localLinked = await db.Customers
            .Where(c => c.ExternalId != null && c.Provider == accountingService.ProviderId)
            .Select(c => new { c.Id, c.ExternalId })
            .ToListAsync(ct);

        if (localLinked.Count == 0)
        {
            logger.LogInformation("No QuickBooks-linked local customers found — skipping orphan check");
            return;
        }

        var qbCustomers = await accountingService.GetCustomersAsync(ct);
        var qbExternalIds = qbCustomers
            .Select(c => c.ExternalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphanCount = 0;

        foreach (var local in localLinked)
        {
            if (!qbExternalIds.Contains(local.ExternalId!))
            {
                orphanCount++;
                logger.LogWarning(
                    "Orphan detected: Customer {Id} has ExternalId {ExternalId} not found in QuickBooks",
                    local.Id, local.ExternalId);
            }
        }

        if (orphanCount == 0)
        {
            logger.LogInformation(
                "Orphan detection complete — all {Count} linked customers confirmed in QuickBooks",
                localLinked.Count);
        }
        else
        {
            logger.LogWarning(
                "Orphan detection complete — {OrphanCount} orphan(s) detected out of {Total} linked customers",
                orphanCount, localLinked.Count);
        }
    }
}
