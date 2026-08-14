namespace Forge.Core.Models;

public record MapChannelListingRequestModel
{
    /// <summary>The part this listing fulfils from. Null clears the mapping.</summary>
    public int? PartId { get; init; }
}
