namespace Forge.Core.Models;

public record RecordConsignmentTransactionRequestModel
{
    public decimal Quantity { get; init; }
    public string? Notes { get; init; }
}
