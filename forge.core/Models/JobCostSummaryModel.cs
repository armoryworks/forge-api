namespace Forge.Core.Models;

public record JobCostSummaryModel
{
    public int JobId { get; init; }
    public string JobNumber { get; init; } = string.Empty;
    public decimal QuotedPrice { get; init; }

    // Material
    public decimal MaterialEstimated { get; init; }
    public decimal MaterialActual { get; init; }
    public decimal MaterialVariance => MaterialActual - MaterialEstimated;
    public decimal MaterialVariancePercent => MaterialEstimated != 0 ? MaterialVariance / MaterialEstimated * 100 : 0;

    // Labor
    public decimal LaborEstimated { get; init; }
    public decimal LaborActual { get; init; }
    public decimal LaborVariance => LaborActual - LaborEstimated;
    public decimal LaborVariancePercent => LaborEstimated != 0 ? LaborVariance / LaborEstimated * 100 : 0;

    // Burden
    public decimal BurdenEstimated { get; init; }
    public decimal BurdenActual { get; init; }
    public decimal BurdenVariance => BurdenActual - BurdenEstimated;

    // Subcontract
    public decimal SubcontractEstimated { get; init; }
    public decimal SubcontractActual { get; init; }
    public decimal SubcontractVariance => SubcontractActual - SubcontractEstimated;

    // Totals
    public decimal TotalEstimated => MaterialEstimated + LaborEstimated + BurdenEstimated + SubcontractEstimated;
    public decimal TotalActual => MaterialActual + LaborActual + BurdenActual + SubcontractActual;
    public decimal TotalVariance => TotalActual - TotalEstimated;
    public decimal TotalVariancePercent => TotalEstimated != 0 ? TotalVariance / TotalEstimated * 100 : 0;
    public decimal ActualMargin => QuotedPrice - TotalActual;
    public decimal ActualMarginPercent => QuotedPrice != 0 ? ActualMargin / QuotedPrice * 100 : 0;
}
