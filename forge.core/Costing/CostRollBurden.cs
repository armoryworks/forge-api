using Forge.Core.Enums;

namespace Forge.Core.Costing;

/// <summary>A flat item-level burden applied during the roll. Basis: "pct_of_mat" or "per_unit".</summary>
public sealed record CostRollBurden(CostElement Element, string Basis, decimal Value);
