using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ChatRoomResponseModel(
    int Id,
    string Name,
    bool IsGroup,
    int CreatedById,
    DateTimeOffset CreatedAt,
    List<ChatRoomMemberResponseModel> Members,
    ChannelType ChannelType = ChannelType.DirectMessage,
    string? Description = null,
    int? TeamId = null,
    bool IsReadOnly = false,
    string? IconName = null,
    int UnreadCount = 0,
    string? LastMessage = null,
    DateTimeOffset? LastMessageAt = null);
