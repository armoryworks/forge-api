using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Communications;

namespace Forge.Integrations.Communications;

/// <summary>
/// Wave 8 — Mock <see cref="ICommunicationSyncProvider"/> for development +
/// matcher testing. Returns canned <see cref="InboundCommunication"/> events
/// when SyncRecentAsync is called; webhook ingestion accepts any payload
/// and forwards a synthetic event to the matcher (so dev can POST a body
/// like <c>{"from":"x@y.com","to":"sales@us.com","subject":"hi"}</c> to
/// the webhook endpoint and watch it match).
///
/// Default registration when MockIntegrations=true (matches the rest of
/// the integration adapters' Mock vs Real selection at startup).
/// </summary>
public class MockEmailSyncProvider(
    ICommunicationMatcher matcher,
    ILogger<MockEmailSyncProvider> logger) : ICommunicationSyncProvider
{
    public string ProviderId => "mock-email";
    public CommunicationKind Kind => CommunicationKind.Email;

    public Task<string?> StartAuthAsync(int userId, CancellationToken ct)
    {
        logger.LogInformation("MockEmailSyncProvider: StartAuthAsync (userId={UserId}) — no-op", userId);
        return Task.FromResult<string?>(null);
    }

    public Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct)
    {
        logger.LogInformation("MockEmailSyncProvider: CompleteAuthAsync (userId={UserId}) — accepted", userId);
        return Task.FromResult(true);
    }

    /// <summary>Returns a single canned event so dev can verify the matcher
    /// + ContactInteraction flow without standing up a real mailbox.</summary>
    public async Task<int> SyncRecentAsync(int connectionId, CancellationToken ct)
    {
        var canned = new InboundCommunication(
            ProviderId: ProviderId,
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Inbound,
            ExternalId: $"mock-{Guid.NewGuid():N}",
            From: "demo-customer@example.test",
            To: ["sales@us.test"],
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Mock inbound email",
            Body: "This is a synthetic email from MockEmailSyncProvider.",
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await matcher.MatchAndLogAsync(canned, ct);
        logger.LogInformation("MockEmailSyncProvider: synced 1 canned event, matched={Matched}", result.Matched);
        return result.Matched ? 1 : 0;
    }

    public async Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct)
    {
        // Dev path: accept any string, build a synthetic event from it.
        // Real adapters parse provider-specific JSON / MIME here.
        var synthetic = new InboundCommunication(
            ProviderId: ProviderId,
            Kind: CommunicationKind.Email,
            Direction: CommunicationDirection.Inbound,
            ExternalId: $"mock-webhook-{Guid.NewGuid():N}",
            From: "demo-webhook@example.test",
            To: ["sales@us.test"],
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Mock webhook event",
            Body: rawPayload,
            DurationMinutes: null,
            RecordingUrl: null);

        var result = await matcher.MatchAndLogAsync(synthetic, ct);
        logger.LogInformation("MockEmailSyncProvider: webhook ingested, matched={Matched}", result.Matched);
    }
}

/// <summary>
/// Wave 8 — Mock voice sync provider sibling. Same shape; the matcher
/// expects Voice events to carry a phone number in From / To and
/// duration in minutes.
/// </summary>
public class MockVoiceSyncProvider(
    ICommunicationMatcher matcher,
    ILogger<MockVoiceSyncProvider> logger) : ICommunicationSyncProvider
{
    public string ProviderId => "mock-voip";
    public CommunicationKind Kind => CommunicationKind.Voice;

    public Task<string?> StartAuthAsync(int userId, CancellationToken ct)
    {
        logger.LogInformation("MockVoiceSyncProvider: StartAuthAsync (userId={UserId}) — no-op", userId);
        return Task.FromResult<string?>(null);
    }

    public Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct)
    {
        logger.LogInformation("MockVoiceSyncProvider: CompleteAuthAsync (userId={UserId}) — accepted", userId);
        return Task.FromResult(true);
    }

    public async Task<int> SyncRecentAsync(int connectionId, CancellationToken ct)
    {
        var canned = new InboundCommunication(
            ProviderId: ProviderId,
            Kind: CommunicationKind.Voice,
            Direction: CommunicationDirection.Inbound,
            ExternalId: $"mock-call-{Guid.NewGuid():N}",
            From: "+15555550123",
            To: ["+15555550100"],
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Mock inbound call",
            Body: "Synthetic call summary from MockVoiceSyncProvider.",
            DurationMinutes: 12,
            RecordingUrl: null);

        var result = await matcher.MatchAndLogAsync(canned, ct);
        logger.LogInformation("MockVoiceSyncProvider: synced 1 canned event, matched={Matched}", result.Matched);
        return result.Matched ? 1 : 0;
    }

    public async Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct)
    {
        var synthetic = new InboundCommunication(
            ProviderId: ProviderId,
            Kind: CommunicationKind.Voice,
            Direction: CommunicationDirection.Inbound,
            ExternalId: $"mock-voip-webhook-{Guid.NewGuid():N}",
            From: "+15555550123",
            To: ["+15555550100"],
            OccurredAt: DateTimeOffset.UtcNow,
            Subject: "Mock VoIP webhook",
            Body: rawPayload,
            DurationMinutes: 5,
            RecordingUrl: null);

        var result = await matcher.MatchAndLogAsync(synthetic, ct);
        logger.LogInformation("MockVoiceSyncProvider: webhook ingested, matched={Matched}", result.Matched);
    }
}
