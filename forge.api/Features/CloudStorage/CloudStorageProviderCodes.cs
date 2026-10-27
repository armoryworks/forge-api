using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.CloudStorage;

public static class CloudStorageProviderCodes
{
    public const string UmbrellaCapability = "CAP-EXT-CLOUD-STORAGE";
    public const string Mock = "mock";

    public static readonly IReadOnlyDictionary<string, string?> CapabilityByCode =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["gdrive"] = "CAP-EXT-CLOUD-STORAGE-GDRIVE",
            ["onedrive"] = "CAP-EXT-CLOUD-STORAGE-ONEDRIVE",
            ["dropbox"] = "CAP-EXT-CLOUD-STORAGE-DROPBOX",
            [Mock] = null,
        };

    public static string Normalize(string code) => code.Trim().ToLowerInvariant();

    public static CloudStorageProviderResponseModel ToModel(CloudStorageProvider p, int linkedRecordCount) =>
        new(
            p.Id,
            p.ProviderCode,
            p.Mode,
            p.IsActive,
            p.RootFolderId,
            p.ServiceAccountId,
            !string.IsNullOrEmpty(p.OAuthTokenEncrypted) && !string.IsNullOrEmpty(p.RefreshTokenEncrypted),
            p.TokenExpiresAt,
            p.LastConnectedAt,
            p.LastError,
            linkedRecordCount,
            p.CreatedAt);
}
