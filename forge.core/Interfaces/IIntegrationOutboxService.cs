using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IIntegrationOutboxService
{
    Task<IntegrationOutboxEntry> EnqueueEmailAsync(
        string operationKey,
        EmailMessage message,
        string? entityType = null,
        int? entityId = null,
        CancellationToken ct = default);

    Task<IntegrationOutboxEntry> EnqueueAsync(
        IntegrationProvider provider,
        string operationKey,
        string payload,
        string? entityType = null,
        int? entityId = null,
        int maxAttempts = 5,
        CancellationToken ct = default);
}
