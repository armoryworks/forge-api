
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
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
