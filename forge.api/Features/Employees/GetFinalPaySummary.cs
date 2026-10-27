using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record GetFinalPaySummaryQuery(int EmployeeId, string? Kind, int CallerUserId, bool CallerIsAdmin)
    : IRequest<FinalPaySummaryResponseModel>;

/// <summary>
/// The figures payroll needs for a final paycheck: hours recorded after the last locked pay
/// period up to the termination date (or today while none is set), and the paid-leave balance
/// to pay out, valued at the hourly rate when the profile has one.
/// </summary>
public class GetFinalPaySummaryHandler(AppDbContext db, Core.Interfaces.IClock clock)
    : IRequestHandler<GetFinalPaySummaryQuery, FinalPaySummaryResponseModel>
{
    public async Task<FinalPaySummaryResponseModel> Handle(GetFinalPaySummaryQuery request, CancellationToken ct)
    {
        var profile = await EmployeeTarget.ResolveAsync(
            db, request.EmployeeId, request.Kind, request.CallerUserId, request.CallerIsAdmin, ct, refuseSelf: false, refuseAdminTarget: false);

        var end = profile.TerminationDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var lockedThrough = await TimeLockBoundary.GetAsync(db, ct);
        var start = lockedThrough is DateOnly locked
            ? locked.AddDays(1)
            : (await PayPeriodCalendar.LoadAsync(db, ct)).PeriodContaining(end).Start;

        decimal hours = 0;
        var lines = new List<FinalPayLeaveLineModel>();
        if (profile.UserId is int userId)
        {
            var minutes = start <= end
                ? await db.TimeEntries.AsNoTracking()
                    .Where(t => t.UserId == userId && t.Date >= start && t.Date <= end)
                    .SumAsync(t => (int?)t.DurationMinutes, ct) ?? 0
                : 0;
            hours = Math.Round(minutes / 60m, 2);

            lines = await db.LeaveBalances.AsNoTracking()
                .Where(b => b.UserId == userId && b.Policy.IsPaidLeave && b.Balance > 0)
                .OrderBy(b => b.Policy.Name)
                .Select(b => new FinalPayLeaveLineModel(b.PolicyId, b.Policy.Name, b.Balance))
                .ToListAsync(ct);
        }

        var payout = lines.Sum(l => l.BalanceHours);
        decimal? amount = profile.HourlyRate is decimal rate ? Math.Round(payout * rate, 2) : null;

        return new FinalPaySummaryResponseModel(
            profile.Id,
            profile.TerminationDate,
            profile.FinalPayRequired ?? false,
            profile.FinalPayProcessedAt,
            start,
            end,
            hours,
            payout,
            profile.HourlyRate,
            amount,
            lines);
    }
}
