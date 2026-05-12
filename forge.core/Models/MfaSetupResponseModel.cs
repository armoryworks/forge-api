namespace Forge.Core.Models;

public record MfaSetupResponseModel
{
    public string Secret { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
    public string ManualEntryKey { get; init; } = string.Empty;
    public int DeviceId { get; init; }
}
