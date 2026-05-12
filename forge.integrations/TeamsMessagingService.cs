using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class TeamsMessagingService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<TeamsMessagingService> logger) : IMessagingIntegrationService
{
    public string ProviderId => "teams";

    public async Task SendNotificationAsync(int userId, int integrationId, NotificationMessage message, CancellationToken ct = default)
    {
        var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
        var client = httpClientFactory.CreateClient("Teams");

        var themeColor = message.Severity switch
        {
            "critical" or "error" => "FF0000",
            "warning" => "FFA500",
            "success" => "00FF00",
            _ => "0078D4",
        };

        var payload = new
        {
            type = "MessageCard",
            themeColor,
            summary = message.Title,
            sections = new[]
            {
                new
                {
                    activityTitle = message.Title,
                    text = message.Body,
                }
            },
        };

        var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Teams: Sent notification '{Title}' for user {UserId}", message.Title, userId);
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
            var client = httpClientFactory.CreateClient("Teams");

            var payload = new
            {
                type = "MessageCard",
                themeColor = "0078D4",
                summary = "Connection Test",
                sections = new[]
                {
                    new { activityTitle = "QB Engineer", text = "Connection test — this confirms your integration is working." }
                },
            };

            var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Teams: TestConnection failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<string> GetWebhookUrl(int userId, int integrationId, CancellationToken ct)
    {
        var credsJson = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct)
            ?? throw new InvalidOperationException("No credentials found for Teams integration");

        var creds = JsonSerializer.Deserialize<JsonElement>(credsJson);
        return creds.GetProperty("webhook_url").GetString()
            ?? throw new InvalidOperationException("Missing webhook_url in Teams credentials");
    }
}
