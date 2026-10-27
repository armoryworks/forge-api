using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PhysicalInventoryResponseModel
{
    public int Id { get; init; }
    public string InventoryNumber { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? ScopeLocationId { get; init; }
    public string? ScopeLocationName { get; init; }
    public PhysicalInventoryStatus Status { get; init; }
    public DateOnly CountDate { get; init; }
    public bool BlindCount { get; init; }
    public decimal RecountTolerancePercent { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset? FrozenAt { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public decimal TotalVarianceValue { get; init; }
    public int TagCount { get; init; }
    public int CountedTagCount { get; init; }
    public int RecountTagCount { get; init; }
    public bool BookQuantitiesVisible { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
