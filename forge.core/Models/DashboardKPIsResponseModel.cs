namespace Forge.Core.Models;

public record DashboardKPIsResponseModel(
    int ActiveCount,
    int ActiveChange,
    int OverdueCount,
    int OverdueChange,
    string TotalHours,
    string HoursStatus);
