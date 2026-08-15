namespace Forge.Core.Models;

public record ChannelSettlementDetailResponseModel
{
    public ChannelSettlementResponseModel Settlement { get; init; } = new();
    public IReadOnlyList<ChannelSettlementLineResponseModel> Lines { get; init; } = [];
}
