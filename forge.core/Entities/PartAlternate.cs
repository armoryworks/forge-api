using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class PartAlternate : BaseAuditableEntity
{
    public int PartId { get; set; }
    public int AlternatePartId { get; set; }
    public int Priority { get; set; } = 1;
    public AlternateType Type { get; set; } = AlternateType.Substitute;
    public decimal? ConversionFactor { get; set; }
    public bool IsApproved { get; set; }
    public int? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public bool IsBidirectional { get; set; }

    public Part Part { get; set; } = null!;
    public Part AlternatePart { get; set; } = null!;
}
