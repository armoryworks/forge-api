using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Notifications;

public record MarkAllReadCommand : IRequest;

public class MarkAllReadHandler(
    INotificationRepository repo,
    IHttpContextAccessor httpContext) : IRequestHandler<MarkAllReadCommand>
{
    public async Task Handle(MarkAllReadCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await repo.MarkAllReadAsync(userId, cancellationToken);
    }
}
