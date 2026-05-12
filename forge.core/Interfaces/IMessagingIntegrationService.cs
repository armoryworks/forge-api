using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IMessagingIntegrationService
{
    string ProviderId { get; }

    Task SendNotificationAsync(int userId, int integrationId, NotificationMessage message, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default);
}
