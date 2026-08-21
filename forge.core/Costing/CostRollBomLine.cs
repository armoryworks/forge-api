using Forge.Core.Enums;

namespace Forge.Core.Costing;

/// <summary>One BOM line as the cost roll sees it.</summary>
public sealed record CostRollBomLine(
    int ComponentItemId,
    decimal QtyPer,
    decimal ScrapPct,
    BomComponentType ComponentType,
    bool ComponentIsManufactured);
