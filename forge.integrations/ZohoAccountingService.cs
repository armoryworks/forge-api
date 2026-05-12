using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public record ZohoTokenData(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public class ZohoAccountingService(
    ISystemSettingRepository settings,
    ITokenEncryptionService encryption,
    IHttpClientFactory httpClientFactory,
    IOptions<ZohoOptions> options,
    ILogger<ZohoAccountingService> logger) : IAccountingService
{
    private const string TokenKey = "zoho_oauth_token";
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    public string ProviderId => "zoho";
    public string ProviderName => "Zoho Books";

    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return [];

        var response = await client.GetAsync($"{opts.BaseApiUrl}/contacts?organization_id={opts.OrganizationId}&contact_type=customer&per_page=200", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Zoho] GetCustomers failed: {Status}", response.StatusCode);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var customers = new List<AccountingCustomer>();
        if (doc.RootElement.TryGetProperty("contacts", out var contacts))
        {
            foreach (var c in contacts.EnumerateArray())
            {
                customers.Add(new AccountingCustomer(
                    ExternalId: c.TryGetProperty("contact_id", out var id) ? id.GetString() ?? "" : "",
                    Name: c.TryGetProperty("contact_name", out var n) ? n.GetString() ?? "" : "",
                    Email: c.TryGetProperty("email", out var em) ? em.GetString() : null,
                    Phone: c.TryGetProperty("phone", out var ph) ? ph.GetString() : null,
                    CompanyName: c.TryGetProperty("company_name", out var co) ? co.GetString() : null,
                    Balance: c.TryGetProperty("outstanding_receivable_amount", out var bal) ? bal.GetDecimal() : 0m));
            }
        }

        return customers;
    }

    public async Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return null;

        var response = await client.GetAsync($"{opts.BaseApiUrl}/contacts/{externalId}?organization_id={opts.OrganizationId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("contact", out var c)) return null;
        return new AccountingCustomer(
            ExternalId: externalId,
            Name: c.TryGetProperty("contact_name", out var n) ? n.GetString() ?? "" : "",
            Email: c.TryGetProperty("email", out var em) ? em.GetString() : null,
            Phone: null,
            CompanyName: c.TryGetProperty("company_name", out var co) ? co.GetString() : null,
            Balance: 0m);
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Zoho not connected");

        var payload = new { contact_name = customer.Name, email = customer.Email, company_name = customer.CompanyName, contact_type = "customer" };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent($"JSONString={Uri.EscapeDataString(json)}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync($"{opts.BaseApiUrl}/contacts?organization_id={opts.OrganizationId}", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("contact").GetProperty("contact_id").GetString()!;
    }

    public async Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Zoho not connected");

        var payload = new
        {
            customer_id = document.CustomerExternalId,
            date = document.Date.ToString("yyyy-MM-dd"),
            reference_number = document.RefNumber,
            line_items = document.LineItems.Select(li => new
            {
                description = li.Description,
                quantity = li.Quantity,
                rate = li.UnitPrice,
                item_id = li.ItemExternalId,
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent($"JSONString={Uri.EscapeDataString(json)}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync($"{opts.BaseApiUrl}/estimates?organization_id={opts.OrganizationId}", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("estimate").GetProperty("estimate_id").GetString()!;
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Zoho not connected");

        var payload = new
        {
            customer_id = document.CustomerExternalId,
            date = document.Date.ToString("yyyy-MM-dd"),
            invoice_number = document.RefNumber,
            line_items = document.LineItems.Select(li => new
            {
                description = li.Description,
                quantity = li.Quantity,
                rate = li.UnitPrice,
                item_id = li.ItemExternalId,
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent($"JSONString={Uri.EscapeDataString(json)}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync($"{opts.BaseApiUrl}/invoices?organization_id={opts.OrganizationId}", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("invoice").GetProperty("invoice_id").GetString()!;
    }

    public async Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Zoho not connected");

        var payload = new
        {
            vendor_id = document.CustomerExternalId,
            date = document.Date.ToString("yyyy-MM-dd"),
            purchaseorder_number = document.RefNumber,
            line_items = document.LineItems.Select(li => new
            {
                description = li.Description,
                quantity = li.Quantity,
                rate = li.UnitPrice,
                item_id = li.ItemExternalId,
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent($"JSONString={Uri.EscapeDataString(json)}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync($"{opts.BaseApiUrl}/purchaseorders?organization_id={opts.OrganizationId}", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("purchaseorder").GetProperty("purchaseorder_id").GetString()!;
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        logger.LogWarning("[Zoho] CreateTimeActivity not supported in Zoho Books — returning local ID");
        return Task.FromResult($"zoho-local-{Guid.NewGuid():N}");
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return [];

        var response = await client.GetAsync($"{opts.BaseApiUrl}/items?organization_id={opts.OrganizationId}&per_page=200", ct);
        if (!response.IsSuccessStatusCode) return [];
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var items = new List<AccountingItem>();
        if (doc.RootElement.TryGetProperty("items", out var itemArr))
        {
            foreach (var i in itemArr.EnumerateArray())
            {
                items.Add(new AccountingItem(
                    ExternalId: i.TryGetProperty("item_id", out var id) ? id.GetString() : null,
                    Name: i.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Description: i.TryGetProperty("description", out var d) ? d.GetString() : null,
                    Type: i.TryGetProperty("product_type", out var pt) ? pt.GetString() ?? "Inventory" : "Inventory",
                    UnitPrice: i.TryGetProperty("rate", out var rate) ? rate.GetDecimal() : null,
                    PurchaseCost: i.TryGetProperty("purchase_rate", out var pr) ? pr.GetDecimal() : null,
                    Sku: i.TryGetProperty("sku", out var sku) ? sku.GetString() : null,
                    Active: i.TryGetProperty("status", out var status) && status.GetString() == "active"));
            }
        }
        return items;
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public async Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var (client, opts) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Zoho not connected");

        var payload = new { name = item.Name, description = item.Description, rate = item.UnitPrice ?? 0, sku = item.Sku };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent($"JSONString={Uri.EscapeDataString(json)}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync($"{opts.BaseApiUrl}/items?organization_id={opts.OrganizationId}", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("item").GetProperty("item_id").GetString()!;
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) =>
        Task.FromResult($"zoho-local-{Guid.NewGuid():N}");

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
            var (client, opts) = await GetAuthenticatedClientAsync(ct);
            if (client is null) return false;
            var response = await client.GetAsync($"{opts.BaseApiUrl}/organizations?organization_id={opts.OrganizationId}", ct);
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

    private async Task<(HttpClient? Client, ZohoOptions Opts)> GetAuthenticatedClientAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var token = await GetTokenAsync(ct);
        if (token is null) return (null, opts);

        if (token.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            token = await RefreshTokenAsync(token, ct);
            if (token is null) return (null, opts);
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token.AccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (client, opts);
    }

    private async Task<ZohoTokenData?> GetTokenAsync(CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(TokenKey, ct);
        if (setting is null) return null;
        try
        {
            var json = encryption.Decrypt(setting.Value);
            return JsonSerializer.Deserialize<ZohoTokenData>(json);
        }
        catch { return null; }
    }

    private async Task<ZohoTokenData?> RefreshTokenAsync(ZohoTokenData current, CancellationToken ct)
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
            logger.LogWarning("[Zoho] Token refresh failed: {Status}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var newToken = new ZohoTokenData(
            AccessToken: doc.RootElement.GetProperty("access_token").GetString()!,
            RefreshToken: current.RefreshToken, // Zoho reuses the same refresh token
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600));

        var json = JsonSerializer.Serialize(newToken);
        var encrypted = encryption.Encrypt(json);
        await settings.UpsertAsync(TokenKey, encrypted, "Encrypted Zoho OAuth tokens", ct);
        await settings.SaveChangesAsync(ct);
        return newToken;
    }
}
