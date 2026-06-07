using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>⚡ A fiscal year + its periods, for the close screen.</summary>
public sealed record FiscalYearModel(
    int Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    FiscalYearStatus Status,
    IReadOnlyList<FiscalPeriodModel> Periods);
