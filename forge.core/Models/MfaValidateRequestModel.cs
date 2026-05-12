namespace Forge.Core.Models;

public record MfaValidateRequestModel
{
    public string ChallengeToken { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool RememberDevice { get; init; }
}
