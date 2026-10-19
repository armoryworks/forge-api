using System.Text.Json;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public record UserIntegrationTestResult(bool Success, bool Contacted);

public class UserIntegrationConnectionTester(
    IUserIntegrationService integrationService,
    IEnumerable<IMessagingIntegrationService> messagingServices,
    IEnumerable<ICalendarIntegrationService> calendarServices)
{
    private static readonly Dictionary<string, string[]> WebhookHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slack"] = ["hooks.slack.com"],
        ["discord"] = ["discord.com", "discordapp.com"],
        ["google_chat"] = ["chat.googleapis.com"],
        ["teams"] = ["webhook.office.com", "logic.azure.com", "api.powerplatform.com"],
    };

    public async Task<UserIntegrationTestResult> TestAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        var integration = await integrationService.GetByIdAsync(userId, integrationId, ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found");

        Func<Task<bool>>? liveTest =
            messagingServices.FirstOrDefault(s => s.ProviderId == integration.ProviderId) is { } messaging
                ? () => messaging.TestConnectionAsync(userId, integrationId, ct)
            : calendarServices.FirstOrDefault(s => s.ProviderId == integration.ProviderId) is { } calendar
                ? () => calendar.TestConnectionAsync(userId, integrationId, ct)
            : null;

        if (liveTest is null)
        {
            var stored = await integrationService.TestConnectionAsync(userId, integrationId, ct);
            return new UserIntegrationTestResult(stored, Contacted: false);
        }

        if (WebhookHosts.TryGetValue(integration.ProviderId, out var hosts)
            && !await IsAllowedWebhookAsync(userId, integrationId, hosts, ct))
        {
            await integrationService.UpdateSyncStatusAsync(
                integrationId,
                integration.LastSyncAt,
                $"Webhook URL is not an https {integration.ProviderId} address",
                ct);
            return new UserIntegrationTestResult(false, Contacted: false);
        }

        var success = await liveTest();
        await integrationService.UpdateSyncStatusAsync(
            integrationId,
            integration.LastSyncAt,
            success ? null : $"Connection test to {integration.ProviderId} failed",
            ct);
        return new UserIntegrationTestResult(success, Contacted: true);
    }

    private async Task<bool> IsAllowedWebhookAsync(int userId, int integrationId, string[] hosts, CancellationToken ct)
    {
        var credentials = await integrationService.GetDecryptedCredentialsAsync(userId, integrationId, ct);
        if (string.IsNullOrWhiteSpace(credentials))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(credentials);
            if (!doc.RootElement.TryGetProperty("webhook_url", out var value) || value.ValueKind != JsonValueKind.String)
                return false;

            return Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.IsDefaultPort
                && string.IsNullOrEmpty(uri.UserInfo)
                && hosts.Any(h => uri.IdnHost.Equals(h, StringComparison.OrdinalIgnoreCase)
                    || uri.IdnHost.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
