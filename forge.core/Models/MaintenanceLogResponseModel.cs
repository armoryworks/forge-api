namespace Forge.Core.Models;

public record MaintenanceLogResponseModel(
    int Id,
    int MaintenanceScheduleId,
    string PerformedByName,
    DateTimeOffset PerformedAt,
    decimal? HoursAtService,
    string? Notes,
    decimal? Cost);
