using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CloudStorageProviderResponseModel(
    int Id,
    string ProviderCode,
    CloudStorageProviderMode Mode,
    bool IsActive,
    string? RootFolderId,
    string? ServiceAccountId,
    bool CredentialsConfigured,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? LastConnectedAt,
    string? LastError,
    int LinkedRecordCount,
    DateTimeOffset CreatedAt);
