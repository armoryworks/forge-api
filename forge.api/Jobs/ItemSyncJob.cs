using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class ItemSyncJob(
    IAccountingProviderFactory providerFactory,
    AppDbContext db,
    ILogger<ItemSyncJob> logger)
{
    public async Task SyncItemsAsync(CancellationToken ct = default)
    {
        var accountingService = await providerFactory.GetActiveProviderAsync(ct);
        if (accountingService is null)
        {
            logger.LogInformation("No accounting provider configured — skipping item sync");
            return;
        }

        logger.LogInformation("Starting bidirectional Item ↔ {Provider} Item sync", accountingService.ProviderName);

        AccountingSyncStatus syncStatus;
        try
        {
            syncStatus = await accountingService.GetSyncStatusAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check accounting connection status — skipping item sync");
            return;
        }

        if (!syncStatus.Connected)
        {
            logger.LogInformation("Accounting provider not connected — skipping item sync");
            return;
        }

        // Pull all items from accounting provider
        var qbItems = await accountingService.GetItemsAsync(ct);
        if (qbItems.Count == 0)
        {
            logger.LogInformation("No items found in accounting provider");
            return;
        }

        var qbItemsByExternalId = qbItems.Where(i => i.ExternalId is not null)
            .ToDictionary(i => i.ExternalId!, StringComparer.OrdinalIgnoreCase);

        // Get all local parts that are linked to QB
        var linkedParts = await db.Parts
            .Where(p => p.ExternalId != null && p.Provider == accountingService.ProviderId)
            .ToListAsync(ct);

        var updatedCount = 0;
        var orphanCount = 0;

        foreach (var part in linkedParts)
        {
            if (qbItemsByExternalId.TryGetValue(part.ExternalId!, out var qbItem))
            {
                // QB item exists — update local part description if QB name changed
                if (qbItem.Name != part.PartNumber)
                {
                    logger.LogInformation(
                        "QB Item {ExternalId} name changed from {Old} to {New} — local part number retained",
                        part.ExternalId, part.PartNumber, qbItem.Name);
                }

                // Sync ExternalRef (QB's display name for reference)
                part.ExternalRef = qbItem.Name;
                updatedCount++;
            }
            else
            {
                orphanCount++;
                logger.LogWarning(
                    "Orphan detected: Part {PartId} (PartNumber: {PartNumber}) has ExternalId {ExternalId} not found in QuickBooks",
                    part.Id, part.PartNumber, part.ExternalId);
            }
        }

        // Find QB items that don't exist locally — log for awareness (don't auto-create)
        var localExternalIds = linkedParts
            .Where(p => p.ExternalId is not null)
            .Select(p => p.ExternalId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmatchedQbItems = qbItems
            .Where(i => i.ExternalId is not null && !localExternalIds.Contains(i.ExternalId!))
            .ToList();

        if (unmatchedQbItems.Count > 0)
        {
            logger.LogInformation(
                "Found {Count} QB items not linked to any local part",
                unmatchedQbItems.Count);
        }

        if (updatedCount > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Item sync complete — {Updated} updated, {Orphans} orphan(s), {Unmatched} unmatched QB items",
            updatedCount, orphanCount, unmatchedQbItems.Count);
    }
}
