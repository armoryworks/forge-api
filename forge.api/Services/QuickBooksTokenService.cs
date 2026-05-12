using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

public class QuickBooksTokenService(
    ISystemSettingRepository settingRepository,
    ITokenEncryptionService encryption,
    IHttpClientFactory httpClientFactory,
    IOptions<QuickBooksOptions> options,
    ILogger<QuickBooksTokenService> logger) : IQuickBooksTokenService
{
    private const string TokenKey = "qb_oauth_token";
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    public async Task<QuickBooksTokenData?> GetTokenAsync(CancellationToken ct)
    {
        var setting = await settingRepository.FindByKeyAsync(TokenKey, ct);
        if (setting is null) return null;

        try
        {
            var json = encryption.Decrypt(setting.Value);
            return JsonSerializer.Deserialize<QuickBooksTokenData>(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt QuickBooks token data");
            return null;
        }
    }

    public async Task SaveTokenAsync(QuickBooksTokenData tokenData, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(tokenData);
        var encrypted = encryption.Encrypt(json);
        await settingRepository.UpsertAsync(TokenKey, encrypted, "Encrypted QuickBooks OAuth tokens", ct);
        await settingRepository.SaveChangesAsync(ct);
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        if (token is null) return null;

        // Access token still valid
        if (token.AccessTokenExpiresAt > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
            return token.AccessToken;

        // Refresh token expired — user must re-authorize
        if (token.RefreshTokenExpiresAt < DateTimeOffset.UtcNow)
        {
            logger.LogWarning("QuickBooks refresh token expired — re-authorization required");
            return null;
        }

        // Refresh the access token
        var refreshed = await RefreshAccessTokenAsync(token.RefreshToken, token.RealmId, ct);
        if (refreshed is null) return null;

        await SaveTokenAsync(refreshed, ct);
        return refreshed.AccessToken;
    }

    public async Task ClearTokenAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        if (token is not null)
        {
            await RevokeTokenAsync(token.RefreshToken, ct);
        }

        await settingRepository.UpsertAsync(TokenKey, string.Empty, "Encrypted QuickBooks OAuth tokens (disconnected)", ct);
        await settingRepository.SaveChangesAsync(ct);
    }

    public async Task<bool> IsConnectedAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        return token is not null && token.RefreshTokenExpiresAt > DateTimeOffset.UtcNow;
    }

    private async Task<QuickBooksTokenData?> RefreshAccessTokenAsync(string refreshToken, string realmId, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient();

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        try
        {
            var response = await client.PostAsync(opts.TokenEndpoint, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("QuickBooks token refresh failed: {StatusCode} {Body}", response.StatusCode, body);
                return null;
            }

            var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            return new QuickBooksTokenData(
                AccessToken: root.GetProperty("access_token").GetString()!,
                RefreshToken: root.GetProperty("refresh_token").GetString()!,
                RealmId: realmId,
                AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
                RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("x_refresh_token_expires_in").GetInt32()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh QuickBooks access token");
            return null;
        }
    }

    private async Task RevokeTokenAsync(string token, CancellationToken ct)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient();

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
        });

        try
        {
            await client.PostAsync(opts.RevokeEndpoint, content, ct);
            logger.LogInformation("QuickBooks token revoked successfully");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to revoke QuickBooks token — continuing with disconnect");
        }
    }
}
