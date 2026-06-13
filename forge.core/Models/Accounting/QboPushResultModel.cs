namespace Forge.Core.Models.Accounting;

/// <summary>QB-001 push outcome: the QBO doc id plus the balanced JE's shape.</summary>
public record QboPushResultModel(
    string QboDocId,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal TotalDebit,
    int LineCount);
