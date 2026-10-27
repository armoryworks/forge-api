using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Employees;

public record RehireEmployeeCommand(
    int EmployeeId,
    string? Kind,
    RehireEmployeeRequestModel Request,
    int CallerUserId,
    bool CallerIsAdmin) : IRequest;

/// <summary>
/// Reverses a termination, or cancels one that is still scheduled. The user is reactivated
/// (sign-in credentials that were withdrawn stay withdrawn and are set up again), an optional
/// rehire date becomes the new start date, and leave balances restart accruing from the last
/// closed pay period so the time away earns nothing. The termination details move to the
/// activity log and audit trail.
/// </summary>
public class RehireEmployeeHandler(AppDbContext db, ISystemAuditWriter audit, IClock clock)
    : IRequestHandler<RehireEmployeeCommand>
{
    public async Task Handle(RehireEmployeeCommand command, CancellationToken ct)
    {
        var profile = await EmployeeTarget.ResolveAsync(
            db, command.EmployeeId, command.Kind, command.CallerUserId, command.CallerIsAdmin, ct);

        if (profile.TerminationDate is null)
            throw new InvalidOperationException("This employee has no termination to reverse.");

        var wasApplied = profile.TerminationAppliedAt is not null;
        var previous = new
        {
            terminationDate = profile.TerminationDate,
            reason = profile.TerminationReasonCode,
            notes = profile.TerminationNotes,
            profile.RehireEligible,
            profile.FinalPayRequired,
            profile.FinalPayProcessedAt,
            profile.TerminatedByUserId,
            appliedAt = profile.TerminationAppliedAt,
        };

        if (wasApplied && profile.UserId is int userId)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is not null)
            {
                user.IsActive = true;
                user.UpdatedAt = clock.UtcNow;
            }

            await ResetAccrualAnchorAsync(userId, ct);
        }

        if (wasApplied && command.Request.RehireDate is DateOnly rehired)
            profile.StartDate = new DateTimeOffset(rehired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        profile.TerminationDate = null;
        profile.TerminationReasonCode = null;
        profile.TerminationNotes = null;
        profile.RehireEligible = null;
        profile.FinalPayRequired = null;
        profile.FinalPayProcessedAt = null;
        profile.TerminatedByUserId = null;
        profile.TerminationAppliedAt = null;

        db.LogActivityAt(
            wasApplied ? "rehired" : "termination-cancelled",
            wasApplied
                ? $"Rehired{(command.Request.RehireDate is DateOnly d ? $" effective {d:yyyy-MM-dd}" : string.Empty)} (was terminated {previous.terminationDate:yyyy-MM-dd}, {previous.reason})"
                : $"Scheduled termination for {previous.terminationDate:yyyy-MM-dd} cancelled",
            ("Employee", profile.UserId ?? profile.Id));
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            EmployeeTermination.RehireAuditAction,
            command.CallerUserId,
            entityType: "EmployeeProfile",
            entityId: profile.Id,
            details: JsonSerializer.Serialize(new { profile.UserId, wasApplied, command.Request.RehireDate, previous }),
            ct: ct);
    }

    private async Task ResetAccrualAnchorAsync(int userId, CancellationToken ct)
    {
        var balances = await db.LeaveBalances.Where(b => b.UserId == userId).ToListAsync(ct);
        if (balances.Count == 0)
            return;

        var calendar = await PayPeriodCalendar.LoadAsync(db, ct);
        var lockedThrough = await TimeLockBoundary.GetAsync(db, ct)
            ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var lastClosed = calendar.LastClosedPeriod(lockedThrough);
        var anchor = new DateTimeOffset(lastClosed.End.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        foreach (var balance in balances)
            balance.LastAccrualDate = anchor;
    }
}
