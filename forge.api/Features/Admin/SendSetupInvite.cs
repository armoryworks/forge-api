using MediatR;
using Microsoft.AspNetCore.Identity;
using Forge.Api.Jobs;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record SendSetupInviteCommand(int UserId, string BaseUrl) : IRequest;

public class SendSetupInviteHandler(
    UserManager<ApplicationUser> userManager,
    IMediator mediator,
    IIntegrationOutboxService outbox,
    ISystemSettingRepository settings) : IRequestHandler<SendSetupInviteCommand>
{
    public const string InviteEntityType = "ApplicationUser";
    public const string OperationKeyPrefix = "setup-invite:";

    public async Task Handle(SendSetupInviteCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        var tokenResult = await mediator.Send(new GenerateSetupTokenCommand(request.UserId), cancellationToken);

        var companySetting = await settings.FindByKeyAsync("company_name", cancellationToken);
        var companyName = companySetting?.Value ?? "Forge";

        var baseUrl = request.BaseUrl.TrimEnd('/');
        var setupUrl = $"{baseUrl}/setup/{Uri.EscapeDataString(tokenResult.Token)}";

        var html = EmailTemplateBuilder.BuildNotificationEmail(
            companyName,
            user.FirstName,
            "Welcome! Complete Your Account Setup",
            $"You've been invited to join {companyName}. Use the button below to set your password and complete your account setup.\n\n" +
            $"This link expires in 7 days. If the button doesn't work, copy and paste this URL:\n{setupUrl}",
            actionUrl: setupUrl,
            actionLabel: "Complete Setup");

        var operationKey = $"{OperationKeyPrefix}{user.Id}:{Guid.NewGuid():N}";
        await outbox.EnqueueEmailAsync(
            operationKey,
            new EmailMessage(
                user.Email!,
                $"[{companyName}] Complete Your Account Setup",
                html),
            entityType: InviteEntityType,
            entityId: user.Id,
            ct: cancellationToken);
    }
}
