
namespace Forge.Core.Models;

public record AndonAlertResponseModel
{
    public int Id { get; init; }
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public AndonAlertType Type { get; init; }
    public AndonAlertStatus Status { get; init; }
    public string RequestedByName { get; init; } = string.Empty;
    public DateTimeOffset RequestedAt { get; init; }
    public string? AcknowledgedByName { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
    public string? ResolvedByName { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public decimal? ResponseTimeMinutes { get; init; }
    public decimal? ResolutionTimeMinutes { get; init; }
    public string? Notes { get; init; }
    public int? JobId { get; init; }
    public string? JobNumber { get; init; }
}
