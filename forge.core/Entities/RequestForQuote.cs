
namespace Forge.Core.Entities;

public class RequestForQuote : BaseAuditableEntity
{
    public string RfqNumber { get; set; } = string.Empty;
    public int PartId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset RequiredDate { get; set; }
    public RfqStatus Status { get; set; } = RfqStatus.Draft;
    public string? Description { get; set; }
    public string? SpecialInstructions { get; set; }
    public DateTimeOffset? ResponseDeadline { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? AwardedAt { get; set; }
    public int? AwardedVendorResponseId { get; set; }
    public int? GeneratedPurchaseOrderId { get; set; }
    public string? Notes { get; set; }

    public Part Part { get; set; } = null!;
    public ICollection<RfqVendorResponse> VendorResponses { get; set; } = [];
}
