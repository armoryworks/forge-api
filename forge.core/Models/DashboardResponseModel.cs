namespace Forge.Core.Models;

public record DashboardResponseModel(
    List<DashboardTaskResponseModel> Tasks,
    List<StageCountResponseModel> Stages,
    List<TeamMemberResponseModel> Team,
    List<ActivityEntryResponseModel> Activity,
    List<DeadlineEntryResponseModel> Deadlines,
    DashboardKPIsResponseModel Kpis,
    int CustomerCount,
    int TrackTypeCount,
    int WorkCenterCount,
    int PartsWithOperationsCount,
    int QuoteCount,
    int ShipmentCount,
    int TotalJobCount);
