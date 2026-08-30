using Forge.Core.Entities;

namespace Forge.Api.Services;

public record DeviceCredential(
    UserDevice Device,
    string RawRefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Mints device credentials: upserts the device row for a UUID and starts a
/// fresh refresh-token family (revoking any previous one). Shared by the
/// QR-enrollment and authenticated self-enrollment paths.
/// </summary>
public interface IDeviceCredentialService
{
    Task<DeviceCredential> MintAsync(
        int userId,
        string deviceUuid,
        string deviceName,
        string platform,
        string? osVersion,
        string? appVersion,
        int enrolledByUserId,
        CancellationToken ct);
}
