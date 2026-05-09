using QBEngineer.Core.Enums;
using QBEngineer.Core.Models.Communications;

namespace QBEngineer.Core.Interfaces.Communications;

/// <summary>
/// Wave 8 — pluggable adapter for one communication-sync provider (Gmail
/// API, Microsoft Graph, IMAP universal, Twilio webhook, RingCentral
/// webhook, etc.). Mirrors the Mock + real-impl + factory shape that the
/// existing IAccountingService / IShippingService / IAddressValidationService
/// integrations already use.
///
/// One concrete implementation per (provider × channel). The matcher
/// (<see cref="ICommunicationMatcher"/>) consumes the provider-agnostic
/// <c>InboundCommunication</c> envelope so adding new adapters never
/// touches matching code.
/// </summary>
public interface ICommunicationSyncProvider
{
    /// <summary>Stable string id used to discriminate providers in the
    /// CommunicationSyncConfig table and in routing webhook URLs.
    /// Lowercase kebab-case ("imap", "gmail", "microsoft-graph",
    /// "twilio-voip", "ringcentral", "mock-email", "mock-voip").</summary>
    string ProviderId { get; }

    /// <summary>Email or Voice. The matcher uses this to pick the lookup
    /// field on Lead/Contact (Email vs Phone).</summary>
    CommunicationKind Kind { get; }

    /// <summary>Begin the OAuth round-trip for a user. Returns the URL the
    /// user should be redirected to (provider's consent screen). Null when
    /// the provider doesn't use OAuth (e.g. Twilio uses app-level creds and
    /// per-user webhook URLs instead).</summary>
    Task<string?> StartAuthAsync(int userId, CancellationToken ct);

    /// <summary>Complete the OAuth round-trip with the code the provider
    /// redirected back with. Persists the access/refresh tokens encrypted
    /// in CommunicationSyncConfig. Returns true on success.</summary>
    Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct);

    /// <summary>
    /// Pull recent communications for a single connection (polling-based
    /// adapters). The argument is the <see cref="Entities.CommunicationSyncConfig.Id"/>
    /// — the provider reads its per-connection config (host/mailbox/uid-
    /// checkpoint/encrypted creds) directly off the row, and updates
    /// <see cref="Entities.CommunicationSyncConfig.LastSyncedExternalId"/>
    /// when applicable. Returns the count of newly-matched events.
    /// Webhook-driven adapters can return 0 / no-op — they ingest via
    /// <see cref="IngestWebhookEventAsync"/> instead.
    /// </summary>
    Task<int> SyncRecentAsync(int connectionId, CancellationToken ct);

    /// <summary>Process a single inbound webhook payload. Provider-specific
    /// shape; the adapter parses and translates into one or more
    /// <see cref="InboundCommunication"/> envelopes, hands each to the matcher.
    /// Idempotent — re-deliveries with the same external id no-op.</summary>
    Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct);
}
