namespace Forge.Core.Models;

public record EmployeeProductivityReportItem(
    int UserId,
    string UserName,
    decimal TotalHours,
    int JobsCompleted,
    decimal AvgHoursPerJob,
    decimal OnTimePercentage);
