using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Middleware;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Services;

/// <summary>
/// Decides, at each sign-in, whether a user whose role requires MFA may keep working
/// while they enrol. The window opens the first time the user signs in flagged and not
/// enrolled, lasts <c>mfa.enrollment_grace_days</c> days, and is cleared once the user
/// enrols or stops being flagged. After it closes the enrolment hold applies.
/// </summary>
public interface IMfaEnrollmentGrace
{
    Task<MfaEnrollmentState> EvaluateAsync(ApplicationUser user, CancellationToken ct = default);
}

/// <summary>
/// Where a user stands with the MFA enrolment policy: held to enrolment, inside a grace
/// window ending at <see cref="GraceEndsAt"/>, or not required to enrol.
/// </summary>
public record MfaEnrollmentState(bool Hold, DateTimeOffset? GraceEndsAt)
{
    public static readonly MfaEnrollmentState NotRequired = new(false, null);
}

public class MfaEnrollmentGrace(
    AppDbContext db,
    ISystemAuditWriter auditWriter,
    IClock clock) : IMfaEnrollmentGrace
{
    public const string StartedAction = "MfaEnrollmentGraceStarted";
    public const string LapsedAction = "MfaEnrollmentGraceLapsed";

    public async Task<MfaEnrollmentState> EvaluateAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (!await MfaEnrollmentGateMiddleware.IsRequiredAsync(db, user, ct))
        {
            if (user.MfaEnrollmentGraceStartedAt is not null)
            {
                user.MfaEnrollmentGraceStartedAt = null;
                await db.SaveChangesAsync(ct);
            }
            return MfaEnrollmentState.NotRequired;
        }

        var now = clock.UtcNow;
        var graceDays = await MfaRolePolicy.GetGraceDaysAsync(db, ct);

        if (user.MfaEnrollmentGraceStartedAt is null)
        {
            if (graceDays <= 0)
                return new MfaEnrollmentState(true, null);

            user.MfaEnrollmentGraceStartedAt = now;
            await WriteAuditAsync(StartedAction, user, now, now.AddDays(graceDays), ct);
        }

        var startedAt = user.MfaEnrollmentGraceStartedAt.Value;
        var deadline = startedAt.AddDays(graceDays);
        if (now < deadline)
            return new MfaEnrollmentState(false, deadline);

        var lapseRecorded = await db.AuditLogEntries.AsNoTracking()
            .AnyAsync(e => e.UserId == user.Id && e.Action == LapsedAction && e.CreatedAt >= deadline, ct);
        if (!lapseRecorded)
            await WriteAuditAsync(LapsedAction, user, startedAt, deadline, ct);

        return new MfaEnrollmentState(true, null);
    }

    private Task WriteAuditAsync(string action, ApplicationUser user, DateTimeOffset startedAt, DateTimeOffset deadline, CancellationToken ct)
        => auditWriter.WriteAsync(action, user.Id,
            entityType: "ApplicationUser",
            entityId: user.Id,
            details: JsonSerializer.Serialize(new
            {
                email = user.Email,
                graceStartedAt = startedAt,
                graceEndsAt = deadline,
            }),
            ct: ct);
}
