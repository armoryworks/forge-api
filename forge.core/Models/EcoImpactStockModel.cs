namespace Forge.Core.Models;

/// <summary>On-hand stock of the affected part at one revision (from the lot's revision; null when the stock is unlotted or the lot has none).</summary>
public record EcoImpactStockModel(string? Revision, decimal Quantity, int LotCount);
