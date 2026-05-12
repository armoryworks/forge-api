using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public record XeroTokenData(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public class XeroAccountingService(
    ISystemSettingRepository settings,
    ITokenEncryptionService encryption,
    IHttpClientFactory httpClientFactory,
    IOptions<XeroOptions> options,
    ILogger<XeroAccountingService> logger) : IAccountingService
{
    private const string TokenKey = "xero_oauth_token";
    private const string TenantKey = "xero_tenant_id";
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    public string ProviderId => "xero";
    public string ProviderName => "Xero";

    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return [];

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/Contacts?where=IsCustomer%3D%3Dtrue&pageSize=100", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Xero] GetContacts failed: {Status}", response.StatusCode);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var customers = new List<AccountingCustomer>();
        if (doc.RootElement.TryGetProperty("Contacts", out var contacts))
        {
            foreach (var c in contacts.EnumerateArray())
            {
                var phone = c.TryGetProperty("Phones", out var phones)
                    ? phones.EnumerateArray().FirstOrDefault().TryGetProperty("PhoneNumber", out var ph) ? ph.GetString() : null
                    : null;
                customers.Add(new AccountingCustomer(
                    ExternalId: c.GetProperty("ContactID").GetString()!,
                    Name: c.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                    Email: c.TryGetProperty("EmailAddress", out var e) ? e.GetString() : null,
                    Phone: phone,
                    CompanyName: c.TryGetProperty("Name", out var co) ? co.GetString() : null,
                    Balance: c.TryGetProperty("Balances", out var bal) &&
                             bal.TryGetProperty("AccountsReceivable", out var ar) &&
                             ar.TryGetProperty("Outstanding", out var outstanding)
                        ? outstanding.GetDecimal() : 0m));
            }
        }

        return customers;
    }

    public async Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return null;

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/Contacts/{externalId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("Contacts", out var contacts)) return null;
        var c = contacts.EnumerateArray().FirstOrDefault();
        if (c.ValueKind == JsonValueKind.Undefined) return null;
        return new AccountingCustomer(
            ExternalId: c.GetProperty("ContactID").GetString()!,
            Name: c.TryGetProperty("Name", out var nm) ? nm.GetString() ?? "" : "",
            Email: null,
            Phone: null,
            CompanyName: null,
            Balance: 0m);
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Xero not connected");

        var payload = new { Contacts = new[] { new { Name = customer.Name, EmailAddress = customer.Email, IsCustomer = true } } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/Contacts", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Contacts").EnumerateArray().First().GetProperty("ContactID").GetString()!;
    }

    public async Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Xero not connected");

        var payload = new
        {
            Quotes = new[] { new
            {
                Contact = new { ContactID = document.CustomerExternalId },
                LineItems = document.LineItems.Select(li => new
                {
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitAmount = li.UnitPrice,
                    ItemCode = li.ItemExternalId,
                }).ToArray(),
                Date = document.Date.ToString("yyyy-MM-dd"),
                QuoteNumber = document.RefNumber,
            } },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{options.Value.BaseApiUrl}/Quotes", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Quotes").EnumerateArray().First().GetProperty("QuoteID").GetString()!;
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Xero not connected");

        var payload = new
        {
            Invoices = new[] { new
            {
                Type = "ACCREC",
                Contact = new { ContactID = document.CustomerExternalId },
                LineItems = document.LineItems.Select(li => new
                {
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitAmount = li.UnitPrice,
                    ItemCode = li.ItemExternalId,
                }).ToArray(),
                Date = document.Date.ToString("yyyy-MM-dd"),
                InvoiceNumber = document.RefNumber,
                Status = "DRAFT",
            } },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{options.Value.BaseApiUrl}/Invoices", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Invoices").EnumerateArray().First().GetProperty("InvoiceID").GetString()!;
    }

    public async Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Xero not connected");

        var payload = new
        {
            PurchaseOrders = new[] { new
            {
                Contact = new { ContactID = document.CustomerExternalId },
                LineItems = document.LineItems.Select(li => new
                {
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitAmount = li.UnitPrice,
                }).ToArray(),
                Date = document.Date.ToString("yyyy-MM-dd"),
                PurchaseOrderNumber = document.RefNumber,
                Status = "DRAFT",
            } },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{options.Value.BaseApiUrl}/PurchaseOrders", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("PurchaseOrders").EnumerateArray().First().GetProperty("PurchaseOrderID").GetString()!;
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        logger.LogWarning("[Xero] CreateTimeActivity not supported in Xero API — logging locally");
        return Task.FromResult($"xero-local-{Guid.NewGuid():N}");
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) return [];

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/Items", ct);
        if (!response.IsSuccessStatusCode) return [];
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var items = new List<AccountingItem>();
        if (doc.RootElement.TryGetProperty("Items", out var itemArr))
        {
            foreach (var i in itemArr.EnumerateArray())
            {
                items.Add(new AccountingItem(
                    ExternalId: i.GetProperty("ItemID").GetString()!,
                    Name: i.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                    Description: i.TryGetProperty("Description", out var d) ? d.GetString() : null,
                    Type: "Inventory",
                    UnitPrice: i.TryGetProperty("SalesDetails", out var sd) && sd.TryGetProperty("UnitPrice", out var up) ? up.GetDecimal() : null,
                    PurchaseCost: i.TryGetProperty("PurchaseDetails", out var pd) && pd.TryGetProperty("UnitPrice", out var pu) ? pu.GetDecimal() : null,
                    Sku: i.TryGetProperty("Code", out var code) ? code.GetString() : null,
                    Active: !i.TryGetProperty("IsTrackedAsInventory", out var tracked) || tracked.GetBoolean()));
            }
        }
        return items;
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public async Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var (client, _) = await GetAuthenticatedClientAsync(ct);
        if (client is null) throw new InvalidOperationException("Xero not connected");

        var payload = new { Items = new[] { new { Code = item.Sku ?? item.Name, Name = item.Name, Description = item.Description } } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{options.Value.BaseApiUrl}/Items", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Items").EnumerateArray().First().GetProperty("ItemID").GetString()!;
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) =>
        Task.FromResult($"xero-local-{Guid.NewGuid():N}");

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
            var (client, _) = await GetAuthenticatedClientAsync(ct);
            if (client is null) return false;
            var response = await client.GetAsync($"{options.Value.BaseApiUrl}/Organisation", ct);
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

    private async Task<(HttpClient? Client, string TenantId)> GetAuthenticatedClientAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        if (token is null) return (null, string.Empty);

        if (token.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            token = await RefreshTokenAsync(token, ct);
            if (token is null) return (null, string.Empty);
        }

        var tenantSetting = await settings.FindByKeyAsync(TenantKey, ct);
        var tenantId = tenantSetting?.Value ?? string.Empty;

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        client.DefaultRequestHeaders.Add("Xero-tenant-id", tenantId);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (client, tenantId);
    }

    private async Task<XeroTokenData?> GetTokenAsync(CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(TokenKey, ct);
        if (setting is null) return null;
        try
        {
            var json = encryption.Decrypt(setting.Value);
            return JsonSerializer.Deserialize<XeroTokenData>(json);
        }
        catch { return null; }
    }

    private async Task<XeroTokenData?> RefreshTokenAsync(XeroTokenData current, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", current.RefreshToken),
        ]);

        var response = await client.PostAsync(opts.TokenEndpoint, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[Xero] Token refresh failed: {Status}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var newToken = new XeroTokenData(
            AccessToken: doc.RootElement.GetProperty("access_token").GetString()!,
            RefreshToken: doc.RootElement.GetProperty("refresh_token").GetString()!,
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32()));

        var json = JsonSerializer.Serialize(newToken);
        var encrypted = encryption.Encrypt(json);
        await settings.UpsertAsync(TokenKey, encrypted, "Encrypted Xero OAuth tokens", ct);
        await settings.SaveChangesAsync(ct);
        return newToken;
    }
}
