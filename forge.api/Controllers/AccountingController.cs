using System.Text;
using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
[RequiresCapability("CAP-ACCT-EXTERNAL")]
public class AccountingController(
    IAccountingProviderFactory providerFactory,
    IQuickBooksTokenService tokenService,
    IOptions<QuickBooksOptions> qbOptions,
    IOptions<XeroOptions> xeroOptions,
    IOptions<FreshBooksOptions> freshBooksOptions,
    IOptions<SageOptions> sageOptions,
    IOptions<ZohoOptions> zohoOptions,
    ISystemSettingRepository settingRepository,
    ITokenEncryptionService tokenEncryption,
    IHttpClientFactory httpClientFactory,
    IMediator mediator,
    ILogger<AccountingController> logger) : ControllerBase
{
    // ─── Accounting Mode ───

    [HttpGet("admin/accounting-mode")]
    public async Task<ActionResult<AccountingModeResponse>> GetAccountingMode(CancellationToken ct)
    {
        var providerId = await providerFactory.GetActiveProviderIdAsync(ct);
        var provider = await providerFactory.GetActiveProviderAsync(ct);

        return Ok(new AccountingModeResponse(
            IsConfigured: provider is not null,
            ProviderName: provider?.ProviderName,
            ProviderId: providerId));
    }

    [HttpPut("admin/accounting-mode")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetAccountingMode([FromBody] SetAccountingModeRequest request, CancellationToken ct)
    {
        await providerFactory.SetActiveProviderAsync(request.ProviderId, ct);
        return NoContent();
    }

    // ─── Provider Registry ───

    [HttpGet("accounting/providers")]
    public async Task<ActionResult<List<AccountingProviderInfo>>> GetProviders(CancellationToken ct)
    {
        var providers = await providerFactory.GetAvailableProvidersAsync(ct);
        return Ok(providers);
    }

    // ─── Connection Status ───

    [HttpGet("accounting/status")]
    public async Task<ActionResult<AccountingConnectionStatusResponse>> GetStatus(CancellationToken ct)
    {
        var provider = await providerFactory.GetActiveProviderAsync(ct);
        if (provider is null)
        {
            return Ok(new AccountingConnectionStatusResponse(
                IsConnected: false,
                ProviderId: null,
                ProviderName: null,
                SyncStatus: null));
        }

        var syncStatus = await provider.GetSyncStatusAsync(ct);
        return Ok(new AccountingConnectionStatusResponse(
            IsConnected: syncStatus.Connected,
            ProviderId: provider.ProviderId,
            ProviderName: provider.ProviderName,
            SyncStatus: syncStatus));
    }

    [HttpPost("accounting/test")]
    public async Task<ActionResult> TestConnection(CancellationToken ct)
    {
        var provider = await providerFactory.GetActiveProviderAsync(ct);
        if (provider is null)
            return Ok(new { success = false, message = "No accounting provider configured" });

        var success = await provider.TestConnectionAsync(ct);
        return Ok(new { success, providerName = provider.ProviderName, message = success ? "Connection verified" : "Connection failed" });
    }

    [HttpPost("accounting/disconnect")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var providerId = await providerFactory.GetActiveProviderIdAsync(ct);

        switch (providerId)
        {
            case "quickbooks":
                await tokenService.ClearTokenAsync(ct);
                break;
            case "xero":
                await settingRepository.UpsertAsync("xero_oauth_token", "", null, ct);
                await settingRepository.UpsertAsync("xero_tenant_id", "", null, ct);
                await settingRepository.SaveChangesAsync(ct);
                break;
            case "freshbooks":
                await settingRepository.UpsertAsync("freshbooks_oauth_token", "", null, ct);
                await settingRepository.UpsertAsync("freshbooks_account_id", "", null, ct);
                await settingRepository.SaveChangesAsync(ct);
                break;
            case "sage":
                await settingRepository.UpsertAsync("sage_oauth_token", "", null, ct);
                await settingRepository.SaveChangesAsync(ct);
                break;
            case "zoho":
                await settingRepository.UpsertAsync("zoho_oauth_token", "", null, ct);
                await settingRepository.SaveChangesAsync(ct);
                break;
        }

        await providerFactory.SetActiveProviderAsync(null, ct);
        return NoContent();
    }

    // ─── QuickBooks OAuth ───

    [HttpGet("quickbooks/authorize")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAuthorizationUrl(CancellationToken ct)
    {
        // Thin dispatch — all per-provider URL-build logic lives in the
        // unified InitiateAccountingOAuthCommand handler. Any other call
        // site that needs to start a QB OAuth flow can dispatch the same
        // command (e.g. accounting-screen reconnect prompts when a token
        // refresh fails).
        try
        {
            var result = await mediator.Send(new InitiateAccountingOAuthCommand("quickbooks"), ct);
            return Ok(new { authorizationUrl = result.AuthorizationUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("quickbooks/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> OAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        [FromQuery] string realmId,
        CancellationToken ct)
    {
        var savedState = HttpContext.Session.GetString("qb_oauth_state");
        if (savedState is null || savedState != state)
        {
            logger.LogWarning("[QuickBooks] OAuth state mismatch");
            return BadRequest("Invalid OAuth state");
        }

        var opts = qbOptions.Value;
        var tokenUrl = opts.TokenEndpoint;

        using var client = httpClientFactory.CreateClient();
        var authHeader = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = opts.RedirectUri,
        });

        var response = await client.PostAsync(tokenUrl, tokenRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[QuickBooks] Token exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            return BadRequest("Token exchange failed");
        }

        var tokenDoc = System.Text.Json.JsonDocument.Parse(body);
        var root = tokenDoc.RootElement;

        var tokenData = new QuickBooksTokenData(
            AccessToken: root.GetProperty("access_token").GetString()!,
            RefreshToken: root.GetProperty("refresh_token").GetString()!,
            RealmId: realmId,
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("x_refresh_token_expires_in").GetInt32()));

        await tokenService.SaveTokenAsync(tokenData, ct);
        await providerFactory.SetActiveProviderAsync("quickbooks", ct);

        logger.LogInformation("[QuickBooks] OAuth completed — connected to realm {RealmId}", realmId);

        // Redirect back to admin integrations page
        return Redirect("/admin?tab=integrations&qb=connected");
    }

    // ─── Xero OAuth ───

    [HttpGet("xero/authorize")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetXeroAuthorizationUrl(CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new InitiateAccountingOAuthCommand("xero"), ct);
            return Ok(new { authorizationUrl = result.AuthorizationUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("xero/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> XeroOAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        var savedState = HttpContext.Session.GetString("xero_oauth_state");
        if (savedState is null || savedState != state)
        {
            logger.LogWarning("[Xero] OAuth state mismatch");
            return BadRequest("Invalid OAuth state");
        }

        var opts = xeroOptions.Value;

        using var client = httpClientFactory.CreateClient();
        var authHeader = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = opts.RedirectUri,
        });

        var response = await client.PostAsync(opts.TokenEndpoint, tokenRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Xero] Token exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            return BadRequest("Token exchange failed");
        }

        var tokenDoc = JsonDocument.Parse(body);
        var root = tokenDoc.RootElement;

        var tokenJson = JsonSerializer.Serialize(new
        {
            access_token = root.GetProperty("access_token").GetString(),
            refresh_token = root.GetProperty("refresh_token").GetString(),
            expires_at = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToString("O"),
        });

        await settingRepository.UpsertAsync("xero_oauth_token", tokenEncryption.Encrypt(tokenJson), "Xero OAuth token", ct);

        // Fetch the active tenant ID
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", root.GetProperty("access_token").GetString());
        var connectionsResponse = await client.GetAsync("https://api.xero.com/connections", ct);
        if (connectionsResponse.IsSuccessStatusCode)
        {
            var connectionsBody = await connectionsResponse.Content.ReadAsStringAsync(ct);
            var connections = JsonDocument.Parse(connectionsBody).RootElement;
            if (connections.GetArrayLength() > 0)
            {
                var tenantId = connections[0].GetProperty("tenantId").GetString() ?? "";
                await settingRepository.UpsertAsync("xero_tenant_id", tenantId, "Xero tenant ID", ct);
            }
        }

        await settingRepository.SaveChangesAsync(ct);
        await providerFactory.SetActiveProviderAsync("xero", ct);

        logger.LogInformation("[Xero] OAuth completed successfully");

        return Redirect("/admin?tab=integrations&provider=connected");
    }

    // ─── FreshBooks OAuth ───

    [HttpGet("freshbooks/authorize")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetFreshBooksAuthorizationUrl(CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new InitiateAccountingOAuthCommand("freshbooks"), ct);
            return Ok(new { authorizationUrl = result.AuthorizationUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("freshbooks/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> FreshBooksOAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        var savedState = HttpContext.Session.GetString("freshbooks_oauth_state");
        if (savedState is null || savedState != state)
        {
            logger.LogWarning("[FreshBooks] OAuth state mismatch");
            return BadRequest("Invalid OAuth state");
        }

        var opts = freshBooksOptions.Value;

        using var client = httpClientFactory.CreateClient();
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = opts.RedirectUri,
        });

        var response = await client.PostAsync(opts.TokenEndpoint, tokenRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[FreshBooks] Token exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            return BadRequest("Token exchange failed");
        }

        var tokenDoc = JsonDocument.Parse(body);
        var root = tokenDoc.RootElement;

        var tokenJson = JsonSerializer.Serialize(new
        {
            access_token = root.GetProperty("access_token").GetString(),
            refresh_token = root.GetProperty("refresh_token").GetString(),
            expires_at = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToString("O"),
        });

        await settingRepository.UpsertAsync("freshbooks_oauth_token", tokenEncryption.Encrypt(tokenJson), "FreshBooks OAuth token", ct);

        // Fetch the account ID from the identity endpoint
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", root.GetProperty("access_token").GetString());
        var userResponse = await client.GetAsync($"{opts.BaseApiUrl}/auth/api/v1/users/me", ct);
        if (userResponse.IsSuccessStatusCode)
        {
            var userBody = await userResponse.Content.ReadAsStringAsync(ct);
            var userDoc = JsonDocument.Parse(userBody).RootElement;
            if (userDoc.TryGetProperty("response", out var responseEl) &&
                responseEl.TryGetProperty("business_memberships", out var memberships) &&
                memberships.GetArrayLength() > 0)
            {
                var accountId = memberships[0].GetProperty("business").GetProperty("account_id").GetString() ?? "";
                await settingRepository.UpsertAsync("freshbooks_account_id", accountId, "FreshBooks account ID", ct);
            }
        }

        await settingRepository.SaveChangesAsync(ct);
        await providerFactory.SetActiveProviderAsync("freshbooks", ct);

        logger.LogInformation("[FreshBooks] OAuth completed successfully");

        return Redirect("/admin?tab=integrations&provider=connected");
    }

    // ─── Sage OAuth ───

    [HttpGet("sage/authorize")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSageAuthorizationUrl(CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new InitiateAccountingOAuthCommand("sage"), ct);
            return Ok(new { authorizationUrl = result.AuthorizationUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("sage/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> SageOAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        var savedState = HttpContext.Session.GetString("sage_oauth_state");
        if (savedState is null || savedState != state)
        {
            logger.LogWarning("[Sage] OAuth state mismatch");
            return BadRequest("Invalid OAuth state");
        }

        var opts = sageOptions.Value;

        using var client = httpClientFactory.CreateClient();
        var authHeader = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = opts.RedirectUri,
        });

        var response = await client.PostAsync(opts.TokenEndpoint, tokenRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Sage] Token exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            return BadRequest("Token exchange failed");
        }

        var tokenDoc = JsonDocument.Parse(body);
        var root = tokenDoc.RootElement;

        var tokenJson = JsonSerializer.Serialize(new
        {
            access_token = root.GetProperty("access_token").GetString(),
            refresh_token = root.GetProperty("refresh_token").GetString(),
            expires_at = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToString("O"),
        });

        await settingRepository.UpsertAsync("sage_oauth_token", tokenEncryption.Encrypt(tokenJson), "Sage OAuth token", ct);
        await settingRepository.SaveChangesAsync(ct);
        await providerFactory.SetActiveProviderAsync("sage", ct);

        logger.LogInformation("[Sage] OAuth completed successfully");

        return Redirect("/admin?tab=integrations&provider=connected");
    }

    // ─── Zoho OAuth ───

    [HttpGet("zoho/authorize")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetZohoAuthorizationUrl(CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new InitiateAccountingOAuthCommand("zoho"), ct);
            return Ok(new { authorizationUrl = result.AuthorizationUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("zoho/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ZohoOAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        var savedState = HttpContext.Session.GetString("zoho_oauth_state");
        if (savedState is null || savedState != state)
        {
            logger.LogWarning("[Zoho] OAuth state mismatch");
            return BadRequest("Invalid OAuth state");
        }

        var opts = zohoOptions.Value;

        using var client = httpClientFactory.CreateClient();
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = opts.RedirectUri,
        });

        var response = await client.PostAsync(opts.TokenEndpoint, tokenRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Zoho] Token exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            return BadRequest("Token exchange failed");
        }

        var tokenDoc = JsonDocument.Parse(body);
        var root = tokenDoc.RootElement;

        var tokenJson = JsonSerializer.Serialize(new
        {
            access_token = root.GetProperty("access_token").GetString(),
            refresh_token = root.GetProperty("refresh_token").GetString(),
            expires_at = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToString("O"),
        });

        await settingRepository.UpsertAsync("zoho_oauth_token", tokenEncryption.Encrypt(tokenJson), "Zoho OAuth token", ct);
        await settingRepository.SaveChangesAsync(ct);
        await providerFactory.SetActiveProviderAsync("zoho", ct);

        logger.LogInformation("[Zoho] OAuth completed successfully");

        return Redirect("/admin?tab=integrations&provider=connected");
    }

    // ─── QuickBooks Status ───

    [HttpGet("quickbooks/status")]
    public async Task<ActionResult<QuickBooksConnectionStatus>> GetQuickBooksStatus(CancellationToken ct)
    {
        var isConnected = await tokenService.IsConnectedAsync(ct);
        var token = await tokenService.GetTokenAsync(ct);

        return Ok(new QuickBooksConnectionStatus(
            IsConnected: isConnected,
            CompanyId: token?.RealmId,
            CompanyName: null,
            ConnectedAt: null,
            TokenExpiresAt: token?.AccessTokenExpiresAt,
            LastSyncAt: null));
    }

    // ─── Employees (from accounting) ───

    [HttpGet("accounting/employees")]
    public async Task<ActionResult<List<AccountingEmployee>>> GetEmployees(CancellationToken ct)
    {
        var provider = await providerFactory.GetActiveProviderAsync(ct);
        if (provider is null)
            return Ok(new List<AccountingEmployee>());

        var employees = await provider.GetEmployeesAsync(ct);
        return Ok(employees);
    }

    // ─── Items (for linkage) ───

    [HttpGet("accounting/items")]
    public async Task<ActionResult<List<AccountingItem>>> GetItems(CancellationToken ct)
    {
        var provider = await providerFactory.GetActiveProviderAsync(ct);
        if (provider is null)
            return Ok(new List<AccountingItem>());

        var items = await provider.GetItemsAsync(ct);
        return Ok(items);
    }

    // ─── Sync Status ───

    [HttpGet("accounting/sync-status")]
    public async Task<ActionResult<AccountingSyncStatus>> GetSyncStatus(CancellationToken ct)
    {
        var provider = await providerFactory.GetActiveProviderAsync(ct);
        if (provider is null)
            return Ok(new AccountingSyncStatus(false, null, 0, 0));

        var status = await provider.GetSyncStatusAsync(ct);
        return Ok(status);
    }
}

public record SetAccountingModeRequest(string? ProviderId);

public record AccountingConnectionStatusResponse(
    bool IsConnected,
    string? ProviderId,
    string? ProviderName,
    AccountingSyncStatus? SyncStatus);
