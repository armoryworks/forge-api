using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class GoogleChatMessagingService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<GoogleChatMessagingService> logger) : IMessagingIntegrationService
{
    public string ProviderId => "google_chat";

    public async Task SendNotificationAsync(int userId, int integrationId, NotificationMessage message, CancellationToken ct = default)
    {
        var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
        var client = httpClientFactory.CreateClient("GoogleChat");

        var severityLabel = message.Severity switch
        {
            "critical" => "[CRITICAL]",
            "error" => "[ERROR]",
            "warning" => "[WARNING]",
            "success" => "[OK]",
            _ => "[INFO]",
        };

        var payload = new
        {
            text = $"{severityLabel} *{message.Title}*\n{message.Body}",
        };

        var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Google Chat: Sent notification '{Title}' for user {UserId}", message.Title, userId);
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
            var client = httpClientFactory.CreateClient("GoogleChat");

            var payload = new { text = "QB Engineer connection test — this confirms your integration is working." };
            var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Chat: TestConnection failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<string> GetWebhookUrl(int userId, int integrationId, CancellationToken ct)
    {
        var credsJson = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct)
            ?? throw new InvalidOperationException("No credentials found for Google Chat integration");

        var creds = JsonSerializer.Deserialize<JsonElement>(credsJson);
        return creds.GetProperty("webhook_url").GetString()
            ?? throw new InvalidOperationException("Missing webhook_url in Google Chat credentials");
    }
}
