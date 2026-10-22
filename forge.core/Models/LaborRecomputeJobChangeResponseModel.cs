namespace Forge.Core.Models;

public record LaborRecomputeJobChangeResponseModel(
    int JobId,
    string JobNumber,
    int EntriesChanged,
    decimal LaborMinutesBefore,
    decimal LaborMinutesAfter,
    decimal LaborCostBefore,
    decimal LaborCostAfter,
    decimal BurdenCostBefore,
    decimal BurdenCostAfter);
