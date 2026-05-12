namespace Forge.Core.Models;

public record MfaVerifySetupRequestModel
{
    public int DeviceId { get; init; }
    public string Code { get; init; } = string.Empty;
}
