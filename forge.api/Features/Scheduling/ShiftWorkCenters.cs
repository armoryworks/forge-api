using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public static class ShiftWorkCenters
{
    public const int AllDays = (int)(DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday
        | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Saturday | DaysOfWeek.Sunday);

    public static async Task<List<string>> ApplyAsync(
        AppDbContext db, Shift shift, List<ShiftWorkCenterModel> requested, CancellationToken ct)
    {
        var ids = requested.Select(r => r.WorkCenterId).ToList();
        var known = await db.WorkCenters.Where(w => ids.Contains(w.Id)).Select(w => w.Id).ToListAsync(ct);
        var missing = ids.Except(known).ToList();
        if (missing.Count > 0)
            throw new KeyNotFoundException($"Work center {missing[0]} not found.");

        var existing = shift.Id == 0
            ? new List<WorkCenterShift>()
            : await db.WorkCenterShifts.Where(w => w.ShiftId == shift.Id).ToListAsync(ct);
        var changed = new List<string>();

        foreach (var row in existing.Where(e => !ids.Contains(e.WorkCenterId)).ToList())
        {
            db.WorkCenterShifts.Remove(row);
            changed.Add("work centers");
        }

        foreach (var item in requested)
        {
            var row = existing.FirstOrDefault(e => e.WorkCenterId == item.WorkCenterId);
            if (row is null)
            {
                db.WorkCenterShifts.Add(new WorkCenterShift
                {
                    Shift = shift,
                    WorkCenterId = item.WorkCenterId,
                    DaysOfWeek = (DaysOfWeek)item.DaysOfWeek,
                });
                changed.Add("work centers");
            }
            else if ((int)row.DaysOfWeek != item.DaysOfWeek)
            {
                row.DaysOfWeek = (DaysOfWeek)item.DaysOfWeek;
                changed.Add("work center days");
            }
        }

        return changed.Distinct().ToList();
    }

    public static async Task<Dictionary<int, List<ShiftWorkCenterModel>>> LoadAsync(
        AppDbContext db, IReadOnlyCollection<int> shiftIds, CancellationToken ct)
    {
        var rows = await db.WorkCenterShifts.AsNoTracking()
            .Where(w => shiftIds.Contains(w.ShiftId))
            .OrderBy(w => w.WorkCenter.Name)
            .Select(w => new { w.ShiftId, w.WorkCenterId, w.DaysOfWeek, w.WorkCenter.Name })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.ShiftId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new ShiftWorkCenterModel(r.WorkCenterId, (int)r.DaysOfWeek, r.Name)).ToList());
    }

    public static ShiftResponseModel ToResponse(Shift shift, List<ShiftWorkCenterModel>? workCenters) =>
        new(
            shift.Id, shift.Name, shift.StartTime, shift.EndTime,
            shift.BreakMinutes,
            shift.NetHours > 0 ? shift.NetHours : ShiftHours.Net(shift.StartTime, shift.EndTime, shift.BreakMinutes),
            shift.IsActive,
            workCenters ?? [],
            shift.WorkingCalendarId);
}
