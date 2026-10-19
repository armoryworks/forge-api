using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Webhooks;

public class WebhookDeliveryService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtection,
    IClock clock,
    ILogger<WebhookDeliveryService> logger) : IWebhookDeliveryService
{
    public const string HttpClientName = "Webhooks";
    internal const string SecretPurpose = "WebhookSecret";
    private const int ResponseExcerptLength = 2000;

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Length", "Host", "X-Forge-Event", "X-Forge-Delivery", "X-Forge-Signature",
    };

    public async Task DeliverAsync(IntegrationOutboxEntry entry, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(entry.Payload)
            ?? throw new InvalidOperationException($"Failed to deserialize webhook payload for entry {entry.Id}");

        var subscription = await db.WebhookSubscriptions.FindAsync([payload.SubscriptionId], ct);
        if (subscription is null || !subscription.IsActive)
        {
            logger.LogInformation(
                "Webhook delivery skipped: subscription {SubscriptionId} is missing or inactive (outbox entry {EntryId})",
                payload.SubscriptionId, entry.Id);
            return;
        }

        var secret = dataProtection.CreateProtector(SecretPurpose).Unprotect(subscription.EncryptedSecret);
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload.Body))).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(payload.Body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Forge-Event", payload.EventType);
        request.Headers.TryAddWithoutValidation("X-Forge-Delivery", payload.DeliveryId.ToString());
        request.Headers.TryAddWithoutValidation("X-Forge-Signature", $"sha256={signature}");
        foreach (var (name, value) in ParseHeaders(subscription.HeadersJson))
        {
            if (!ReservedHeaders.Contains(name))
                request.Headers.TryAddWithoutValidation(name, value);
        }

        var delivery = new WebhookDelivery
        {
            SubscriptionId = subscription.Id,
            EventType = payload.EventType,
            PayloadJson = payload.Body,
            AttemptedAt = clock.UtcNow,
            AttemptNumber = entry.AttemptCount,
        };

        var stopwatch = Stopwatch.StartNew();
        Exception? failure = null;
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            delivery.StatusCode = (int)response.StatusCode;
            delivery.ResponseBody = Truncate(body, ResponseExcerptLength);
            delivery.IsSuccess = response.IsSuccessStatusCode;
            if (!response.IsSuccessStatusCode)
                failure = new HttpRequestException($"Webhook {subscription.Url} returned {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        stopwatch.Stop();
        delivery.DurationMs = (decimal)stopwatch.Elapsed.TotalMilliseconds;

        if (failure is null)
        {
            subscription.FailureCount = 0;
            subscription.LastDeliveredAt = clock.UtcNow;
        }
        else
        {
            delivery.ErrorMessage = Truncate(failure.Message, ResponseExcerptLength);
            subscription.FailureCount++;
            subscription.LastFailedAt = clock.UtcNow;
            if (subscription.AutoDisableOnFailure && subscription.FailureCount > subscription.MaxRetries)
            {
                subscription.IsActive = false;
                logger.LogWarning(
                    "Webhook subscription {SubscriptionId} disabled after {Failures} consecutive failed deliveries",
                    subscription.Id, subscription.FailureCount);
            }
        }

        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        if (failure is not null)
            throw failure as HttpRequestException ?? new HttpRequestException(failure.Message, failure);
    }

    internal static IEnumerable<KeyValuePair<string, string>> ParseHeaders(string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson)) return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
