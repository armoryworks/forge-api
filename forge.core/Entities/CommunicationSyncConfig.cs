
namespace Forge.Core.Entities;

/// <summary>
/// Wave 8 — one row per (User × CommunicationKind × ProviderId). Stores
/// the connection state for a salesperson's Gmail / Outlook / IMAP /
/// Twilio / etc. integration. Real adapters populate <see cref="AccessToken"/> /
/// <see cref="RefreshToken"/> via OAuth (SASL-OAUTHBEARER for IMAP); polling
/// adapters store <see cref="LastSyncedAt"/> + <see cref="LastSyncedExternalId"/>
/// to bound the next-fetch window; webhook-driven adapters mostly leave
/// those null and rely on idempotency by ExternalId at the
/// <c>ContactInteraction</c> insertion site.
///
/// Tokens are stored encrypted via ASP.NET Data Protection API at the
/// service tier (matches the QuickBooks accounting adapter pattern;
/// raw rows in the DB are sealed envelopes, not plain access tokens).
///
/// Default state is <see cref="IsConnected"/>=false; the connect flow
/// flips it true after the OAuth round-trip succeeds.
/// </summary>
public class CommunicationSyncConfig : BaseAuditableEntity
{
    // ApplicationUser lives in forge.data and Core cannot reference it,
    // so the nav prop is configured at the EF-config level (Data project)
    // via WithMany().HasForeignKey(UserId). Only the FK column is here.
    public int UserId { get; set; }

    /// <summary>Email vs Voice. Combined with <see cref="ProviderId"/> the
    /// (UserId, Kind, ProviderId) tuple is unique — a user has at most one
    /// active connection per provider per channel.</summary>
    public CommunicationKind Kind { get; set; }

    /// <summary>Stable string id matching the provider adapter's
    /// <c>ICommunicationSyncProvider.ProviderId</c> ("imap", "gmail",
    /// "microsoft-graph", "twilio-voip", "ringcentral", etc.).</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Display label the user picked at connect time
    /// ("My Gmail", "Sales line", "Twilio main"). Falls back to
    /// ProviderId at the UI when null.</summary>
    public string? DisplayLabel { get; set; }

    public bool IsConnected { get; set; }

    /// <summary>Encrypted OAuth access token. Null for adapters that don't
    /// use OAuth (e.g. webhook-only providers like Twilio whose creds
    /// live in app config, not per-user).</summary>
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>External-system identity for the connected mailbox / phone
    /// number — Gmail email address, Microsoft Graph user-id, Twilio phone,
    /// etc. Used for display + deduplication at re-connect time.</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Last successful sync timestamp. Polling adapters use this
    /// to bound the next fetch window; webhook adapters update it on each
    /// successful delivery for telemetry.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>The external id of the most-recent message / call processed.
    /// Provider-specific (Gmail historyId, IMAP UIDVALIDITY+UID, Twilio
    /// CallSid, etc.). Optional checkpoint for delta-style sync.</summary>
    public string? LastSyncedExternalId { get; set; }

    /// <summary>Per-row override of provider-level config (retention policy,
    /// folder filter, etc.). JSONB; provider adapters interpret the shape.
    /// Null means use install-wide defaults.</summary>
    public string? ConfigJson { get; set; }

    /// <summary>
    /// Wave 8 phase 1i — last sync error message, surfaced on the user's
    /// connections panel so failures are visible (broken creds, expired
    /// token, host unreachable). Cleared on the next successful sync.
    /// Null when the connection has never failed OR has succeeded since
    /// its last failure.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>Timestamp of the last sync failure. Paired with
    /// <see cref="LastError"/>; rendered alongside it on the UI.</summary>
    public DateTimeOffset? LastErrorAt { get; set; }
}
