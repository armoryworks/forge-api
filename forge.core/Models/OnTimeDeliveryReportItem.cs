namespace Forge.Core.Models;

public record OnTimeDeliveryReportItem(
    int TotalCompleted,
    int OnTime,
    int Late,
    decimal OnTimePercent);
