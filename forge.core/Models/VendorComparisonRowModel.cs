using Forge.Core.Enums;

namespace Forge.Core.Models;

public record VendorComparisonRowModel(
    int VendorId,
    string VendorName,
    decimal OnTimePercent,
    decimal QualityPercent,
    decimal TotalSpend,
    decimal OverallScore,
    VendorGrade Grade,
    decimal Trend);
