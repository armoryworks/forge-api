namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — full projection of a NACHA payment batch (header + entry lines).</summary>
public record PaymentBatchDetailModel(
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
    DateTimeOffset? GeneratedAt,
    bool HasFile,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PaymentBatchItemModel> Items);
