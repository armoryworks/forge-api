using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ChatRoomMemberResponseModel(
    int UserId,
    string DisplayName,
    string Initials,
    string Color,
    ChannelMemberRole Role = ChannelMemberRole.Member,
    bool IsMuted = false);
