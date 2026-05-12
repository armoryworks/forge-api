namespace Forge.Core.Models;

public record AndonBoardWorkCenterResponseModel
{
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public string Status { get; init; } = "Green";
    public string? CurrentJobNumber { get; init; }
    public string? CurrentOperationName { get; init; }
    public decimal? OeePercent { get; init; }
    public List<AndonAlertResponseModel> ActiveAlerts { get; init; } = [];
    public int? DailyTarget { get; init; }
    public int? DailyActual { get; init; }
}
