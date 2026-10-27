using System.Security.Cryptography;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Features.Webhooks;

public static class WebhookSubscriptionMapping
{
    public static WebhookSubscriptionResponseModel ToModel(WebhookSubscription s, string? revealedSecret = null) =>
        new(
            s.Id,
            s.Url,
            s.EventTypesJson,
            s.IsActive,
            s.FailureCount,
            s.MaxRetries,
            s.LastDeliveredAt,
            s.LastFailedAt,
            s.AutoDisableOnFailure,
            s.Description,
            s.CreatedAt,
            s.UpdatedAt,
            s.HeadersJson,
            revealedSecret);

    public static WebhookDeliveryResponseModel ToModel(WebhookDelivery d) =>
        new(
            d.Id,
            d.SubscriptionId,
            d.EventType,
            d.StatusCode,
            d.DurationMs,
            d.AttemptedAt,
            d.AttemptNumber,
            d.IsSuccess,
            d.ErrorMessage,
            d.ResponseBody);

    public static string GenerateSecret() => $"whsec_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
}
