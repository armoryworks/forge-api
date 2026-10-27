using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record UpdateWebhookSubscriptionCommand(
    int Id,
    string Url,
    string EventTypesJson,
    string? Description,
    string? HeadersJson,
    int MaxRetries,
    bool AutoDisableOnFailure,
    bool IsActive,
    bool RotateSecret) : IRequest<WebhookSubscriptionResponseModel>;

public class UpdateWebhookSubscriptionCommandValidator : AbstractValidator<UpdateWebhookSubscriptionCommand>
{
    public UpdateWebhookSubscriptionCommandValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL is required.")
            .MaximumLength(2000).WithMessage("URL must not exceed 2000 characters.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("URL must be a valid HTTP or HTTPS URL.");

        RuleFor(x => x.EventTypesJson)
            .Must(WebhookEventTypes.IsValidList)
            .WithMessage($"EventTypesJson must be a JSON array of event types: {string.Join(", ", WebhookEventTypes.All.Order())} or \"{WebhookEventTypes.Wildcard}\".");

        RuleFor(x => x.HeadersJson)
            .Must(h => CreateWebhookSubscriptionCommandValidator.BeAHeaderObject(h!))
            .When(x => !string.IsNullOrWhiteSpace(x.HeadersJson))
            .WithMessage("HeadersJson must be a JSON object of header names to string values.");

        RuleFor(x => x.MaxRetries)
            .InclusiveBetween(0, 20).WithMessage("MaxRetries must be between 0 and 20.");
    }
}

public class UpdateWebhookSubscriptionHandler(
    AppDbContext db,
    IDataProtectionProvider dataProtection) : IRequestHandler<UpdateWebhookSubscriptionCommand, WebhookSubscriptionResponseModel>
{
    public async Task<WebhookSubscriptionResponseModel> Handle(UpdateWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook subscription {request.Id} not found.");

        if (request.IsActive && !subscription.IsActive)
            subscription.FailureCount = 0;

        subscription.Url = request.Url;
        subscription.EventTypesJson = request.EventTypesJson;
        subscription.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        subscription.HeadersJson = string.IsNullOrWhiteSpace(request.HeadersJson) ? null : request.HeadersJson;
        subscription.MaxRetries = request.MaxRetries;
        subscription.AutoDisableOnFailure = request.AutoDisableOnFailure;
        subscription.IsActive = request.IsActive;

        string? revealed = null;
        if (request.RotateSecret)
        {
            revealed = WebhookSubscriptionMapping.GenerateSecret();
            subscription.EncryptedSecret = dataProtection.CreateProtector(WebhookDeliveryService.SecretPurpose).Protect(revealed);
        }

        await db.SaveChangesAsync(cancellationToken);
        return WebhookSubscriptionMapping.ToModel(subscription, revealed);
    }
}
