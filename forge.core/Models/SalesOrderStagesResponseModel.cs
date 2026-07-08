namespace Forge.Core.Models;

/// <summary>
/// The staged-schedule view for a sales order: the editable stage layer plus the
/// advisory derived backward-scheduling timeline, so the UI can show
/// planned-vs-derived drift. <see cref="IsActivated"/> is true once any stage
/// exists (the staged schedule has been seeded from the derived milestones).
/// </summary>
public record SalesOrderStagesResponseModel(
    int SalesOrderId,
    bool IsActivated,
    List<SalesOrderStageResponseModel> Stages,
    List<ScheduleMilestoneModel> DerivedTimeline);
