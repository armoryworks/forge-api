using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IMfaService
{
    // Setup
    Task<MfaSetupResponseModel> BeginTotpSetupAsync(int userId, string? deviceName, CancellationToken ct);
    Task<bool> VerifyTotpSetupAsync(int userId, int deviceId, string code, CancellationToken ct);
    Task DisableMfaAsync(int userId, CancellationToken ct);
    Task RemoveDeviceAsync(int userId, int deviceId, CancellationToken ct);

    // Challenge/Validate (login flow)
    Task<MfaChallengeResponseModel> CreateChallengeAsync(int userId, CancellationToken ct);
    Task<MfaValidateResponseModel?> ValidateChallengeAsync(string challengeToken, string code, bool rememberDevice, CancellationToken ct);

    // Recovery
    Task<MfaRecoveryCodesResponseModel> GenerateRecoveryCodesAsync(int userId, CancellationToken ct);
    Task<MfaValidateResponseModel?> ValidateRecoveryCodeAsync(string challengeToken, string recoveryCode, CancellationToken ct);

    // TOTP
    bool ValidateTotpCode(string secret, string code, int toleranceSteps = 1);
    string GenerateTotpSecret();
    string GenerateQrCodeUri(string secret, string email, string issuer);

    // Status
    Task<MfaStatusResponseModel> GetMfaStatusAsync(int userId, CancellationToken ct);
    Task<bool> IsMfaRequiredAsync(int userId, CancellationToken ct);
}
