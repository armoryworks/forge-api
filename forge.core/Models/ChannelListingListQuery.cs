namespace Forge.Core.Models;

public record ChannelListingListQuery : PagedQuery
{
    public int? ChannelId { get; init; }

    /// <summary>True surfaces the triage queue: active listings with no part mapped.</summary>
    public bool? IsUnmapped { get; init; }

    public bool IncludeInactive { get; init; }
}
