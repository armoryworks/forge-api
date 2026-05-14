
namespace Forge.Core.Entities;

public class ChatRoomMember : BaseAuditableEntity
{
    public int ChatRoomId { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public ChannelMemberRole Role { get; set; } = ChannelMemberRole.Member;
    public DateTimeOffset? MutedUntil { get; set; }
    public int? LastReadMessageId { get; set; }

    public ChatRoom ChatRoom { get; set; } = null!;
    public ChatMessage? LastReadMessage { get; set; }
}
