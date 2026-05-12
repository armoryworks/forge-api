using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class OvertimeService(AppDbContext db) : IOvertimeService
{
    public async Task<OvertimeBreakdownResponseModel> CalculateOvertimeAsync(
        int userId, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct)
    {
        // Get the user's overtime rule (via employee profile or default)
        var rule = await GetOvertimeRuleForUserAsync(userId, ct)
            ?? GetDefaultRule();

        // Get time entries for the week
        var weekStartUtc = weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var weekEndUtc = weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var entries = await db.TimeEntries.AsNoTracking()
            .Where(t => t.UserId == userId && t.TimerStart >= weekStartUtc && t.TimerStart < weekEndUtc && t.TimerStop != null)
            .OrderBy(t => t.TimerStart)
            .ToListAsync(ct);

        // Get hourly rate from employee profile
        var hourlyRate = await db.Set<EmployeeProfile>().AsNoTracking()
            .Where(ep => ep.UserId == userId)
            .Select(ep => ep.HourlyRate)
            .FirstOrDefaultAsync(ct) ?? 0m;

        // Get shift differential if applicable
        var shiftDiff = await db.Set<ShiftAssignment>().AsNoTracking()
            .Where(sa => sa.UserId == userId && sa.EffectiveFrom <= weekStart
                && (sa.EffectiveTo == null || sa.EffectiveTo >= weekEnd))
            .Select(sa => sa.ShiftDifferentialRate)
            .FirstOrDefaultAsync(ct) ?? 0m;

        var effectiveRate = hourlyRate + shiftDiff;

        // Group entries by date and calculate daily hours
        var dailyHours = new Dictionary<DateOnly, decimal>();
        foreach (var entry in entries)
        {
            var date = DateOnly.FromDateTime(entry.TimerStart!.Value.UtcDateTime);
            var hours = (decimal)(entry.TimerStop!.Value - entry.TimerStart.Value).TotalHours;
            dailyHours[date] = dailyHours.GetValueOrDefault(date) + hours;
        }

        var dailyBreakdown = new List<DailyOvertimeDetailResponseModel>();
        decimal totalRegular = 0, totalOt = 0, totalDt = 0;

        if (rule.ApplyDailyBeforeWeekly)
        {
            // California-style: apply daily thresholds first
            for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
            {
                var dayHours = dailyHours.GetValueOrDefault(date);
                var (reg, ot, dt) = CalculateDailyOt(dayHours, rule);
                totalRegular += reg;
                totalOt += ot;
                totalDt += dt;
                dailyBreakdown.Add(new DailyOvertimeDetailResponseModel(date, dayHours, reg, ot, dt));
            }
        }
        else
        {
            // Standard: weekly threshold only
            decimal weeklyAccum = 0;
            for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
            {
                var dayHours = dailyHours.GetValueOrDefault(date);
                var prevAccum = weeklyAccum;
                weeklyAccum += dayHours;

                decimal reg = 0, ot = 0, dt = 0;
                if (prevAccum >= rule.WeeklyThresholdHours)
                {
                    // All hours this day are OT/DT
                    if (rule.DoubletimeThresholdWeeklyHours.HasValue && prevAccum >= rule.DoubletimeThresholdWeeklyHours.Value)
                    {
                        dt = dayHours;
                    }
                    else if (rule.DoubletimeThresholdWeeklyHours.HasValue && weeklyAccum > rule.DoubletimeThresholdWeeklyHours.Value)
                    {
                        ot = rule.DoubletimeThresholdWeeklyHours.Value - prevAccum;
                        dt = weeklyAccum - rule.DoubletimeThresholdWeeklyHours.Value;
                    }
                    else
                    {
                        ot = dayHours;
                    }
                }
                else if (weeklyAccum > rule.WeeklyThresholdHours)
                {
                    reg = rule.WeeklyThresholdHours - prevAccum;
                    var remaining = dayHours - reg;
                    if (rule.DoubletimeThresholdWeeklyHours.HasValue && weeklyAccum > rule.DoubletimeThresholdWeeklyHours.Value)
                    {
                        ot = rule.DoubletimeThresholdWeeklyHours.Value - rule.WeeklyThresholdHours;
                        dt = weeklyAccum - rule.DoubletimeThresholdWeeklyHours.Value;
                    }
                    else
                    {
                        ot = remaining;
                    }
                }
                else
                {
                    reg = dayHours;
                }

                totalRegular += reg;
                totalOt += ot;
                totalDt += dt;
                dailyBreakdown.Add(new DailyOvertimeDetailResponseModel(date, dayHours, reg, ot, dt));
            }
        }

        return new OvertimeBreakdownResponseModel(
            RegularHours: Math.Round(totalRegular, 2),
            OvertimeHours: Math.Round(totalOt, 2),
            DoubletimeHours: Math.Round(totalDt, 2),
            RegularCost: Math.Round(totalRegular * effectiveRate, 2),
            OvertimeCost: Math.Round(totalOt * effectiveRate * rule.OvertimeMultiplier, 2),
            DoubletimeCost: Math.Round(totalDt * effectiveRate * rule.DoubletimeMultiplier, 2),
            TotalCost: Math.Round(
                totalRegular * effectiveRate
                + totalOt * effectiveRate * rule.OvertimeMultiplier
                + totalDt * effectiveRate * rule.DoubletimeMultiplier, 2),
            DailyBreakdown: dailyBreakdown);
    }

    private static (decimal regular, decimal overtime, decimal doubletime) CalculateDailyOt(
        decimal hours, OvertimeRule rule)
    {
        if (hours <= 0) return (0, 0, 0);

        decimal reg, ot = 0, dt = 0;

        if (hours <= rule.DailyThresholdHours)
        {
            reg = hours;
        }
        else if (rule.DoubletimeThresholdDailyHours.HasValue && hours > rule.DoubletimeThresholdDailyHours.Value)
        {
            reg = rule.DailyThresholdHours;
            ot = rule.DoubletimeThresholdDailyHours.Value - rule.DailyThresholdHours;
            dt = hours - rule.DoubletimeThresholdDailyHours.Value;
        }
        else
        {
            reg = rule.DailyThresholdHours;
            ot = hours - rule.DailyThresholdHours;
        }

        return (reg, ot, dt);
    }

    private async Task<OvertimeRule?> GetOvertimeRuleForUserAsync(int userId, CancellationToken ct)
    {
        // For now, return the default rule. Future: per-user or per-department rules.
        return await db.Set<OvertimeRule>().AsNoTracking()
            .Where(r => r.IsDefault && r.DeletedAt == null)
            .FirstOrDefaultAsync(ct);
    }

    private static OvertimeRule GetDefaultRule() => new()
    {
        Name = "Default",
        DailyThresholdHours = 8,
        WeeklyThresholdHours = 40,
        OvertimeMultiplier = 1.5m,
        DoubletimeThresholdDailyHours = 12,
        DoubletimeMultiplier = 2.0m,
        IsDefault = true,
        ApplyDailyBeforeWeekly = true,
    };
}
