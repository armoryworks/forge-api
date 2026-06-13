using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>⚡ A fiscal period's identity + lifecycle status (close API surface).</summary>
public sealed record FiscalPeriodModel(
    int Id,
    int FiscalYearId,
    int PeriodNumber,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    FiscalPeriodStatus Status);
