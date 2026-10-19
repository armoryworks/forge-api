using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public static class LeaveBalanceRules
{
    public static async Task EnsureCoveredAsync(
        AppDbContext db, LeavePolicy policy, int userId, decimal hours, int? requestId, bool countPending,
        CancellationToken ct)
    {
        if (!policy.IsPaidLeave)
            return;

        var balance = await db.LeaveBalances.AsNoTracking()
            .Where(b => b.UserId == userId && b.PolicyId == policy.Id)
            .Select(b => (decimal?)b.Balance)
            .FirstOrDefaultAsync(ct);
        if (balance is null && policy.AccrualRatePerPayPeriod <= 0)
            return;

        var pending = countPending
            ? await db.LeaveRequests.AsNoTracking()
                .Where(r => r.UserId == userId && r.PolicyId == policy.Id
                    && r.Status == LeaveRequestStatus.Pending && r.Id != (requestId ?? 0))
                .SumAsync(r => r.Hours, ct)
            : 0m;

        var available = (balance ?? 0m) - pending;
        if (hours > available)
            throw new InvalidOperationException(
                $"Not enough {policy.Name} balance: {hours:0.##} h requested, {Math.Max(available, 0m):0.##} h available.");
    }
}
