namespace Forge.Core.Costing;

/// <summary>Everything the roll needs for one item: its lot size, BOM, routing, and burdens.</summary>
public sealed record CostRollItem(
    int ItemId,
    decimal StandardLotSize,
    IReadOnlyList<CostRollBomLine> Bom,
    IReadOnlyList<CostRollRoutingOp> Routing,
    IReadOnlyList<CostRollBurden> Burdens);
