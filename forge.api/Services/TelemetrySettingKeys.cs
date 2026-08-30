namespace Forge.Api.Services;

/// <summary>
/// The <c>system_settings</c> keys that hold this install's monitoring state. Kept in
/// one place because they're written from a handler, a background job and a
/// bootstrap, and a typo'd key is a silently-forgotten consent decision.
///
/// Deliberately settings rather than a new table: forge-db owns the schema and the
/// current state here is a handful of scalars. The decision HISTORY goes to
/// <c>audit_log_entries</c>, which already records the user, IP and user agent that a
/// consent record wants.
/// </summary>
public static class TelemetrySettingKeys
{
    /// <summary>The operator's switch. "true" only after an accepted agreement.</summary>
    public const string Enabled = "telemetry.enabled";

    /// <summary>Stable identity for this install, generated once and kept across upgrades.</summary>
    public const string InstallId = "telemetry.install_id";

    /// <summary>NotEnrolled / Pending / Accepted / Rejected, as last observed from Armory Works.</summary>
    public const string EnrollmentStatus = "telemetry.enrollment_status";

    /// <summary>Held only until the decision is collected, then replaced by the real token.</summary>
    public const string PendingToken = "telemetry.pending_token";

    /// <summary>Authenticates heartbeats. Present only once Armory Works has accepted.</summary>
    public const string Token = "telemetry.token";

    public const string ConsentVersion = "telemetry.consent_version";
    public const string ConsentDecision = "telemetry.consent_decision";
    public const string ConsentAt = "telemetry.consent_at";
    public const string ConsentBy = "telemetry.consent_by";

    public const string LastHeartbeatAt = "telemetry.last_heartbeat_at";

    /// <summary>Last failure reason, surfaced on the settings screen so a stuck enrollment is visible.</summary>
    public const string LastError = "telemetry.last_error";

    /// <summary>Audit action recorded for every accept/decline.</summary>
    public const string ConsentAuditAction = "telemetry.consent";
}
