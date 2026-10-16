namespace Forge.Core.Models;

public record QuoteLineCostBreakdownModel(
    decimal Labor,
    decimal Burden,
    decimal Material,
    decimal Osp,
    decimal Total);
