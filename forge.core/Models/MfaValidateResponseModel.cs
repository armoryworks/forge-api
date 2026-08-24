namespace Forge.Core.Models;

public record MfaValidateResponseModel
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Set only when the user chose "remember this device for 30 days" on a
    /// successful challenge. The client stores it and presents it on the next
    /// login to skip the MFA challenge. Null/empty otherwise.
    /// </summary>
    public string? TrustedDeviceToken { get; init; }
}
