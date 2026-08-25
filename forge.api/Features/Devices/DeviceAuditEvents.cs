namespace Forge.Api.Features.Devices;

/// <summary>
/// Audit vocabulary for the mobile-device lifecycle, written through
/// ISystemAuditWriter with EntityType "UserDevice".
/// </summary>
public static class DeviceAuditEvents
{
    public const string EntityType = "UserDevice";

    public const string EnrollmentTokenIssued = "DeviceEnrollmentTokenIssued";
    public const string Enrolled = "DeviceEnrolled";
    public const string Revoked = "DeviceRevoked";
    public const string Renamed = "DeviceRenamed";
    public const string TokenReuseDetected = "DeviceTokenReuseDetected";
}
