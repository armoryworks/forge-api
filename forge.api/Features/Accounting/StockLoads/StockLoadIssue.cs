namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// One finding of the stock-load dry run. <see cref="LineNumber"/> is 0 for a load-level finding;
/// <see cref="Code"/> is the stable key the screen words (<c>stock-load-location-not-empty</c>, ...).
/// </summary>
public sealed record StockLoadIssue(int LineNumber, string Code, StockLoadIssueSeverity Severity, string Message);
