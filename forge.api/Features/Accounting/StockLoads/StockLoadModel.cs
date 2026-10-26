using Forge.Core.Enums.Accounting;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>An opening stock load with its lines and, once posted, its journal.</summary>
public sealed record StockLoadModel(
    int Id,
    string LoadNumber,
    int BookId,
    DateOnly AsOfDate,
    StockLoadStatus Status,
    StockLoadGlMode GlMode,
    string? SourceFileName,
    string? Notes,
    decimal TotalValue,
    long? JournalEntryId,
    long? JournalEntryNumber,
    DateTimeOffset? PostedAt,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    uint Version,
    IReadOnlyList<StockLoadLineModel> Lines);
