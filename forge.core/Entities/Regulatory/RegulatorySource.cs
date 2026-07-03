using Forge.Core.Enums;

namespace Forge.Core.Entities.Regulatory;

/// <summary>
/// regulatory-watchtower (cluster B): an external regulatory source to monitor for upcoming/
/// changing regulations. Seeded from the `regulatory-source-inventory` reference doc; admins
/// add/remove/activate at runtime. Requires outbound internet — not for air-gapped installs.
/// </summary>
public class RegulatorySource : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? IssuingBody { get; set; }

    /// <summary>Domain grouping (e.g. "safety", "environmental", "firearms", "tax").</summary>
    public string? Domain { get; set; }

    public string Url { get; set; } = string.Empty;
    public RegulatoryFeedType FeedType { get; set; }

    /// <summary>Optional industry gate — surface only for the declared industry (e.g. "firearms").</summary>
    public string? IndustryGate { get; set; }

    public bool IsActive { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
}
