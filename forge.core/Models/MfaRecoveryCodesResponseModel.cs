namespace Forge.Core.Models;

public record MfaRecoveryCodesResponseModel
{
    public IReadOnlyList<string> Codes { get; init; } = [];
    public string Warning { get; init; } = "Save these codes in a safe place. Each can only be used once.";
}
