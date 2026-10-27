using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Terminology;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Notifications;

public class AssignmentNotifier(AppDbContext db, ISender sender, ITerminologyLabels? terminology = null) : IAssignmentNotifier
{
    public const string Source = "assignments";

    public async Task JobAssignedAsync(int jobId, string jobNumber, string jobTitle, int assigneeId, CancellationToken ct)
    {
        var jobTerm = (await terminology.GetOrDefaultsAsync(ct)).Singular("job");
        await NotifyAsync(
            assigneeId, "Job", jobId,
            $"{jobTerm} {jobNumber} assigned to you",
            $"{jobTerm} {jobNumber} — {jobTitle} is now assigned to you.",
            ct);
    }

    public async Task SubtaskAssignedAsync(int jobId, string subtaskText, int assigneeId, CancellationToken ct)
    {
        var jobNumber = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.JobNumber)
            .FirstOrDefaultAsync(ct) ?? jobId.ToString();
        var jobTerm = (await terminology.GetOrDefaultsAsync(ct)).Singular("job");
        await NotifyAsync(
            assigneeId, "Job", jobId,
            $"Task on {jobTerm} {jobNumber} assigned to you",
            Truncate(subtaskText, 500),
            ct);
    }

    public Task LeadAssignedAsync(int leadId, string leadName, int ownerId, CancellationToken ct) =>
        NotifyAsync(ownerId, "Lead", leadId, "Lead assigned to you", $"{leadName} is now assigned to you.", ct);

    public async Task ApprovalAssignedAsync(
        string entityType, int entityId, string? entitySummary, IReadOnlyCollection<int> approverIds, CancellationToken ct)
    {
        var subject = string.IsNullOrWhiteSpace(entitySummary) ? $"{entityType} #{entityId}" : entitySummary;
        foreach (var approverId in approverIds.Distinct())
        {
            await NotifyAsync(
                approverId, entityType, entityId,
                "Approval waiting for you",
                $"{subject} is waiting for your approval.",
                ct);
        }
    }

    private async Task NotifyAsync(int userId, string entityType, int entityId, string title, string message, CancellationToken ct)
    {
        var actorId = db.CurrentUserId;
        if (actorId == userId)
            return;

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, ct))
            return;

        await sender.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
            userId,
            NotificationEmailPlanner.AssignmentType,
            "info",
            Source,
            Truncate(title, 200),
            Truncate(message, 2000),
            entityType,
            entityId,
            actorId)), ct);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";
}
