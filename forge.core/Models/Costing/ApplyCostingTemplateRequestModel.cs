namespace Forge.Core.Models.Costing;

public sealed record ApplyCostingTemplateRequestModel
{
    public required int FiscalYear { get; init; }
    /// <summary>Direct (production) employees — drives labor hours and per-employee bases.</summary>
    public required decimal DirectHeadcount { get; init; }
    /// <summary>Average direct hourly wage — drives percent-of-wages bases and labor rates.</summary>
    public required decimal AverageHourlyWage { get; init; }
    /// <summary>Answer per template line code; a missing entry falls back to the line's default.</summary>
    public required Dictionary<string, decimal> Values { get; init; }
    /// <summary>Also mirror lines with a GL account into expense accounts + budget lines (needs CAP-ACCT-FULLGL).</summary>
    public bool CreateGlBudgets { get; init; } = true;
    /// <summary>Set a standard labor rate (wage + hourly burden) for active users that have none.</summary>
    public bool SetDefaultLaborRates { get; init; }
}
