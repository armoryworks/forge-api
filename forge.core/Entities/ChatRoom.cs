
namespace Forge.Core.Entities;

public class ChatRoom : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public int CreatedById { get; set; }
    public ChannelType ChannelType { get; set; } = ChannelType.DirectMessage;
    public string? Description { get; set; }
    public int? TeamId { get; set; }
    public bool IsReadOnly { get; set; }
    public string? IconName { get; set; }
    public bool CreatedBySystem { get; set; }

    public Team? Team { get; set; }
    public ICollection<ChatRoomMember> Members { get; set; } = new List<ChatRoomMember>();
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
