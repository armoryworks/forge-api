using FluentValidation;
using MediatR;

using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Activity;

public record CreateEntityCommentCommand(
    string EntityType,
    int EntityId,
    string Comment,
    int[] MentionedUserIds) : IRequest<ActivityResponseModel>;

public class CreateEntityCommentValidator : AbstractValidator<CreateEntityCommentCommand>
{
    public CreateEntityCommentValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(5000);
    }
}

public class CreateEntityCommentHandler(
    IActivityLogRepository activityRepo,
    ISender sender,
    IHttpContextAccessor httpContext)
    : IRequestHandler<CreateEntityCommentCommand, ActivityResponseModel>
{
    public async Task<ActivityResponseModel> Handle(CreateEntityCommentCommand request, CancellationToken ct)
    {
        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var log = new ActivityLog
        {
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            UserId = userId,
            Action = "Comment",
            Description = request.Comment,
        };

        await activityRepo.AddAsync(log, ct);
        await activityRepo.SaveChangesAsync(ct);

        // Notify mentioned users
        var mentionedIds = (request.MentionedUserIds ?? []).Distinct().ToList();
        var snippet = request.Comment.Length > 200 ? request.Comment[..200] + "..." : request.Comment;

        foreach (var mentionedUserId in mentionedIds)
        {
            await sender.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: mentionedUserId,
                Type: "mention",
                Severity: "info",
                Source: "user",
                Title: "You were mentioned in a comment",
                Message: snippet,
                EntityType: request.EntityType,
                EntityId: request.EntityId,
                SenderId: userId)), ct);
        }

        var activities = await activityRepo.GetByEntityAsync(request.EntityType, request.EntityId, ct);
        return activities.First(a => a.Id == log.Id);
    }
}
