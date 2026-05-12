using FluentValidation;
using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Activity;

public record CreateEntityNoteCommand(
    string EntityType,
    int EntityId,
    string Text,
    int[] MentionedUserIds) : IRequest<EntityNoteResponseModel>;

public class CreateEntityNoteValidator : AbstractValidator<CreateEntityNoteCommand>
{
    public CreateEntityNoteValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(4000);
    }
}

public class CreateEntityNoteHandler(
    AppDbContext db,
    ISender sender,
    IHttpContextAccessor httpContext)
    : IRequestHandler<CreateEntityNoteCommand, EntityNoteResponseModel>
{
    public async Task<EntityNoteResponseModel> Handle(CreateEntityNoteCommand request, CancellationToken ct)
    {
        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var note = new EntityNote
        {
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Text = request.Text.Trim(),
            CreatedBy = userId,
        };

        db.EntityNotes.Add(note);
        await db.SaveChangesAsync(ct);

        var user = await db.Users
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync(ct);

        // Notify mentioned users
        var mentionedIds = (request.MentionedUserIds ?? []).Distinct().ToList();
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
                EntityType: request.EntityType,
                EntityId: request.EntityId,
                SenderId: userId)), ct);
        }

        return new EntityNoteResponseModel(
            note.Id,
            note.Text,
            user is not null ? $"{user.LastName}, {user.FirstName}".Trim(',', ' ') : "Unknown",
            user?.Initials ?? "?",
            user?.AvatarColor ?? "#0d9488",
            note.CreatedAt,
            null);
    }
}
