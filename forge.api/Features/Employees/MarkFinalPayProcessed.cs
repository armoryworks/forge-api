using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Employees;

public record MarkFinalPayProcessedCommand(int EmployeeId, string? Kind, int CallerUserId, bool CallerIsAdmin) : IRequest;

/// <summary>
/// Records that payroll issued the final paycheck. The paid-leave balances it paid out are
/// set to zero so they cannot be paid twice; the hours are kept in the activity log.
/// </summary>
public class MarkFinalPayProcessedHandler(AppDbContext db, IClock clock) : IRequestHandler<MarkFinalPayProcessedCommand>
{
    public async Task Handle(MarkFinalPayProcessedCommand request, CancellationToken ct)
    {
        var profile = await EmployeeTarget.ResolveAsync(
            db, request.EmployeeId, request.Kind, request.CallerUserId, request.CallerIsAdmin, ct);

        if (profile.TerminationAppliedAt is null)
            throw new InvalidOperationException("Final pay can be marked processed only after the termination takes effect.");
        if (profile.FinalPayProcessedAt is not null)
            throw new InvalidOperationException("Final pay is already marked processed.");

        decimal paidOut = 0;
        if (profile.UserId is int userId)
        {
            var balances = await db.LeaveBalances
                .Where(b => b.UserId == userId && b.Policy.IsPaidLeave && b.Balance > 0)
                .ToListAsync(ct);
            foreach (var balance in balances)
            {
                paidOut += balance.Balance;
                balance.Balance = 0;
            }
        }

        profile.FinalPayProcessedAt = clock.UtcNow;
        db.LogActivityAt(
            "final-pay-processed",
            $"Final pay processed; {paidOut:0.##} h of paid leave paid out",
            ("Employee", profile.UserId ?? profile.Id));
        await db.SaveChangesAsync(ct);
    }
}
