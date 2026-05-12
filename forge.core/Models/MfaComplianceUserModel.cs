using Forge.Core.Enums;

namespace Forge.Core.Models;

public record MfaComplianceUserModel
{
    public int UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool MfaEnabled { get; init; }
    public MfaDeviceType? MfaDeviceType { get; init; }
    public bool IsEnforcedByPolicy { get; init; }
}
