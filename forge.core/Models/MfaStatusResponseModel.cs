namespace Forge.Core.Models;

public record MfaStatusResponseModel
{
    public bool IsEnabled { get; init; }
    public bool IsEnforcedByPolicy { get; init; }
    public IReadOnlyList<MfaDeviceSummary> Devices { get; init; } = [];
    public int RecoveryCodesRemaining { get; init; }
}
