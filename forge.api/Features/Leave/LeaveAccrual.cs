using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public static class LeaveAccrual
{
    public static async Task<int> AccrueThroughAsync(AppDbContext db, DateOnly lockedThrough, CancellationToken ct)
    {
        var policies = await db.LeavePolicies
            .Where(p => p.IsActive && p.AccrualRatePerPayPeriod > 0)
            .ToListAsync(ct);
        if (policies.Count == 0)
            return 0;

        var staff = (await db.EmployeeProfiles.AsNoTracking()
                .Where(p => p.UserId != null && p.StartDate != null)
                .Join(db.Users.Where(u => u.IsActive), p => p.UserId, u => (int?)u.Id,
                    (p, u) => new { UserId = u.Id, StartDate = p.StartDate!.Value })
                .ToListAsync(ct))
            .GroupBy(s => s.UserId)
            .Select(g => (UserId: g.Key, Hired: DateOnly.FromDateTime(g.Min(s => s.StartDate).DateTime)))
            .ToList();
        if (staff.Count == 0)
            return 0;

        var userIds = staff.Select(s => s.UserId).ToList();
        var policyIds = policies.Select(p => p.Id).ToList();
        var balances = await db.LeaveBalances
            .Where(b => userIds.Contains(b.UserId) && policyIds.Contains(b.PolicyId))
            .ToListAsync(ct);

        var calendar = await PayPeriodCalendar.LoadAsync(db, ct);
        var lastClosed = calendar.LastClosedPeriod(lockedThrough);
        var credited = 0;

        foreach (var policy in policies)
        {
            var policyStart = DateOnly.FromDateTime(policy.CreatedAt.UtcDateTime);
            foreach (var (userId, hired) in staff)
            {
                var anchor = policy.AccrueFromHireDate || hired > policyStart ? hired : policyStart;
                var eligibleFrom = anchor.AddDays(Math.Max(policy.WaitingPeriodDays ?? 0, 0));

                var balance = balances.FirstOrDefault(b => b.UserId == userId && b.PolicyId == policy.Id);
                DateOnly? accruedThrough = balance is null || balance.LastAccrualDate == default
                    ? null
                    : DateOnly.FromDateTime(balance.LastAccrualDate.UtcDateTime);

                var periods = PeriodsToCredit(calendar, lastClosed, eligibleFrom, accruedThrough);
                if (periods == 0)
                    continue;

                if (balance is null)
                {
                    balance = new LeaveBalance { UserId = userId, PolicyId = policy.Id };
                    db.LeaveBalances.Add(balance);
                    balances.Add(balance);
                }

                for (var i = 0; i < periods; i++)
                {
                    var add = policy.AccrualRatePerPayPeriod;
                    if (policy.MaxBalance is decimal cap)
                        add = Math.Max(0m, Math.Min(add, cap - balance.Balance));
                    balance.Balance += add;
                    balance.AccruedThisYear += add;
                }

                balance.LastAccrualDate = new DateTimeOffset(lastClosed.End.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                credited++;
            }
        }

        return credited;
    }

    private static int PeriodsToCredit(
        PayPeriodCalendar calendar, PayPeriodRange lastClosed, DateOnly eligibleFrom, DateOnly? accruedThrough)
    {
        if (accruedThrough is null)
            return lastClosed.End >= eligibleFrom ? 1 : 0;

        var count = 0;
        for (var period = lastClosed;
             period.End > accruedThrough.Value && period.End >= eligibleFrom;
             period = calendar.Previous(period))
            count++;
        return count;
    }
}
