using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record CreateJobNoteCommand(int JobId, string Text, int UserId, int[] MentionedUserIds) : IRequest<JobNoteResponseModel>;

public class CreateJobNoteHandler(AppDbContext db, ISender sender) : IRequestHandler<CreateJobNoteCommand, JobNoteResponseModel>
{
    public async Task<JobNoteResponseModel> Handle(CreateJobNoteCommand request, CancellationToken cancellationToken)
    {
        var note = new JobNote
        {
            JobId = request.JobId,
            Text = request.Text.Trim(),
            CreatedBy = request.UserId,
        };
        db.JobNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        var user = await db.Users
            .Where(u => u.Id == request.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        // Notify mentioned users
        var mentionedIds = (request.MentionedUserIds ?? [])
            .Distinct()
            .ToList();

        var snippet = request.Text.Length > 200 ? request.Text[..200] + "..." : request.Text;

        foreach (var mentionedUserId in mentionedIds)
        {
            await sender.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: mentionedUserId,
                Type: "mention",
                Severity: "info",
                Source: "user",
                Title: "You were mentioned in a note",
                Message: snippet,
                EntityType: "Job",
                EntityId: request.JobId,
                SenderId: request.UserId)), cancellationToken);
        }

        return new JobNoteResponseModel(
            note.Id,
            note.Text,
            user is not null ? $"{user.LastName}, {user.FirstName}".Trim(',', ' ') : "Unknown",
            user?.Initials ?? "?",
            user?.AvatarColor ?? "#0d9488",
            note.CreatedAt,
            null
        );
    }
}
