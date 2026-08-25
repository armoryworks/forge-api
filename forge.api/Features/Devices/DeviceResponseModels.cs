using Forge.Api.Features.Auth;

namespace Forge.Api.Features.Devices;

public record DeviceResponseModel(
    int Id,
    int? UserId,
    string? UserName,
    string Name,
    string Platform,
    string? OsVersion,
    string? AppVersion,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    bool IsFlagged,
    bool IsStale);

public record EnrollmentTokenResponseModel(
    string Token,
    DateTimeOffset ExpiresAt,
    string InstanceName,
    string? CertSha256);

public record MobileAuthResponseModel(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    int DeviceId,
    string DeviceName,
    AuthUserResponseModel User);
