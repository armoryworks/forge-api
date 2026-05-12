using Forge.Core.Enums;

namespace Forge.Core.Models;

public record MrpRunOptions(
    MrpRunType RunType = MrpRunType.Full,
    int PlanningHorizonDays = 90,
    List<int>? PartIds = null,
    bool IsSimulation = false,
    int? InitiatedByUserId = null
);
