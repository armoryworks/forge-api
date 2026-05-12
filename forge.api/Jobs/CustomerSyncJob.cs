using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class CustomerSyncJob(
    IAccountingProviderFactory providerFactory,
    ICustomerRepository customerRepository,
    AppDbContext db,
    ILogger<CustomerSyncJob> logger)
{
    public async Task SyncCustomersAsync(CancellationToken ct = default)
    {
        var accountingService = await providerFactory.GetActiveProviderAsync(ct);
        if (accountingService is null)
        {
            logger.LogInformation("No accounting provider configured — skipping customer sync");
            return;
        }

        logger.LogInformation("Starting {Provider} → local customer sync", accountingService.ProviderName);

        var qbCustomers = await accountingService.GetCustomersAsync(ct);

        if (qbCustomers.Count == 0)
        {
            logger.LogInformation("No customers returned from accounting provider");
            return;
        }

        var updatedCount = 0;
        var createdCount = 0;

        foreach (var qbCustomer in qbCustomers)
        {
            ct.ThrowIfCancellationRequested();

            var local = await db.Customers
                .FirstOrDefaultAsync(
                    c => c.ExternalId == qbCustomer.ExternalId && c.Provider == accountingService.ProviderId,
                    ct);

            if (local is not null)
            {
                var changed = false;

                if (local.Name != qbCustomer.Name)
                {
                    local.Name = qbCustomer.Name;
                    changed = true;
                }

                if (local.Email != qbCustomer.Email)
                {
                    local.Email = qbCustomer.Email;
                    changed = true;
                }

                if (local.Phone != qbCustomer.Phone)
                {
                    local.Phone = qbCustomer.Phone;
                    changed = true;
                }

                if (local.CompanyName != qbCustomer.CompanyName)
                {
                    local.CompanyName = qbCustomer.CompanyName;
                    changed = true;
                }

                if (changed)
                {
                    updatedCount++;
                    logger.LogDebug(
                        "Updated local customer {Id} from QuickBooks ExternalId {ExternalId}",
                        local.Id, qbCustomer.ExternalId);
                }
            }
            else
            {
                var newCustomer = new Customer
                {
                    Name = qbCustomer.Name,
                    Email = qbCustomer.Email,
                    Phone = qbCustomer.Phone,
                    CompanyName = qbCustomer.CompanyName,
                    ExternalId = qbCustomer.ExternalId,
                    Provider = accountingService.ProviderId,
                    IsActive = true
                };

                await customerRepository.AddAsync(newCustomer, ct);
                createdCount++;

                logger.LogDebug(
                    "Created local customer from QuickBooks ExternalId {ExternalId} (Name: {Name})",
                    qbCustomer.ExternalId, qbCustomer.Name);
            }
        }

        await customerRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Synced {Total} customers from QuickBooks — {Created} created, {Updated} updated",
            qbCustomers.Count, createdCount, updatedCount);
    }
}
