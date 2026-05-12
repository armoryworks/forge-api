using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class SyncQueueProcessorJob(
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    AppDbContext db,
    ILogger<SyncQueueProcessorJob> logger)
{
    private const int BatchSize = 10;

    public async Task ProcessQueueAsync(CancellationToken ct = default)
    {
        var accountingService = await providerFactory.GetActiveProviderAsync(ct);
        if (accountingService is null)
        {
            logger.LogInformation("No accounting provider configured — skipping sync queue processing");
            return;
        }

        var pending = await syncQueue.GetPendingAsync(BatchSize, ct);

        if (pending.Count == 0)
        {
            logger.LogInformation("Sync queue is empty — nothing to process");
            return;
        }

        logger.LogInformation("Processing {Count} pending sync queue entries", pending.Count);

        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();

            await syncQueue.MarkProcessingAsync(entry.Id, ct);

            try
            {
                await ProcessEntryAsync(accountingService, entry.Id, entry.EntityType, entry.EntityId, entry.Operation, entry.Payload, ct);
                await syncQueue.MarkCompletedAsync(entry.Id, ct);

                logger.LogInformation(
                    "Sync queue entry {Id} completed: {Operation} for {EntityType} {EntityId}",
                    entry.Id, entry.Operation, entry.EntityType, entry.EntityId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await syncQueue.MarkFailedAsync(entry.Id, ex.Message, ct);

                logger.LogError(ex,
                    "Sync queue entry {Id} failed: {Operation} for {EntityType} {EntityId}",
                    entry.Id, entry.Operation, entry.EntityType, entry.EntityId);
            }
        }
    }

    private async Task ProcessEntryAsync(IAccountingService accountingService, int entryId, string entityType, int entityId, string operation, string? payload, CancellationToken ct)
    {
        switch (operation)
        {
            case "CreateCustomer":
            {
                var customer = Deserialize<AccountingCustomer>(payload, entryId, operation);
                var externalId = await accountingService.CreateCustomerAsync(customer, ct);
                logger.LogInformation(
                    "Created customer in accounting provider — ExternalId: {ExternalId}", externalId);

                // Update local customer with external ID
                var localCustomer = await db.Customers.FindAsync([entityId], ct);
                if (localCustomer is not null)
                {
                    localCustomer.ExternalId = externalId;
                    localCustomer.Provider = accountingService.ProviderId;
                    await db.SaveChangesAsync(ct);
                }
                break;
            }

            case "CreateEstimate":
            {
                var document = Deserialize<AccountingDocument>(payload, entryId, operation);
                var externalId = await accountingService.CreateEstimateAsync(document, ct);
                logger.LogInformation(
                    "Created estimate in accounting provider — ExternalId: {ExternalId}", externalId);
                await UpdateJobExternalRef(accountingService, entityId, externalId, ct);
                break;
            }

            case "CreateInvoice":
            {
                var document = Deserialize<AccountingDocument>(payload, entryId, operation);
                var externalId = await accountingService.CreateInvoiceAsync(document, ct);
                logger.LogInformation(
                    "Created invoice in accounting provider — ExternalId: {ExternalId}", externalId);
                await UpdateJobExternalRef(accountingService, entityId, externalId, ct);
                break;
            }

            case "CreatePurchaseOrder":
            {
                var document = Deserialize<AccountingDocument>(payload, entryId, operation);
                var externalId = await accountingService.CreatePurchaseOrderAsync(document, ct);
                logger.LogInformation(
                    "Created purchase order in accounting provider — ExternalId: {ExternalId}", externalId);
                await UpdateJobExternalRef(accountingService, entityId, externalId, ct);
                break;
            }

            case "CreateTimeActivity":
            {
                var activity = Deserialize<AccountingTimeActivity>(payload, entryId, operation);
                var externalId = await accountingService.CreateTimeActivityAsync(activity, ct);
                logger.LogInformation(
                    "Created time activity in accounting provider — ExternalId: {ExternalId}", externalId);

                // Update local time entry with accounting reference
                var timeEntry = await db.TimeEntries.FindAsync([entityId], ct);
                if (timeEntry is not null)
                {
                    timeEntry.AccountingTimeActivityId = externalId;
                    await db.SaveChangesAsync(ct);
                }
                break;
            }

            case "CreateItem":
            {
                var item = Deserialize<AccountingItem>(payload, entryId, operation);
                var externalId = await accountingService.CreateItemAsync(item, ct);
                logger.LogInformation(
                    "Created item in accounting provider — ExternalId: {ExternalId}", externalId);

                // Update local part with external ID
                var part = await db.Parts.FindAsync([entityId], ct);
                if (part is not null)
                {
                    part.ExternalId = externalId;
                    part.ExternalRef = item.Name;
                    part.Provider = accountingService.ProviderId;
                    await db.SaveChangesAsync(ct);
                }
                break;
            }

            case "UpdateItem":
            {
                var item = Deserialize<AccountingItem>(payload, entryId, operation);
                if (item.ExternalId is null)
                    throw new InvalidOperationException($"UpdateItem requires ExternalId on entry {entryId}");

                await accountingService.UpdateItemAsync(item.ExternalId, item, ct);
                logger.LogInformation(
                    "Updated item in accounting provider — ExternalId: {ExternalId}", item.ExternalId);
                break;
            }

            case "CreateExpense":
            {
                var expense = Deserialize<AccountingExpense>(payload, entryId, operation);
                var externalId = await accountingService.CreateExpenseAsync(expense, ct);
                logger.LogInformation(
                    "Created expense in accounting provider — ExternalId: {ExternalId}", externalId);

                // Update local expense with external ID
                var localExpense = await db.Expenses.FindAsync([entityId], ct);
                if (localExpense is not null)
                {
                    localExpense.ExternalId = externalId;
                    localExpense.Provider = accountingService.ProviderId;
                    await db.SaveChangesAsync(ct);
                }
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"Unknown sync operation '{operation}' on entry {entryId}");
        }
    }

    private async Task UpdateJobExternalRef(IAccountingService accountingService, int jobId, string externalId, CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync([jobId], ct);
        if (job is not null)
        {
            job.ExternalRef = externalId;
            job.Provider = accountingService.ProviderId;
            await db.SaveChangesAsync(ct);
        }
    }

    private static T Deserialize<T>(string? payload, int entryId, string operation)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException(
                $"Sync queue entry {entryId} ({operation}) has null or empty payload");

        return JsonSerializer.Deserialize<T>(payload)
            ?? throw new InvalidOperationException(
                $"Sync queue entry {entryId} ({operation}) payload deserialized to null");
    }
}
