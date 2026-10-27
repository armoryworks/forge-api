using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Notifications;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Applies a recorded termination on its effective date: the linked user is deactivated
/// (open jobs unassigned, timers stopped, clocked out, sessions revoked), every credential
/// the user signs in with is withdrawn (kiosk PIN and badge, scan identifiers, passkeys,
/// user-bound API keys, enrolled devices and their refresh tokens), the team's managers are
/// told about the unassigned jobs, and open leave requests are cancelled (approved leave after
/// the last day goes back onto the balance so final pay counts it). Leave stops
/// accruing because accrual only credits active users.
/// </summary>
public class EmployeeTermination(
    AppDbContext db,
    UserDeactivation deactivation,
    ISystemAuditWriter audit,
    IMediator mediator,
    IClock clock)
{
    public const string AuditAction = "EmployeeTerminated";
    public const string RehireAuditAction = "EmployeeRehired";
    public const string LeaveCancelReason = "Employee terminated";

    public async Task<TerminationEffectsModel> ApplyAsync(Core.Entities.EmployeeProfile profile, int actorId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var effects = new TerminationEffectsModel(0, false, false, false, 0, 0, 0, 0, 0);

        if (profile.UserId is int userId)
            effects = await RevokeUserAsync(userId, now, ct);

        var cancelled = await CancelOpenLeaveAsync(profile, now, ct);
        effects = effects with { LeaveRequestsCancelled = cancelled };

        profile.TerminationAppliedAt = now;
        db.LogActivityAt(
            "terminated",
            $"Terminated effective {profile.TerminationDate:yyyy-MM-dd} ({profile.TerminationReasonCode})",
            ("Employee", profile.UserId ?? profile.Id));
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditAction,
            actorId,
            entityType: "EmployeeProfile",
            entityId: profile.Id,
            details: JsonSerializer.Serialize(new
            {
                profile.UserId,
                terminationDate = profile.TerminationDate,
                reason = profile.TerminationReasonCode,
                profile.RehireEligible,
                profile.FinalPayRequired,
                effects,
            }),
            ct: ct);

        return effects;
    }

    private async Task<TerminationEffectsModel> RevokeUserAsync(int userId, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return new TerminationEffectsModel(0, false, false, false, 0, 0, 0, 0, 0);

        var wasActive = user.IsActive;
        if (wasActive)
            await UserDeactivation.EnsureNotLastActiveAdminAsync(db, userId, ct);

        var openJobs = await db.Jobs.AsNoTracking()
            .Where(j => j.AssigneeId == userId && !j.IsArchived && j.CompletedDate == null)
            .Select(j => new { j.Id, j.JobNumber })
            .ToListAsync(ct);

        var kioskCleared = user.PinHash is not null || user.EmployeeBarcode is not null;
        user.IsActive = false;
        user.PinHash = null;
        user.EmployeeBarcode = null;
        user.UpdatedAt = now;

        var scanIds = await db.UserScanIdentifiers
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync(ct);
        foreach (var scan in scanIds)
            scan.IsActive = false;

        var passkeys = await db.UserMfaDevices
            .Where(d => d.UserId == userId && d.DeviceType == MfaDeviceType.WebAuthn)
            .ToListAsync(ct);
        foreach (var passkey in passkeys)
            passkey.DeletedAt = now;

        var apiKeys = await db.SystemApiKeys
            .Where(k => k.UserId == userId && k.IsActive)
            .ToListAsync(ct);
        foreach (var key in apiKeys)
            key.IsActive = false;

        var devices = await db.UserDevices
            .Where(d => d.UserId == userId && d.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var device in devices)
        {
            device.RevokedAt = now;
            device.RevokedByUserId = db.CurrentUserId;
        }

        var tokens = await db.DeviceRefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.RevokedAt = now;

        await db.SaveChangesAsync(ct);

        var deactivated = await deactivation.ApplyAsync(userId, ct);

        if (openJobs.Count > 0)
            await NotifyManagersAsync(user, openJobs.Select(j => j.JobNumber).ToList(), openJobs[0].Id, ct);

        return new TerminationEffectsModel(
            deactivated.UnassignedJobCount,
            deactivated.StoppedTimer,
            deactivated.ClockedOut,
            kioskCleared,
            scanIds.Count,
            passkeys.Count,
            apiKeys.Count,
            devices.Count,
            0);
    }

    private async Task<int> CancelOpenLeaveAsync(Core.Entities.EmployeeProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        if (profile.UserId is not int userId || profile.TerminationDate is not DateOnly lastDay)
            return 0;

        var open = await db.LeaveRequests
            .Where(r => r.UserId == userId
                && (r.Status == LeaveRequestStatus.Pending
                    || (r.Status == LeaveRequestStatus.Approved && r.StartDate > lastDay)))
            .ToListAsync(ct);
        var refunds = open.Where(r => r.Status == LeaveRequestStatus.Approved)
            .GroupBy(r => r.PolicyId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Hours));
        if (refunds.Count > 0)
        {
            var balances = await db.LeaveBalances
                .Where(b => b.UserId == userId && refunds.Keys.Contains(b.PolicyId))
                .ToListAsync(ct);
            foreach (var balance in balances)
            {
                balance.Balance += refunds[balance.PolicyId];
                balance.UsedThisYear -= refunds[balance.PolicyId];
            }
        }

        foreach (var request in open)
        {
            request.Status = LeaveRequestStatus.Cancelled;
            request.DecidedAt = now;
            request.DenialReason = LeaveCancelReason;
        }

        return open.Count;
    }

    private async Task NotifyManagersAsync(ApplicationUser user, List<string> jobNumbers, int firstJobId, CancellationToken ct)
    {
        var recipients = await RecipientsAsync(user, ct);
        if (recipients.Count == 0)
            return;

        var name = $"{user.LastName}, {user.FirstName}";
        var list = string.Join(", ", jobNumbers.Take(10));
        var more = jobNumbers.Count > 10 ? $" (+{jobNumbers.Count - 10} more)" : string.Empty;
        foreach (var recipientId in recipients)
        {
            await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: recipientId,
                Type: "employee-terminated",
                Severity: "warning",
                Source: "system",
                Title: $"{name} was terminated: {jobNumbers.Count} job(s) need a new assignee",
                Message: $"Unassigned: {list}{more}",
                EntityType: "Job",
                EntityId: firstJobId,
                SenderId: null)), ct);
        }
    }

    private async Task<List<int>> RecipientsAsync(ApplicationUser user, CancellationToken ct)
    {
        var managerRoleIds = db.Roles.Where(r => r.Name == "Manager").Select(r => r.Id);
        var adminRoleIds = db.Roles.Where(r => r.Name == UserDeactivation.AdminRole).Select(r => r.Id);

        var managers = user.TeamId is int teamId
            ? await db.Users.AsNoTracking()
                .Where(u => u.Id != user.Id && u.IsActive && u.TeamId == teamId)
                .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && managerRoleIds.Contains(ur.RoleId)))
                .Select(u => u.Id)
                .ToListAsync(ct)
            : [];
        if (managers.Count > 0)
            return managers;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id != user.Id && u.IsActive)
            .WithoutServiceAccounts(db)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && adminRoleIds.Contains(ur.RoleId)))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }
}
