using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Wave accounting service using GraphQL API with personal access token.
/// Uses Bearer token authentication — no OAuth flow required.
/// </summary>
public class WaveAccountingService(
    IHttpClientFactory httpClientFactory,
    IOptions<WaveOptions> options,
    ILogger<WaveAccountingService> logger) : IAccountingService
{
    public string ProviderId => "wave";
    public string ProviderName => "Wave";

    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) return [];

        var query = $$"""
            {
              "query": "query { business(id: \"{{opts.BusinessId}}\") { customers(page: 1, pageSize: 50) { edges { node { id name email phone } } } } }"
            }
            """;

        var doc = await ExecuteGraphQlAsync(query, opts, ct);
        if (doc is null) return [];

        var customers = new List<AccountingCustomer>();
        try
        {
            var edges = doc.RootElement
                .GetProperty("data")
                .GetProperty("business")
                .GetProperty("customers")
                .GetProperty("edges");

            foreach (var edge in edges.EnumerateArray())
            {
                var node = edge.GetProperty("node");
                customers.Add(new AccountingCustomer(
                    ExternalId: node.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name: node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Email: node.TryGetProperty("email", out var em) ? em.GetString() : null,
                    Phone: node.TryGetProperty("phone", out var ph) ? ph.GetString() : null,
                    CompanyName: null,
                    Balance: 0m));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Wave] GetCustomers — failed to parse GraphQL response");
        }

        return customers;
    }

    public Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        // Wave GraphQL does not support fetching a single customer by ID in the same way — return null
        return Task.FromResult<AccountingCustomer?>(null);
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) throw new InvalidOperationException("Wave not configured");

        var mutation = $$"""
            {
              "query": "mutation { customerCreate(input: { businessId: \"{{opts.BusinessId}}\", name: \"{{EscapeGraphQl(customer.Name)}}\", email: \"{{EscapeGraphQl(customer.Email ?? "")}}\" }) { customer { id } } }"
            }
            """;

        var doc = await ExecuteGraphQlAsync(mutation, opts, ct);
        if (doc is null) throw new InvalidOperationException("Wave CreateCustomer returned null");
        return doc.RootElement.GetProperty("data").GetProperty("customerCreate").GetProperty("customer").GetProperty("id").GetString()!;
    }

    public Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        logger.LogWarning("[Wave] CreateEstimate not supported via Wave GraphQL API — returning local ID");
        return Task.FromResult($"wave-local-{Guid.NewGuid():N}");
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) throw new InvalidOperationException("Wave not configured");

        var items = string.Join(", ", document.LineItems.Select(li =>
            $"{{ productId: null, description: \\\"{EscapeGraphQl(li.Description)}\\\", quantity: {li.Quantity}, unitPrice: {li.UnitPrice} }}"));

        var mutation = $$"""
            {
              "query": "mutation { invoiceCreate(input: { businessId: \"{{opts.BusinessId}}\", customerId: \"{{document.CustomerExternalId}}\", items: [{{items}}] }) { invoice { id } } }"
            }
            """;

        var doc = await ExecuteGraphQlAsync(mutation, opts, ct);
        if (doc is null) throw new InvalidOperationException("Wave CreateInvoice returned null");
        return doc.RootElement.GetProperty("data").GetProperty("invoiceCreate").GetProperty("invoice").GetProperty("id").GetString()!;
    }

    public Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        logger.LogWarning("[Wave] CreatePurchaseOrder not supported — returning local ID");
        return Task.FromResult($"wave-local-{Guid.NewGuid():N}");
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        logger.LogWarning("[Wave] CreateTimeActivity not supported — returning local ID");
        return Task.FromResult($"wave-local-{Guid.NewGuid():N}");
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) return [];

        var query = $$"""
            {
              "query": "query { business(id: \"{{opts.BusinessId}}\") { products(page: 1, pageSize: 50) { edges { node { id name description defaultSellPrice } } } } }"
            }
            """;

        var doc = await ExecuteGraphQlAsync(query, opts, ct);
        if (doc is null) return [];

        var items = new List<AccountingItem>();
        try
        {
            var edges = doc.RootElement
                .GetProperty("data")
                .GetProperty("business")
                .GetProperty("products")
                .GetProperty("edges");

            foreach (var edge in edges.EnumerateArray())
            {
                var node = edge.GetProperty("node");
                items.Add(new AccountingItem(
                    ExternalId: node.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name: node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Description: node.TryGetProperty("description", out var d) ? d.GetString() : null,
                    Type: "Product",
                    UnitPrice: node.TryGetProperty("defaultSellPrice", out var sp) ? sp.GetDecimal() : null,
                    PurchaseCost: null,
                    Sku: null,
                    Active: true));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Wave] GetItems — failed to parse GraphQL response");
        }

        return items;
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public async Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) throw new InvalidOperationException("Wave not configured");

        var mutation = $$"""
            {
              "query": "mutation { productCreate(input: { businessId: \"{{opts.BusinessId}}\", name: \"{{EscapeGraphQl(item.Name)}}\", description: \"{{EscapeGraphQl(item.Description ?? "")}}\" }) { product { id } } }"
            }
            """;

        var doc = await ExecuteGraphQlAsync(mutation, opts, ct);
        if (doc is null) throw new InvalidOperationException("Wave CreateItem returned null");
        return doc.RootElement.GetProperty("data").GetProperty("productCreate").GetProperty("product").GetProperty("id").GetString()!;
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) =>
        Task.FromResult($"wave-local-{Guid.NewGuid():N}");

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

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) return false;
        try
        {
            var query = $$"""{ "query": "query { user { id } }" }""";
            var doc = await ExecuteGraphQlAsync(query, opts, ct);
            return doc is not null && doc.RootElement.TryGetProperty("data", out _);
        }
        catch { return false; }
    }

    public Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct) =>
        Task.FromResult(new AccountingSyncStatus(IsConfigured(options.Value), DateTimeOffset.UtcNow, 0, 0));

    private async Task<JsonDocument?> ExecuteGraphQlAsync(string queryJson, WaveOptions opts, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
            var content = new StringContent(queryJson, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(opts.GraphQlUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("[Wave] GraphQL request failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }
            return JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Wave] GraphQL request exception");
            return null;
        }
    }

    private static bool IsConfigured(WaveOptions opts) =>
        !string.IsNullOrEmpty(opts.AccessToken) && !string.IsNullOrEmpty(opts.BusinessId);

    private static string EscapeGraphQl(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
