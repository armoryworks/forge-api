using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class DiscordMessagingService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<DiscordMessagingService> logger) : IMessagingIntegrationService
{
    public string ProviderId => "discord";

    public async Task SendNotificationAsync(int userId, int integrationId, NotificationMessage message, CancellationToken ct = default)
    {
        var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
        var client = httpClientFactory.CreateClient("Discord");

        var color = message.Severity switch
        {
            "critical" or "error" => 0xFF0000,
            "warning" => 0xFFA500,
            "success" => 0x00FF00,
            _ => 0x5865F2,
        };

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = message.Title,
                    description = message.Body,
                    color,
                }
            },
        };

        var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Discord: Sent notification '{Title}' for user {UserId}", message.Title, userId);
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
            var client = httpClientFactory.CreateClient("Discord");

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = "QB Engineer",
                        description = "Connection test — this confirms your integration is working.",
                        color = 0x5865F2,
                    }
                },
            };

            var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discord: TestConnection failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<string> GetWebhookUrl(int userId, int integrationId, CancellationToken ct)
    {
        var credsJson = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct)
            ?? throw new InvalidOperationException("No credentials found for Discord integration");

        var creds = JsonSerializer.Deserialize<JsonElement>(credsJson);
        return creds.GetProperty("webhook_url").GetString()
            ?? throw new InvalidOperationException("Missing webhook_url in Discord credentials");
    }
}
