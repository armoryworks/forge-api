using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs;

public record CreateJobCommentCommand(int JobId, string Comment, int[] MentionedUserIds) : IRequest<ActivityResponseModel>;

public class CreateJobCommentValidator : AbstractValidator<CreateJobCommentCommand>
{
    public CreateJobCommentValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(5000);
    }
}

public class CreateJobCommentHandler(
    IActivityLogRepository activityRepo,
    ISender sender,
    IHttpContextAccessor httpContext)
    : IRequestHandler<CreateJobCommentCommand, ActivityResponseModel>
{
    public async Task<ActivityResponseModel> Handle(
        CreateJobCommentCommand request, CancellationToken cancellationToken)
    {
        var jobExists = await activityRepo.JobExistsAsync(request.JobId, cancellationToken);
        if (!jobExists)
            throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var log = new JobActivityLog
        {
            JobId = request.JobId,
            UserId = userId,
            Action = ActivityAction.CommentAdded,
            Description = request.Comment,
        };

        await activityRepo.AddAsync(log, cancellationToken);
        await activityRepo.SaveChangesAsync(cancellationToken);

        // Notify mentioned users using IDs supplied by the client
        var mentionedIds = (request.MentionedUserIds ?? [])
            .Distinct()
            .ToList();

        var snippet = request.Comment.Length > 200
            ? request.Comment[..200] + "..."
            : request.Comment;

        foreach (var mentionedUserId in mentionedIds)
        {
            await sender.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: mentionedUserId,
                Type: "mention",
                Severity: "info",
                Source: "user",
                Title: "You were mentioned in a comment",
                Message: snippet,
                EntityType: "Job",
                EntityId: request.JobId,
                SenderId: userId)), cancellationToken);
        }

        var activities = await activityRepo.GetByJobIdAsync(request.JobId, cancellationToken);
        return activities.First(a => a.Id == log.Id);
    }
}
