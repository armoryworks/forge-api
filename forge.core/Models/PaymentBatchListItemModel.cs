namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — list projection of a NACHA payment batch.</summary>
public record PaymentBatchListItemModel(
    int Id,
    string BatchNumber,
    string Status,
    bool IsPrenote,
    DateTimeOffset EffectiveEntryDate,
    decimal TotalAmount,
    int EntryCount,
    int CreatedByUserId,
    string CreatedByName,
    int? ReleasedByUserId,
    string? ReleasedByName,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset CreatedAt);
