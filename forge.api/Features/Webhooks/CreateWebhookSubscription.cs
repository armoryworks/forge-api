using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public record CreateWebhookSubscriptionCommand(
    string Url,
    string EventTypesJson,
    string Secret,
    string? Description,
    string? HeadersJson,
    int MaxRetries,
    bool AutoDisableOnFailure) : IRequest<WebhookSubscriptionResponseModel>;

public class CreateWebhookSubscriptionCommandValidator : AbstractValidator<CreateWebhookSubscriptionCommand>
{
    public CreateWebhookSubscriptionCommandValidator()
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
            .Must(h => BeAHeaderObject(h!))
            .When(x => !string.IsNullOrWhiteSpace(x.HeadersJson))
            .WithMessage("HeadersJson must be a JSON object of header names to string values.");

        RuleFor(x => x.Secret)
            .NotEmpty().WithMessage("Secret is required.");

        RuleFor(x => x.MaxRetries)
            .InclusiveBetween(0, 20).WithMessage("MaxRetries must be between 0 and 20.");
    }

    private static bool BeAHeaderObject(string headersJson)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

public class CreateWebhookSubscriptionHandler(
    AppDbContext db,
    IDataProtectionProvider dataProtection) : IRequestHandler<CreateWebhookSubscriptionCommand, WebhookSubscriptionResponseModel>
{
    private static readonly string PurposeName = "WebhookSecret";

    public async Task<WebhookSubscriptionResponseModel> Handle(CreateWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var protector = dataProtection.CreateProtector(PurposeName);
        var encryptedSecret = protector.Protect(request.Secret);

        var subscription = new WebhookSubscription
        {
            Url = request.Url,
            EventTypesJson = request.EventTypesJson,
            EncryptedSecret = encryptedSecret,
            Description = request.Description,
            HeadersJson = request.HeadersJson,
            MaxRetries = request.MaxRetries,
            AutoDisableOnFailure = request.AutoDisableOnFailure,
            IsActive = true,
        };

        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);

        return new WebhookSubscriptionResponseModel(
            subscription.Id,
            subscription.Url,
            subscription.EventTypesJson,
            subscription.IsActive,
            subscription.FailureCount,
            subscription.MaxRetries,
            subscription.LastDeliveredAt,
            subscription.LastFailedAt,
            subscription.AutoDisableOnFailure,
            subscription.Description,
            subscription.CreatedAt,
            subscription.UpdatedAt);
    }
}
