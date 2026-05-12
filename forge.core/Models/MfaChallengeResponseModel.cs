using Forge.Core.Enums;

namespace Forge.Core.Models;

public record MfaChallengeResponseModel
{
    public string ChallengeToken { get; init; } = string.Empty;
    public MfaDeviceType DeviceType { get; init; }
    public string? MaskedTarget { get; init; }
}
