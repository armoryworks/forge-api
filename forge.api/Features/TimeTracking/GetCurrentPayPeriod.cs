using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record GetCurrentPayPeriodQuery : IRequest<PayPeriodResponseModel>;

public class GetCurrentPayPeriodHandler(AppDbContext db) : IRequestHandler<GetCurrentPayPeriodQuery, PayPeriodResponseModel>
{
    public async Task<PayPeriodResponseModel> Handle(GetCurrentPayPeriodQuery request, CancellationToken ct)
    {
        var typeSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "pay_period_type", ct);
        var anchorSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "pay_period_anchor", ct);

        var type = typeSetting?.Value ?? "biweekly";
        var anchor = anchorSetting is not null && DateTimeOffset.TryParse(anchorSetting.Value, out var a)
            ? a
            : new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero); // default Monday anchor

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset periodStart;
        DateTimeOffset periodEnd;

        switch (type.ToLower())
        {
            case "weekly":
                var daysSinceAnchor = (int)(now.Date - anchor.Date).TotalDays;
                var weekOffset = daysSinceAnchor % 7;
                if (weekOffset < 0) weekOffset += 7;
                periodStart = now.Date.AddDays(-weekOffset);
                periodEnd = periodStart.AddDays(7).AddSeconds(-1);
                break;

            case "biweekly":
                var daysSinceAnchorBi = (int)(now.Date - anchor.Date).TotalDays;
                var biweekOffset = daysSinceAnchorBi % 14;
                if (biweekOffset < 0) biweekOffset += 14;
                periodStart = now.Date.AddDays(-biweekOffset);
                periodEnd = periodStart.AddDays(14).AddSeconds(-1);
                break;

            case "semimonthly":
                if (now.Day <= 15)
                {
                    periodStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                    periodEnd = new DateTimeOffset(now.Year, now.Month, 15, 23, 59, 59, TimeSpan.Zero);
                }
                else
                {
                    periodStart = new DateTimeOffset(now.Year, now.Month, 16, 0, 0, 0, TimeSpan.Zero);
                    var lastDay = DateTime.DaysInMonth(now.Year, now.Month);
                    periodEnd = new DateTimeOffset(now.Year, now.Month, lastDay, 23, 59, 59, TimeSpan.Zero);
                }
                break;

            case "monthly":
                periodStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                var last = DateTime.DaysInMonth(now.Year, now.Month);
                periodEnd = new DateTimeOffset(now.Year, now.Month, last, 23, 59, 59, TimeSpan.Zero);
                break;

            default:
                goto case "biweekly";
        }

        var daysRemaining = (int)(periodEnd.Date - now.Date).TotalDays;

        return new PayPeriodResponseModel(type, periodStart, periodEnd, daysRemaining);
    }
}
