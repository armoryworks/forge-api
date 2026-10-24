using Fido2NetLib;

namespace Forge.Api.Services;

/// <summary>
/// WebAuthn/passkey ceremonies. Registration runs on the desktop (signed-in
/// session); assertion serves as the second factor during login — the phone
/// uses the platform authenticator, synced or cross-device. Challenge state
/// lives in a short-lived server-side cache.
/// </summary>
public interface IPasskeyService
{
    Task<CredentialCreateOptions> BeginRegistrationAsync(int userId, PasskeyRelyingParty relyingParty, CancellationToken ct);

    Task<string> CompleteRegistrationAsync(
        int userId, PasskeyRelyingParty relyingParty, AuthenticatorAttestationRawResponse response,
        string? deviceName, CancellationToken ct);

    Task<AssertionOptions?> BeginAssertionAsync(int userId, PasskeyRelyingParty relyingParty, CancellationToken ct);

    Task<bool> CompleteAssertionAsync(
        int userId, PasskeyRelyingParty relyingParty, AuthenticatorAssertionRawResponse response, CancellationToken ct);
}
