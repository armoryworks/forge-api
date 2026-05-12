using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAtpService
{
    Task<AtpResult> CalculateAtpAsync(int partId, decimal quantity, CancellationToken ct = default);
    Task<DateOnly?> GetEarliestAvailableDateAsync(int partId, decimal quantity, CancellationToken ct = default);
    Task<List<AtpBucket>> GetAtpTimelineAsync(int partId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
