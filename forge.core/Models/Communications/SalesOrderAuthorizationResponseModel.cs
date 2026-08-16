namespace Forge.Core.Models.Communications;

/// <summary>
/// The Authorized-by line on a sales order.
///
/// <para>Renders as: <c>Authorized by PO-8832.pdf — received 2026-08-15 09:12
/// MDT from bob@bobsparts.com — sha256:ab3f91…</c>, with each part linking to
/// the document, the original message, and the agreements behind it.</para>
/// </summary>
public record SalesOrderAuthorizationResponseModel(
    int AttestationId,
    string StatementType,
    string Method,
    string Status,
    /// <summary>When the party stated it — the moment the email was sent, not when staff acted on it.</summary>
    DateTimeOffset? CapturedAt,
    string? FromAddress,
    string? Channel,
    int? ArtifactId,
    string? Filename,
    /// <summary>Full 64-character digest. The UI truncates for display; a reviewer verifying needs all of it.</summary>
    string? Sha256,
    /// <summary>Route back to the original message.</summary>
    int? CommunicationId,
    /// <summary>The chain of standing agreements this leans on, nearest first.</summary>
    IReadOnlyList<PriorAgreementResponseModel> AuthorizationChain);
