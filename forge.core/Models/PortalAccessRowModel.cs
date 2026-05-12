namespace Forge.Core.Models;

/// <summary>
/// Phase 1r — admin row for /customers/portal-access. Includes the
/// linked contact + customer for the table display + admin-managed
/// IsEnabled toggle + LastLoginAt as the activity signal.
/// </summary>
public record PortalAccessRowModel(
    int AccessId,
    int ContactId,
    int CustomerId,
    string CustomerName,
    string ContactFirstName,
    string ContactLastName,
    string? ContactEmail,
    bool IsEnabled,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);
