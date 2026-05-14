
namespace Forge.Core.Models;

public record CreateChannelRequestModel(
    string Name,
    ChannelType ChannelType,
    string? Description,
    string? IconName,
    List<int> MemberIds);
