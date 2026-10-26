using Forge.Core.Enums.Accounting;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>One stock load in the list.</summary>
public sealed record StockLoadSummaryModel(
    int Id,
    string LoadNumber,
    DateOnly AsOfDate,
    StockLoadStatus Status,
    StockLoadGlMode GlMode,
    int LineCount,
    decimal TotalValue,
    long? JournalEntryId,
    DateTimeOffset? PostedAt,
    DateTimeOffset CreatedAt);
