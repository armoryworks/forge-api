
namespace Forge.Core.Models.Communications;

/// <summary>
/// Wave 8 — projection of <see cref="Entities.CommunicationSyncConfig"/>
/// for the user-settings UI. Tokens are deliberately omitted (the sealed
/// envelope is implementation detail; the user only ever sees state).
/// </summary>
public record CommunicationSyncConfigResponseModel(
    int Id,
    int UserId,
    CommunicationKind Kind,
    string ProviderId,
    string? DisplayLabel,
    bool IsConnected,
    string? ExternalAccountId,
    DateTimeOffset? LastSyncedAt,
    /// <summary>Last sync error message (phase 1i). Null when the
    /// connection is healthy or has never failed. Cleared on next success.</summary>
    string? LastError,
    DateTimeOffset? LastErrorAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
