
namespace Forge.Core.Entities;

public class ClockEvent : BaseEntity
{
    public int UserId { get; set; }
    public ClockEventType EventType { get; set; }

    /// <summary>
    /// Reference-data-driven event type code (replaces enum).
    /// Maps to reference_data group "clock_event_type".
    /// </summary>
    public string EventTypeCode { get; set; } = string.Empty;

    public int? OperationId { get; set; }
    public string? Reason { get; set; }
    public string? ScanMethod { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Source { get; set; }
}
