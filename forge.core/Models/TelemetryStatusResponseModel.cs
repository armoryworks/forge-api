namespace Forge.Core.Models;

/// <summary>
/// Where this install stands with remote health monitoring: whether it's on, how the
/// enrollment is going, and who last decided.
/// </summary>
/// <param name="Enabled">The operator's switch. Off by default and off after a decline.</param>
/// <param name="EnrollmentStatus">NotEnrolled / Pending / Accepted / Rejected — Armory Works must accept before anything is sent.</param>
/// <param name="ConsentVersion">Which agreement version the current decision was made against.</param>
/// <param name="ConsentDecision">accepted / declined, or null if never asked.</param>
/// <param name="AgreementOutOfDate">True when the terms changed since the decision, so the operator is asked again rather than carried along.</param>
public sealed record TelemetryStatusResponseModel(
    bool Enabled,
    string EnrollmentStatus,
    string? ConsentVersion,
    string? ConsentDecision,
    DateTimeOffset? ConsentAt,
    string? ConsentBy,
    DateTimeOffset? LastHeartbeatAt,
    string? LastError,
    bool AgreementOutOfDate);
