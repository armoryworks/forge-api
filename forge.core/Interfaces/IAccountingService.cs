using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAccountingService
{
    Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct);
    Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct);
    Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct);

    Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct);
    Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct);
    Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct);

    Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct);
    Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct);

    // Item (Part) sync
    Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct);
    Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct);
    Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct);
    Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct);

    // Expense (Purchase) sync
    Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct);

    // Employee sync
    Task<List<AccountingEmployee>> GetEmployeesAsync(CancellationToken ct);
    Task<AccountingEmployee?> GetEmployeeAsync(string externalId, CancellationToken ct);

    // Inventory quantity sync
    Task UpdateInventoryQuantityAsync(string externalItemId, decimal quantityOnHand, CancellationToken ct);

    // Payroll visibility
    Task<List<AccountingPayStub>> GetPayStubsAsync(string employeeExternalId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct);
    Task<byte[]?> GetPayStubPdfAsync(string payStubExternalId, CancellationToken ct);
    Task<List<AccountingTaxDocument>> GetTaxDocumentsAsync(string employeeExternalId, int? taxYear, CancellationToken ct);
    Task<byte[]?> GetTaxDocumentPdfAsync(string taxDocumentExternalId, CancellationToken ct);

    Task<bool> TestConnectionAsync(CancellationToken ct);
    Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct);

    /// <summary>
    /// Provider identifier (e.g., "quickbooks", "xero", "mock").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable provider name.
    /// </summary>
    string ProviderName { get; }
}
