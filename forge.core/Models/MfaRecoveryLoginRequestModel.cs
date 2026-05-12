namespace Forge.Core.Models;

public record MfaRecoveryLoginRequestModel
{
    public string ChallengeToken { get; init; } = string.Empty;
    public string RecoveryCode { get; init; } = string.Empty;
}
