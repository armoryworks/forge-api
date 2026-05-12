using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public record FreshBooksTokenData(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public class FreshBooksAccountingService(
    ISystemSettingRepository settings,
    ITokenEncryptionService encryption,
    IHttpClientFactory httpClientFactory,
    IOptions<FreshBooksOptions> options,
    ILogger<FreshBooksAccountingService> logger) : IAccountingService
{
    private const string TokenKey = "freshbooks_oauth_token";
    private const string AccountIdKey = "freshbooks_account_id";
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    public string ProviderId => "freshbooks";
    public string ProviderName => "FreshBooks";

    public async Task<List<AccountingCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) return [];

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/users/clients", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[FreshBooks] GetCustomers failed: {Status}", response.StatusCode);
            return [];
        }

        var doc = JsonDocument.Parse(body);
        var customers = new List<AccountingCustomer>();
        if (doc.RootElement.TryGetProperty("response", out var resp) &&
            resp.TryGetProperty("result", out var result) &&
            result.TryGetProperty("clients", out var clients))
        {
            foreach (var c in clients.EnumerateArray())
            {
                customers.Add(new AccountingCustomer(
                    ExternalId: c.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : "",
                    Name: c.TryGetProperty("organization", out var org) && !string.IsNullOrEmpty(org.GetString())
                        ? org.GetString()!
                        : $"{(c.TryGetProperty("fname", out var fn) ? fn.GetString() ?? "" : "")} {(c.TryGetProperty("lname", out var ln) ? ln.GetString() ?? "" : "")}".Trim(),
                    Email: c.TryGetProperty("email", out var em) ? em.GetString() : null,
                    Phone: null,
                    CompanyName: c.TryGetProperty("organization", out var org2) ? org2.GetString() : null,
                    Balance: c.TryGetProperty("balance", out var bal) && bal.TryGetProperty("amount", out var amt)
                        ? decimal.TryParse(amt.GetString(), out var d) ? d : 0m : 0m));
            }
        }

        return customers;
    }

    public async Task<AccountingCustomer?> GetCustomerAsync(string externalId, CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) return null;

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/users/clients/{externalId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("response", out var resp) ||
            !resp.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("client", out var c)) return null;

        return new AccountingCustomer(
            ExternalId: externalId,
            Name: c.TryGetProperty("organization", out var org) ? org.GetString() ?? "" : "",
            Email: c.TryGetProperty("email", out var em) ? em.GetString() : null,
            Phone: null,
            CompanyName: null,
            Balance: 0m);
    }

    public async Task<string> CreateCustomerAsync(AccountingCustomer customer, CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) throw new InvalidOperationException("FreshBooks not connected");

        var payload = new { client = new { email = customer.Email, organization = customer.Name } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/users/clients", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("response").GetProperty("result").GetProperty("client").GetProperty("id").GetInt64().ToString();
    }

    public async Task<string> CreateEstimateAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) throw new InvalidOperationException("FreshBooks not connected");

        var payload = new
        {
            estimate = new
            {
                customerid = long.TryParse(document.CustomerExternalId, out var cid) ? cid : 0,
                lines = document.LineItems.Select((li, i) => new
                {
                    name = li.Description,
                    qty = li.Quantity,
                    unit_cost = new { amount = li.UnitPrice.ToString("F2"), code = "USD" },
                }).ToArray(),
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/estimates/estimates", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("response").GetProperty("result").GetProperty("estimate").GetProperty("id").GetInt64().ToString();
    }

    public async Task<string> CreateInvoiceAsync(AccountingDocument document, CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) throw new InvalidOperationException("FreshBooks not connected");

        var payload = new
        {
            invoice = new
            {
                customerid = long.TryParse(document.CustomerExternalId, out var cid) ? cid : 0,
                lines = document.LineItems.Select(li => new
                {
                    name = li.Description,
                    qty = li.Quantity,
                    unit_cost = new { amount = li.UnitPrice.ToString("F2"), code = "USD" },
                }).ToArray(),
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/invoices/invoices", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("response").GetProperty("result").GetProperty("invoice").GetProperty("id").GetInt64().ToString();
    }

    public Task<string> CreatePurchaseOrderAsync(AccountingDocument document, CancellationToken ct)
    {
        logger.LogWarning("[FreshBooks] CreatePurchaseOrder not natively supported — returning local ID");
        return Task.FromResult($"freshbooks-local-{Guid.NewGuid():N}");
    }

    public Task<AccountingPayment?> GetPaymentAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingPayment?>(null);

    public Task<string> CreateTimeActivityAsync(AccountingTimeActivity activity, CancellationToken ct)
    {
        logger.LogWarning("[FreshBooks] CreateTimeActivity — time tracking entries logged locally");
        return Task.FromResult($"freshbooks-local-{Guid.NewGuid():N}");
    }

    public async Task<List<AccountingItem>> GetItemsAsync(CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) return [];

        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/items/items", ct);
        if (!response.IsSuccessStatusCode) return [];
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var items = new List<AccountingItem>();
        if (doc.RootElement.TryGetProperty("response", out var resp) &&
            resp.TryGetProperty("result", out var result) &&
            result.TryGetProperty("items", out var itemArr))
        {
            foreach (var i in itemArr.EnumerateArray())
            {
                var unitCost = i.TryGetProperty("unit_cost", out var uc) && uc.TryGetProperty("amount", out var amt)
                    ? decimal.TryParse(amt.GetString(), out var d) ? (decimal?)d : null : null;
                items.Add(new AccountingItem(
                    ExternalId: i.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : null,
                    Name: i.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Description: i.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    Type: "Service",
                    UnitPrice: unitCost,
                    PurchaseCost: null,
                    Sku: null,
                    Active: true));
            }
        }
        return items;
    }

    public Task<AccountingItem?> GetItemAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<AccountingItem?>(null);

    public async Task<string> CreateItemAsync(AccountingItem item, CancellationToken ct)
    {
        var (client, accountId) = await GetAuthenticatedClientAsync(ct);
        if (client is null || string.IsNullOrEmpty(accountId)) throw new InvalidOperationException("FreshBooks not connected");

        var payload = new { item = new { name = item.Name, description = item.Description, unit_cost = new { amount = item.UnitPrice?.ToString("F2") ?? "0.00", code = "USD" } } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{options.Value.BaseApiUrl}/accounting/account/{accountId}/items/items", content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("response").GetProperty("result").GetProperty("item").GetProperty("id").GetInt64().ToString();
    }

    public Task UpdateItemAsync(string externalId, AccountingItem item, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string> CreateExpenseAsync(AccountingExpense expense, CancellationToken ct) =>
        Task.FromResult($"freshbooks-local-{Guid.NewGuid():N}");

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
            var response = await client.GetAsync($"{options.Value.BaseApiUrl}/auth/api/v1/users/me", ct);
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

    private async Task<(HttpClient? Client, string AccountId)> GetAuthenticatedClientAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        if (token is null) return (null, string.Empty);

        if (token.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            token = await RefreshTokenAsync(token, ct);
            if (token is null) return (null, string.Empty);
        }

        var accountIdSetting = await settings.FindByKeyAsync(AccountIdKey, ct);
        var accountId = accountIdSetting?.Value ?? string.Empty;

        // If we don't have an account ID yet, fetch it
        if (string.IsNullOrEmpty(accountId))
        {
            accountId = await FetchAndStoreAccountIdAsync(token.AccessToken, ct);
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (client, accountId);
    }

    private async Task<string> FetchAndStoreAccountIdAsync(string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.GetAsync($"{options.Value.BaseApiUrl}/auth/api/v1/users/me", ct);
        if (!response.IsSuccessStatusCode) return string.Empty;

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var accountId = string.Empty;
        if (doc.RootElement.TryGetProperty("response", out var resp) &&
            resp.TryGetProperty("business_memberships", out var memberships))
        {
            foreach (var m in memberships.EnumerateArray())
            {
                if (m.TryGetProperty("business", out var biz) &&
                    biz.TryGetProperty("account_id", out var aid))
                {
                    accountId = aid.GetString() ?? string.Empty;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(accountId))
        {
            await settings.UpsertAsync(AccountIdKey, accountId, "FreshBooks account ID", ct);
            await settings.SaveChangesAsync(ct);
        }

        return accountId;
    }

    private async Task<FreshBooksTokenData?> GetTokenAsync(CancellationToken ct)
    {
        var setting = await settings.FindByKeyAsync(TokenKey, ct);
        if (setting is null) return null;
        try
        {
            var json = encryption.Decrypt(setting.Value);
            return JsonSerializer.Deserialize<FreshBooksTokenData>(json);
        }
        catch { return null; }
    }

    private async Task<FreshBooksTokenData?> RefreshTokenAsync(FreshBooksTokenData current, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", opts.ClientId),
            new KeyValuePair<string, string>("client_secret", opts.ClientSecret),
            new KeyValuePair<string, string>("redirect_uri", opts.RedirectUri),
            new KeyValuePair<string, string>("refresh_token", current.RefreshToken),
        ]);

        var response = await client.PostAsync(opts.TokenEndpoint, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[FreshBooks] Token refresh failed: {Status}", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var newToken = new FreshBooksTokenData(
            AccessToken: doc.RootElement.GetProperty("access_token").GetString()!,
            RefreshToken: doc.RootElement.GetProperty("refresh_token").GetString()!,
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600));

        var json = JsonSerializer.Serialize(newToken);
        var encrypted = encryption.Encrypt(json);
        await settings.UpsertAsync(TokenKey, encrypted, "Encrypted FreshBooks OAuth tokens", ct);
        await settings.SaveChangesAsync(ct);
        return newToken;
    }
}
