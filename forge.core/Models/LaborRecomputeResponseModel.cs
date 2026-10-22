namespace Forge.Core.Models;

public record LaborRecomputeResponseModel(
    bool Applied,
    int EntriesChanged,
    int LockedSkipped,
    decimal LaborMinutesBefore,
    decimal LaborMinutesAfter,
    decimal CostBefore,
    decimal CostAfter,
    IReadOnlyList<LaborRecomputeJobChangeResponseModel> Jobs);
