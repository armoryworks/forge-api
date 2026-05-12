using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface IBackwardSchedulingService
{
    Task<List<ScheduleMilestone>> CalculateMilestonesAsync(int salesOrderLineId, CancellationToken ct = default);
}
