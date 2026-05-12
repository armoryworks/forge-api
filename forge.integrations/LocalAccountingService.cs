using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Local (standalone) accounting mode. No external accounting system is connected.
/// All data lives in the QB Engineer database. Create operations return generated IDs.
/// </summary>
public class LocalAccountingService(ILogger<LocalAccountingService> logger) : IAccountingService
{
    public string ProviderId => "local";
    public string ProviderName => "Local (Standalone)";

    public Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        logger.LogDebug("[Local] GetCustomers — standalone mode, returning empty (use app DB)");
        return Task.FromResult(new List<AccountingCustomer>());
    }

    public Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingCustomer?>(null);

    public Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        logger.LogInformation("[Local] CreateCustomer({Name}) — assigned local ID {Id}", customer.Name, id);
        return Task.FromResult(id);
    }

    public Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        logger.LogInformation("[Local] CreateEstimate — assigned local ID {Id}", id);
        return Task.FromResult(id);
    }

    public Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        logger.LogInformation("[Local] CreateInvoice — assigned local ID {Id}", id);
        return Task.FromResult(id);
    }

    public Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        logger.LogInformation("[Local] CreatePurchaseOrder — assigned local ID {Id}", id);
        return Task.FromResult(id);
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        return Task.FromResult(id);
    }

    public Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct) =>
        Task.FromResult(new List<AccountingItem>());

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        logger.LogInformation("[Local] CreateItem({Name}) — assigned local ID {Id}", item.Name, id);
        return Task.FromResult(id);
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct)
    {
        var id = $"local-{Guid.NewGuid():N}";
        return Task.FromResult(id);
    }

    public Task<List<AccountingEmployee>> GetEmployeesAsync(CancellationToken ct) =>
        Task.FromResult(new List<AccountingEmployee>());

    public Task<AccountingEmployee?> GetEmployeeAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingEmployee?>(null);

    public Task UpdateInventoryQuantityAsync(string externalItemId, decimal quantityOnHand, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<List<AccountingPayStub>> GetPayStubsAsync(string employeeExternalId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct) =>
        Task.FromResult(new List<AccountingPayStub>());

    public Task<byte[]?> GetPayStubPdfAsync(string payStubExternalId, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);

    public Task<List<AccountingTaxDocument>> GetTaxDocumentsAsync(string employeeExternalId, int? taxYear, CancellationToken ct) =>
        Task.FromResult(new List<AccountingTaxDocument>());

    public Task<byte[]?> GetTaxDocumentPdfAsync(string taxDocumentExternalId, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);

    public Task<bool> TestConnectionAsync(CancellationToken ct) => Task.FromResult(true);

    public Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct) =>
        Task.FromResult(new AccountingSyncStatus(true, DateTimeOffset.UtcNow, 0, 0));
}
