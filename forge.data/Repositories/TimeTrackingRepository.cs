using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class TimeTrackingRepository(AppDbContext db) : ITimeTrackingRepository
{
    public async Task<List<TimeEntryResponseModel>> GetTimeEntriesAsync(int? userId, int? jobId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.TimeEntries.Include(t => t.Job).AsQueryable();

        if (userId.HasValue)
            query = query.Where(t => t.UserId == userId.Value);

        if (jobId.HasValue)
            query = query.Where(t => t.JobId == jobId.Value);

        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);

        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);

        var entries = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var userIds = entries.Select(t => t.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return entries.Select(t => ToTimeEntryResponse(t, users)).ToList();
    }

    public async Task<TimeEntryResponseModel?> GetTimeEntryByIdAsync(int id, CancellationToken ct)
    {
        var entry = await db.TimeEntries.Include(t => t.Job)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (entry is null) return null;

        var users = await db.Users
            .Where(u => u.Id == entry.UserId)
            .ToDictionaryAsync(u => u.Id, ct);

        return ToTimeEntryResponse(entry, users);
    }

    public Task<TimeEntry?> FindTimeEntryAsync(int id, CancellationToken ct)
        => db.TimeEntries.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<TimeEntry?> GetActiveTimerAsync(int userId, CancellationToken ct)
        => db.TimeEntries.FirstOrDefaultAsync(t =>
            t.UserId == userId && t.TimerStart != null && t.TimerStop == null, ct);

    public async Task AddTimeEntryAsync(TimeEntry entry, CancellationToken ct)
    {
        // Denormalize work center from the operation, frozen at write time.
        // Reporting filters time by work center without joining through
        // Operation (whose WorkCenterId can be reassigned later as routing
        // is edited). Skip if the caller already supplied an override —
        // future kiosk flows may pass the kiosk's bound center as the
        // physical truth even when it differs from the operation's planned
        // center.
        if (entry.OperationId.HasValue && !entry.WorkCenterId.HasValue)
        {
            entry.WorkCenterId = await db.Operations
                .Where(o => o.Id == entry.OperationId.Value)
                .Select(o => o.WorkCenterId)
                .FirstOrDefaultAsync(ct);
        }

        await db.TimeEntries.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ClockEventResponseModel>> GetClockEventsAsync(int? userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.ClockEvents.AsQueryable();

        if (userId.HasValue)
            query = query.Where(c => c.UserId == userId.Value);

        if (from.HasValue)
        {
            var fromDate = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(c => c.Timestamp >= fromDate);
        }

        if (to.HasValue)
        {
            var toDate = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(c => c.Timestamp <= toDate);
        }

        var events = await query
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync(ct);

        var userIds = events.Select(c => c.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return events.Select(c =>
        {
            var userName = users.TryGetValue(c.UserId, out var user)
                ? $"{user.FirstName} {user.LastName}" : "Unknown";
            return new ClockEventResponseModel(
                c.Id, c.UserId, userName, c.EventTypeCode,
                c.Reason, c.ScanMethod, c.Timestamp, c.Source);
        }).ToList();
    }

    public Task<ClockEvent?> GetLastClockEventAsync(int userId, CancellationToken ct)
        => db.ClockEvents
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.Timestamp)
            .FirstOrDefaultAsync(ct);

    public async Task AddClockEventAsync(ClockEvent clockEvent, CancellationToken ct)
    {
        await db.ClockEvents.AddAsync(clockEvent, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    private static TimeEntryResponseModel ToTimeEntryResponse(TimeEntry t, Dictionary<int, ApplicationUser> users)
    {
        var userName = users.TryGetValue(t.UserId, out var user)
            ? $"{user.FirstName} {user.LastName}" : "Unknown";
        return new TimeEntryResponseModel
        {
            Id = t.Id,
            JobId = t.JobId,
            JobNumber = t.Job?.JobNumber,
            UserId = t.UserId,
            UserName = userName,
            Date = t.Date,
            DurationMinutes = t.DurationMinutes,
            Category = t.Category,
            Notes = t.Notes,
            TimerStart = t.TimerStart,
            TimerStop = t.TimerStop,
            IsManual = t.IsManual,
            IsLocked = t.IsLocked,
            CreatedAt = t.CreatedAt,
            OperationId = t.OperationId,
            EntryType = t.EntryType.ToString(),
            LaborCost = t.LaborCost,
            BurdenCost = t.BurdenCost,
        };
    }
}
