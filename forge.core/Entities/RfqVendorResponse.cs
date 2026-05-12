using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class RfqVendorResponse : BaseAuditableEntity
{
    public int RfqId { get; set; }
    public int VendorId { get; set; }
    public RfqResponseStatus ResponseStatus { get; set; } = RfqResponseStatus.Pending;
    public decimal? UnitPrice { get; set; }
    public int? LeadTimeDays { get; set; }
    public decimal? MinimumOrderQuantity { get; set; }
    public decimal? ToolingCost { get; set; }
    public DateTimeOffset? QuoteValidUntil { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public bool IsAwarded { get; set; }
    public string? DeclineReason { get; set; }

    public RequestForQuote Rfq { get; set; } = null!;
    public Vendor Vendor { get; set; } = null!;
}
