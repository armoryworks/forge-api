using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// NetSuite accounting service using OAuth 1.0a Token-Based Authentication (TBA).
/// Does not use OAuth2 — credentials are stored directly in options (ConsumerKey/Secret + TokenId/Secret).
/// </summary>
public class NetSuiteAccountingService(
    IHttpClientFactory httpClientFactory,
    IOptions<NetSuiteOptions> options,
    ILogger<NetSuiteAccountingService> logger) : IAccountingService
{
    public string ProviderId => "netsuite";
    public string ProviderName => "NetSuite";

    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) return [];

        var url = $"{opts.BaseUrl}/customer?limit=200";
        var client = CreateClient(opts, "GET", url);
        var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[NetSuite] GetCustomers failed: {Status}", response.StatusCode);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var customers = new List<AccountingCustomer>();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var c in items.EnumerateArray())
            {
                customers.Add(new AccountingCustomer(
                    ExternalId: c.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name: c.TryGetProperty("companyName", out var co) ? co.GetString() ?? "" : "",
                    Email: c.TryGetProperty("email", out var em) ? em.GetString() : null,
                    Phone: c.TryGetProperty("phone", out var ph) ? ph.GetString() : null,
                    CompanyName: c.TryGetProperty("companyName", out var cn) ? cn.GetString() : null,
                    Balance: 0m));
            }
        }

        return customers;
    }

    public async Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) return null;

        var url = $"{opts.BaseUrl}/customer/{externalId}";
        var client = CreateClient(opts, "GET", url);
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return new AccountingCustomer(
            ExternalId: externalId,
            Name: doc.RootElement.TryGetProperty("companyName", out var n) ? n.GetString() ?? "" : "",
            Email: null,
            Phone: null,
            CompanyName: null,
            Balance: 0m);
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) throw new InvalidOperationException("NetSuite not configured");

        var url = $"{opts.BaseUrl}/customer";
        var payload = JsonSerializer.Serialize(new { companyName = customer.Name, email = customer.Email, isPerson = false });
        var client = CreateClient(opts, "POST", url);
        var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[NetSuite] CreateCustomer failed: {Status}", response.StatusCode);
            throw new InvalidOperationException($"NetSuite CreateCustomer failed: {response.StatusCode}");
        }

        // NetSuite returns the new record ID in the Location header
        var location = response.Headers.Location?.ToString() ?? "";
        var id = location.Split('/').LastOrDefault() ?? $"ns-{Guid.NewGuid():N}";
        logger.LogInformation("[NetSuite] CreateCustomer({Name}) — ID {Id}", customer.Name, id);
        return id;
    }

    public Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        logger.LogWarning("[NetSuite] CreateEstimate not supported via REST record API — returning local ID");
        return Task.FromResult($"netsuite-local-{Guid.NewGuid():N}");
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) throw new InvalidOperationException("NetSuite not configured");

        var url = $"{opts.BaseUrl}/invoice";
        var payload = JsonSerializer.Serialize(new
        {
            entity = new { id = document.CustomerExternalId },
            tranDate = document.Date.ToString("MM/dd/yyyy"),
            item = new
            {
                items = document.LineItems.Select(li => new
                {
                    item = new { id = li.ItemExternalId ?? "1" },
                    description = li.Description,
                    quantity = li.Quantity,
                    rate = li.UnitPrice,
                }).ToArray(),
            },
        });

        var client = CreateClient(opts, "POST", url);
        var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("[NetSuite] CreateInvoice failed: {Status} {Body}", response.StatusCode, errBody);
            throw new InvalidOperationException($"NetSuite CreateInvoice failed: {response.StatusCode}");
        }

        var location = response.Headers.Location?.ToString() ?? "";
        var id = location.Split('/').LastOrDefault() ?? $"ns-{Guid.NewGuid():N}";
        logger.LogInformation("[NetSuite] CreateInvoice — ID {Id}", id);
        return id;
    }

    public async Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) throw new InvalidOperationException("NetSuite not configured");

        var url = $"{opts.BaseUrl}/purchaseOrder";
        var payload = JsonSerializer.Serialize(new
        {
            entity = new { id = document.CustomerExternalId },
            tranDate = document.Date.ToString("MM/dd/yyyy"),
            item = new
            {
                items = document.LineItems.Select(li => new
                {
                    item = new { id = li.ItemExternalId ?? "1" },
                    description = li.Description,
                    quantity = li.Quantity,
                    rate = li.UnitPrice,
                }).ToArray(),
            },
        });

        var client = CreateClient(opts, "POST", url);
        var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[NetSuite] CreatePurchaseOrder failed: {Status}", response.StatusCode);
            throw new InvalidOperationException($"NetSuite CreatePurchaseOrder failed: {response.StatusCode}");
        }

        var location = response.Headers.Location?.ToString() ?? "";
        return location.Split('/').LastOrDefault() ?? $"ns-{Guid.NewGuid():N}";
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        logger.LogWarning("[NetSuite] CreateTimeActivity — returning local ID");
        return Task.FromResult($"netsuite-local-{Guid.NewGuid():N}");
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (!IsConfigured(opts)) return [];

        var url = $"{opts.BaseUrl}/inventoryItem?limit=200";
        var client = CreateClient(opts, "GET", url);
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var items = new List<AccountingItem>();
        if (doc.RootElement.TryGetProperty("items", out var itemArr))
        {
            foreach (var i in itemArr.EnumerateArray())
            {
                items.Add(new AccountingItem(
                    ExternalId: i.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name: i.TryGetProperty("displayName", out var n) ? n.GetString() ?? "" : "",
                    Description: null,
                    Type: "Inventory",
                    UnitPrice: i.TryGetProperty("salesPrice", out var sp) ? sp.GetDecimal() : null,
                    PurchaseCost: i.TryGetProperty("cost", out var cost) ? cost.GetDecimal() : null,
                    Sku: i.TryGetProperty("itemId", out var sku) ? sku.GetString() : null,
                    Active: i.TryGetProperty("isInactive", out var inactive) ? !inactive.GetBoolean() : true));
            }
        }
        return items;
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        logger.LogWarning("[NetSuite] CreateItem — inventory item creation requires specific account configuration, returning local ID");
        return Task.FromResult($"netsuite-local-{Guid.NewGuid():N}");
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) =>
        Task.FromResult($"netsuite-local-{Guid.NewGuid():N}");

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
            var url = $"{opts.BaseUrl}/customer?limit=1";
            var client = CreateClient(opts, "GET", url);
            var response = await client.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct) =>
        Task.FromResult(new AccountingSyncStatus(IsConfigured(options.Value), DateTimeOffset.UtcNow, 0, 0));

    private static bool IsConfigured(NetSuiteOptions opts) =>
        !string.IsNullOrEmpty(opts.AccountId) &&
        !string.IsNullOrEmpty(opts.ConsumerKey) &&
        !string.IsNullOrEmpty(opts.TokenId);

    /// <summary>
    /// Creates an HttpClient with OAuth 1.0a TBA Authorization header.
    /// </summary>
    private HttpClient CreateClient(NetSuiteOptions opts, string method, string url)
    {
        var client = httpClientFactory.CreateClient();
        var authHeader = BuildOAuth1Header(opts, method, url);
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Builds an OAuth 1.0a HMAC-SHA256 Authorization header for NetSuite TBA.
    /// </summary>
    private static string BuildOAuth1Header(NetSuiteOptions opts, string method, string url)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");

        // OAuth params (must be sorted alphabetically for signature)
        var oauthParams = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = opts.ConsumerKey,
            ["oauth_nonce"] = nonce,
            ["oauth_signature_method"] = "HMAC-SHA256",
            ["oauth_timestamp"] = timestamp,
            ["oauth_token"] = opts.TokenId,
            ["oauth_version"] = "1.0",
        };

        // Build parameter string (sorted, URL-encoded)
        var paramString = string.Join("&", oauthParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        // Signature base string
        var baseString = $"{method.ToUpperInvariant()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(paramString)}";

        // Signing key
        var signingKey = $"{Uri.EscapeDataString(opts.ConsumerSecret)}&{Uri.EscapeDataString(opts.TokenSecret)}";

        // HMAC-SHA256 signature
        var keyBytes = Encoding.ASCII.GetBytes(signingKey);
        var dataBytes = Encoding.ASCII.GetBytes(baseString);
        string signature;
        using (var hmac = new HMACSHA256(keyBytes))
        {
            signature = Convert.ToBase64String(hmac.ComputeHash(dataBytes));
        }

        // Build Authorization header
        var realm = Uri.EscapeDataString(opts.AccountId);
        return $"OAuth realm=\"{realm}\"," +
               $"oauth_consumer_key=\"{Uri.EscapeDataString(opts.ConsumerKey)}\"," +
               $"oauth_token=\"{Uri.EscapeDataString(opts.TokenId)}\"," +
               $"oauth_signature_method=\"HMAC-SHA256\"," +
               $"oauth_timestamp=\"{timestamp}\"," +
               $"oauth_nonce=\"{nonce}\"," +
               $"oauth_version=\"1.0\"," +
               $"oauth_signature=\"{Uri.EscapeDataString(signature)}\"";
    }
}
