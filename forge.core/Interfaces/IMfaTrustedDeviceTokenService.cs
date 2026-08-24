namespace Forge.Core.Interfaces;

/// <summary>
/// Issues and validates "remember this device for 30 days" trusted-device tokens.
/// When a user completes an MFA challenge with the remember option, a signed,
/// user-bound, 30-day token is issued and stored on the device. On the next
/// login the client presents it; a valid token lets the login flow skip the MFA
/// challenge (the password is still required). Stateless and single-purpose —
/// signed with a key derived from the JWT key, so it cannot authenticate against
/// the main bearer pipeline or the MFA-pending pipeline.
/// </summary>
public interface IMfaTrustedDeviceTokenService
{
    /// <summary>Mint a 30-day trusted-device token bound to <paramref name="userId"/>.</summary>
    string Issue(int userId);

    /// <summary>Return the bound userId iff <paramref name="token"/> is a valid,
    /// unexpired, single-purpose trusted-device token; otherwise null.</summary>
    int? ValidateAndGetUserId(string token);
}
