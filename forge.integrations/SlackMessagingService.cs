using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class SlackMessagingService(
    IHttpClientFactory httpClientFactory,
    IUserIntegrationService integrationService,
    ILogger<SlackMessagingService> logger) : IMessagingIntegrationService
{
    public string ProviderId => "slack";

    public async Task SendNotificationAsync(int userId, int integrationId, NotificationMessage message, CancellationToken ct = default)
    {
        var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
        var client = httpClientFactory.CreateClient("Slack");

        var severityEmoji = message.Severity switch
        {
            "critical" => ":rotating_light:",
            "error" => ":x:",
            "warning" => ":warning:",
            "success" => ":white_check_mark:",
            _ => ":information_source:",
        };

        var payload = new
        {
            text = $"{severityEmoji} *{message.Title}*\n{message.Body}",
            unfurl_links = false,
        };

        var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Slack: Sent notification '{Title}' for user {UserId}", message.Title, userId);
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        try
        {
            var webhookUrl = await GetWebhookUrl(userId, integrationId, ct);
            var client = httpClientFactory.CreateClient("Slack");

            var payload = new { text = "QB Engineer connection test — this message confirms your integration is working." };
            var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Slack: TestConnection failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<string> GetWebhookUrl(int userId, int integrationId, CancellationToken ct)
    {
        var credsJson = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct)
            ?? throw new InvalidOperationException("No credentials found for Slack integration");

        var creds = JsonSerializer.Deserialize<JsonElement>(credsJson);
        return creds.GetProperty("webhook_url").GetString()
            ?? throw new InvalidOperationException("Missing webhook_url in Slack credentials");
    }
}
