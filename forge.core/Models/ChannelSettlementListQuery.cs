using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ChannelSettlementListQuery : PagedQuery
{
    public int? ChannelId { get; init; }
    public ChannelSettlementStatus? Status { get; init; }
}
