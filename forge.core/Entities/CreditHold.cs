using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class CreditHold : BaseAuditableEntity
{
    public int CustomerId { get; set; }
    public CreditHoldReason Reason { get; set; }
    public string? Notes { get; set; }
    public int PlacedById { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public int? ReleasedById { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool IsActive { get; set; } = true;

    public Customer Customer { get; set; } = null!;
}
