namespace Forge.Core.Entities;

public class ChatMessageMention : BaseEntity
{
    public int ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;

    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}
