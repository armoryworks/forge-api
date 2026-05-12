using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Notifications;

public record GetNotificationsQuery : IRequest<List<NotificationResponseModel>>;

public class GetNotificationsHandler(
    INotificationRepository repo,
    IHttpContextAccessor httpContext) : IRequestHandler<GetNotificationsQuery, List<NotificationResponseModel>>
{
    public Task<List<NotificationResponseModel>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return repo.GetByUserIdAsync(userId, cancellationToken);
    }
}
