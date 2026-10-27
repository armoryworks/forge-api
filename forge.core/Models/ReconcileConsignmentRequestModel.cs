namespace Forge.Core.Models;

public record ReconcileConsignmentRequestModel
{
    public decimal? PhysicalCount { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public bool CreateBill { get; init; } = true;
    public string? VendorInvoiceNumber { get; init; }
}
