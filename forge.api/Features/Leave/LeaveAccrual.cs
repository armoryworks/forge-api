using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leave;

public static class LeaveAccrual
{
    public const string NoHireDateSource = "leave-accrual";

    public static async Task<int> AccrueThroughAsync(AppDbContext db, DateOnly lockedThrough, CancellationToken ct)
    {
        var policies = await db.LeavePolicies
            .Where(p => p.IsActive && p.AccrualRatePerPayPeriod > 0)
            .ToListAsync(ct);
        if (policies.Count == 0)
            return 0;

        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && !u.IsNonEmployee)
            .WithoutServiceAccounts(db)
            .Select(u => new { u.Id, u.CreatedAt, Name = u.LastName + ", " + u.FirstName })
            .ToListAsync(ct);
        var hireDates = (await db.EmployeeProfiles.AsNoTracking()
                .Where(p => p.UserId != null && p.StartDate != null)
                .Select(p => new { UserId = p.UserId!.Value, StartDate = p.StartDate!.Value })
                .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => DateOnly.FromDateTime(g.Min(p => p.StartDate).DateTime));
        var staff = users
            .Select(u => hireDates.TryGetValue(u.Id, out var hiredOn)
                ? (UserId: u.Id, Hired: hiredOn, HasHireDate: true)
                : (UserId: u.Id, Hired: DateOnly.FromDateTime(u.CreatedAt.UtcDateTime), HasHireDate: false))
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
        var noHireDate = new Dictionary<int, DateOnly>();

        foreach (var policy in policies)
        {
            var policyStart = DateOnly.FromDateTime(policy.CreatedAt.UtcDateTime);
            foreach (var (userId, hired, hasHireDate) in staff)
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

                if (!hasHireDate && accruedThrough is null)
                {
                    noHireDate.TryAdd(userId, hired);
                    db.LogActivityAt(
                        "leave-accrual-no-hire-date",
                        $"No hire date, accruing {policy.Name} from account creation ({hired:yyyy-MM-dd})",
                        ("ApplicationUser", userId));
                }

                balance.LastAccrualDate = new DateTimeOffset(lastClosed.End.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                credited++;
            }
        }

        if (noHireDate.Count > 0)
            await NotifyAdminsOfMissingHireDatesAsync(
                db, noHireDate, users.ToDictionary(u => u.Id, u => u.Name), ct);

        return credited;
    }

    private static async Task NotifyAdminsOfMissingHireDatesAsync(
        AppDbContext db, Dictionary<int, DateOnly> accruingFrom, Dictionary<int, string> names, CancellationToken ct)
    {
        var userIds = accruingFrom.Keys.ToList();
        var alreadyNoticed = await db.Notifications.AsNoTracking()
            .Where(n => n.Source == NoHireDateSource && n.EntityType == "users"
                && n.EntityId != null && userIds.Contains(n.EntityId.Value))
            .Select(n => n.EntityId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var admins = await LeaveNotifications.UserIdsInRolesAsync(db, ["Admin"], ct);
        foreach (var (userId, from) in accruingFrom.Where(kv => !alreadyNoticed.Contains(kv.Key)))
        {
            var name = names.GetValueOrDefault(userId) ?? $"User {userId}";
            foreach (var adminId in admins)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = adminId,
                    Type = "leave",
                    Severity = "warning",
                    Source = NoHireDateSource,
                    Title = "No hire date",
                    Message = $"No hire date for {name}: accruing leave from {from:MM/dd/yyyy}. Add the hire date on the user to correct it.",
                    EntityType = "users",
                    EntityId = userId,
                });
            }
        }
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
