using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Notifications;

public record CreateNotificationCommand(CreateNotificationRequestModel Data) : IRequest<NotificationResponseModel>;

public class CreateNotificationValidator : AbstractValidator<CreateNotificationCommand>
{
    public CreateNotificationValidator()
    {
        RuleFor(x => x.Data.UserId).GreaterThan(0);
        RuleFor(x => x.Data.Type).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Severity).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Data.Source).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Message).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Data.EntityType).MaximumLength(50).When(x => x.Data.EntityType is not null);
    }
}

public class CreateNotificationHandler(
    INotificationRepository repo,
    IHubContext<NotificationHub> notificationHub) : IRequestHandler<CreateNotificationCommand, NotificationResponseModel>
{
    public async Task<NotificationResponseModel> Handle(CreateNotificationCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var notification = new Notification
        {
            UserId = data.UserId,
            Type = data.Type,
            Severity = data.Severity,
            Source = data.Source,
            Title = data.Title,
            Message = data.Message,
            EntityType = data.EntityType,
            EntityId = data.EntityId,
            SenderId = data.SenderId,
        };

        await repo.AddAsync(notification, cancellationToken);

        var result = (await repo.GetByUserIdAsync(data.UserId, cancellationToken))
            .First(n => n.Id == notification.Id);

        // Push to user via SignalR
        await notificationHub.Clients.Group($"user:{data.UserId}")
            .SendAsync("notificationReceived", result, cancellationToken);

        return result;
    }
}
