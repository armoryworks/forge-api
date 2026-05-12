namespace Forge.Core.Models;

public record QuickBooksConnectionStatus(
    bool IsConnected,
    string? CompanyId,
    string? CompanyName,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? LastSyncAt);
