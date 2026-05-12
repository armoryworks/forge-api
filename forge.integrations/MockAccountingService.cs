using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockAccountingService : IAccountingService
{
    private readonly ILogger<MockAccountingService> _logger;

    private static readonly List<AccountingCustomer> MockCustomers =
    [
        new("MOCK-CUST-001", "Acme Corp", "acme@example.com", "555-0101", "Acme Corporation", 2500.00m),
        new("MOCK-CUST-002", "Quantum Dynamics", "info@quantum.com", "555-0102", "Quantum Dynamics LLC", 0.00m),
        new("MOCK-CUST-003", "Apex Manufacturing", "sales@apex.com", "555-0103", "Apex Manufacturing Inc", 750.00m),
        new("MOCK-CUST-004", "Meridian Systems", "contact@meridian.com", "555-0104", "Meridian Systems Corp", 0.00m),
    ];

    public MockAccountingService(ILogger<MockAccountingService> logger)
    {
        _logger = logger;
    }

    public Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetCustomers — returning {Count} customers", MockCustomers.Count);
        return Task.FromResult(MockCustomers);
    }

    public Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var customer = MockCustomers.FirstOrDefault(c => c.ExternalId == externalId);
        _logger.LogInformation("[MockAccounting] GetCustomer({ExternalId}) — {Result}", externalId, customer != null ? "found" : "not found");
        return Task.FromResult(customer);
    }

    public Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var id = $"MOCK-CUST-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreateCustomer({Name}) — assigned {ExternalId}", customer.Name, id);
        return Task.FromResult(id);
    }

    public Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        var id = $"MOCK-EST-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreateEstimate for {Customer}, {Amount:C} — assigned {Id}", document.CustomerExternalId, document.Amount, id);
        return Task.FromResult(id);
    }

    public Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var id = $"MOCK-INV-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreateInvoice for {Customer}, {Amount:C} — assigned {Id}", document.CustomerExternalId, document.Amount, id);
        return Task.FromResult(id);
    }

    public Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var id = $"MOCK-PO-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreatePurchaseOrder, {Amount:C} — assigned {Id}", document.Amount, id);
        return Task.FromResult(id);
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetPayment({ExternalId}) — returning mock payment", externalId);
        var payment = new AccountingPayment(externalId, 1000.00m, DateTimeOffset.UtcNow.AddDays(-3), "Check");
        return Task.FromResult<AccountingPayment?>(payment);
    }

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        var id = $"MOCK-TIME-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreateTimeActivity for {Employee}, {Hours}h — assigned {Id}", activity.EmployeeExternalId, activity.Hours, id);
        return Task.FromResult(id);
    }

    public Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetItems — returning mock items");
        var items = new List<AccountingItem>
        {
            new("MOCK-ITEM-001", "Widget A", "Standard widget", "NonInventory", 25.00m, 10.00m, "WDG-A", true),
            new("MOCK-ITEM-002", "Gizmo B", "Premium gizmo", "NonInventory", 75.00m, 30.00m, "GZM-B", true),
            new("MOCK-ITEM-003", "Component C", "Raw component", "NonInventory", 5.00m, 2.50m, "CMP-C", true),
        };
        return Task.FromResult(items);
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetItem({ExternalId})", externalId);
        var item = new AccountingItem(externalId, "Mock Item", "Mock description", "NonInventory", 25.00m, 10.00m, "MOCK-SKU", true);
        return Task.FromResult<AccountingItem?>(item);
    }

    public Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var id = $"MOCK-ITEM-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreateItem({Name}) — assigned {Id}", item.Name, id);
        return Task.FromResult(id);
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] UpdateItem({ExternalId}) — {Name}", externalId, item.Name);
        return Task.CompletedTask;
    }

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct)
    {
        var id = $"MOCK-EXP-{Guid.NewGuid().ToString("N")[..8]}";
        _logger.LogInformation("[MockAccounting] CreateExpense, {Amount:C} — assigned {Id}", expense.Amount, id);
        return Task.FromResult(id);
    }

    public string ProviderId => "mock";
    public string ProviderName => "Mock Accounting";

    public Task<List<AccountingEmployee>> GetEmployeesAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetEmployees — returning mock employees");
        var employees = new List<AccountingEmployee>
        {
            new("MOCK-EMP-001", "John Smith", "john@example.com", "555-0201", true),
            new("MOCK-EMP-002", "Jane Doe", "jane@example.com", "555-0202", true),
            new("MOCK-EMP-003", "Bob Wilson", "bob@example.com", "555-0203", true),
            new("MOCK-EMP-004", "Alice Chen", "alice@example.com", "555-0204", false),
        };
        return Task.FromResult(employees);
    }

    public Task<AccountingEmployee?> GetEmployeeAsync(string externalId, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetEmployee({ExternalId})", externalId);
        var emp = new AccountingEmployee(externalId, "Mock Employee", "mock@example.com", "555-0000", true);
        return Task.FromResult<AccountingEmployee?>(emp);
    }

    public Task UpdateInventoryQuantityAsync(string externalItemId, decimal quantityOnHand, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] UpdateInventoryQuantity({ExternalItemId}) — qty={Qty}", externalItemId, quantityOnHand);
        return Task.CompletedTask;
    }

    public Task<List<AccountingPayStub>> GetPayStubsAsync(string employeeExternalId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetPayStubs({ExternalId}, from={From}, to={To})", employeeExternalId, fromDate, toDate);
        var stubs = new List<AccountingPayStub>
        {
            new("MOCK-PAYSTUB-001",
                DateTimeOffset.UtcNow.AddDays(-28), DateTimeOffset.UtcNow.AddDays(-15), DateTimeOffset.UtcNow.AddDays(-14),
                3500.00m, 2612.50m,
                [
                    new("FederalTax", "Federal Income Tax", 420.00m),
                    new("StateTax", "State Income Tax", 175.00m),
                    new("SocialSecurity", "Social Security", 217.00m),
                    new("Medicare", "Medicare", 50.75m),
                    new("HealthInsurance", "Health Insurance Premium", 24.75m),
                ]),
            new("MOCK-PAYSTUB-002",
                DateTimeOffset.UtcNow.AddDays(-42), DateTimeOffset.UtcNow.AddDays(-29), DateTimeOffset.UtcNow.AddDays(-28),
                3500.00m, 2612.50m,
                [
                    new("FederalTax", "Federal Income Tax", 420.00m),
                    new("StateTax", "State Income Tax", 175.00m),
                    new("SocialSecurity", "Social Security", 217.00m),
                    new("Medicare", "Medicare", 50.75m),
                    new("HealthInsurance", "Health Insurance Premium", 24.75m),
                ]),
            new("MOCK-PAYSTUB-003",
                DateTimeOffset.UtcNow.AddDays(-56), DateTimeOffset.UtcNow.AddDays(-43), DateTimeOffset.UtcNow.AddDays(-42),
                3500.00m, 2612.50m,
                [
                    new("FederalTax", "Federal Income Tax", 420.00m),
                    new("StateTax", "State Income Tax", 175.00m),
                    new("SocialSecurity", "Social Security", 217.00m),
                    new("Medicare", "Medicare", 50.75m),
                    new("HealthInsurance", "Health Insurance Premium", 24.75m),
                ]),
        };

        if (fromDate.HasValue)
            stubs = stubs.Where(s => s.PayDate >= fromDate.Value).ToList();
        if (toDate.HasValue)
            stubs = stubs.Where(s => s.PayDate <= toDate.Value).ToList();

        return Task.FromResult(stubs);
    }

    public Task<byte[]?> GetPayStubPdfAsync(string payStubExternalId, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetPayStubPdf({ExternalId}) — no mock PDFs", payStubExternalId);
        return Task.FromResult<byte[]?>(null);
    }

    public Task<List<AccountingTaxDocument>> GetTaxDocumentsAsync(string employeeExternalId, int? taxYear, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetTaxDocuments({ExternalId}, year={Year})", employeeExternalId, taxYear);
        var docs = new List<AccountingTaxDocument>
        {
            new("MOCK-W2-2025", "W2", 2025, "QB Engineer Co."),
        };

        if (taxYear.HasValue)
            docs = docs.Where(d => d.TaxYear == taxYear.Value).ToList();

        return Task.FromResult(docs);
    }

    public Task<byte[]?> GetTaxDocumentPdfAsync(string taxDocumentExternalId, CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetTaxDocumentPdf({ExternalId}) — no mock PDFs", taxDocumentExternalId);
        return Task.FromResult<byte[]?>(null);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] TestConnection — returning true");
        return Task.FromResult(true);
    }

    public Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockAccounting] GetSyncStatus");
        var status = new AccountingSyncStatus(true, DateTimeOffset.UtcNow, 0, 0);
        return Task.FromResult(status);
    }
}
