namespace Forge.Core.Models;

/// <summary>
/// ⚡ EDI BOUNDARY — one partner part-number → our part-number translation row (PAY of EDI:
/// the per-partner mapping the 850 line loop consults before the exact Part.PartNumber match).
/// <see cref="OurPartId"/> / <see cref="OurPartDescription"/> are resolved on read for display +
/// to flag rows whose target no longer exists; only the two number strings are stored.
/// </summary>
public record EdiPartNumberMapRow
{
    public string PartnerPartNumber { get; init; } = string.Empty;
    public string OurPartNumber { get; init; } = string.Empty;

    /// <summary>Resolved on read: the Part this maps to, or null when our number matches nothing.</summary>
    public int? OurPartId { get; init; }
    public string? OurPartDescription { get; init; }
}
