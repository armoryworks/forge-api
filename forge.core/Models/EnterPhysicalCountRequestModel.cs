namespace Forge.Core.Models;

public record EnterPhysicalCountRequestModel
{
    public decimal Quantity { get; init; }
    public string? Notes { get; init; }
}
