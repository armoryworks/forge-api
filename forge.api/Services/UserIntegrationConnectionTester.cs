using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public record UserIntegrationTestResult(bool Success, bool Contacted);

public class UserIntegrationConnectionTester(
    IUserIntegrationService integrationService,
    IEnumerable<IMessagingIntegrationService> messagingServices,
    IEnumerable<ICalendarIntegrationService> calendarServices)
{
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

        var success = await liveTest();
        await integrationService.UpdateSyncStatusAsync(
            integrationId,
            integration.LastSyncAt,
            success ? null : $"Connection test to {integration.ProviderId} failed",
            ct);
        return new UserIntegrationTestResult(success, Contacted: true);
    }
}
