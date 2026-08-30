namespace Forge.Core.Models;

/// <summary>
/// Whether a newer release is published for every tier this box runs.
/// <c>Status</c> is current, behind or unknown. <b>unknown is not current</b>: an unreachable
/// registry reported as up-to-date is how an install sits years behind without anyone noticing.
/// </summary>
public record DeployAvailabilityModel(string Status, string? NewestRelease, string? Message);

/// <summary>
/// The generic envelope broadcast to every console when an upgrade starts or ends, and the shape
/// of the marker file nginx serves unauthenticated at /upgrade-status.json.
/// <para>
/// This payload reaches every logged-in tablet on the shop floor. It carries no version tag, tier
/// name, job id or schema statement — operator detail travels the authenticated admin path only.
/// </para>
/// </summary>
public record UpgradeStatusModel(
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? ExpiresAt,
    string? Message);
