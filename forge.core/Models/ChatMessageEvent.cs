namespace Forge.Core.Models;

public record ChatMessageEvent(
    int Id,
    int SenderId,
    string SenderName,
    string SenderInitials,
    string SenderColor,
    int RecipientId,
    string Content,
    DateTimeOffset CreatedAt,
    int? ParentMessageId = null,
    int ThreadReplyCount = 0);
