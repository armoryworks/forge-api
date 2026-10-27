namespace Forge.Core.Models;

/// <summary>The impact of an ECO on one affected part (named directly, or as the part of an affected operation).</summary>
public record EcoPartImpactModel(
    int PartId,
    string PartNumber,
    string? PartName,
    string CurrentRevision,
    List<EcoImpactJobModel> OpenJobs,
    List<EcoImpactPurchaseOrderLineModel> OpenPurchaseOrderLines,
    List<EcoImpactSalesOrderLineModel> OpenSalesOrderLines,
    List<EcoImpactStockModel> StockByRevision);
