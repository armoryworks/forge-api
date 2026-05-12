using Forge.Core.Enums;

namespace Forge.Core.Models.Communications;

/// <summary>
/// Wave 8 — provider-agnostic envelope for an inbound (or outbound) email or
/// call event. Each <c>ICommunicationSyncProvider</c> adapter translates its
/// native event shape (Gmail message JSON, Microsoft Graph notification,
/// Twilio webhook payload, IMAP MIME envelope, etc.) into this single shape;
/// the matcher consumes one shape regardless of source so adding new
/// providers doesn't ripple into matching code.
///
/// Direction is from the <em>tenant's</em> perspective:
/// <list type="bullet">
///   <item><c>Inbound</c> — external party → tenant user. From-address is
///   external (matched against Lead/Contact); To-address is internal.</item>
///   <item><c>Outbound</c> — tenant user → external party. From-address is
///   internal; To-address(es) include external recipients (matched).</item>
/// </list>
/// </summary>
public record InboundCommunication(
    /// <summary>Provider id ("gmail", "outlook", "imap", "twilio-voip", etc.).
    /// Audit-only; the matcher ignores it.</summary>
    string ProviderId,

    /// <summary>Which channel this came through. Picks the match field
    /// (Email → Lead.Email/Contact.Email; Voice → Lead.Phone/Contact.Phone).</summary>
    CommunicationKind Kind,

    /// <summary>Inbound or Outbound from the tenant's perspective.</summary>
    CommunicationDirection Direction,

    /// <summary>The provider's stable id for this event (Gmail messageId,
    /// Twilio CallSid, IMAP UID + folder, etc.). Used for idempotency —
    /// a re-delivered webhook with the same ExternalId is a no-op.</summary>
    string ExternalId,

    /// <summary>Sender's email or phone number (lowercase / E.164 expected
    /// after normalization in the matcher; provider adapters can pass raw).</summary>
    string From,

    /// <summary>Primary recipient(s) — email To+CC list, or call's other-party
    /// number. Order is preserved but the matcher iterates all of them.</summary>
    IReadOnlyList<string> To,

    /// <summary>When the message was sent / call was placed (UTC).</summary>
    DateTimeOffset OccurredAt,

    /// <summary>Email subject, or call's recording-summary headline. Empty
    /// for raw call events with no transcript yet.</summary>
    string? Subject,

    /// <summary>Email body (text preferred; HTML stripped to text by adapters)
    /// or call transcript. Provider adapters may truncate per the install's
    /// retention policy (CAP-EXT-EMAIL-SYNC config).</summary>
    string? Body,

    /// <summary>Call duration in minutes, or null for email.</summary>
    int? DurationMinutes,

    /// <summary>Optional URL to the call recording / message attachments
    /// blob. Storage backend is the install's MinIO; the URL is signed.</summary>
    string? RecordingUrl);

public enum CommunicationDirection
{
    Inbound = 0,
    Outbound = 1,
}
