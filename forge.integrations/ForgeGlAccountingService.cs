using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Provider shim that lists the native Forge Accounting Suite in the
/// <see cref="IAccountingProviderFactory"/> provider list
/// (ACCOUNTING_SUITE_PLAN §5.5). It is intentionally a <b>thin shim</b>, NOT the
/// GL boundary.
/// <para>
/// Per the §3 review correction (finding <c>gap-4</c>), <see cref="IAccountingService"/>
/// is an external-sync / CRM contract (CreateEstimate / TestConnection /
/// GetSyncStatus) with zero ledger primitives. The native GL is reached only
/// through <see cref="IPostingEngine"/> + the read/config interfaces. Selecting
/// "forge-native" as the active provider means "the books live inside Forge" —
/// there is no external system to sync to — so the sync surface here throws
/// <see cref="NotSupportedException"/> directing callers to the real seam, while
/// the connectivity probes report a healthy, sync-free local provider.
/// </para>
/// <para>
/// Registering this provider makes it <i>selectable</i>; it does NOT make it the
/// active provider. The active provider stays whatever the <c>accounting_provider</c>
/// system setting names (default standalone / "local"). CAP-ACCT-FULLGL remains
/// OFF — the native GL stays dark until a later phase turns it on.
/// </para>
/// </summary>
public sealed class ForgeGlAccountingService(ILogger<ForgeGlAccountingService> logger) : IAccountingService
{
    /// <summary>Stable provider id surfaced in the provider list and the <c>accounting_provider</c> setting.</summary>
    public const string Id = "forge-native";

    public string ProviderId => Id;
    public string ProviderName => "Forge Accounting Suite";

    // --- The native GL is not reached through this CRM/sync contract. Every
    // ledger-relevant operation must go through IPostingEngine + the read/config
    // interfaces; calling them here is a wiring bug, surfaced loudly. ---
    private static NotSupportedException NotTheSeam(string op) => new(
        $"ForgeGlAccountingService.{op} is not supported: the native Forge Accounting Suite is reached " +
        "through IPostingEngine + the GL read/config interfaces, not the IAccountingService external-sync contract. " +
        "This type is only a provider-list shim (ACCOUNTING_SUITE_PLAN §5.5 / §3 gap-4).");

    public Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct) => throw NotTheSeam(nameof(GetCustomersAsync));
    public Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct) => throw NotTheSeam(nameof(GetCustomerAsync));
    public Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct) => throw NotTheSeam(nameof(CreateCustomerAsync));

    public Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct) => throw NotTheSeam(nameof(CreateEstimateAsync));
    public Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct) => throw NotTheSeam(nameof(CreateInvoiceAsync));
    public Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct) => throw NotTheSeam(nameof(CreatePurchaseOrderAsync));

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) => throw NotTheSeam(nameof(GetPaymentAsync));
    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct) => throw NotTheSeam(nameof(CreateTimeActivityAsync));

    public Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct) => throw NotTheSeam(nameof(GetItemsAsync));
    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) => throw NotTheSeam(nameof(GetItemAsync));
    public Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct) => throw NotTheSeam(nameof(CreateItemAsync));
    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) => throw NotTheSeam(nameof(UpdateItemAsync));

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) => throw NotTheSeam(nameof(CreateExpenseAsync));

    public Task<List<AccountingEmployee>> GetEmployeesAsync(CancellationToken ct) => throw NotTheSeam(nameof(GetEmployeesAsync));
    public Task<AccountingEmployee?> GetEmployeeAsync(string externalId, CancellationToken ct) => throw NotTheSeam(nameof(GetEmployeeAsync));

    public Task UpdateInventoryQuantityAsync(string externalItemId, decimal quantityOnHand, CancellationToken ct) => throw NotTheSeam(nameof(UpdateInventoryQuantityAsync));

    public Task<List<AccountingPayStub>> GetPayStubsAsync(string employeeExternalId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct) => throw NotTheSeam(nameof(GetPayStubsAsync));
    public Task<byte[]?> GetPayStubPdfAsync(string payStubExternalId, CancellationToken ct) => throw NotTheSeam(nameof(GetPayStubPdfAsync));
    public Task<List<AccountingTaxDocument>> GetTaxDocumentsAsync(string employeeExternalId, int? taxYear, CancellationToken ct) => throw NotTheSeam(nameof(GetTaxDocumentsAsync));
    public Task<byte[]?> GetTaxDocumentPdfAsync(string taxDocumentExternalId, CancellationToken ct) => throw NotTheSeam(nameof(GetTaxDocumentPdfAsync));

    // --- Connectivity probes: a native-in-Forge provider is always "connected"
    // and has no external sync to report. These let the provider be listed /
    // selected / health-checked without erroring. ---
    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        logger.LogDebug("[Forge-native] TestConnection — native GL is in-process; always connected.");
        return Task.FromResult(true);
    }

    public Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct) =>
        // Native GL is the system of record; there is nothing to sync to an
        // external system. Report connected with no pending/failed items.
        Task.FromResult(new AccountingSyncStatus(true, DateTimeOffset.UtcNow, 0, 0));
}
