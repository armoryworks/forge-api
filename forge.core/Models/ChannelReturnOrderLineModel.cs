namespace Forge.Core.Models;

/// <summary>One line of a channel order offered for return.</summary>
public record ChannelReturnOrderLineModel(
    int Id,
    int LineNumber,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal ShippedQuantity,
    decimal UnitPrice);
