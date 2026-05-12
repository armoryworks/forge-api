namespace Forge.Core.Models;

public record MaintenanceLogListItemResponseModel(
    int Id,
    string ScheduleName,
    DateTimeOffset PerformedAt,
    string PerformedByName,
    decimal? HoursSpent,
    string? Notes,
    decimal? Cost);
