using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Notifications;

public record UpdateNotificationCommand(int Id, bool? IsRead, bool? IsPinned, bool? IsDismissed) : IRequest;

public class UpdateNotificationValidator : AbstractValidator<UpdateNotificationCommand>
{
    public UpdateNotificationValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public class UpdateNotificationHandler(
    INotificationRepository repo,
    IHttpContextAccessor httpContext) : IRequestHandler<UpdateNotificationCommand>
{
    public async Task Handle(UpdateNotificationCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var notification = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {request.Id} not found");

        if (notification.UserId != userId)
            throw new UnauthorizedAccessException("Cannot modify another user's notification");

        if (request.IsRead.HasValue) notification.IsRead = request.IsRead.Value;
        if (request.IsPinned.HasValue) notification.IsPinned = request.IsPinned.Value;
        if (request.IsDismissed.HasValue) notification.IsDismissed = request.IsDismissed.Value;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
