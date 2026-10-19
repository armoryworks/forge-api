namespace Forge.Core.Interfaces;

public interface IWorkedHoursService
{
    Task<Dictionary<int, Dictionary<DateOnly, decimal>>> DailyHoursAsync(
        IReadOnlyCollection<int> userIds, DateOnly from, DateOnly to, CancellationToken ct);
}
