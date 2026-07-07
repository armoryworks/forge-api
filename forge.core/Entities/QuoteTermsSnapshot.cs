using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// Immutable compiled T&C captured when a quote email is sent — the public
/// "view full terms" link renders THIS, so the recipient always sees exactly
/// what was sent even after terms documents are edited. AccessToken is the
/// unguessable public key for the anonymous endpoint.
/// </summary>
public class QuoteTermsSnapshot : BaseAuditableEntity
{
    public int QuoteId { get; set; }
    public string CompiledHtml { get; set; } = string.Empty;
    public string? CompiledSource { get; set; }
    [MaxLength(64)]
    public string AccessToken { get; set; } = string.Empty;
    [MaxLength(320)]
    public string? SentTo { get; set; }

    public Quote Quote { get; set; } = null!;
}
