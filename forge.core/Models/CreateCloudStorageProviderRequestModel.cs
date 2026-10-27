using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateCloudStorageProviderRequestModel(
    string ProviderCode,
    CloudStorageProviderMode Mode,
    string? RootFolderId,
    string? ServiceAccountId,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? TokenExpiresAt,
    bool IsActive = true);
