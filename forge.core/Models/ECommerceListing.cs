namespace Forge.Core.Models;

/// <summary>A saleable listing as reported by a platform, normalised for mapping to a part.</summary>
public record ECommerceListing
{
    public string ExternalListingId { get; init; } = string.Empty;
    public string? ExternalSku { get; init; }
    public string? Title { get; init; }
    public decimal? Price { get; init; }

    /// <summary>Quantity the platform currently shows as available.</summary>
    public decimal? AvailableQuantity { get; init; }

    public bool IsActive { get; init; } = true;
}
