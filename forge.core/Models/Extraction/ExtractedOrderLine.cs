namespace Forge.Core.Models.Extraction;

/// <summary>One proposed order line. Every field is optional — a line with only a part reference is still useful.</summary>
public sealed record ExtractedOrderLine(
    ExtractedField<string>? PartReference = null,
    ExtractedField<decimal>? Quantity = null,
    ExtractedField<decimal>? UnitPrice = null,
    ExtractedField<string>? Description = null);
