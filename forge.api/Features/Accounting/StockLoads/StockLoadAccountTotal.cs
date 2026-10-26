namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>The value a stock load puts on one inventory control account.</summary>
public sealed record StockLoadAccountTotal(int? GlAccountId, string? AccountNumber, string? AccountName, decimal Total);
