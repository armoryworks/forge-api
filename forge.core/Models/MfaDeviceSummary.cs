
namespace Forge.Core.Models;

public record MfaDeviceSummary
{
    public int Id { get; init; }
    public MfaDeviceType DeviceType { get; init; }
    public string? DeviceName { get; init; }
    public bool IsDefault { get; init; }
    public bool IsVerified { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
}
