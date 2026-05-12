using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public record SageTokenData(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public class SageAccountingService(
    ISystemSettingRepository settings,
    ITokenEncryptionService encryption,
    IHttpClientFactory httpClientFactory,
    IOptions<SageOptions> options,
    ILogger<SageAccountingService> logger) : IAccountingService
{
    private const string TokenKey = "sage_oauth_token";
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    public string ProviderId => "sage";
    public string ProviderName => "Sage Business Cloud";

    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) return [];

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/contacts?contact_type_ids[]=CUSTOMER&items_per_page=200", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Sage] GetCustomers failed: {Status}", response.StatusCode);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var customers = new List<AccountingCustomer>();
        if (doc.RootElement.TryGetProperty("$items", out var items))
        {
            foreach (var c in items.EnumerateArray())
            {
                customers.Add(new AccountingCustomer(
                    ExternalId: c.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name: c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Email: c.TryGetProperty("email", out var em) ? em.GetString() : null,
                    Phone: null,
                    CompanyName: c.TryGetProperty("name", out var co) ? co.GetString() : null,
                    Balance: 0m));
            }
        }

        return customers;
    }

    public async Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) return null;

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/contacts/{externalId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return new AccountingCustomer(
            ExternalId: externalId,
            Name: doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Email: null,
            Phone: null,
            CompanyName: null,
            Balance: 0m);
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Sage not connected");

        var payload = new { contact = new { name = customer.Name, email = customer.Email, contact_type_ids = new[] { "CUSTOMER" } } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/contacts", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        logger.LogWarning("[Sage] CreateEstimate — Sage Business Cloud does not natively support quotes via API v3.1, returning local ID");
        return Task.FromResult($"sage-local-{Guid.NewGuid():N}");
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Sage not connected");

        var payload = new
        {
            sales_invoice = new
            {
                contact_id = document.CustomerExternalId,
                date = document.Date.ToString("yyyy-MM-dd"),
                line_items = document.LineItems.Select(li => new
                {
                    description = li.Description,
                    quantity = li.Quantity,
                    unit_price = li.UnitPrice,
                }).ToArray(),
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/sales_invoices", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Sage not connected");

        var payload = new
        {
            purchase_invoice = new
            {
                contact_id = document.CustomerExternalId,
                date = document.Date.ToString("yyyy-MM-dd"),
                line_items = document.LineItems.Select(li => new
                {
                    description = li.Description,
                    quantity = li.Quantity,
                    unit_price = li.UnitPrice,
                }).ToArray(),
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/purchase_invoices", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        logger.LogWarning("[Sage] CreateTimeActivity not supported — returning local ID");
        return Task.FromResult($"sage-local-{Guid.NewGuid():N}");
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) return [];

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/products?items_per_page=200", ct);
        if (!response.IsSuccessStatusCode) return [];
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var items = new List<AccountingItem>();
        if (doc.RootElement.TryGetProperty("$items", out var itemArr))
        {
            foreach (var i in itemArr.EnumerateArray())
            {
                items.Add(new AccountingItem(
                    ExternalId: i.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name: i.TryGetProperty("item_code", out var code) ? code.GetString() ?? "" : "",
                    Description: i.TryGetProperty("description", out var d) ? d.GetString() : null,
                    Type: "Product",
                    UnitPrice: i.TryGetProperty("sales_prices", out var sp) &&
                               sp.EnumerateArray().FirstOrDefault().TryGetProperty("price", out var pr)
                        ? pr.GetDecimal() : null,
                    PurchaseCost: null,
                    Sku: i.TryGetProperty("item_code", out var sku) ? sku.GetString() : null,
                    Active: i.TryGetProperty("active", out var active) && active.GetBoolean()));
            }
        }
        return items;
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public async Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var client = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Sage not connected");

        var payload = new { product = new { item_code = item.Sku ?? item.Name, description = item.Description ?? item.Name } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/products", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) =>
        Task.FromResult($"sage-local-{Guid.NewGuid():N}");

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
        try
        {
            var client = await GetAuthenticatedClientAsync(ct);
            if (client is null) return false;
            var response = await client.GetAsync($"{options.Value.BaseApiUrl}/business", ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<AccountingSyncStatus> GetSyncStatusAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var connected = token is not null && token.AccessTokenExpiresAt > DateTimeOffset.UtcNow;
        return new AccountingSyncStatus(connected, connected ? DateTimeOffset.UtcNow : null, 0, 0);
    }

    private async Task<HttpClient?> GetAuthenticatedClientAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        if (token is null) return null;

        if (token.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            token = await RefreshTokenAsync(token, ct);
            if (token is null) return null;
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<SageTokenData?> GetTokenAsync(CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(TokenKey, ct);
        if (setting is null) return null;
        try
        {
            var json = encryption.Decrypt(setting.Value);
            return JsonSerializer.Deserialize<SageTokenData>(json);
        }
        catch { return null; }
    }

    private async Task<SageTokenData?> RefreshTokenAsync(SageTokenData current, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", opts.ClientId),
            new KeyValuePair<string, string>("client_secret", opts.ClientSecret),
            new KeyValuePair<string, string>("refresh_token", current.RefreshToken),
        ]);

        var response = await client.PostAsync(opts.TokenEndpoint, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[Sage] Token refresh failed: {Status}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var newToken = new SageTokenData(
            AccessToken: doc.RootElement.GetProperty("access_token").GetString()!,
            RefreshToken: doc.RootElement.GetProperty("refresh_token").GetString()!,
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600));

        var json = JsonSerializer.Serialize(newToken);
        var encrypted = encryption.Encrypt(json);
        await settings.UpsertAsync(TokenKey, encrypted, "Encrypted Sage OAuth tokens", ct);
        await settings.SaveChangesAsync(ct);
        return newToken;
    }
}
