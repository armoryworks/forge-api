namespace Forge.Core.Models;

public record TimeEntryResponseModel
{
    public int Id { get; init; }
    public int? JobId { get; init; }
    public string? JobNumber { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public int DurationMinutes { get; init; }
    public string? Category { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset? TimerStart { get; init; }
    public DateTimeOffset? TimerStop { get; init; }
    public bool IsManual { get; init; }
    public bool IsLocked { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int? OperationId { get; init; }
    public string? EntryType { get; init; }
    public decimal LaborCost { get; init; }
    public decimal BurdenCost { get; init; }
}
