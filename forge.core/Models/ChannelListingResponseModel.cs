namespace Forge.Core.Models;

public record ChannelListingResponseModel
{
    public int Id { get; init; }
    public int ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string ExternalListingId { get; init; } = string.Empty;
    public string? ExternalSku { get; init; }
    public string? Title { get; init; }
    public int? PartId { get; init; }
    public string? PartNumber { get; init; }
    public string? PartName { get; init; }
    public decimal? ListedPrice { get; init; }
    public decimal? PublishedQuantity { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
    public bool IsActive { get; init; }

    /// <summary>True when no part is mapped. Orders for it still import, but their lines land without a part.</summary>
    public bool IsUnmapped { get; init; }
}
