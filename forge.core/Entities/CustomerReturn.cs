
namespace Forge.Core.Entities;

public class CustomerReturn : BaseAuditableEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int OriginalJobId { get; set; }
    public int? ReworkJobId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public CustomerReturnStatus Status { get; set; } = CustomerReturnStatus.Received;
    public DateTimeOffset ReturnDate { get; set; }
    public int? InspectedById { get; set; }
    public DateTimeOffset? InspectedAt { get; set; }
    public string? InspectionNotes { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public Job OriginalJob { get; set; } = null!;
    public Job? ReworkJob { get; set; }
}
