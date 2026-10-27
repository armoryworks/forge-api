namespace Forge.Core.Models;

/// <summary>The open work an ECO touches, per affected part: open jobs, open purchase-order lines, open sales-order lines and on-hand stock by revision.</summary>
public record EcoImpactResponseModel(int EcoId, List<EcoPartImpactModel> Parts);
