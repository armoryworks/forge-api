using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public class UserDeactivation(
    AppDbContext db,
    IMediator mediator,
    IClockEventTypeService clockEventTypes,
    IClock clock,
    ISessionStore sessionStore)
{
    public const string AdminRole = "Admin";
    public const string ClockOutSource = "system-deactivation";
    public const string ClockOutReason = "User deactivated";
    public const string LastAdminMessage = "At least one active Admin is required. Give another active user the Admin role first.";

    public static async Task EnsureNotLastActiveAdminAsync(AppDbContext db, int userId, CancellationToken ct)
    {
        var adminRoleIds = db.Roles.Where(r => r.Name == AdminRole).Select(r => r.Id);

        var targetIsActiveAdmin = await db.Users
            .Where(u => u.Id == userId && u.IsActive)
            .AnyAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id && adminRoleIds.Contains(ur.RoleId)), ct);
        if (!targetIsActiveAdmin)
            return;

        var otherActiveAdmin = await db.Users
            .Where(u => u.Id != userId && u.IsActive)
            .WithoutServiceAccounts(db)
            .AnyAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id && adminRoleIds.Contains(ur.RoleId)), ct);
        if (!otherActiveAdmin)
            throw new InvalidOperationException(LastAdminMessage);
    }

    public async Task<UserDeactivationResult> ApplyAsync(int userId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var openJobs = await db.Jobs
            .Where(j => j.AssigneeId == userId && !j.IsArchived && j.CompletedDate == null)
            .ToListAsync(ct);
        foreach (var job in openJobs)
            job.AssigneeId = null;

        var stopped = await mediator.Send(
            new StopActiveTimerCommand(userId, now, Reason: ClockOutReason, IncludeOperationTimers: true), ct);

        var clockedOut = await AddClockOutIfClockedInAsync(userId, now, ct);

        await db.SaveChangesAsync(ct);

        await sessionStore.RevokeAllUserSessionsAsync(userId, ct);

        return new UserDeactivationResult(openJobs.Count, stopped is not null, clockedOut);
    }

    private async Task<bool> AddClockOutIfClockedInAsync(int userId, DateTimeOffset now, CancellationToken ct)
    {
        var latest = await ClockStateRules.LatestEventAsync(db, userId, ct);
        var definitions = await clockEventTypes.GetAllAsync(ct);
        var (_, countsAsActive) = ClockStateRules.ResolveStatus(latest, definitions);
        if (!countsAsActive)
            return false;

        var clockOut = definitions.FirstOrDefault(d => d.Code == "ClockOut")
            ?? definitions.FirstOrDefault(d => d.StatusMapping == ClockStateRules.StatusOut && d.Category == "work")
            ?? definitions.FirstOrDefault(d => d.StatusMapping == ClockStateRules.StatusOut);
        if (clockOut is null)
            return false;

        db.ClockEvents.Add(new ClockEvent
        {
            UserId = userId,
            EventType = LegacyClockEventType.From(clockOut),
            EventTypeCode = clockOut.Code,
            Timestamp = now,
            Source = ClockOutSource,
            Reason = ClockOutReason,
        });
        return true;
    }
}
