namespace Forge.Core.Models;

public record MaintenanceReportItem(
    int AssetId,
    string AssetName,
    int ScheduledCount,
    int CompletedCount,
    int OverdueCount,
    decimal TotalCost,
    int DowntimeEvents,
    int FailureCount,
    decimal DowntimeHours,
    decimal UnplannedDowntimeHours,
    decimal? MtbfHours);
