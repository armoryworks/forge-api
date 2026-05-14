using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ITimeTrackingRepository
{
    // Time entries
    Task<List<TimeEntryResponseModel>> GetTimeEntriesAsync(int? userId, int? jobId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<TimeEntryResponseModel?> GetTimeEntryByIdAsync(int id, CancellationToken ct);
    Task<TimeEntry?> FindTimeEntryAsync(int id, CancellationToken ct);
    Task<TimeEntry?> GetActiveTimerAsync(int userId, CancellationToken ct);
    Task AddTimeEntryAsync(TimeEntry entry, CancellationToken ct);

    // Clock events
    Task<List<ClockEventResponseModel>> GetClockEventsAsync(int? userId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<ClockEvent?> GetLastClockEventAsync(int userId, CancellationToken ct);
    Task AddClockEventAsync(ClockEvent clockEvent, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
