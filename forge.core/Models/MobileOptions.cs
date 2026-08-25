namespace Forge.Core.Models;

public class MobileOptions
{
    public const string SectionName = "Mobile";

    /// <summary>Display name shown on the phone during enrollment ("Connecting to …").</summary>
    public string InstanceName { get; set; } = "Forge";

    /// <summary>
    /// SHA-256 fingerprint of this instance's TLS certificate, embedded in the
    /// enrollment QR and /.well-known/forge.json for trust-on-first-use pinning.
    /// The API can't see the edge certificate (TLS terminates at the front
    /// proxy), so the operator sets this. Empty = omitted from responses.
    /// </summary>
    public string? CertSha256 { get; set; }

    /// <summary>Oldest app version this instance accepts.</summary>
    public string MinAppVersion { get; set; } = "1.0.0";

    /// <summary>Sliding lifetime of a device refresh token.</summary>
    public int RefreshTokenLifetimeDays { get; set; } = 90;

    public int EnrollmentTokenLifetimeMinutes { get; set; } = 10;

    /// <summary>Devices with no check-in for this many days are flagged stale in the admin list.</summary>
    public int StaleDeviceDays { get; set; } = 30;

    /// <summary>Default local-lock idle timeout for floor-tier roles (8 hours).</summary>
    public int IdleTimeoutFloorMinutes { get; set; } = 480;

    /// <summary>Default local-lock idle timeout for office-tier roles.</summary>
    public int IdleTimeoutOfficeMinutes { get; set; } = 15;
}
