using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockMessagingIntegrationService(ILogger<MockMessagingIntegrationService> logger) : IMessagingIntegrationService
{
    public string ProviderId => "mock_messaging";

    public Task SendNotificationAsync(int userId, int integrationId, NotificationMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("[MockMessaging] SendNotification to user {UserId}: [{Severity}] {Title} — {Body}",
            userId, message.Severity, message.Title, message.Body);
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        logger.LogInformation("[MockMessaging] TestConnection for user {UserId}", userId);
        return Task.FromResult(true);
    }
}
