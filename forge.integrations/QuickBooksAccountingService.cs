using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class QuickBooksAccountingService(
    IQuickBooksTokenService tokenService,
    IHttpClientFactory httpClientFactory,
    IOptions<QuickBooksOptions> options,
    ILogger<QuickBooksAccountingService> logger) : IAccountingService
{
    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var json = await QueryAsync("SELECT * FROM Customer WHERE Active = true MAXRESULTS 1000", ct);
        if (json is null) return [];

        var customers = new List<AccountingCustomer>();
        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Customer", out var arr))
        {
            foreach (var c in arr.EnumerateArray())
            {
                customers.Add(MapCustomer(c));
            }
        }

        logger.LogInformation("[QuickBooks] GetCustomers — returned {Count} customers", customers.Count);
        return customers;
    }

    public async Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var json = await QueryAsync($"SELECT * FROM Customer WHERE Id = '{externalId}'", ct);
        if (json is null) return null;

        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Customer", out var arr))
        {
            foreach (var c in arr.EnumerateArray())
            {
                return MapCustomer(c);
            }
        }

        return null;
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var payload = new
        {
            DisplayName = customer.Name,
            CompanyName = customer.CompanyName,
            PrimaryEmailAddr = customer.Email is not null ? new { Address = customer.Email } : null,
            PrimaryPhone = customer.Phone is not null ? new { FreeFormNumber = customer.Phone } : null,
        };

        var result = await PostEntityAsync("customer", payload, ct);
        var id = result?.RootElement.GetProperty("Customer").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks customer");

        logger.LogInformation("[QuickBooks] CreateCustomer({Name}) — assigned {Id}", customer.Name, id);
        return id;
    }

    public async Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        var payload = BuildDocumentPayload(document);
        var result = await PostEntityAsync("estimate", payload, ct);
        var id = result?.RootElement.GetProperty("Estimate").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks estimate");

        logger.LogInformation("[QuickBooks] CreateEstimate for {Customer}, {Amount:C} — assigned {Id}",
            document.CustomerExternalId, document.Amount, id);
        return id;
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var payload = BuildDocumentPayload(document);
        var result = await PostEntityAsync("invoice", payload, ct);
        var id = result?.RootElement.GetProperty("Invoice").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks invoice");

        logger.LogInformation("[QuickBooks] CreateInvoice for {Customer}, {Amount:C} — assigned {Id}",
            document.CustomerExternalId, document.Amount, id);
        return id;
    }

    public async Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var payload = new
        {
            VendorRef = new { value = document.CustomerExternalId },
            Line = document.LineItems.Select((li, i) => new
            {
                Id = (i + 1).ToString(),
                DetailType = "ItemBasedExpenseLineDetail",
                Amount = li.Quantity * li.UnitPrice,
                ItemBasedExpenseLineDetail = new
                {
                    ItemRef = li.ItemExternalId is not null ? new { value = li.ItemExternalId } : null,
                    Qty = li.Quantity,
                    UnitPrice = li.UnitPrice,
                },
                Description = li.Description,
            }).ToArray(),
            DocNumber = document.RefNumber,
            TxnDate = document.Date.ToString("yyyy-MM-dd"),
        };

        var result = await PostEntityAsync("purchaseorder", payload, ct);
        var id = result?.RootElement.GetProperty("PurchaseOrder").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks purchase order");

        logger.LogInformation("[QuickBooks] CreatePurchaseOrder, {Amount:C} — assigned {Id}", document.Amount, id);
        return id;
    }

    public async Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct)
    {
        var json = await QueryAsync($"SELECT * FROM Payment WHERE Id = '{externalId}'", ct);
        if (json is null) return null;

        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Payment", out var arr))
        {
            foreach (var p in arr.EnumerateArray())
            {
                return new AccountingPayment(
                    ExternalId: p.GetProperty("Id").GetString()!,
                    Amount: p.GetProperty("TotalAmt").GetDecimal(),
                    Date: DateTimeOffset.Parse(p.GetProperty("TxnDate").GetString()!),
                    Method: p.TryGetProperty("PaymentMethodRef", out var pm)
                        ? pm.GetProperty("name").GetString() ?? "Unknown"
                        : "Unknown");
            }
        }

        return null;
    }

    public async Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        var payload = new
        {
            NameOf = "Employee",
            EmployeeRef = new { value = activity.EmployeeExternalId },
            CustomerRef = activity.CustomerExternalId is not null
                ? new { value = activity.CustomerExternalId }
                : null,
            Hours = (int)activity.Hours,
            Minutes = (int)((activity.Hours - (int)activity.Hours) * 60),
            TxnDate = activity.Date.ToString("yyyy-MM-dd"),
            Description = activity.Description,
            ItemRef = activity.ServiceItemExternalId is not null
                ? new { value = activity.ServiceItemExternalId }
                : null,
        };

        var result = await PostEntityAsync("timeactivity", payload, ct);
        var id = result?.RootElement.GetProperty("TimeActivity").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks time activity");

        logger.LogInformation("[QuickBooks] CreateTimeActivity for {Employee}, {Hours}h — assigned {Id}",
            activity.EmployeeExternalId, activity.Hours, id);
        return id;
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var json = await QueryAsync("SELECT * FROM Item WHERE Active = true MAXRESULTS 1000", ct);
        if (json is null) return [];

        var items = new List<AccountingItem>();
        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Item", out var arr))
        {
            foreach (var i in arr.EnumerateArray())
            {
                items.Add(MapItem(i));
            }
        }

        logger.LogInformation("[QuickBooks] GetItems — returned {Count} items", items.Count);
        return items;
    }

    public async Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct)
    {
        var json = await QueryAsync($"SELECT * FROM Item WHERE Id = '{externalId}'", ct);
        if (json is null) return null;

        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Item", out var arr))
        {
            foreach (var i in arr.EnumerateArray())
            {
                return MapItem(i);
            }
        }

        return null;
    }

    public async Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var payload = new
        {
            Name = item.Name,
            Description = item.Description,
            Type = item.Type ?? "NonInventory",
            UnitPrice = item.UnitPrice,
            PurchaseCost = item.PurchaseCost,
            Sku = item.Sku,
            IncomeAccountRef = new { value = "1" },
            ExpenseAccountRef = new { value = "1" },
        };

        var result = await PostEntityAsync("item", payload, ct);
        var id = result?.RootElement.GetProperty("Item").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks item");

        logger.LogInformation("[QuickBooks] CreateItem({Name}) — assigned {Id}", item.Name, id);
        return id;
    }

    public async Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct)
    {
        // QB requires SyncToken for updates — fetch current first
        var current = await QueryAsync($"SELECT * FROM Item WHERE Id = '{externalId}'", ct);
        if (current is null) throw new InvalidOperationException($"QuickBooks item {externalId} not found");

        var syncToken = "0";
        if (current.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Item", out var arr))
        {
            foreach (var i in arr.EnumerateArray())
            {
                syncToken = i.GetProperty("SyncToken").GetString() ?? "0";
            }
        }

        var payload = new
        {
            Id = externalId,
            SyncToken = syncToken,
            Name = item.Name,
            Description = item.Description,
            UnitPrice = item.UnitPrice,
            PurchaseCost = item.PurchaseCost,
            Sku = item.Sku,
        };

        await PostEntityAsync("item", payload, ct);
        logger.LogInformation("[QuickBooks] UpdateItem({ExternalId}) — updated", externalId);
    }

    public async Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct)
    {
        var line = new[]
        {
            new
            {
                DetailType = "AccountBasedExpenseLineDetail",
                Amount = expense.Amount,
                AccountBasedExpenseLineDetail = new
                {
                    AccountRef = new { value = "1" },
                    CustomerRef = expense.CustomerExternalId is not null
                        ? new { value = expense.CustomerExternalId }
                        : null,
                },
                Description = expense.Description ?? expense.Category,
            },
        };

        var payload = new
        {
            PayType = "Cash",
            Line = line,
            EntityRef = expense.VendorExternalId is not null
                ? new { value = expense.VendorExternalId, type = "Vendor" }
                : null,
            TxnDate = expense.Date.ToString("yyyy-MM-dd"),
            DocNumber = expense.RefNumber,
        };

        var result = await PostEntityAsync("purchase", payload, ct);
        var id = result?.RootElement.GetProperty("Purchase").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("Failed to create QuickBooks expense");

        logger.LogInformation("[QuickBooks] CreateExpense, {Amount:C} — assigned {Id}", expense.Amount, id);
        return id;
    }

    public string ProviderId => "quickbooks";
    public string ProviderName => "QuickBooks Online";

    public async Task<List<AccountingEmployee>> GetEmployeesAsync(CancellationToken ct)
    {
        var json = await QueryAsync("SELECT * FROM Employee WHERE Active = true MAXRESULTS 1000", ct);
        if (json is null) return [];

        var employees = new List<AccountingEmployee>();
        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Employee", out var arr))
        {
            foreach (var e in arr.EnumerateArray())
            {
                employees.Add(MapEmployee(e));
            }
        }

        logger.LogInformation("[QuickBooks] GetEmployees — returned {Count} employees", employees.Count);
        return employees;
    }

    public async Task<AccountingEmployee?> GetEmployeeAsync(string externalId, CancellationToken ct)
    {
        var json = await QueryAsync($"SELECT * FROM Employee WHERE Id = '{externalId}'", ct);
        if (json is null) return null;

        if (json.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Employee", out var arr))
        {
            foreach (var e in arr.EnumerateArray())
            {
                return MapEmployee(e);
            }
        }

        return null;
    }

    public async Task UpdateInventoryQuantityAsync(string externalItemId, decimal quantityOnHand, CancellationToken ct)
    {
        // QB tracks inventory via InventoryAdjustment transactions
        var current = await QueryAsync($"SELECT * FROM Item WHERE Id = '{externalItemId}'", ct);
        if (current is null) throw new InvalidOperationException($"QuickBooks item {externalItemId} not found");

        var syncToken = "0";
        decimal currentQty = 0;
        if (current.RootElement.TryGetProperty("QueryResponse", out var qr) &&
            qr.TryGetProperty("Item", out var arr))
        {
            foreach (var i in arr.EnumerateArray())
            {
                syncToken = i.GetProperty("SyncToken").GetString() ?? "0";
                if (i.TryGetProperty("QtyOnHand", out var qty))
                    currentQty = qty.GetDecimal();
            }
        }

        var adjustment = quantityOnHand - currentQty;
        if (adjustment == 0) return;

        var payload = new
        {
            AdjDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            Line = new[]
            {
                new
                {
                    DetailType = "ItemAdjustmentLineDetail",
                    ItemAdjustmentLineDetail = new
                    {
                        ItemRef = new { value = externalItemId },
                        QtyDiff = adjustment,
                    },
                },
            },
        };

        await PostEntityAsync("inventoryadjustment", payload, ct);
        logger.LogInformation("[QuickBooks] UpdateInventoryQuantity({ExternalItemId}) — adjusted by {Adjustment}", externalItemId, adjustment);
    }

    public Task<List<AccountingPayStub>> GetPayStubsAsync(string employeeExternalId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken ct)
    {
        logger.LogWarning("QB Payroll API not yet implemented — GetPayStubsAsync");
        return Task.FromResult(new List<AccountingPayStub>());
    }

    public Task<byte[]?> GetPayStubPdfAsync(string payStubExternalId, CancellationToken ct)
    {
        logger.LogWarning("QB Payroll API not yet implemented — GetPayStubPdfAsync");
        return Task.FromResult<byte[]?>(null);
    }

    public Task<List<AccountingTaxDocument>> GetTaxDocumentsAsync(string employeeExternalId, int? taxYear, CancellationToken ct)
    {
        logger.LogWarning("QB Payroll API not yet implemented — GetTaxDocumentsAsync");
        return Task.FromResult(new List<AccountingTaxDocument>());
    }

    public Task<byte[]?> GetTaxDocumentPdfAsync(string taxDocumentExternalId, CancellationToken ct)
    {
        logger.LogWarning("QB Payroll API not yet implemented — GetTaxDocumentPdfAsync");
        return Task.FromResult<byte[]?>(null);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var json = await GetEntityAsync("companyinfo", ct);
            return json is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct)
    {
        var isConnected = await tokenService.IsConnectedAsync(ct);
        return new AccountingSyncStatus(isConnected, isConnected ? DateTimeOffset.UtcNow : null, 0, 0);
    }

    // --- Private helpers ---

    private async Task<JsonDocument?> QueryAsync(string query, CancellationToken ct)
    {
        var (client, realmId) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return null;

        var opts = options.Value;
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{opts.BaseApiUrl}/v3/company/{realmId}/query?query={encodedQuery}";

        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[QuickBooks] Query failed: {StatusCode} {Body}", response.StatusCode, body);
            return null;
        }

        return JsonDocument.Parse(body);
    }

    private async Task<JsonDocument?> GetEntityAsync(string entityPath, CancellationToken ct)
    {
        var (client, realmId) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return null;

        var opts = options.Value;
        var url = $"{opts.BaseApiUrl}/v3/company/{realmId}/{entityPath}/{realmId}";

        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[QuickBooks] GET {Entity} failed: {StatusCode}", entityPath, response.StatusCode);
            return null;
        }

        return JsonDocument.Parse(body);
    }

    private async Task<JsonDocument?> PostEntityAsync(string entity, object payload, CancellationToken ct)
    {
        var (client, realmId) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("QuickBooks not connected");

        var opts = options.Value;
        var url = $"{opts.BaseApiUrl}/v3/company/{realmId}/{entity}";

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(url, jsonContent, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[QuickBooks] POST {Entity} failed: {StatusCode} {Body}", entity, response.StatusCode, body);
            throw new InvalidOperationException($"QuickBooks API error: {response.StatusCode}");
        }

        return JsonDocument.Parse(body);
    }

    private async Task<(HttpClient? Client, string RealmId)> GetAuthenticatedClientAsync(CancellationToken ct)
    {
        var accessToken = await tokenService.GetValidAccessTokenAsync(ct);
        if (accessToken is null)
        {
            logger.LogWarning("[QuickBooks] No valid access token available");
            return (null, string.Empty);
        }

        var token = await tokenService.GetTokenAsync(ct);
        if (token is null) return (null, string.Empty);

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return (client, token.RealmId);
    }

    private static object BuildDocumentPayload(AccountingDocument document)
    {
        return new
        {
            CustomerRef = new { value = document.CustomerExternalId },
            Line = document.LineItems.Select((li, i) => new
            {
                Id = (i + 1).ToString(),
                DetailType = "SalesItemLineDetail",
                Amount = li.Quantity * li.UnitPrice,
                SalesItemLineDetail = new
                {
                    ItemRef = li.ItemExternalId is not null ? new { value = li.ItemExternalId } : null,
                    Qty = li.Quantity,
                    UnitPrice = li.UnitPrice,
                },
                Description = li.Description,
            }).ToArray(),
            DocNumber = document.RefNumber,
            TxnDate = document.Date.ToString("yyyy-MM-dd"),
        };
    }

    private static AccountingItem MapItem(JsonElement i)
    {
        return new AccountingItem(
            ExternalId: i.GetProperty("Id").GetString()!,
            Name: i.GetProperty("Name").GetString()!,
            Description: i.TryGetProperty("Description", out var desc) ? desc.GetString() : null,
            Type: i.TryGetProperty("Type", out var type) ? type.GetString() : null,
            UnitPrice: i.TryGetProperty("UnitPrice", out var price) ? price.GetDecimal() : null,
            PurchaseCost: i.TryGetProperty("PurchaseCost", out var cost) ? cost.GetDecimal() : null,
            Sku: i.TryGetProperty("Sku", out var sku) ? sku.GetString() : null,
            Active: !i.TryGetProperty("Active", out var active) || active.GetBoolean());
    }

    private static AccountingEmployee MapEmployee(JsonElement e)
    {
        var name = e.TryGetProperty("DisplayName", out var dn) ? dn.GetString()! :
            $"{(e.TryGetProperty("GivenName", out var gn) ? gn.GetString() : "")} {(e.TryGetProperty("FamilyName", out var fn) ? fn.GetString() : "")}".Trim();

        return new AccountingEmployee(
            ExternalId: e.GetProperty("Id").GetString()!,
            DisplayName: name,
            Email: e.TryGetProperty("PrimaryEmailAddr", out var email)
                ? email.GetProperty("Address").GetString()
                : null,
            Phone: e.TryGetProperty("PrimaryPhone", out var phone)
                ? phone.GetProperty("FreeFormNumber").GetString()
                : null,
            Active: !e.TryGetProperty("Active", out var active) || active.GetBoolean());
    }

    private static AccountingCustomer MapCustomer(JsonElement c)
    {
        return new AccountingCustomer(
            ExternalId: c.GetProperty("Id").GetString()!,
            Name: c.GetProperty("DisplayName").GetString()!,
            Email: c.TryGetProperty("PrimaryEmailAddr", out var email)
                ? email.GetProperty("Address").GetString()
                : null,
            Phone: c.TryGetProperty("PrimaryPhone", out var phone)
                ? phone.GetProperty("FreeFormNumber").GetString()
                : null,
            CompanyName: c.TryGetProperty("CompanyName", out var company)
                ? company.GetString()
                : null,
            Balance: c.TryGetProperty("Balance", out var balance)
                ? balance.GetDecimal()
                : 0m);
    }
}
