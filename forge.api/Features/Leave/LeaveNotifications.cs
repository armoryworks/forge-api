using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Leave;

public static class LeaveNotifications
{
    public const string ApproverEntityType = "LeaveApproval";
    public const string RequesterEntityType = "LeaveRequest";

    public static async Task<List<int>> UserIdsInRolesAsync(AppDbContext db, string[] roles, CancellationToken ct)
        => await db.UserRoles
            .Join(db.Roles.Where(r => roles.Contains(r.Name!)), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.IsActive), id => id, u => u.Id, (id, _) => id)
            .Distinct()
            .ToListAsync(ct);

    public static async Task NotifyApproversAsync(AppDbContext db, LeaveRequest request, string requesterName, string policyName, CancellationToken ct)
    {
        var approvers = await UserIdsInRolesAsync(db, ["Admin", "Manager"], ct);
        foreach (var approverId in approvers.Where(id => id != request.UserId))
        {
            db.Notifications.Add(new Notification
            {
                UserId = approverId,
                Type = "leave",
                Severity = "info",
                Source = "leave",
                Title = "Leave request to review",
                Message = $"{requesterName} asked for {request.Hours:0.##} h of {policyName}, {request.StartDate:MM/dd/yyyy} to {request.EndDate:MM/dd/yyyy}.",
                EntityType = ApproverEntityType,
                EntityId = request.Id,
                SenderId = request.UserId,
            });
        }
    }

    public static void NotifyRequester(AppDbContext db, LeaveRequest request, string policyName, bool approved, int decidedBy)
    {
        db.Notifications.Add(new Notification
        {
            UserId = request.UserId,
            Type = "leave",
            Severity = approved ? "info" : "warning",
            Source = "leave",
            Title = approved ? "Leave approved" : "Leave denied",
            Message = approved
                ? $"Your {policyName} request for {request.StartDate:MM/dd/yyyy} to {request.EndDate:MM/dd/yyyy} was approved."
                : $"Your {policyName} request for {request.StartDate:MM/dd/yyyy} to {request.EndDate:MM/dd/yyyy} was denied."
                    + (string.IsNullOrWhiteSpace(request.DenialReason) ? string.Empty : $" Note: {request.DenialReason}"),
            EntityType = RequesterEntityType,
            EntityId = request.Id,
            SenderId = decidedBy,
        });
    }
}
