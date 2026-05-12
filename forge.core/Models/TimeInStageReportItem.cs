namespace Forge.Core.Models;

public record TimeInStageReportItem(
    string StageName,
    string StageColor,
    decimal AverageDays,
    int JobCount,
    bool IsBottleneck);
