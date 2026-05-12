namespace Forge.Core.Models;

public record ConfirmPickLineRequestModel
{
    public decimal PickedQuantity { get; init; }
    public string? ShortNotes { get; init; }
}
