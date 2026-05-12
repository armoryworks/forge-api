namespace Forge.Core.Models;

public record EmployeeStatsResponseModel(
    decimal HoursThisPeriod,
    int CompliancePercent,
    int ActiveJobCount,
    int TrainingProgressPercent,
    int OutstandingExpenseCount,
    decimal OutstandingExpenseTotal);
