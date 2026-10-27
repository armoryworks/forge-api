using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ConsignmentTransactionResponseModel
{
    public int Id { get; init; }
    public int AgreementId { get; init; }
    public ConsignmentTransactionType Type { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal ExtendedAmount { get; init; }
    public int? PurchaseOrderId { get; init; }
    public int? InvoiceId { get; init; }
    public int? BinContentId { get; init; }
    public int? StorageLocationId { get; init; }
    public string? StorageLocationName { get; init; }
    public string? LotNumber { get; init; }
    public int? JobId { get; init; }
    public string? JobNumber { get; init; }
    public int? VendorBillId { get; init; }
    public string? VendorBillNumber { get; init; }
    public long? JournalEntryId { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
