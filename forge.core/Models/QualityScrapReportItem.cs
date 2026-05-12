namespace Forge.Core.Models;

public record QualityScrapReportItem(
    int PartId,
    string PartNumber,
    int TotalProduced,
    int TotalScrapped,
    decimal ScrapRate,
    decimal YieldRate);
