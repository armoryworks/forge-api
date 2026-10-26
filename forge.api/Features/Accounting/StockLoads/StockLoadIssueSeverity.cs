namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>An error blocks the post; a warning is shown and the post may go ahead.</summary>
public enum StockLoadIssueSeverity
{
    Error,
    Warning,
}
