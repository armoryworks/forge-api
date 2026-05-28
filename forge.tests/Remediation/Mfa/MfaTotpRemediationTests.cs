using FluentAssertions;
using OtpNet;

using Forge.Data.Services;

namespace Forge.Tests.Remediation.Mfa;

/// <summary>
/// Region 5 · MFA RED test (see ../README.md). Finding G-MFA-3 (ship-gate): TOTP enrolment
/// was broken — the server HMAC'd over the UTF-8 bytes of the Base32 secret string instead
/// of Base32-decoding it to the raw key, so codes from a standard authenticator never
/// matched. Golden-vector: generate a secret, compute the code the way an authenticator
/// does (Base32-decode → TOTP), and assert the server validates it.
///
/// GenerateTotpSecret + ValidateTotpCode are pure (don't touch the injected deps), so the
/// service is constructed with stub deps.
/// </summary>
public class MfaTotpRemediationTests
{
    private static MfaService NewService() => new(null!, null!, null!, null!, null!, null!);

    [Fact] // G-MFA-3 GREEN — a code from a standard authenticator (Base32-decoded secret) validates
    public void Totp_code_from_a_standard_authenticator_validates()
    {
        var svc = NewService();
        var secret = svc.GenerateTotpSecret(); // Base32 string — exactly what the QR / manual-entry shows

        // Authenticator behaviour: Base32-DECODE the displayed secret to the raw key, then TOTP.
        var authenticatorCode = new Totp(Base32Encoding.ToBytes(secret), step: 30, totpSize: 6).ComputeTotp();

        svc.ValidateTotpCode(secret, authenticatorCode).Should().BeTrue(
            "the server must accept the same code a standard authenticator app produces from the Base32 secret");
    }

    [Fact] // sanity — a wrong code is still rejected (the fix didn't make validation permissive)
    public void An_incorrect_totp_code_is_rejected()
    {
        var svc = NewService();
        var secret = svc.GenerateTotpSecret();

        svc.ValidateTotpCode(secret, "000000").Should().BeFalse(
            "an arbitrary 6-digit code must not validate");
    }
}
