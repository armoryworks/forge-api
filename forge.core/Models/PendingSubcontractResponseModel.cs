namespace Forge.Core.Models;

public record PendingSubcontractResponseModel
{
    public int Id { get; init; }
    public int JobId { get; init; }
    public string JobNumber { get; init; } = string.Empty;
    public int OperationId { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public int VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public DateTimeOffset SentAt { get; init; }
    public DateTimeOffset? ExpectedReturnDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }
    public int DaysOut { get; init; }
}
