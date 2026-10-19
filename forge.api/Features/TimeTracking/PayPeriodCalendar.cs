using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public sealed record PayPeriodRange(DateOnly Start, DateOnly End);

public sealed record PayPeriodCalendar(string Type, DateOnly Anchor)
{
    public static readonly DateOnly DefaultAnchor = new(2026, 1, 5);

    public static async Task<PayPeriodCalendar> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var type = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == "pay_period_type").Select(s => s.Value).FirstOrDefaultAsync(ct);
        var anchorText = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == "pay_period_anchor").Select(s => s.Value).FirstOrDefaultAsync(ct);

        var anchor = DateTimeOffset.TryParse(anchorText, out var a)
            ? DateOnly.FromDateTime(a.Date)
            : DefaultAnchor;

        return new PayPeriodCalendar((type ?? "biweekly").ToLowerInvariant(), anchor);
    }

    public PayPeriodRange PeriodContaining(DateOnly date)
    {
        switch (Type)
        {
            case "weekly":
                return Anchored(date, 7);
            case "semimonthly":
                return date.Day <= 15
                    ? new PayPeriodRange(new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 15))
                    : new PayPeriodRange(new DateOnly(date.Year, date.Month, 16), MonthEnd(date));
            case "monthly":
                return new PayPeriodRange(new DateOnly(date.Year, date.Month, 1), MonthEnd(date));
            default:
                return Anchored(date, 14);
        }
    }

    public PayPeriodRange LastClosedPeriod(DateOnly through)
    {
        var containing = PeriodContaining(through);
        return containing.End == through ? containing : PeriodContaining(containing.Start.AddDays(-1));
    }

    public PayPeriodRange Previous(PayPeriodRange period) => PeriodContaining(period.Start.AddDays(-1));

    private PayPeriodRange Anchored(DateOnly date, int length)
    {
        var offset = (date.DayNumber - Anchor.DayNumber) % length;
        if (offset < 0) offset += length;
        var start = date.AddDays(-offset);
        return new PayPeriodRange(start, start.AddDays(length - 1));
    }

    private static DateOnly MonthEnd(DateOnly date)
        => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
